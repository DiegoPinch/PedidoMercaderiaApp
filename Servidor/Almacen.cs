using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Servidor.DatosModelo;
using System.Data;
using Npgsql;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

namespace Servidor;

public sealed class ConflictoException(string mensaje) : Exception(mensaje);

public sealed partial class Almacen
{
    private async Task<long> Mutar(long revision, string usuario, string accion, Func<PedidosDbContext, EstadoTrabajo, Task> operacion, bool recomendar = false, bool recalcular = true)
    {
         { await using var db = await Abrir(); await using var tx = await db.Database.BeginTransactionAsync();
            await BloquearTrabajo(db);
            var estado = await Estado(db, recomendar);
            if (revision != estado.Revision) throw new ConflictoException("Otra persona actualizó el trabajo. Tus cambios locales siguen visibles. Actualiza desde el equipo antes de volver a editar.");
            await operacion(db, estado);
            if (recalcular) await ActualizarAnalitica(db);
            await db.Trabajo.Where(t => t.Id == 1).ExecuteUpdateAsync(t => t.SetProperty(x => x.Revision, x => x.Revision + 1)); await Auditar(db, usuario, accion); await tx.CommitAsync(); return revision + 1; }
    }
    public async Task<BorradorRespuesta> Guardar(GuardarBorrador solicitud, string usuario)
    {
        
        {
        await using var db = await Abrir(); await using var tx = await db.Database.BeginTransactionAsync();
            await BloquearTrabajo(db);
        var estado = await Estado(db, false);
        if (solicitud.CorteId != estado.Corte?.Id) throw new ConflictoException("El inventario cambió. Actualiza desde el equipo.");
        if (solicitud.BaseItems is null && solicitud.Revision != estado.Revision)
            throw new ConflictoException("El pedido cambió. Actualiza desde el equipo.");
        var originales = estado.Borrador.Select(i => i.Copiar()).ToList();
        var combinado = solicitud.Items;
        if (solicitud.BaseItems is not null)
        {
            if (solicitud.BaseItems.Select(i => i.CodigoProducto).Distinct(StringComparer.OrdinalIgnoreCase).Count() != solicitud.BaseItems.Count
                || solicitud.Items.Select(i => i.CodigoProducto).Distinct(StringComparer.OrdinalIgnoreCase).Count() != solicitud.Items.Count)
                throw new InvalidOperationException("El borrador contiene códigos repetidos.");
            var baseItems = solicitud.BaseItems.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
            var editados = solicitud.Items.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
            var actuales = estado.Borrador.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
            foreach (var codigo in baseItems.Keys.Union(editados.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var antes = baseItems.GetValueOrDefault(codigo); var nuevo = editados.GetValueOrDefault(codigo); var actual = actuales.GetValueOrDefault(codigo);
                if (ItemPedido.MismaSolicitud(antes, nuevo)) continue;
                if (!ItemPedido.MismaSolicitud(antes, actual) && !ItemPedido.MismaSolicitud(nuevo, actual)) throw new ConflictoException($"Otra persona modificó {codigo}. Tus cambios siguen visibles. Revisa la versión del equipo antes de reemplazarlos.");
                if (nuevo is null) actuales.Remove(codigo); else actuales[codigo] = nuevo;
            }
            combinado = actuales.Values.ToList();
        }
        ValidarLineas(combinado, estado, permitirConservadas: true);
        await GuardarLineas(db, combinado, anteriores: originales);
        await db.Trabajo.Where(t => t.Id == 1).ExecuteUpdateAsync(t => t.SetProperty(x => x.Revision, x => x.Revision + 1));
        await Auditar(db, usuario, "Guardar borrador"); await tx.CommitAsync();
        return new(estado.Revision + 1, combinado);
        }
    }
    public Task<long> Configurar(GuardarConfiguracion solicitud, string usuario) => Mutar(solicitud.Revision, usuario, "Guardar configuración", async (db, estado) =>
    {
        var c = solicitud.Configuracion;
        if (c.Reposicion is null) throw new InvalidOperationException("Falta la configuración de reposición.");
        c.Reposicion.Validar();
        await RegistrarExclusiones(db, estado, c);
        var cambiaBodegas = c.BodegaLocal != estado.Configuracion.BodegaLocal
            || !c.BodegasOrigen.SetEquals(estado.Configuracion.BodegasOrigen)
            || c.NombresBodegas.Any(b => estado.Configuracion.NombresBodegas.GetValueOrDefault(b.Key) != b.Value);
        if (cambiaBodegas && LeerUsuario(await db.Usuarios.SingleAsync(u => u.Id == UsuarioId(usuario))).Rol != "Administrador")
            throw new InvalidOperationException("Solo el administrador puede cambiar las bodegas del equipo.");
        var conocidas = await Bodegas(db);
        var codigos = conocidas.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!codigos.Contains(c.BodegaLocal) || c.BodegasOrigen.Count == 0 || c.BodegasOrigen.Contains(c.BodegaLocal)
            || c.BodegasOrigen.Any(b => !codigos.Contains(b)) || c.ProductosNoSugerir.Count > 100000)
            throw new InvalidOperationException("Selecciona tu sucursal y al menos un centro distinto entre las bodegas importadas.");
        if (c.BodegaLocal != estado.Configuracion.BodegaLocal && estado.Borrador.Count > 0)
            throw new InvalidOperationException("Termina o vacía el borrador antes de cambiar la sucursal del equipo.");
        if (c.NombresBodegas.Any(b => !codigos.Contains(b.Key) || string.IsNullOrWhiteSpace(b.Value) || b.Value.Length > 100))
            throw new InvalidOperationException("Cada nombre debe tener entre 1 y 100 caracteres y corresponder a una bodega registrada.");
        foreach (var b in await db.Bodegas.ToListAsync())
        {
            var codigo = conocidas[b.Id];
            if (c.NombresBodegas.TryGetValue(codigo, out var nombre))
                b.NombreCifrado = cifrado.Cifrar(nombre.Trim(), $"bodegas/{b.Id}/nombre");
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var contenido = cifrado.Cifrar(c, "trabajo/1/configuracion");
        await db.Trabajo.Where(t => t.Id == 1).ExecuteUpdateAsync(t => t.SetProperty(x => x.ConfiguracionCifrada, contenido));
    });
    public Task<long> Importar(ImportarCorte solicitud, string usuario) => Mutar(solicitud.Revision, usuario, "Importar inventario", async (db, estado) =>
    {
        if (estado.PedidoAbierto)
            throw new InvalidOperationException("Guarda y cierra el pedido abierto, o anula su preparación, antes de cargar otro Excel.");
        var inventario = solicitud.Inventario;
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
        if (solicitud.Fecha > hoy || solicitud.Fecha.Year < 2000 || (estado.Corte is not null && solicitud.Fecha <= estado.Corte.Fecha))
            throw new InvalidOperationException("La fecha del corte debe ser posterior a la última carga y no futura.");
        if (inventario.Productos.Count == 0 || inventario.Productos.Count > 100000 || inventario.Productos.Any(p => string.IsNullOrWhiteSpace(p.Codigo) || p.Codigo.Length > 150)
            || inventario.Productos.Select(p => p.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inventario.Productos.Count)
            throw new InvalidOperationException("El inventario debe tener códigos únicos y columnas de bodegas.");
        var bodegas = inventario.BodegasDetectadas.Concat(inventario.Productos.SelectMany(p => p.Existencias.Keys)).Distinct().ToList();
        if (bodegas.Count == 0 || bodegas.Any(b => !ConfiguracionBodegas.EsCodigoValido(b)))
            throw new InvalidOperationException("El archivo debe contener bodegas con códigos como B1, B4 o B40.");
        inventario.BodegasDetectadas = bodegas;
        await InsertarCorte(db, solicitud.Fecha, inventario, usuario);
        foreach (var item in estado.Borrador) item.RequiereRevision = true;
        await GuardarLineas(db, estado.Borrador);
    });
    public Task<long> Generar(GenerarPropuesta solicitud, string usuario) => Mutar(solicitud.Revision, usuario, "Generar borrador sugerido", async (db, estado) =>
    {
        ExigirPedidoAbierto(estado);
        if (string.IsNullOrWhiteSpace(estado.Configuracion.BodegaLocal) || !estado.Configuracion.OrigenesHabilitados.Any())
            throw new InvalidOperationException("Configura tu sucursal y centros antes de generar un pedido.");
        var incluidos = estado.Borrador.Select(i => i.CodigoProducto).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in estado.Propuestas.Where(p => p.Rapido || (solicitud.IncluirNuevos && p.Tipo == "Nuevo")))
            if (incluidos.Add(p.Codigo))
                estado.Borrador.Add(new() { CodigoProducto = p.Codigo, DescripcionProducto = p.Descripcion, BodegaOrigen = p.Origen, CantidadSolicitada = p.Cantidad, ReservaCentro = 1 });
        await GuardarLineas(db, estado.Borrador);
    }, recomendar: true, recalcular: false);
    public Task<long> Realizar(long revision, string usuario) => Mutar(revision, usuario, "Registrar pedido realizado", async (db, estado) =>
    {
        ExigirPedidoAbierto(estado);
        if (estado.Borrador.Count == 0) throw new InvalidOperationException("El pedido está vacío.");
        ValidarLineas(estado.Borrador, estado, false);
        var pedidoId = await InsertarPedido(db, new(0, DateTime.UtcNow, usuario, "Guardado", estado.Borrador.Count, estado.Corte!.Id, estado.Configuracion.BodegaLocal), estado.Borrador);
        await RegistrarDecisiones(db, estado, pedidoId);
        await GuardarLineas(db, []);
        await CambiarCiclo(db, estado.Corte!, "Cerrado");
    });
    public Task<long> CambiarEstado(long id, CambiarEstadoPedido solicitud, string usuario) => Mutar(solicitud.Revision, usuario, $"Pedido {id}: {solicitud.Estado}", async (db, estado) =>
    {
        if (solicitud.Estado != "Cancelado")
            throw new InvalidOperationException("Solo se permite anular un pedido guardado.");
        var resumen = await ResumenPedido(db, id);
        if (resumen is null || resumen.Estado == "Cancelado") throw new InvalidOperationException("El pedido no existe o ya fue anulado.");
        var contenido = cifrado.Cifrar(resumen with { Estado = solicitud.Estado }, $"pedidos/{id}/resumen");
        await QuitarDecisionesPedido(db, resumen.Destino, id);
        var indice = cifrado.Indice("estado", solicitud.Estado);
        await db.Pedidos.Where(p => p.Id == id).ExecuteUpdateAsync(p => p.SetProperty(x => x.ResumenCifrado, contenido)
            .SetProperty(x => x.EstadoIndice, indice).SetProperty(x => x.CerradoCorteId, estado.Corte!.Id));
        if (estado.Corte is not null && resumen.CorteId == estado.Corte.Id && estado.Corte.Estado == "Cerrado")
        {
            var existentes = estado.Borrador.Select(i => i.CodigoProducto).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in await LeerLineas(db, id))
                if (existentes.Add(item.CodigoProducto)) estado.Borrador.Add(item);
            await GuardarLineas(db, estado.Borrador);
            await CambiarCiclo(db, estado.Corte, "Abierto");
        }
    });
    public async Task<PedidoDetalle> Detalle(long id)
    {
        await using var db = await Abrir();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var resumen = await ResumenPedido(db, id) ?? throw new InvalidOperationException("Pedido no encontrado.");
        var items = await LeerLineas(db, id);
        await tx.CommitAsync();
        return new(resumen, items);
    }
    private static void ValidarLineas(List<ItemPedido> items, EstadoTrabajo estado, bool permitirConservadas)
    {
        if (items.Count > 100000 || items.Select(i => i.CodigoProducto).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new InvalidOperationException("El pedido contiene códigos repetidos.");
        var productos = estado.Inventario?.Productos.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase) ?? new();
        var anteriores = estado.Borrador.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.CodigoProducto) || i.Comentario.Length > 300) throw new InvalidOperationException("Código o comentario inválido.");
            var anterior = anteriores.GetValueOrDefault(i.CodigoProducto);
            if (i.CantidadSolicitada <= 0 || i.ReservaCentro < 0)
                throw new InvalidOperationException("La cantidad debe ser positiva y la reserva no negativa.");
            if (!estado.PedidoAbierto && !i.RequiereRevision)
                throw new InvalidOperationException("El pedido ya fue cerrado. Las nuevas anotaciones deben quedar pendientes de revisión.");
            if (i.RequiereRevision)
            {
                if (!permitirConservadas) throw new InvalidOperationException($"Confirma el origen y la cantidad de {i.CodigoProducto} antes de cerrar el pedido.");
                if (!productos.ContainsKey(i.CodigoProducto) && anterior is null)
                    throw new InvalidOperationException("El producto no pertenece al inventario ni al borrador existente.");
                if (i.BodegaOrigen.Length > 0 && !estado.Configuracion.NombresBodegas.ContainsKey(i.BodegaOrigen))
                    throw new InvalidOperationException("La bodega no está registrada.");
                if (productos.TryGetValue(i.CodigoProducto, out var ficha)) i.DescripcionProducto = ficha.Descripcion;
                continue;
            }
            // Una importación conserva líneas ahora inválidas para que el usuario las corrija, sin borrarlas al guardar otras.
            if (permitirConservadas && anterior is not null && !anterior.RequiereRevision && anterior.BodegaOrigen == i.BodegaOrigen && anterior.CantidadSolicitada == i.CantidadSolicitada && anterior.ReservaCentro == i.ReservaCentro) continue;
            if (!productos.TryGetValue(i.CodigoProducto, out var p) || i.ReservaCentro < 0 || i.CantidadSolicitada <= 0
                || !estado.Configuracion.OrigenesHabilitados.Contains(i.BodegaOrigen) || i.CantidadSolicitada > ReglasAbastecimiento.Disponible(p, i.BodegaOrigen, i.ReservaCentro))
                throw new InvalidOperationException($"Revisa {i.CodigoProducto}: origen o cantidad no disponible en el inventario actual.");
            i.DescripcionProducto = p.Descripcion;
        }
    }
}
