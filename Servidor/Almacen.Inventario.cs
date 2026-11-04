using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NpgsqlTypes;
using PedidoMercaderia.Models;
using Servidor.DatosModelo;
using System.Globalization;

namespace Servidor;

public sealed partial class Almacen
{
    private sealed record InventariosComparables(ResultadoImportacion Actual, ResultadoImportacion? Anterior);

    // Dominio criptográfico histórico: se conserva para poder trasladar los datos cifrados sin cambiar su clave.
    private static string ContextoFicha(long carga, string producto) => $"productos_por_corte/{carga}/{producto}";

    private async Task<InventariosComparables> CargarInventarioActual(PedidosDbContext db, long actual, long? anterior)
    {
        if (cortesCache.TryGetValue(actual, out InventariosComparables? cache)) return cache!;
        var cargas = await db.Cortes.AsNoTracking().Where(c => c.Id == actual || c.Id == anterior).ToListAsync();
        ResultadoImportacion LeerCarga(long id) => cifrado.Leer<ResultadoImportacion>(cargas.Single(c => c.Id == id).MetadataCifrada, $"cortes/{id}/metadata");
        var ahora = LeerCarga(actual);
        var antes = anterior.HasValue ? LeerCarga(anterior.Value) : null;
        var fichas = await db.Productos.AsNoTracking().Where(p => p.UltimoCorteId == actual
            || (anterior.HasValue && (p.UltimoCorteId == anterior || p.AnteriorCorteId == anterior))).ToListAsync();
        var presentes = new Dictionary<string, ProductoInventario>();
        var previos = new Dictionary<string, ProductoInventario>();
        foreach (var p in fichas)
        {
            ProductoInventario LeerFicha()
            {
                if (p.AtributosCifrados is null || p.UltimoCorteId is null)
                    throw new InvalidOperationException("Falta consolidar el catálogo. Revisa los pasos de la migración InventarioActual.");
                var ficha = cifrado.Leer<ProductoInventario>(p.AtributosCifrados, ContextoFicha(p.UltimoCorteId.Value, p.Id));
                ficha.Existencias.Clear();
                return ficha;
            }
            if (p.UltimoCorteId == actual) presentes.Add(p.Id, LeerFicha());
            if (anterior.HasValue && (p.UltimoCorteId == anterior || p.AnteriorCorteId == anterior)) previos.Add(p.Id, LeerFicha());
        }
        if (presentes.Count == 0 && cifrado.Leer<CorteResumen>(cargas.Single(c => c.Id == actual).ResumenCifrado, $"cortes/{actual}/resumen").Productos > 0)
            throw new InvalidOperationException("El catálogo no se ha consolidado. Completa la migración InventarioActual antes de iniciar.");
        var bodegas = await Bodegas(db);
        await foreach (var e in db.Existencias.AsNoTracking().AsAsyncEnumerable())
        {
            if (e.CorteId == actual && presentes.TryGetValue(e.ProductoId, out var p))
                p.Existencias[bodegas[e.BodegaId]] = cifrado.Leer<decimal>(e.CantidadCifrada, $"existencias/{actual}/{e.ProductoId}/{e.BodegaId}");
            if (anterior.HasValue && previos.TryGetValue(e.ProductoId, out var previo))
            {
                var bytes = e.CorteId == anterior ? e.CantidadCifrada : e.CorteAnteriorId == anterior ? e.CantidadAnteriorCifrada : null;
                if (bytes is not null) previo.Existencias[bodegas[e.BodegaId]] = cifrado.Leer<decimal>(bytes, $"existencias/{anterior}/{e.ProductoId}/{e.BodegaId}");
            }
        }
        void Completar(ResultadoImportacion resultado, Dictionary<string, ProductoInventario> productos)
        {
            foreach (var p in productos.Values)
                foreach (var b in resultado.BodegasDetectadas) p.Existencias.TryAdd(b, 0);
            resultado.Productos = productos.Values.OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
        }
        Completar(ahora, presentes);
        if (antes is not null) Completar(antes, previos);
        var resultado = new InventariosComparables(ahora, antes);
        cortesCache.Set(actual, resultado, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(20) });
        return resultado;
    }

    private async Task InsertarCorte(PedidosDbContext db, DateOnly fecha, ResultadoImportacion inventario, string usuario)
    {
        var anterior = await db.Cortes.MaxAsync(c => (long?)c.Id);
        long id = await SiguienteId(db, "cortes");
        var metadata = new ResultadoImportacion { NombreArchivo = inventario.NombreArchivo, NombreHoja = inventario.NombreHoja,
            FilasLeidas = inventario.FilasLeidas, FilasOmitidas = inventario.FilasOmitidas, DiferenciasTotales = inventario.DiferenciasTotales,
            Advertencias = inventario.Advertencias, BodegasDetectadas = inventario.BodegasDetectadas };
        db.Cortes.Add(new() { Id = id, FechaIndice = cifrado.Indice("fecha", fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), AutorId = UsuarioId(usuario),
            ResumenCifrado = cifrado.Cifrar(new CorteResumen(id, fecha, Path.GetFileName(inventario.NombreArchivo), usuario, inventario.Productos.Count), $"cortes/{id}/resumen"),
            MetadataCifrada = cifrado.Cifrar(metadata, $"cortes/{id}/metadata") });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        foreach (var b in inventario.BodegasDetectadas) await AsegurarBodega(db, b);
        await Ejecutar(db, "UPDATE productos SET anterior_corte_id=CASE WHEN ultimo_corte_id=@a THEN ultimo_corte_id END", ("@a", anterior ?? 0));
        await Ejecutar(db, """
            UPDATE existencias SET
                corte_anterior_id=CASE WHEN corte_id=@a THEN corte_id END,
                cantidad_anterior_cifrada=CASE WHEN corte_id=@a THEN cantidad_cifrada END
            """, ("@a", anterior ?? 0));
        await Ejecutar(db, "CREATE TEMP TABLE importar_productos(id text,codigo bytea,descripcion bytea,atributos bytea) ON COMMIT DROP");
        await using (var copia = await Conexion(db).BeginBinaryImportAsync("COPY importar_productos FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var p in inventario.Productos)
            {
                var pid = ProductoId(p.Codigo);
                var ficha = p.CopiarSinExistencias();
                await copia.StartRowAsync(); await copia.WriteAsync(pid, NpgsqlDbType.Text);
                await copia.WriteAsync(cifrado.Cifrar(p.Codigo, $"productos/{pid}/codigo"), NpgsqlDbType.Bytea);
                await copia.WriteAsync(cifrado.Cifrar(p.Descripcion, $"productos/{pid}/descripcion"), NpgsqlDbType.Bytea);
                await copia.WriteAsync(cifrado.Cifrar(ficha, ContextoFicha(id, pid)), NpgsqlDbType.Bytea);
            }
            await copia.CompleteAsync();
        }
        await Ejecutar(db, """
            INSERT INTO productos(id,codigo_cifrado,descripcion_cifrada,primer_corte_id,ultimo_corte_id,atributos_cifrados)
            SELECT id,codigo,descripcion,@c,@c,atributos FROM importar_productos
            ON CONFLICT(id) DO UPDATE SET descripcion_cifrada=EXCLUDED.descripcion_cifrada,
                ultimo_corte_id=EXCLUDED.ultimo_corte_id,atributos_cifrados=EXCLUDED.atributos_cifrados
            """, ("@c", id));
        await Ejecutar(db, "CREATE TEMP TABLE importar_existencias(producto_id text,bodega_id text,cantidad bytea) ON COMMIT DROP");
        await using (var copia = await Conexion(db).BeginBinaryImportAsync("COPY importar_existencias FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var p in inventario.Productos)
            foreach (var (bodega, cantidad) in p.Existencias)
            {
                var pid = ProductoId(p.Codigo); var bid = BodegaId(bodega);
                await copia.StartRowAsync(); await copia.WriteAsync(pid, NpgsqlDbType.Text); await copia.WriteAsync(bid, NpgsqlDbType.Text);
                await copia.WriteAsync(cifrado.Cifrar(cantidad, $"existencias/{id}/{pid}/{bid}"), NpgsqlDbType.Bytea);
            }
            await copia.CompleteAsync();
        }
        await Ejecutar(db, """
            INSERT INTO existencias(producto_id,bodega_id,corte_id,cantidad_cifrada)
            SELECT producto_id,bodega_id,@c,cantidad FROM importar_existencias
            ON CONFLICT(producto_id,bodega_id) DO UPDATE SET corte_id=EXCLUDED.corte_id,cantidad_cifrada=EXCLUDED.cantidad_cifrada;
            DELETE FROM existencias WHERE corte_id<>@c AND corte_id<>@a;
            DROP TABLE importar_existencias;
            DROP TABLE importar_productos;
            """, ("@c", id), ("@a", anterior ?? 0));
    }
}
