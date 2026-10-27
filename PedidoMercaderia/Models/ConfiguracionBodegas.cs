
namespace PedidoMercaderia.Models
{
    public class ConfiguracionBodegas
    {
        // Sucursal que solicita mercadería
        public string BodegaLocal { get; set; } = "B4";

        // Centros autorizados para abastecer
        public HashSet<string> BodegasOrigen { get; set; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "B1",
                "B40"
            };

        public static bool EsOrigenAutorizado(string bodega) =>
            string.Equals(bodega, "B1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bodega, "B40", StringComparison.OrdinalIgnoreCase);

        [System.Text.Json.Serialization.JsonIgnore]
        public IEnumerable<string> OrigenesHabilitados =>
            BodegasOrigen.Where(EsOrigenAutorizado);

        // Productos que decidiste no sugerir
        public HashSet<string> ProductosNoSugerir { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
