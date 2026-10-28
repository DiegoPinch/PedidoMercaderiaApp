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
        var almacenamiento = new AlmacenamientoVista();
        servicios.AddSingleton<IJSRuntime>(almacenamiento);
        servicios.AddSingleton<ConfiguracionService>();
        servicios.AddSingleton<PedidoService>();
        servicios.AddSingleton<ActividadService>();
        servicios.AddSingleton<ExcelImportacionService>();
        servicios.AddSingleton<InventarioService>();
        await using var proveedor = servicios.BuildServiceProvider();
        using var inventario = LibroPrueba.Crear(
            ["COD_ITEM", "DESCRIPCION", "MARCA", "B4", "B1", "B40"],
            ["JR-DF308", "PAR DE VIDEO BALUM HD CON CONECTORES RJ45", "TECLAM", 0, 524, 255],
            ["CABLE-01", "Cable de red UTP categoría 6", "TECLAM", 10, 100, 200],
            ["CONECTOR-02", "Conector RJ45", "TECLAM", 20, 40, 60]);
        proveedor.GetRequiredService<InventarioService>().Importar(inventario, "Inventario ficticio");
        await using var renderer = new HtmlRenderer(proveedor, proveedor.GetRequiredService<ILoggerFactory>());
        string contenido = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tipo = typeof(VistaPrevia).Assembly.GetTypes().Single(t => t.Name == "RevisarPedido");
            var componente = await renderer.RenderComponentAsync(tipo, ParameterView.Empty);
            return componente.ToHtmlString();
        });
        if (contenido.Contains("participación") || !contenido.Contains("JR-DF308") || !contenido.Contains("Opciones avanzadas"))
            throw new Exception("La pantalla simplificada no se renderizó correctamente.");
        var actividad = proveedor.GetRequiredService<ActividadService>();
        using var movimientos = LibroPrueba.Crear(
            ["COD_ITEM", "STOCK", "DIAS_SIN_ACTIVIDAD"],
            ["JR-DF308", 0, 400], ["CABLE-01", 10, 180]);
        actividad.Importar(movimientos, "Movimientos ficticios", new DateOnly(2026, 10, 27),
            proveedor.GetRequiredService<InventarioService>().Productos);
        async Task<string> Renderizar() => await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tipo = typeof(VistaPrevia).Assembly.GetTypes().Single(t => t.Name == "RevisarPedido");
            return (await renderer.RenderComponentAsync(tipo, ParameterView.Empty)).ToHtmlString();
        });
        contenido = await Renderizar();
        if (!contenido.Contains("CABLE-01") || !contenido.Contains("apartarActividad"))
            throw new Exception("Cargar el reporte no debe apartar productos hasta activar el filtro.");
        almacenamiento.Filtros = """{"ApartarSinMovimiento":true,"DiasSinMovimiento":180}""";
        contenido = await Renderizar();
        if (contenido.Contains("CABLE-01") || !contenido.Contains("JR-DF308") || !contenido.Contains("CONECTOR-02"))
            throw new Exception("El filtro debe apartar al antiguo, conservando agotados y productos sin información.");
        actividad.Incluir("CABLE-01");
        contenido = await Renderizar();
        if (!contenido.Contains("CABLE-01"))
            throw new Exception("La excepción manual debe recuperar el producto en Por revisar.");
        actividad.Limpiar();
        contenido = await Renderizar();
        if (!contenido.Contains("CABLE-01"))
            throw new Exception("Sin reporte, el filtro guardado no debe ocultar productos.");
        Console.WriteLine("OK: pantalla comprobada sin reporte, con filtro apagado, encendido y con recuperación manual.");
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

sealed class AlmacenamientoVista : IJSRuntime
{
    public string? Filtros { get; set; }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "localStorage.getItem" && args?[0]?.ToString() == "PedidoMercaderia.Filtros.v4")
            return ValueTask.FromResult((TValue)(object)Filtros!);
        return ValueTask.FromResult(default(TValue)!);
    }
}
