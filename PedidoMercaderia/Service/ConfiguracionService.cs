
using System.Text.Json;
using Microsoft.JSInterop;
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services
{
    public class ConfiguracionService
    {
        private const string ClaveAlmacenamiento =
            "PedidoMercaderia.Configuracion.v1";

        private readonly IJSRuntime _jsRuntime;
        private bool _configuracionCargada;

        public ConfiguracionBodegas Actual { get; private set; } = new();

        public IReadOnlyList<string> BodegasDisponibles { get; } =
            Enumerable.Range(1, 45)
                .Select(n => $"B{n}")
                .ToList();

        public IReadOnlyList<string> CentrosDistribucion { get; } =
            new List<string>
            {
                "B1",
                "B40"
            };

        public ConfiguracionService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public string ObtenerNombreBodega(string? codigo)
        {
            return codigo?.ToUpperInvariant() switch
            {
                "B4" => "Mi sucursal",
                "B1" => "Matriz",
                "B39" => "Bame",
                "B40" => "Ebenezer",
                _ => "Bodega sin identificar"
            };
        }

        public string ObtenerEtiquetaBodega(string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return "Sin seleccionar";

            return $"{codigo} - {ObtenerNombreBodega(codigo)}";
        }

        public async Task CargarAsync()
        {
            if (_configuracionCargada)
                return;

            string? json = await _jsRuntime.InvokeAsync<string?>(
                "localStorage.getItem",
                ClaveAlmacenamiento);

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var guardada =
                        JsonSerializer.Deserialize<ConfiguracionBodegas>(
                            json);

                    if (guardada is not null)
                    {
                        var origenes = new HashSet<string>(
                            guardada.BodegasOrigen?
                                .Where(b => CentrosDistribucion.Contains(
                                    b, StringComparer.OrdinalIgnoreCase))
                            ?? Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase);

                        if (origenes.Count == 0)
                        {
                            origenes = new HashSet<string>(
                                CentrosDistribucion,
                                StringComparer.OrdinalIgnoreCase);
                        }

                        Actual = new ConfiguracionBodegas
                        {
                            BodegaLocal = "B4",

                            BodegasOrigen = origenes,

                            ProductosNoSugerir = new HashSet<string>(
                                guardada.ProductosNoSugerir
                                    ?? new HashSet<string>(),
                                StringComparer.OrdinalIgnoreCase)
                        };
                    }
                }
                catch (JsonException)
                {
                    Actual = new ConfiguracionBodegas();
                }
            }

            _configuracionCargada = true;
        }

        public string? Validar()
        {
            if (Actual.BodegaLocal != "B4")
                return "La bodega local debe ser B4.";

            if (Actual.BodegasOrigen.Count == 0)
                return "Selecciona al menos un centro de distribución.";

            if (Actual.BodegasOrigen.Any(
                b => !ConfiguracionBodegas.EsOrigenAutorizado(b)))
            {
                return "Existe un centro no autorizado.";
            }

            return null;
        }

        public bool EstaConfigurado => Validar() is null;

        public async Task GuardarAsync()
        {
            string? error = Validar();

            if (error is not null)
                throw new InvalidOperationException(error);

            string json = JsonSerializer.Serialize(Actual);

            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                ClaveAlmacenamiento,
                json);
        }
    }
}
