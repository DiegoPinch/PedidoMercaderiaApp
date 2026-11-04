using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor.DatosModelo;
using NpgsqlTypes;

namespace Servidor;

public sealed partial class Almacen
{
    private static string ContextoPerfil(string bodega, string producto) => $"perfiles_reposicion/{bodega}/{producto}";

    // Se ejecuta dentro de la transacción que modifica los datos, nunca en cada búsqueda o guardado de una fila.
    private async Task ActualizarAnalitica(PedidosDbContext db)
    {
        var estado = await Estado(db, false);
        await ObservarSucursal(db, estado);
        var historial = await db.Pedidos.AsNoTracking().Select(p => new { p.Id, p.ResumenCifrado }).ToListAsync();
        estado.Historial = historial.Select(p => cifrado.Leer<PedidoResumen>(p.ResumenCifrado, $"pedidos/{p.Id}/resumen")).ToList();
        var analitica = await CalcularAnalitica(db, estado);
        var fila = await db.Analiticas.SingleOrDefaultAsync(a => a.TrabajoId == 1);
        if (fila is null) { fila = new() { TrabajoId = 1 }; db.Analiticas.Add(fila); }
        fila.ContenidoCifrado = cifrado.Cifrar(analitica, "analitica_trabajo/1");
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    private async Task<AnaliticaCalculada> LeerAnalitica(PedidosDbContext db, EstadoTrabajo estado)
    {
        var datos = await db.Analiticas.AsNoTracking().Where(a => a.TrabajoId == 1).Select(a => a.ContenidoCifrado).SingleOrDefaultAsync();
        if (datos is not null)
        {
            var analitica = cifrado.Leer<AnaliticaCalculada>(datos, "analitica_trabajo/1");
            if (analitica.Version == AnaliticaCalculada.VersionActual && analitica.CorteId == estado.Corte?.Id) return analitica;
        }
        return await CalcularAnalitica(db, estado);
    }

    private async Task ObservarSucursal(PedidosDbContext db, EstadoTrabajo estado)
    {
        if (estado.Corte is null || estado.Inventario is null || !ConfiguracionBodegas.EsCodigoValido(estado.Configuracion.BodegaLocal)) return;
        var local = estado.Configuracion.BodegaLocal;
        var bid = BodegaId(local);
        var filas = await db.PerfilesReposicion.AsNoTracking().Where(p => p.BodegaId == bid).ToDictionaryAsync(p => p.ProductoId);
        var cambios = new List<(string Producto, byte[] Contenido)>();
        var actuales = estado.Inventario.Productos.ToDictionary(p => ProductoId(p.Codigo));
        var anterior = estado.Cortes.ElementAtOrDefault(1);
        var comparables = await CargarInventarioActual(db, estado.Corte.Id, anterior?.Id);
        var previos = comparables.Anterior?.Productos.ToDictionary(p => ProductoId(p.Codigo)) ?? new();
        foreach (var pid in actuales.Keys.Union(filas.Keys))
        {
            if (!filas.TryGetValue(pid, out var fila))
            { fila = new() { BodegaId = bid, ProductoId = pid }; }
            var perfil = fila.ContenidoCifrado.Length == 0 ? new PerfilReposicion() : cifrado.Leer<PerfilReposicion>(fila.ContenidoCifrado, ContextoPerfil(bid, pid));
            if (perfil.Observaciones.Any(o => o.CorteId == estado.Corte.Id)) continue;
            decimal? Stock(Dictionary<string, ProductoInventario> productos) => productos.TryGetValue(pid, out var p)
                && p.Existencias.ContainsKey(local) ? p.ObtenerStock(local) : null;
            if (anterior is not null && !perfil.Observaciones.Any(o => o.CorteId == anterior.Id))
                perfil.Observar(new(anterior.Id, anterior.Fecha, Stock(previos)));
            perfil.Observar(new(estado.Corte.Id, estado.Corte.Fecha, Stock(actuales)));
            cambios.Add((pid, cifrado.Cifrar(perfil, ContextoPerfil(bid, pid))));
        }
        if (cambios.Count == 0) return;
        await Ejecutar(db, "CREATE TEMP TABLE importar_perfiles(producto_id text,contenido bytea) ON COMMIT DROP");
        await using (var copia = await Conexion(db).BeginBinaryImportAsync("COPY importar_perfiles FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var cambio in cambios)
            {
                await copia.StartRowAsync(); await copia.WriteAsync(cambio.Producto, NpgsqlDbType.Text);
                await copia.WriteAsync(cambio.Contenido, NpgsqlDbType.Bytea);
            }
            await copia.CompleteAsync();
        }
        await Ejecutar(db, """
            INSERT INTO perfiles_reposicion(bodega_id,producto_id,contenido_cifrado)
            SELECT @b,producto_id,contenido FROM importar_perfiles
            ON CONFLICT(bodega_id,producto_id) DO UPDATE SET contenido_cifrado=EXCLUDED.contenido_cifrado;
            DROP TABLE importar_perfiles;
            """, ("@b", bid));
    }

    private async Task<AnaliticaCalculada> CalcularAnalitica(PedidosDbContext db, EstadoTrabajo estado)
    {
        var resultado = new AnaliticaCalculada { CorteId = estado.Corte?.Id,
            Aprendizaje = new() { CalculadoUtc = DateTime.UtcNow, NoSeVenden = estado.Configuracion.ProductosNoSugerir.Count } };
        if (estado.Corte is null || estado.Inventario is null) return resultado;
        var inventarios = await CargarInventarioActual(db, estado.Corte.Id, estado.Cortes.ElementAtOrDefault(1)?.Id);
        var vistos = await db.Productos.AsNoTracking().Where(p => p.PrimerCorteId < estado.Corte.Id).Select(p => new { p.Id, p.CodigoCifrado }).ToListAsync();
        var conocidos = vistos.Select(p => cifrado.Leer<string>(p.CodigoCifrado, $"productos/{p.Id}/codigo")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cancelado = cifrado.Indice("estado", "Cancelado");
        var destino = BodegaId(estado.Configuracion.BodegaLocal);
        var pendientesQuery = from p in db.Pedidos.AsNoTracking()
            join d in db.Detalles.AsNoTracking() on p.Id equals d.PedidoId
            join pr in db.Productos.AsNoTracking() on d.ProductoId equals pr.Id
            where p.DestinoId == destino && p.CorteId == estado.Corte.Id && p.EstadoIndice != cancelado
            select new { pr.Id, pr.CodigoCifrado };
        var pendientes = (await pendientesQuery.Distinct().ToListAsync()).Select(p => cifrado.Leer<string>(p.CodigoCifrado, $"productos/{p.Id}/codigo")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        resultado.Frecuentes = await Frecuentes(db, estado, pendientes);
        resultado.Cambios = RecomendacionService.Comparar(estado.Inventario.Productos, inventarios.Anterior?.Productos, conocidos, estado.Configuracion);
        var filas = await db.PerfilesReposicion.AsNoTracking().Where(p => p.BodegaId == destino).ToListAsync();
        var perfiles = filas.ToDictionary(p => p.ProductoId, p => cifrado.Leer<PerfilReposicion>(p.ContenidoCifrado, ContextoPerfil(destino, p.ProductoId)));
        foreach (var decision in perfiles.Values.SelectMany(p => p.Decisiones))
        {
            if (decision.Tipo == "Aceptada") resultado.Aprendizaje.Aceptadas++;
            if (decision.Tipo == "Ajustada") resultado.Aprendizaje.Ajustadas++;
            if (decision.Tipo == "Excepcional") resultado.Aprendizaje.Excepcionales++;
        }
        if (!estado.PedidoAbierto) return resultado;
        var catalogo = estado.Inventario.Productos.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        foreach (var propuesta in RecomendacionService.Crear(estado.Inventario.Productos, inventarios.Anterior?.Productos,
            null, conocidos, pendientes, estado.Configuracion, resultado.Frecuentes))
        {
            var perfil = perfiles.GetValueOrDefault(ProductoId(propuesta.Codigo));
            var ajustada = perfil is null ? propuesta : AnalisisReposicionService.Ajustar(propuesta, perfil, estado.Configuracion.Reposicion,
                ReglasAbastecimiento.Disponible(catalogo[propuesta.Codigo], propuesta.Origen, 1));
            if (ajustada.BajadaDiaria.HasValue) resultado.Aprendizaje.ProductosConTendencia++;
            resultado.Propuestas.Add(ajustada);
        }
        return resultado;
    }

    private async Task RegistrarDecisiones(PedidosDbContext db, EstadoTrabajo estado, long pedidoId)
    {
        var analitica = await LeerAnalitica(db, estado);
        var sugeridas = analitica.Propuestas.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        var bid = BodegaId(estado.Configuracion.BodegaLocal);
        var ids = estado.Borrador.Select(i => ProductoId(i.CodigoProducto)).ToArray();
        var perfiles = await db.PerfilesReposicion.Where(p => p.BodegaId == bid && ids.Contains(p.ProductoId)).ToDictionaryAsync(p => p.ProductoId);
        foreach (var item in estado.Borrador)
        {
            if (!perfiles.TryGetValue(ProductoId(item.CodigoProducto), out var fila)) continue;
            var propuesta = sugeridas.GetValueOrDefault(item.CodigoProducto);
            if (!item.CompraExcepcional && (propuesta is null || propuesta.Cantidad <= 0 || !propuesta.Rapido)) continue;
            var tipo = item.CompraExcepcional ? "Excepcional" : item.CantidadSolicitada == propuesta!.Cantidad ? "Aceptada" : "Ajustada";
            var perfil = cifrado.Leer<PerfilReposicion>(fila.ContenidoCifrado, ContextoPerfil(bid, fila.ProductoId));
            perfil.Registrar(new(estado.Corte!.Id, pedidoId, tipo, propuesta?.Cantidad ?? 0, item.CantidadSolicitada, DateTime.UtcNow));
            fila.ContenidoCifrado = cifrado.Cifrar(perfil, ContextoPerfil(bid, fila.ProductoId));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    private async Task RegistrarExclusiones(PedidosDbContext db, EstadoTrabajo estado, ConfiguracionBodegas nueva)
    {
        if (estado.Corte is null || nueva.BodegaLocal != estado.Configuracion.BodegaLocal) return;
        var ids = nueva.ProductosNoSugerir.Except(estado.Configuracion.ProductosNoSugerir, StringComparer.OrdinalIgnoreCase).Select(ProductoId).ToArray();
        if (ids.Length == 0) return;
        var bid = BodegaId(nueva.BodegaLocal);
        foreach (var fila in await db.PerfilesReposicion.Where(p => p.BodegaId == bid && ids.Contains(p.ProductoId)).ToListAsync())
        {
            var perfil = cifrado.Leer<PerfilReposicion>(fila.ContenidoCifrado, ContextoPerfil(bid, fila.ProductoId));
            perfil.Registrar(new(estado.Corte.Id, null, "NoSeVende", 0, 0, DateTime.UtcNow));
            fila.ContenidoCifrado = cifrado.Cifrar(perfil, ContextoPerfil(bid, fila.ProductoId));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    private async Task QuitarDecisionesPedido(PedidosDbContext db, string sucursal, long pedidoId)
    {
        var bid = BodegaId(sucursal);
        var ids = await db.Detalles.Where(d => d.PedidoId == pedidoId).Select(d => d.ProductoId).ToArrayAsync();
        foreach (var fila in await db.PerfilesReposicion.Where(p => p.BodegaId == bid && ids.Contains(p.ProductoId)).ToListAsync())
        {
            var perfil = cifrado.Leer<PerfilReposicion>(fila.ContenidoCifrado, ContextoPerfil(bid, fila.ProductoId));
            if (perfil.Decisiones.RemoveAll(d => d.PedidoId == pedidoId) > 0)
                fila.ContenidoCifrado = cifrado.Cifrar(perfil, ContextoPerfil(bid, fila.ProductoId));
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
}
