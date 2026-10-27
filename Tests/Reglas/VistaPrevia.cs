using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PedidoMercaderia.Services;

public static class VistaPrevia
{
    public static async Task Generar(string raiz)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddSingleton<IJSRuntime>(new AlmacenamientoFalso(null));
        servicios.AddSingleton<ConfiguracionService>();
        servicios.AddSingleton<PedidoService>();
        servicios.AddSingleton<InventarioService>();
        await using var proveedor = servicios.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(proveedor, proveedor.GetRequiredService<ILoggerFactory>());
        string contenido = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tipo = typeof(VistaPrevia).Assembly.GetTypes().Single(t => t.Name == "RevisarPedido");
            var componente = await renderer.RenderComponentAsync(tipo, ParameterView.Empty);
            return componente.ToHtmlString();
        });
        if (contenido.Contains("participación") || !contenido.Contains("JR-DF308") || !contenido.Contains("Opciones avanzadas"))
            throw new Exception("La pantalla simplificada no se renderizó correctamente.");
        string bootstrap = await File.ReadAllTextAsync(Path.Combine(raiz, "PedidoMercaderia/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css"));
        string estilos = await File.ReadAllTextAsync(Path.Combine(raiz, "PedidoMercaderia/Pages/RevisarPedido.razor.css"));
        string html = "<!doctype html><html lang='es'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>Vista previa · Pedido simplificado</title><style>" + bootstrap + estilos +
            "body{background:#fafbfd;padding:24px;font-family:Arial,sans-serif} .pedido-page{max-width:1200px}" +
            "</style><body>" + contenido + "</body></html>";
        await File.WriteAllTextAsync(Path.Combine(raiz, "Tests/vista-previa.html"), html);
        Console.WriteLine("OK: pantalla real renderizada con datos ficticios en Tests/vista-previa.html");
    }
}
