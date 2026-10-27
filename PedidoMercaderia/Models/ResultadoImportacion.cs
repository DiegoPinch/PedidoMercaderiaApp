
namespace PedidoMercaderia.Models
{
    public class ResultadoImportacion
    {
        public string NombreArchivo { get; set; } = string.Empty;

        public string NombreHoja { get; set; } = string.Empty;

        public int FilasLeidas { get; set; }

        public int FilasOmitidas { get; set; }

        public int DiferenciasTotales { get; set; }

        public List<string> BodegasDetectadas { get; set; } = new();

        public List<ProductoInventario> Productos { get; set; } = new();

        public List<string> Advertencias { get; set; } = new();

        public int TotalProductos => Productos.Count;

        public int TotalMarcas => Productos
            .Select(p => p.Marca)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }
}
