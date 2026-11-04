using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Servidor.DatosModelo;
using System.Data;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

namespace Servidor;

public sealed partial class Almacen : IDisposable
{
    private readonly IDbContextFactory<PedidosDbContext> fabrica;
    private readonly CifradoDatos cifrado;
    private readonly MemoryCache cortesCache = new(new MemoryCacheOptions { SizeLimit = 1 });
    public Almacen(IDbContextFactory<PedidosDbContext> fabrica, CifradoDatos cifrado) { this.fabrica = fabrica; this.cifrado = cifrado; }
    public void Dispose() => cortesCache.Dispose();
    private async Task<PedidosDbContext> Abrir()
    {
        var db = await fabrica.CreateDbContextAsync();
        try { await db.Database.OpenConnectionAsync(); return db; }
        catch { await db.DisposeAsync(); throw; }
    }
    private static NpgsqlConnection Conexion(PedidosDbContext db) => (NpgsqlConnection)db.Database.GetDbConnection();
    private static NpgsqlCommand Comando(PedidosDbContext db, string sql, params (string, object?)[] valores)
    {
        var c = Conexion(db).CreateCommand(); c.CommandText = sql;
        c.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var (k, v) in valores) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c;
    }
    private static async Task Ejecutar(PedidosDbContext db, string sql, params (string, object?)[] valores)
    { using var c = Comando(db, sql, valores); await c.ExecuteNonQueryAsync(); }
    private static Task BloquearTrabajo(PedidosDbContext db) => Ejecutar(db, "SELECT id FROM trabajo WHERE id=1 FOR UPDATE");
    private string ProductoId(string codigo) => cifrado.Indice("producto", codigo);
    private string BodegaId(string codigo) => cifrado.Indice("bodega", codigo);
    private string UsuarioId(string usuario) => cifrado.Indice("usuario", usuario);

    public async Task Inicializar()
    {
        await using var db = await Abrir();
        await using var tx = await db.Database.BeginTransactionAsync();
        await Ejecutar(db, "SELECT pg_advisory_xact_lock(718432, 1)");
        try
        {
            // Solo datos iniciales: el administrador gestiona el esquema con EF.
            var control = await db.Set<ControlCifrado>().SingleOrDefaultAsync(x => x.Id == 1);
            if (control is not null)
            {
                if (cifrado.Leer<string>(control.Verificador, "control/1") != "Pedidos/v2")
                    throw new InvalidOperationException("La clave de cifrado no corresponde a esta base.");
            }
            else
            {
                if (await db.Usuarios.AnyAsync() || await db.Trabajo.AnyAsync() || await db.Bodegas.AnyAsync())
                    throw new InvalidOperationException("Falta el verificador de una base con datos. Restaura el respaldo y su clave de cifrado.");
                db.Set<ControlCifrado>().Add(new() { Id = 1, Verificador = cifrado.Cifrar("Pedidos/v2", "control/1") });
            }
            if (!await db.Trabajo.AnyAsync(x => x.Id == 1))
                db.Trabajo.Add(new() { Id = 1, ConfiguracionCifrada = cifrado.Cifrar(new ConfiguracionBodegas { BodegaLocal = "", BodegasOrigen = [] }, "trabajo/1/configuracion") });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await BloquearTrabajo(db);
            await ActualizarAnalitica(db);
            await tx.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            throw new InvalidOperationException("Falta preparar el esquema de PostgreSQL. Genera y aplica tu migración de PedidosDbContext antes de iniciar. El servidor no aplica migraciones automáticamente.", ex);
        }
    }

    private async Task AsegurarBodega(PedidosDbContext db, string codigo)
    {
        var id = BodegaId(codigo);
        if (await db.Bodegas.AnyAsync(b => b.Id == id)) return;
        var nombre = codigo;
        db.Bodegas.Add(new() { Id = id, CodigoCifrado = cifrado.Cifrar(codigo, $"bodegas/{id}/codigo"), NombreCifrado = cifrado.Cifrar(nombre, $"bodegas/{id}/nombre") });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    private async Task<Dictionary<string, string>> Bodegas(PedidosDbContext db) =>
        (await db.Bodegas.AsNoTracking().ToListAsync()).ToDictionary(b => b.Id, b => cifrado.Leer<string>(b.CodigoCifrado, $"bodegas/{b.Id}/codigo"));
    private async Task InsertarUsuario(PedidosDbContext db, string usuario, string nombre, string hash, string rol)
    {
        var id = UsuarioId(usuario);
        db.Usuarios.Add(new() { Id = id, UsuarioCifrado = cifrado.Cifrar(usuario.Trim().ToLowerInvariant(), $"usuarios/{id}/usuario"),
            NombreCifrado = cifrado.Cifrar(nombre.Trim(), $"usuarios/{id}/nombre"), RolCifrado = cifrado.Cifrar(rol, $"usuarios/{id}/rol"), HashCifrado = cifrado.Cifrar(hash, $"usuarios/{id}/hash") });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    private UsuarioSesion LeerUsuario(Usuario u) => new(cifrado.Leer<string>(u.UsuarioCifrado, $"usuarios/{u.Id}/usuario"),
        cifrado.Leer<string>(u.NombreCifrado, $"usuarios/{u.Id}/nombre"), cifrado.Leer<string>(u.RolCifrado, $"usuarios/{u.Id}/rol"));
    public async Task<bool> SinUsuarios() { await using var db = await Abrir(); return !await db.Usuarios.AnyAsync(); }
    public async Task<(UsuarioSesion Usuario, string Hash)?> Usuario(string usuario)
    {
        await using var db = await Abrir(); var id = UsuarioId(usuario);
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return u is null ? null : (LeerUsuario(u), cifrado.Leer<string>(u.HashCifrado, $"usuarios/{id}/hash"));
    }
    public async Task CrearUsuario(string usuario, string nombre, string hash, bool primero)
    {
        await using var db = await Abrir(); await using var tx = await db.Database.BeginTransactionAsync();
        await BloquearTrabajo(db);
        if (primero && await db.Usuarios.AnyAsync()) throw new InvalidOperationException("La cuenta administradora ya fue creada.");
        await InsertarUsuario(db, usuario, nombre, hash, primero ? "Administrador" : "Colaborador");
        await tx.CommitAsync();
    }
    public async Task<bool> CrearAdministradorSiVacio(string usuario, string nombre, string hash)
    {
        await using var db = await Abrir();
        await using var tx = await db.Database.BeginTransactionAsync();
        await BloquearTrabajo(db);
        if (await db.Usuarios.AnyAsync()) return false;
        await InsertarUsuario(db, usuario, nombre, hash, "Administrador");
        await tx.CommitAsync();
        return true;
    }
    public async Task<List<UsuarioSesion>> Usuarios()
    {
        await using var db = await Abrir();
        return (await db.Usuarios.AsNoTracking().ToListAsync()).Select(LeerUsuario).OrderBy(u => u.Usuario).ToList();
    }

    private Task Auditar(PedidosDbContext db, string usuario, string accion) => GuardarEvento(db, usuario, DateTime.UtcNow, accion);
    private async Task GuardarEvento(PedidosDbContext db, string usuario, DateTime fecha, string accion)
    {
        var id = await SiguienteId(db, "auditoria");
        db.Auditoria.Add(new() { Id = id, UsuarioId = UsuarioId(usuario), EventoCifrado = cifrado.Cifrar(new { Fecha = fecha, Accion = accion }, $"auditoria/{id}/{UsuarioId(usuario)}/evento") });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    public async Task<EstadoTrabajo> Estado(long? inventarioConocido = null, bool compacto = false)
    {
        await using var db = await Abrir(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var estado = await Estado(db); await tx.CommitAsync();
        Configuracion.TransferenciaTrabajo.Preparar(estado, inventarioConocido, compacto);
        return estado;
    }
    private async Task<EstadoTrabajo> Estado(PedidosDbContext db, bool recomendar = true)
    {
        var trabajo = await db.Trabajo.AsNoTracking().SingleAsync(t => t.Id == 1);
        var estado = new EstadoTrabajo { Revision = trabajo.Revision,
            Configuracion = cifrado.Leer<ConfiguracionBodegas>(trabajo.ConfiguracionCifrada, "trabajo/1/configuracion"), Borrador = await LeerLineas(db) };
        estado.Configuracion.ProductosNoSugerir = new(estado.Configuracion.ProductosNoSugerir, StringComparer.OrdinalIgnoreCase);
        estado.Configuracion.NombresBodegas = (await db.Bodegas.AsNoTracking().ToListAsync()).ToDictionary(
            b => cifrado.Leer<string>(b.CodigoCifrado, $"bodegas/{b.Id}/codigo"),
            b => cifrado.Leer<string>(b.NombreCifrado, $"bodegas/{b.Id}/nombre"), StringComparer.OrdinalIgnoreCase);
        var cortesQuery = db.Cortes.AsNoTracking().OrderByDescending(c => c.Id).Select(c => new { c.Id, c.ResumenCifrado });
        var cortes = await (recomendar ? cortesQuery : cortesQuery.Take(2)).ToListAsync();
        estado.Cortes = cortes.Select(c => cifrado.Leer<CorteResumen>(c.ResumenCifrado, $"cortes/{c.Id}/resumen")).ToList();
        estado.Corte = estado.Cortes.FirstOrDefault();
        var inventarios = estado.Corte is null ? null : await CargarInventarioActual(db, estado.Corte.Id, estado.Cortes.ElementAtOrDefault(1)?.Id);
        estado.Inventario = inventarios?.Actual;
        if (estado.Corte is not null && estado.Corte.Estado == "Abierto")
        {
            var cancelado = cifrado.Indice("estado", "Cancelado");
            if (await db.Pedidos.AnyAsync(p => p.CorteId == estado.Corte.Id && p.EstadoIndice != cancelado))
            {
                estado.Corte = estado.Corte with { Estado = "Cerrado" };
                estado.Cortes[0] = estado.Corte;
            }
        }
        if (!recomendar) return estado;
        estado.Actividad = await LeerActividad(db, estado.Configuracion.BodegaLocal, estado.Inventario);
        var pedidos = await db.Pedidos.AsNoTracking().OrderByDescending(p => p.Id).Select(p => new { p.Id, p.ResumenCifrado }).ToListAsync();
        estado.Historial = pedidos.Select(p => cifrado.Leer<PedidoResumen>(p.ResumenCifrado, $"pedidos/{p.Id}/resumen")).ToList();
        if (estado.Inventario is null) return estado;
        var analitica = await LeerAnalitica(db, estado);
        estado.MasPedidos = analitica.Frecuentes;
        estado.Cambios = analitica.Cambios;
        estado.Propuestas = estado.PedidoAbierto ? analitica.Propuestas : [];
        estado.Aprendizaje = analitica.Aprendizaje;
        return estado;
    }
    private static async Task<long> SiguienteId(PedidosDbContext db, string tabla)
    {
        // Solo nombres constantes del código, nunca entradas del usuario.
        using var c = Comando(db, "SELECT nextval(pg_get_serial_sequence(@t,'id'))", ("@t", tabla));
        return (long)(await c.ExecuteScalarAsync())!;
    }
    private async Task<long> InsertarPedido(PedidosDbContext db, PedidoResumen resumen, List<ItemPedido> items)
    {
        long id = await SiguienteId(db, "pedidos");
        db.Pedidos.Add(new() { Id = id, AutorId = UsuarioId(resumen.Autor), DestinoId = BodegaId(resumen.Destino), CorteId = resumen.CorteId,
            ResumenCifrado = cifrado.Cifrar(resumen with { Id = id }, $"pedidos/{id}/resumen"), EstadoIndice = cifrado.Indice("estado", resumen.Estado) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await GuardarLineas(db, items, id);
        return id;
    }
    private async Task<PedidoResumen?> ResumenPedido(PedidosDbContext db, long id)
    {
        var bytes = await db.Pedidos.AsNoTracking().Where(p => p.Id == id).Select(p => p.ResumenCifrado).SingleOrDefaultAsync();
        return bytes is null ? null : cifrado.Leer<PedidoResumen>(bytes, $"pedidos/{id}/resumen");
    }
    private async Task GuardarLineas(PedidosDbContext db, List<ItemPedido> items, long? pedido = null, IReadOnlyList<ItemPedido>? anteriores = null)
    {
        if (!pedido.HasValue)
        {
            if (anteriores is null) await db.Borrador.Where(d => d.TrabajoId == 1).ExecuteDeleteAsync();
            else
            {
                var previos = anteriores.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
                var actuales = items.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
                var reemplazados = anteriores.Where(i => !ItemPedido.MismoContenido(i, actuales.GetValueOrDefault(i.CodigoProducto)))
                    .Select(i => ProductoId(i.CodigoProducto)).ToArray();
                if (reemplazados.Length > 0)
                    await db.Borrador.Where(d => d.TrabajoId == 1 && reemplazados.Contains(d.ProductoId)).ExecuteDeleteAsync();
                items = items.Where(i => !ItemPedido.MismoContenido(i, previos.GetValueOrDefault(i.CodigoProducto))).ToList();
            }
        }
        foreach (var i in items)
        {
            var p = ProductoId(i.CodigoProducto); var b = string.IsNullOrWhiteSpace(i.BodegaOrigen) ? null : BodegaId(i.BodegaOrigen);
            var contexto = $"{(pedido.HasValue ? "pedido_detalles" : "borrador_detalles")}/{pedido ?? 1}/{p}/{b}";
            var cantidad = cifrado.Cifrar(i.CantidadSolicitada, contexto + "/cantidad"); var reserva = cifrado.Cifrar(i.ReservaCentro, contexto + "/reserva");
            var descripcion = cifrado.Cifrar(i.DescripcionProducto, contexto + "/descripcion"); var comentario = cifrado.Cifrar(i.Comentario, contexto + "/comentario");
            var excepcional = cifrado.Cifrar(i.CompraExcepcional, contexto + "/excepcional");
            if (pedido.HasValue) db.Detalles.Add(new() { PedidoId = pedido.Value, ProductoId = p, OrigenId = b!, CantidadCifrada = cantidad, ReservaCifrada = reserva, DescripcionCifrada = descripcion, ComentarioCifrado = comentario, ExcepcionalCifrado = excepcional });
            else db.Borrador.Add(new() { TrabajoId = 1, ProductoId = p, OrigenId = b, RevisionCifrada = cifrado.Cifrar(i.RequiereRevision, contexto + "/revision"), CantidadCifrada = cantidad, ReservaCifrada = reserva, DescripcionCifrada = descripcion, ComentarioCifrado = comentario, ExcepcionalCifrado = excepcional });
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    private async Task<List<ItemPedido>> LeerLineas(PedidosDbContext db, long? pedido = null)
    {
        var bodegas = await Bodegas(db);
        var historicas = from d in db.Detalles.AsNoTracking() join p in db.Productos.AsNoTracking() on d.ProductoId equals p.Id
            where d.PedidoId == pedido select new { RevisionCifrada = (byte[]?)null, d.ExcepcionalCifrado, d.ProductoId, OrigenId = (string?)d.OrigenId, d.CantidadCifrada, d.ReservaCifrada, d.DescripcionCifrada, d.ComentarioCifrado, p.CodigoCifrado };
        var borrador = from d in db.Borrador.AsNoTracking() join p in db.Productos.AsNoTracking() on d.ProductoId equals p.Id
            where d.TrabajoId == 1 select new { d.RevisionCifrada, d.ExcepcionalCifrado, d.ProductoId, d.OrigenId, d.CantidadCifrada, d.ReservaCifrada, d.DescripcionCifrada, d.ComentarioCifrado, p.CodigoCifrado };
        var filas = await (pedido.HasValue ? historicas : borrador).ToListAsync();
        return filas.Select(d => {
            var contexto = $"{(pedido.HasValue ? "pedido_detalles" : "borrador_detalles")}/{pedido ?? 1}/{d.ProductoId}/{d.OrigenId}";
            return new ItemPedido { CodigoProducto = cifrado.Leer<string>(d.CodigoCifrado, $"productos/{d.ProductoId}/codigo"), BodegaOrigen = d.OrigenId is null ? "" : bodegas[d.OrigenId],
                RequiereRevision = d.RevisionCifrada is not null && cifrado.Leer<bool>(d.RevisionCifrada, contexto + "/revision"),
                CompraExcepcional = d.ExcepcionalCifrado is not null && cifrado.Leer<bool>(d.ExcepcionalCifrado, contexto + "/excepcional"),
                CantidadSolicitada = cifrado.Leer<decimal>(d.CantidadCifrada, contexto + "/cantidad"), ReservaCentro = cifrado.Leer<decimal>(d.ReservaCifrada, contexto + "/reserva"),
                DescripcionProducto = cifrado.Leer<string>(d.DescripcionCifrada, contexto + "/descripcion"), Comentario = cifrado.Leer<string>(d.ComentarioCifrado, contexto + "/comentario") };
        }).OrderBy(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
