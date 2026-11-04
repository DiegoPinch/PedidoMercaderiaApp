using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using System.Security.Cryptography;

static class PruebasReposicion
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        var politica = new PoliticaReposicion();
        PerfilReposicion Perfil(params decimal?[] stocks)
        {
            var perfil = new PerfilReposicion();
            for (int i = 0; i < stocks.Length; i++) perfil.Observar(new(i + 1, new DateOnly(2026, 1, 1).AddDays(i * 7), stocks[i]));
            return perfil;
        }
        PropuestaPedido Propuesta(decimal local, decimal cantidad = 14) => new("A", "A", "M", "Bajó stock", "Bajada reciente", local, local + cantidad, null, "B1", cantidad, true);
        var corta = AnalisisReposicionService.Ajustar(Propuesta(46), Perfil(60, 46), politica, 999);
        verificar(corta.Cantidad == 14 && corta.BajadaDiaria is null, "Dos Excel no inventan una tendencia de varias semanas");
        var historial = Perfil(60, 46, 32, 18, 4);
        var tendencia = AnalisisReposicionService.Ajustar(Propuesta(4), historial, politica, 999);
        verificar(tendencia.Cantidad == 18 && tendencia.BajadaDiaria == 2 && tendencia.Intervalos == 4,
            "La cobertura descuenta el stock actual: 2 por día × 11 días menos 4 = 18");
        verificar(AnalisisReposicionService.Ajustar(Propuesta(4), historial, politica, 5).Cantidad == 5,
            "La tendencia nunca supera las existencias transferibles del centro");
        verificar(AnalisisReposicionService.Ajustar(Propuesta(32), Perfil(74, 60, 46, 32), politica, 999) is { Cantidad: 0, Rapido: false },
            "Stock suficiente para la cobertura no se añade al pedido automático");
        verificar(AnalisisReposicionService.Ajustar(Propuesta(4), Perfil(60, null, 32, 18, 4), politica, 999).BajadaDiaria is null,
            "Un producto ausente interrumpe el cálculo; no se interpreta como agotado");
        verificar(AnalisisReposicionService.Ajustar(Propuesta(0, 50), Perfil(70, 60, 50, 0), politica, 999).Rapido == false,
            "Una bajada atípica se separa para revisión manual");
        var espaciado = Perfil(60, 46, 32, 18);
        espaciado.Observar(new(5, new(2026, 4, 1), 4));
        verificar(!AnalisisReposicionService.Ajustar(Propuesta(4), espaciado, politica, 999).Rapido,
            "Un intervalo prolongado no reutiliza una tendencia antigua como reposición automática");
        var ajustado = Perfil(60, 46, 32, 18, 4);
        for (int i = 1; i <= 3; i++) ajustado.Registrar(new(i, i, "Ajustada", 10, 5, DateTime.UtcNow));
        ajustado.Registrar(new(4, 4, "Excepcional", 10, 500, DateTime.UtcNow));
        verificar(AnalisisReposicionService.Ajustar(Propuesta(4), ajustado, politica, 999).Cantidad == 9,
            "Tres correcciones habituales ajustan la cantidad; la compra excepcional no altera la preferencia");
        for (int i = 1; i <= 50; i++) ajustado.Observar(new(i, new DateOnly(2026, 1, 1).AddDays(i), i));
        verificar(ajustado.Observaciones.Count == 26, "El historial numérico conserva un máximo fijo de 26 observaciones");

        await using var banco = await BancoPruebas.Crear();
        await banco.CrearModeloEf();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var almacen = new Almacen(banco.Fabrica, cifrado);
        await almacen.Inicializar(); await almacen.CrearUsuario("admin", "Admin", "hash", true);
        ResultadoImportacion Inventario(decimal stock) => new() { NombreArchivo = "semanal.xlsx", BodegasDetectadas = ["B4", "B1", "B40"], Productos = [
            new() { Codigo = "A", Descripcion = "Producto A", Existencias = new() { ["B4"] = stock, ["B1"] = 1000, ["B40"] = 500 } },
            new() { Codigo = "B", Descripcion = "Producto B", Existencias = new() { ["B4"] = 10, ["B1"] = 100, ["B40"] = 50 } }] };
        for (int i = 0; i < 5; i++)
        {
            await BancoPruebas.PrepararOtraCarga(almacen);
            var actual = await almacen.Estado();
            await almacen.Importar(new(actual.Revision, new DateOnly(2026, 1, 1).AddDays(7 * i), Inventario(60 - 14 * i)), "admin");
            if (i == 0) await BancoPruebas.ConfigurarB4(almacen);
        }
        var estado = await almacen.Estado();
        verificar(estado.Propuestas.Single(p => p.Codigo == "A").Cantidad == 18, "Las importaciones alimentan la tendencia precalculada de la sucursal");
        await using var ef = banco.Fabrica.CreateDbContext();
        var cache = (await ef.Analiticas.AsNoTracking().SingleAsync()).ContenidoCifrado;
        var filas = new List<ItemPedido> { new() { CodigoProducto = "A", BodegaOrigen = "B1", CantidadSolicitada = 9 },
            new() { CodigoProducto = "B", BodegaOrigen = "B1", CantidadSolicitada = 5, CompraExcepcional = true } };
        await almacen.Guardar(new(estado.Revision, filas, estado.Corte!.Id), "admin");
        estado = await almacen.Estado(); await almacen.Estado();
        verificar(cache.SequenceEqual((await ef.Analiticas.AsNoTracking().SingleAsync()).ContenidoCifrado),
            "Leer y editar una fila reutilizan el análisis cifrado sin volver a calcularlo");
        verificar(await ef.PerfilesReposicion.CountAsync() == 2 && await ef.PerfilesReposicion.Select(p => p.BodegaId).Distinct().CountAsync() == 1,
            "Hay una fila por producto de la sucursal, sin historial de las bodegas nacionales");
        await almacen.Realizar(estado.Revision, "admin");
        estado = await almacen.Estado();
        var id = estado.Historial.Single().Id;
        verificar(estado.Aprendizaje.Ajustadas == 1 && estado.Aprendizaje.Excepcionales == 1,
            "Guardar el pedido registra la corrección y distingue las compras excepcionales");
        verificar((await almacen.Detalle(id)).Items.Single(p => p.CodigoProducto == "B").CompraExcepcional,
            "La compra excepcional permanece cifrada en el pedido histórico");
        await almacen.CambiarEstado(id, new(estado.Revision, "Cancelado"), "admin");
        estado = await almacen.Estado();
        verificar(estado.Aprendizaje.Ajustadas == 0 && estado.Aprendizaje.Excepcionales == 0,
            "Anular un pedido retira sus decisiones del aprendizaje");
        estado.Configuracion.Reposicion.DiasEntrePedidos = 14;
        await almacen.Configurar(new(estado.Revision, estado.Configuracion), "admin");
        estado = await almacen.Estado();
        verificar(estado.Propuestas.Single(p => p.Codigo == "A").Cantidad == 32, "Cambiar la cobertura invalida el análisis anterior y recalcula las cantidades");
        estado.Configuracion.ProductosNoSugerir.Add("A");
        await almacen.Configurar(new(estado.Revision, estado.Configuracion), "admin");
        estado = await almacen.Estado();
        verificar(estado.Propuestas.All(p => p.Codigo != "A") && estado.Aprendizaje.NoSeVenden == 1,
            "No se vende prevalece sobre tendencias y queda registrado");
        using var reinicio = new Almacen(banco.Fabrica, cifrado);
        verificar((await reinicio.Estado()).Aprendizaje.NoSeVenden == 1, "Otra instancia puede leer las recomendaciones persistidas");
        var muestra = (await ef.PerfilesReposicion.AsNoTracking().FirstAsync());
        verificar(!System.Text.Encoding.UTF8.GetString(muestra.ContenidoCifrado).Contains("Observaciones"), "Las cantidades, fechas y decisiones analíticas se almacenan cifradas");
        for (int i = 1; i <= 27; i++)
        {
            await BancoPruebas.PrepararOtraCarga(almacen);
            estado = await almacen.Estado();
            await almacen.Importar(new(estado.Revision, new DateOnly(2026, 1, 29).AddDays(i * 7), Inventario(10)), "admin");
        }
        var perfilesFinales = await ef.PerfilesReposicion.AsNoTracking().ToListAsync();
        verificar(perfilesFinales.Count == 2 && perfilesFinales.All(p => cifrado.Leer<PerfilReposicion>(p.ContenidoCifrado,
            $"perfiles_reposicion/{p.BodegaId}/{p.ProductoId}").Observaciones.Count == 26),
            "27 cargas adicionales conservan solo dos perfiles con 26 observaciones, sin filas por corte");
    }
}
