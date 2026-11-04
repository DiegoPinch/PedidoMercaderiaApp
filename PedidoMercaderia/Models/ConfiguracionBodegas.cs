
namespace PedidoMercaderia.Models
{
    public class ConfiguracionBodegas
    {
        public PoliticaReposicion Reposicion { get; set; } = new();
        // Sucursal que solicita mercadería
        public string BodegaLocal { get; set; } = "";

        // Centros autorizados para abastecer
        public HashSet<string> BodegasOrigen { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> NombresBodegas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public static bool EsCodigoValido(string? bodega)
        {
            if (bodega is null || bodega.Length is < 2 or > 7 || bodega[0] != 'B') return false;
            for (int i = 1; i < bodega.Length; i++)
                if (bodega[i] is < '0' or > '9') return false;
            return true;
        }
        [System.Text.Json.Serialization.JsonIgnore]
        public IEnumerable<string> OrigenesHabilitados => BodegasOrigen
            .Where(b => EsCodigoValido(b) && !b.Equals(BodegaLocal, StringComparison.OrdinalIgnoreCase));
        public string Nombre(string codigo) => NombresBodegas.GetValueOrDefault(codigo, codigo);

        // Productos que decidiste no sugerir
        public HashSet<string> ProductosNoSugerir { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
