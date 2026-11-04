using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using Servidor.DatosModelo;

namespace Servidor;

public sealed partial class Almacen
{
    private async Task<ReporteActividad?> LeerActividad(PedidosDbContext db, string bodega, ResultadoImportacion? inventario)
    {
        if (string.IsNullOrWhiteSpace(bodega)) return null;
        var id = BodegaId(bodega);
        var contenido = await db.ReportesActividad.AsNoTracking().Where(r => r.BodegaId == id)
            .Select(r => r.ContenidoCifrado).SingleOrDefaultAsync();
        if (contenido is null) return null;
        var reporte = cifrado.Leer<ReporteActividad>(contenido, $"reportes_actividad/{id}");
        reporte.Productos = new(reporte.Productos, StringComparer.OrdinalIgnoreCase);
        reporte.Coincidencias = inventario?.Productos.Count(p => reporte.Productos.ContainsKey(p.Codigo)) ?? 0;
        return reporte;
    }

    public Task<long> GuardarActividad(GuardarReporteActividad solicitud, string usuario) =>
        Mutar(solicitud.Revision, usuario, solicitud.Reporte is null ? "Quitar reporte de movimientos" : "Guardar reporte de movimientos", async (db, estado) =>
        {
            if (estado.Inventario is null || solicitud.CorteId != estado.Corte?.Id
                || string.IsNullOrWhiteSpace(solicitud.Bodega) || solicitud.Bodega != estado.Configuracion.BodegaLocal)
                throw new ConflictoException("El inventario o la sucursal cambió. Actualiza desde el equipo antes de cargar el reporte.");

            var id = BodegaId(solicitud.Bodega);
            var guardado = await db.ReportesActividad.SingleOrDefaultAsync(r => r.BodegaId == id);
            if (solicitud.Reporte is not ReporteActividad reporte)
            {
                if (guardado is not null) db.ReportesActividad.Remove(guardado);
            }
            else
            {
                if (reporte.FechaReporte.Year < 1900 || reporte.FechaReporte > DateOnly.FromDateTime(DateTime.UtcNow)
                    || string.IsNullOrWhiteSpace(reporte.NombreArchivo) || reporte.NombreArchivo.Length > 255
                    || reporte.NombreHoja is null || reporte.NombreHoja.Length > 255
                    || reporte.Productos is null || reporte.Productos.Count is 0 or > 100000
                    || reporte.FilasLeidas < reporte.Productos.Count
                    || reporte.Productos.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 200 || p.Key.Any(char.IsControl)
                        || p.Value is null || p.Key != p.Value.Codigo || p.Value.DiasSinActividad < 0
                        || p.Value.UltimoMovimiento > reporte.FechaReporte)
                    || reporte.Productos.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != reporte.Productos.Count)
                    throw new InvalidOperationException("El reporte de movimientos contiene datos inválidos.");
                var anterior = guardado is null ? null : cifrado.Leer<ReporteActividad>(guardado.ContenidoCifrado, $"reportes_actividad/{id}");
                if (anterior is not null && reporte.FechaReporte < anterior.FechaReporte)
                    throw new InvalidOperationException("El reporte guardado tiene una fecha más reciente. Carga uno de esa fecha o posterior.");
                reporte.Productos = new(reporte.Productos, StringComparer.OrdinalIgnoreCase);
                reporte.Coincidencias = estado.Inventario.Productos.Count(p => reporte.Productos.ContainsKey(p.Codigo));
                if (reporte.Coincidencias == 0)
                    throw new InvalidOperationException("Los códigos del reporte no coinciden con el inventario actual.");
                if (guardado is null)
                {
                    guardado = new() { BodegaId = id };
                    db.ReportesActividad.Add(guardado);
                }
                guardado.ContenidoCifrado = cifrado.Cifrar(reporte, $"reportes_actividad/{id}");
            }
            await db.SaveChangesAsync();
        });
}
