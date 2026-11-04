using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using System.Security.Cryptography;

static class PruebasAnalisis
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        await using var banco = await BancoPruebas.Crear();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var db = new Almacen(banco.Fabrica, cifrado);
        await banco.CrearModeloEf();
        await db.Inicializar();
        await using (var ef = banco.Fabrica.CreateDbContext())
        {
            verificar(ef.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL", "El acceso usa Entity Framework Core con PostgreSQL real");
            verificar(await ef.Bodegas.CountAsync() == 0, "La instalación espera las bodegas del Excel sin inventar un catálogo fijo");
            verificar((await ef.Database.GetAppliedMigrationsAsync()).Count() == 0, "El arranque no genera ni aplica migraciones por cuenta del administrador");
        }
        await db.CrearUsuario("admin", "Administrador", "hash", true);
        ProductoInventario P(string codigo, decimal stock, decimal centro = 100) => new() { Codigo = codigo, Descripcion = "Producto " + codigo,
            Existencias = new() { ["B4"] = stock, ["B1"] = centro, ["B40"] = centro, ["B39"] = 5000 } };
        ResultadoImportacion Inventario(params ProductoInventario[] productos) => new() { NombreArchivo = "semanal.xlsx", Productos = productos.ToList() };
        var primero = Inventario(P("FRECUENTE", 10), P("OCASIONAL", 10), P("AGOTADO", 4), P("SIEMPRE_CERO", 0), P("DESAPARECE", 8));
        await db.Importar(new(0, new(2026, 10, 1), primero), "admin");
        await BancoPruebas.ConfigurarB4(db);
        async Task<long> Registrar(decimal cantidad, bool ocasional = false)
        {
            var e = await db.Estado();
            if (!e.PedidoAbierto) { await db.Importar(new(e.Revision, e.Corte!.Fecha.AddDays(1), primero), "admin"); e = await db.Estado(); }
            var filas = new List<ItemPedido> { new() { CodigoProducto = "FRECUENTE", BodegaOrigen = "B1", CantidadSolicitada = cantidad } };
            if (ocasional) filas.Add(new() { CodigoProducto = "OCASIONAL", BodegaOrigen = "B40", CantidadSolicitada = 30 });
            await db.Guardar(new(e.Revision, filas, e.Corte!.Id), "admin");
            e = await db.Estado(); await db.Realizar(e.Revision, "admin");
            e = await db.Estado(); return e.Historial[0].Id;
        }
        var id = await Registrar(2, true);
        var estado = await db.Estado();
        id = await Registrar(3); estado = await db.Estado();
        id = await Registrar(90); estado = await db.Estado(); await db.CambiarEstado(id, new(estado.Revision, "Cancelado"), "admin");
        // Un pedido antiguo permanece en historial pero no altera la frecuencia de 90 días.
        await using (var ef = banco.Fabrica.CreateDbContext())
        {
            var pedido = await ef.Pedidos.SingleAsync(p => p.Id == id);
            var resumen = cifrado.Leer<PedidoResumen>(pedido.ResumenCifrado, $"pedidos/{id}/resumen");
            pedido.ResumenCifrado = cifrado.Cifrar(resumen with { Estado = "Guardado", Fecha = DateTime.UtcNow.AddDays(-100) }, $"pedidos/{id}/resumen");
            pedido.EstadoIndice = cifrado.Indice("estado", "Guardado");
            await ef.SaveChangesAsync();
        }
        id = await Registrar(80); estado = await db.Estado(); await db.CambiarEstado(id, new(estado.Revision, "Cancelado"), "admin");
        var segundo = Inventario(P("FRECUENTE", 7), P("OCASIONAL", 4), P("AGOTADO", 0), P("SIEMPRE_CERO", 0), P("NUEVO_SIN_CENTRO", 0, 0));
        await BancoPruebas.PrepararOtraCarga(db);
        estado = await db.Estado(); await db.Importar(new(estado.Revision, new(2026, 10, 8), segundo), "admin");
        estado = await db.Estado();
        verificar(estado.MasPedidos[0].Codigo == "FRECUENTE" && estado.MasPedidos[0].Pedidos == 2 && estado.MasPedidos[0].Cantidad == 5,
            "Más pedidos cuenta solicitudes y cantidades reales; excluye cancelados y pedidos de más de 90 días");
        verificar(estado.Propuestas[0].Codigo == "AGOTADO" && estado.Propuestas.Single(p => p.Codigo == "AGOTADO").Cantidad == 4,
            "Se prioriza un agotamiento nuevo y se propone la diferencia observada");
        verificar(estado.Propuestas.FindIndex(p => p.Codigo == "FRECUENTE") < estado.Propuestas.FindIndex(p => p.Codigo == "OCASIONAL"),
            "La frecuencia prioriza reposiciones por número de pedidos, sin comparar unidades de productos diferentes");
        verificar(estado.Propuestas.Single(p => p.Codigo == "SIEMPRE_CERO").Rapido == false,
            "No repite automáticamente un artículo que ya estaba en cero sin movimiento nuevo");
        verificar(estado.Cambios.Any(c => c.Codigo == "NUEVO_SIN_CENTRO" && c.Tipo == "Nuevo") && !estado.Propuestas.Any(p => p.Codigo == "NUEVO_SIN_CENTRO"),
            "Muestra novedades sin disponibilidad y evita pedirlas usando el stock de Bame");
        verificar(estado.Cambios.Single(c => c.Codigo == "DESAPARECE").Ahora is null,
            "Un código ausente del nuevo Excel queda desconocido, nunca se considera agotado");
        var historial = await db.Historial(" frecuente ", "Guardado");
        verificar(historial.Total == 3 && historial.Pedidos.All(p => p.Estado == "Guardado"), "Historial filtra código exacto normalizado y estado con EF");
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        verificar((await db.Historial("FRECUENTE", "Guardado", hoy, hoy)).Total == 2, "El filtro de fechas incluye ambos extremos en horario de B4");
        verificar((await db.Historial("INEXISTENTE")).Total == 0 && (await db.Historial(pagina: 999)).Pagina == 1, "El historial maneja búsquedas vacías y páginas fuera de rango");
        estado.Configuracion.ProductosNoSugerir.Add("FRECUENTE"); await db.Configurar(new(estado.Revision, estado.Configuracion), "admin");
        estado = await db.Estado(); verificar(!estado.Propuestas.Any(p => p.Codigo == "FRECUENTE"), "Ser frecuente no anula la exclusión No sugerir");
        await BancoPruebas.PrepararOtraCarga(db); estado = await db.Estado();
        await db.Importar(new(estado.Revision, new(2026, 10, 15), segundo), "admin");
        estado = await db.Estado(); verificar(!estado.Propuestas.Any(p => p.Rapido), "Repetir las mismas existencias otra semana no genera reposición automática");
        var referenciaVieja = Inventario(P("FRECUENTE", 80));
        var propuesta = RecomendacionService.Crear(segundo.Productos, primero.Productos, referenciaVieja.Productos,
            primero.Productos.Select(p => p.Codigo).ToHashSet(), new HashSet<string>(), new ConfiguracionBodegas { BodegaLocal = "B4", BodegasOrigen = new(["B1", "B40"]) });
        verificar(propuesta.Single(p => p.Codigo == "FRECUENTE").Cantidad == 3, "Una referencia antigua de 80 no infla la bajada actual de 10 a 7");
        await ProbarCatalogoDinamico(verificar);
    }
    private static async Task ProbarCatalogoDinamico(Action<bool, string> verificar)
    {
        await using var banco = await BancoPruebas.Crear();
        await banco.CrearModeloEf();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var db = new Almacen(banco.Fabrica, cifrado);
        await db.Inicializar(); await db.CrearUsuario("admin", "Administrador", "hash", true);
        ProductoInventario P(string codigo, decimal local) => new() { Codigo = codigo, Descripcion = codigo, Existencias = new() { ["B7"] = local, ["B88"] = 50 } };
        await db.Importar(new(0, new(2026, 10, 1), new() { Productos = [P("ART-1", 12)], BodegasDetectadas = ["B7", "B88", "B90"] }), "admin");
        var e = await db.Estado();
        verificar(e.Configuracion.NombresBodegas.Count == 3 && e.Propuestas.Count == 0 && e.Configuracion.BodegaLocal == "", "El primer Excel registra sus bodegas sin asignarlas automáticamente");
        e.Configuracion.BodegaLocal = "B7"; e.Configuracion.BodegasOrigen = new(["B88"]);
        e.Configuracion.NombresBodegas["B7"] = "Ambato"; e.Configuracion.NombresBodegas["B88"] = "Centro Quito";
        await db.Configurar(new(e.Revision, e.Configuracion), "admin");
        e = await db.Estado();
        await BancoPruebas.PrepararOtraCarga(db); e = await db.Estado();
        await db.Importar(new(e.Revision, new(2026, 10, 8), new() { Productos = [P("ART-1", 8), P("ART-2", 0)], BodegasDetectadas = ["B7", "B88", "B90", "B99"] }), "admin");
        e = await db.Estado();
        verificar(e.Configuracion.NombresBodegas["B7"] == "Ambato" && e.Configuracion.NombresBodegas.ContainsKey("B99") && !e.Configuracion.BodegasOrigen.Contains("B99"), "Nueva bodega se registra sin habilitarse y conserva nombres anteriores");
        verificar(e.Propuestas.Single(p => p.Codigo == "ART-1").Cantidad == 4 && e.Propuestas.All(p => p.Origen == "B88"), "Recomienda según B7 y el centro B88 configurados");
        verificar(e.Cambios.Single(p => p.Codigo == "ART-2").Tipo == "Nuevo" && e.Inventario!.Productos[0].Existencias["B90"] == 0, "Detecta productos nuevos y reconoce columnas con stock cero");
        await using (var ef = banco.Fabrica.CreateDbContext())
            verificar(await ef.Productos.CountAsync() == 2 && await ef.Bodegas.CountAsync() == 4, "Reimportar actualiza el catálogo sin duplicar productos ni bodegas");
        await db.CrearUsuario("ana", "Colaboradora", "hash", false);
        e.Configuracion.NombresBodegas["B7"] = "Cambio no autorizado";
        bool rechazo = false;
        try { await db.Configurar(new(e.Revision, e.Configuracion), "ana"); } catch (InvalidOperationException) { rechazo = true; }
        verificar(rechazo && (await db.Estado()).Configuracion.NombresBodegas["B7"] == "Ambato", "Solo el administrador modifica el catálogo de bodegas");
        e = await db.Estado(); await db.Generar(new(e.Revision, false), "admin");
        e = await db.Estado(); e.Configuracion.BodegaLocal = "B90";
        rechazo = false;
        try { await db.Configurar(new(e.Revision, e.Configuracion), "admin"); } catch (InvalidOperationException) { rechazo = true; }
        verificar(rechazo, "Cambiar la sucursal no puede trasladar un borrador existente");
        e = await db.Estado(); await db.Realizar(e.Revision, "admin");
        e = await db.Estado(); verificar(e.Historial[0].Destino == "B7", "El pedido conserva la sucursal destino real");
        e.Configuracion.BodegaLocal = "B90"; await db.Configurar(new(e.Revision, e.Configuracion), "admin");
        verificar((await db.Estado()).MasPedidos.Count == 0, "La frecuencia no mezcla pedidos de otras sucursales");
    }

}
