
namespace PedidoMercaderia.Models
{
    public class ItemPedido
    {
        public string CodigoProducto { get; set; } = string.Empty;

        public string BodegaOrigen { get; set; } = string.Empty;

        public decimal CantidadSolicitada { get; set; }

        public decimal ReservaCentro { get; set; }

        public string Comentario { get; set; } = string.Empty;
        public string DescripcionProducto { get; set; } = string.Empty;
        public bool RequiereRevision { get; set; }
        public bool CompraExcepcional { get; set; }

        public ItemPedido Copiar() => (ItemPedido)MemberwiseClone();

        public static bool MismaSolicitud(ItemPedido? a, ItemPedido? b) => a is null || b is null
            ? a is null && b is null
            : string.Equals(a.CodigoProducto, b.CodigoProducto, StringComparison.OrdinalIgnoreCase)
                && a.BodegaOrigen == b.BodegaOrigen && a.CantidadSolicitada == b.CantidadSolicitada
                && a.ReservaCentro == b.ReservaCentro && a.Comentario == b.Comentario && a.RequiereRevision == b.RequiereRevision
                && a.CompraExcepcional == b.CompraExcepcional;

        public static bool MismoContenido(ItemPedido? a, ItemPedido? b) => MismaSolicitud(a, b)
            && a?.DescripcionProducto == b?.DescripcionProducto;
    }
}
