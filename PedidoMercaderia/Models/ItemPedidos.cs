
namespace PedidoMercaderia.Models
{
    public class ItemPedido
    {
        public string CodigoProducto { get; set; } = string.Empty;

        public string BodegaOrigen { get; set; } = string.Empty;

        public decimal CantidadSolicitada { get; set; }

        public decimal ReservaCentro { get; set; }
    }
}
