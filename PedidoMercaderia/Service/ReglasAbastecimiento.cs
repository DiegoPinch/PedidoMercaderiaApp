using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

public static class ReglasAbastecimiento
{
    public static decimal Disponible(ProductoInventario producto, string bodega, decimal reserva) =>
        Math.Max(0, producto.ObtenerStock(bodega) - Math.Max(0, reserva));

    public static decimal MejorDisponible(ProductoInventario producto,
        ConfiguracionBodegas configuracion, decimal reserva) =>
        configuracion.OrigenesHabilitados.Select(b => Disponible(producto, b, reserva))
            .DefaultIfEmpty(0).Max();

    public static decimal Participacion(ProductoInventario producto, ConfiguracionBodegas configuracion)
    {
        decimal local = Math.Max(0, producto.ObtenerStock(configuracion.BodegaLocal));
        decimal centros = configuracion.OrigenesHabilitados
            .Where(b => !string.Equals(b, configuracion.BodegaLocal, StringComparison.OrdinalIgnoreCase))
            .Sum(b => Math.Max(0, producto.ObtenerStock(b)));
        return local + centros > 0 ? local / (local + centros) * 100 : 0;
    }

    public static bool EsOportunidad(ProductoInventario producto,
        ConfiguracionBodegas configuracion, FiltrosPedido filtros)
    {
        if (configuracion.ProductosNoSugerir.Contains(producto.Codigo))
            return false;

        decimal disponible = MejorDisponible(producto, configuracion, filtros.ReservaCentro);
        return disponible > 0 && disponible >= filtros.MinimoTransferible
            && (!filtros.UsarMaximoB4 || producto.ObtenerStock(configuracion.BodegaLocal) <= filtros.StockMaximoB4);
    }
}
