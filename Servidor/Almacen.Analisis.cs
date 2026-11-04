using System.Data;
using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using Servidor.DatosModelo;

namespace Servidor;

public sealed partial class Almacen
{
    private async Task<List<ProductoFrecuente>> Frecuentes(PedidosDbContext db, EstadoTrabajo estado, ISet<string> pendientes)
    {
        var hasta = DateTime.UtcNow;
        var desde = hasta.AddDays(-estado.DiasHistorial);
        var pedidos = estado.Historial.Where(p => p.Destino == estado.Configuracion.BodegaLocal && p.Estado != "Cancelado" && p.Fecha >= desde && p.Fecha <= hasta).ToDictionary(p => p.Id);
        if (pedidos.Count == 0) return [];
        var ids = pedidos.Keys.ToArray();
        // Un solo SELECT para todas las líneas del período; los importes se descifran
        // después de leerlos. No se hacen SUM ni comparaciones sobre el ciphertext.
        var filas = await (from d in db.Detalles.AsNoTracking()
            join p in db.Productos.AsNoTracking() on d.ProductoId equals p.Id
            where ids.Contains(d.PedidoId)
            select new { d.PedidoId, d.ProductoId, d.OrigenId, d.CantidadCifrada, d.DescripcionCifrada, p.CodigoCifrado }).ToListAsync();
        var actuales = estado.Inventario!.Productos.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        return filas.GroupBy(d => d.ProductoId).Select(grupo =>
        {
            var ultimo = grupo.OrderByDescending(d => pedidos[d.PedidoId].Fecha).ThenByDescending(d => d.PedidoId).First();
            var codigo = cifrado.Leer<string>(ultimo.CodigoCifrado, $"productos/{ultimo.ProductoId}/codigo");
            var contexto = $"pedido_detalles/{ultimo.PedidoId}/{ultimo.ProductoId}/{ultimo.OrigenId}";
            var cantidad = grupo.Sum(d => cifrado.Leer<decimal>(d.CantidadCifrada, $"pedido_detalles/{d.PedidoId}/{d.ProductoId}/{d.OrigenId}/cantidad"));
            actuales.TryGetValue(codigo, out var p);
            return new ProductoFrecuente(codigo, p?.Descripcion ?? cifrado.Leer<string>(ultimo.DescripcionCifrada, contexto + "/descripcion"),
                grupo.Select(d => d.PedidoId).Distinct().Count(), cantidad, pedidos[ultimo.PedidoId].Fecha,
                p?.Existencias.ContainsKey(estado.Configuracion.BodegaLocal) == true ? p.ObtenerStock(estado.Configuracion.BodegaLocal) : null,
                pendientes.Contains(codigo), estado.Configuracion.ProductosNoSugerir.Contains(codigo));
        }).OrderByDescending(p => p.Pedidos).ThenByDescending(p => p.UltimoPedido).ThenBy(p => p.Codigo).ToList();
    }

    public async Task<PaginaHistorial> Historial(string? codigo = null, string? estado = null, DateOnly? desde = null, DateOnly? hasta = null, int pagina = 1)
    {
        if (desde.HasValue && hasta.HasValue && desde > hasta) throw new InvalidOperationException("La fecha inicial debe ser anterior a la final.");
        if (!string.IsNullOrEmpty(estado) && estado is not ("Guardado" or "Pendiente" or "Recibido" or "Cancelado")) throw new InvalidOperationException("Estado de pedido inválido.");
        if (codigo?.Length > 150) throw new InvalidOperationException("El código es demasiado largo.");
        await using var db = await Abrir();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var consulta = db.Pedidos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(codigo))
        {
            var producto = ProductoId(codigo);
            consulta = consulta.Where(p => db.Detalles.Any(d => d.PedidoId == p.Id && d.ProductoId == producto));
        }
        if (!string.IsNullOrEmpty(estado))
        {
            var indice = cifrado.Indice("estado", estado);
            consulta = estado == "Guardado"
                ? consulta.Where(p => p.EstadoIndice != cifrado.Indice("estado", "Cancelado"))
                : consulta.Where(p => p.EstadoIndice == indice);
        }
        var datos = await consulta.OrderByDescending(p => p.Id).Select(p => new { p.Id, p.ResumenCifrado }).ToListAsync();
        // Las fechas están cifradas. Se filtran los encabezados ya descifrados; las
        // líneas y comentarios solo se consultan al abrir el detalle de un pedido.
        var pedidos = datos.Select(p => cifrado.Leer<PedidoResumen>(p.ResumenCifrado, $"pedidos/{p.Id}/resumen"))
            .Where(p => (!desde.HasValue || DateOnly.FromDateTime(p.Fecha.AddHours(-5)) >= desde.Value)
                && (!hasta.HasValue || DateOnly.FromDateTime(p.Fecha.AddHours(-5)) <= hasta.Value)).ToList();
        const int tamano = 20;
        pagina = Math.Clamp(pagina, 1, Math.Max(1, (pedidos.Count + tamano - 1) / tamano));
        await tx.CommitAsync();
        return new(pedidos.Skip((pagina - 1) * tamano).Take(tamano).ToList(), pedidos.Count, pagina, tamano);
    }
}
