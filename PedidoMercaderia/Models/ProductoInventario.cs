
namespace PedidoMercaderia.Models
{
    public class ProductoInventario
    {
        public string Codigo { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public string Referencia { get; set; } = string.Empty;

        public string Marca { get; set; } = string.Empty;

        public string Grupo { get; set; } = string.Empty;

        public string Subgrupo { get; set; } = string.Empty;

        public string TipoItem { get; set; } = string.Empty;

        public string UnidadMedida { get; set; } = string.Empty;

        public string UltimoProveedor { get; set; } = string.Empty;

        public decimal Precio { get; set; }

        public decimal StockMinimoSistema { get; set; }

        public decimal StockMaximoSistema { get; set; }

        public decimal TotalReportado { get; set; }

        public Dictionary<string, decimal> Existencias { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public decimal ObtenerStock(string? bodega)
        {
            if (string.IsNullOrWhiteSpace(bodega))
                return 0;

            return Existencias.TryGetValue(bodega, out decimal cantidad)
                ? cantidad
                : 0;
        }

        public decimal ObtenerStockOrigen(IEnumerable<string> bodegas)
        {
            return bodegas.Sum(ObtenerStock);
        }

        public decimal TotalCalculado =>
            Existencias.Values.Sum();
    }
}
