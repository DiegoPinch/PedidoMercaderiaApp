
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services
{
    public class ConfiguracionService
    {
        public ConfiguracionBodegas Actual { get; private set; } = new();
        public Func<ConfiguracionBodegas, Task>? GuardarCompartido { get; set; }
        public void Restaurar(ConfiguracionBodegas configuracion)
        {
            Actual = configuracion;
            Actual.ProductosNoSugerir = new(Actual.ProductosNoSugerir, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> BodegasDisponibles => Actual.NombresBodegas.Keys.OrderBy(b => b.Length).ThenBy(b => b).ToList();
        public IReadOnlyList<string> CentrosDistribucion => Actual.OrigenesHabilitados.ToList();

        public string ObtenerNombreBodega(string? codigo) => codigo is null ? "Sin seleccionar" : Actual.Nombre(codigo);

        public string ObtenerEtiquetaBodega(string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return "Sin seleccionar";

            return $"{codigo} - {ObtenerNombreBodega(codigo)}";
        }

        public string? Validar()
        {
            if (string.IsNullOrWhiteSpace(Actual.BodegaLocal)) return "Selecciona tu sucursal.";
            if (!Actual.OrigenesHabilitados.Any()) return "Selecciona al menos un centro de distribución.";
            if (Actual.BodegasOrigen.Contains(Actual.BodegaLocal)) return "Tu sucursal no puede ser su propio centro.";
            return null;
        }

        public bool EstaConfigurado => Validar() is null;

        public async Task GuardarAsync()
        {
            string? error = Validar();

            if (error is not null)
                throw new InvalidOperationException(error);

            if (GuardarCompartido is null) throw new InvalidOperationException("Conecta con el servidor para guardar la selección.");
            await GuardarCompartido(Actual);
        }
    }
}
