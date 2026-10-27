
using System.Text;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PedidoMercaderia.Services;

namespace PedidoMercaderia
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Soporte para archivos Excel antiguos (.xls)
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

            // Cliente HTTP
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(
                    builder.HostEnvironment.BaseAddress)
            });

            // Configuración de bodegas
            builder.Services.AddSingleton<ConfiguracionService>();

            // Procesamiento de archivos Excel
            builder.Services.AddSingleton<ExcelImportacionService>();

            // Administración del pedido
            builder.Services.AddSingleton<PedidoService>();

            // Inventario en memoria
            builder.Services.AddSingleton<InventarioService>();

            await builder.Build().RunAsync();
        }
    }
}
