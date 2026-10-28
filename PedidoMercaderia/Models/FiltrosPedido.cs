
namespace PedidoMercaderia.Models
{
    public class FiltrosPedido
    {
        // Unidades que se deben reservar en el centro
        public decimal ReservaCentro { get; set; } = 1;

        // Mínimo transferible desde un solo centro
        public decimal MinimoTransferible { get; set; } = 1;

        // Filtro opcional de stock absoluto en B4
        public bool UsarMaximoB4 { get; set; } = false;

        public decimal StockMaximoB4 { get; set; } = 3;

        // Cantidad inicial al añadir un producto
        public decimal CantidadInicial { get; set; } = 3;

        // Orden de presentación
        public string Orden { get; set; } = "local";

        public bool ApartarSinMovimiento { get; set; }
        public int DiasSinMovimiento { get; set; } = 180;

        public FiltrosPedido Copiar() => (FiltrosPedido)MemberwiseClone();
    }
}
