using PedidoMercaderia.Models;

namespace Servidor.Configuracion;

/// <summary>Reduce la respuesta de lectura sin modificar el inventario usado por las reglas del servidor.</summary>
public static class TransferenciaTrabajo
{
    public static void Preparar(EstadoTrabajo estado, long? inventarioConocido, bool compacto)
    {
        if (estado.Inventario is null) return;
        if (inventarioConocido.HasValue && estado.Corte?.Id == inventarioConocido)
        {
            estado.Inventario = null;
            estado.InventarioSinCambios = true;
            return;
        }
        if (!compacto) return;
        var original = estado.Inventario;
        estado.Inventario = new ResultadoImportacion
        {
            NombreArchivo = original.NombreArchivo, NombreHoja = original.NombreHoja,
            FilasLeidas = original.FilasLeidas, FilasOmitidas = original.FilasOmitidas,
            DiferenciasTotales = original.DiferenciasTotales, Advertencias = original.Advertencias,
            BodegasDetectadas = original.BodegasDetectadas,
            Productos = original.Productos.Select(p =>
            {
                var copia = p.CopiarSinExistencias();
                foreach (var (bodega, cantidad) in p.Existencias)
                    if (cantidad != 0) copia.Existencias.Add(bodega, cantidad);
                return copia;
            }).ToList()
        };
    }
}
