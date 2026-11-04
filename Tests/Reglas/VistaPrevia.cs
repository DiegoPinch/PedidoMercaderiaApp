using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PedidoMercaderia.Services;

public static class VistaPrevia
{
    public static async Task VerificarInicio()
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging(); servicios.AddSingleton<NavigationManager>(new NavegacionPrueba()); servicios.AddSingleton<IJSRuntime>(new AlmacenamientoVista());
        servicios.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost") });
        servicios.AddSingleton<ConfiguracionService>(); servicios.AddSingleton<PedidoService>();
        servicios.AddSingleton(new ActividadService { BodegaLocal = "B4" }); servicios.AddSingleton<ExcelImportacionService>();
        servicios.AddSingleton<InventarioService>(); servicios.AddSingleton<TrabajoService>();
        await using var proveedor = servicios.BuildServiceProvider();
        var config = proveedor.GetRequiredService<ConfiguracionService>();
        config.Restaurar(new() { BodegaLocal = "B4", BodegasOrigen = new(["B1", "B40"]), NombresBodegas = new() { ["B4"] = "Ambato", ["B1"] = "Matriz", ["B40"] = "Ebenezer" } });
        await using var renderer = new HtmlRenderer(proveedor, proveedor.GetRequiredService<ILoggerFactory>());
        async Task<string> Renderizar() => await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync(typeof(VistaPrevia).Assembly.GetTypes().Single(t => t.Name == "Importar"), ParameterView.Empty)).ToHtmlString());
        var html = await Renderizar();
        if (!html.Contains("Carga tu inventario") || !html.Contains("archivoInventario") || html.Contains("Ambato") || html.Contains("Cambiar sucursal") || html.Contains("Guardar selección"))
            throw new Exception("Sin inventario solo debe mostrarse la carga, aunque exista configuración antigua.");
        proveedor.GetRequiredService<TrabajoService>().Estado.Inventario = new() { NombreArchivo = "semanal.xlsx", BodegasDetectadas = ["B4"],
            Productos = Enumerable.Range(0, 51).Select(n => new PedidoMercaderia.Models.ProductoInventario { Codigo = $"INV-{n:D3}", Descripcion = "Producto de prueba", Existencias = new() { ["B4"] = n } }).ToList() };
        html = await Renderizar();
        if (!html.Contains("semanal.xlsx") || !html.Contains("Preparar pedido") || !html.Contains("<details") || !html.Contains("Cambiar sucursal"))
            throw new Exception("Con inventario debe mostrarse el siguiente paso y los ajustes plegables.");
        if (!html.Contains("Productos del inventario") || !html.Contains("<strong>INV-000</strong>") || html.Contains("<strong>INV-050</strong>") || !html.Contains("Siguiente"))
            throw new Exception("El inventario debe mostrar una tabla paginada con códigos destacados.");
        if (html.Contains("archivoActividad")) throw new Exception("Movimientos se carga en Preparar pedido, no en Inventario.");
        Console.WriteLine("OK: inicio vacío sin datos ficticios y flujo con ajustes plegables tras importar.");
    }

    public static async Task Generar(string raiz)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging(); servicios.AddSingleton<NavigationManager>(new NavegacionPrueba());
        servicios.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost") });
        servicios.AddSingleton<TrabajoService>();
        var almacenamiento = new AlmacenamientoVista();
        servicios.AddSingleton<IJSRuntime>(almacenamiento);
        servicios.AddSingleton<ConfiguracionService>();
        servicios.AddSingleton<PedidoService>();
        servicios.AddSingleton(new ActividadService { BodegaLocal = "B4" });
        servicios.AddSingleton<ExcelImportacionService>();
        servicios.AddSingleton<InventarioService>();
        await using var proveedor = servicios.BuildServiceProvider();
        proveedor.GetRequiredService<ConfiguracionService>().Restaurar(new() { BodegaLocal = "B4", BodegasOrigen = new(["B1", "B40"]), NombresBodegas = new() { ["B1"] = "Matriz", ["B40"] = "Ebenezer" } });
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
        if (!contenido.Contains("archivoActividad") || contenido.Contains("href=\"importar\">Cambiar reporte"))
            throw new Exception("El reporte debe poder cargarse directamente en Preparar pedido.");
        if (contenido.Contains("participación") || !contenido.Contains("JR-DF308") || !contenido.Contains("Opciones avanzadas"))
            throw new Exception("La pantalla simplificada no se renderizó correctamente.");
        if (!contenido.Contains("encabezado-filtro") || !contenido.Contains("Filtrar y ordenar Matriz"))
            throw new Exception("Los encabezados deben ofrecer filtros accesibles.");
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
        var trabajo = proveedor.GetRequiredService<TrabajoService>();
        trabajo.Estado.Corte = new(1, new(2026, 10, 20), "stock-base.xlsx", "admin", 3);
        trabajo.Estado.Cortes = [trabajo.Estado.Corte];
        contenido = await Renderizar();
        if (!contenido.Contains("Este Excel es la base") || contenido.Contains("Generar borrador ("))
            throw new Exception("El primer inventario no debe inventar reposiciones históricas.");
        var actual = proveedor.GetRequiredService<InventarioService>().ResultadoActual!;
        actual.Productos.Single(p => p.Codigo == "CABLE-01").Existencias["B4"] = 6;
        trabajo.Estado.Inventario = actual;
        trabajo.Estado.Cortes.Insert(0, new(2, new(2026, 10, 27), "stock-actual.xlsx", "admin", 3));
        trabajo.Estado.Corte = trabajo.Estado.Cortes[0];
        trabajo.Estado.Propuestas = [new("CABLE-01", "Cable", "TECLAM", "Bajó stock", "De 10 a 6", 6, 10, null, "B40", 4, true)];
        contenido = await Renderizar();
        if (!contenido.Contains("stock-actual.xlsx") || !contenido.Contains("20/10/2026") || !contenido.Contains("Recomendados (1)")
            || !contenido.Contains("Reponer 4") || contenido.Contains("JR-DF308"))
            throw new Exception("Con otro Excel la página debe abrir las recomendaciones con el stock actualizado y la cantidad a reponer.");
        Console.WriteLine("OK: primer Excel como base y siguiente Excel con reposiciones integradas en Preparar pedido.");
        if (!contenido.Contains("Cómo recomendar mi pedido") || !contenido.Contains("diasPedidos")
            || !contenido.Contains("diasTraslado") || !contenido.Contains("diasMargen") || !contenido.Contains("Sugerencias para revisar"))
            throw new Exception("La reposición debe ofrecer ajustes plegables de cobertura y una lista manual separada.");
        var pedido = proveedor.GetRequiredService<PedidoService>();
        pedido.Compartido = true;
        pedido.Anotar(actual.Productos.Single(p => p.Codigo == "CABLE-01"), 12);
        contenido = await Renderizar();
        if (!contenido.Contains("pendiente-revision") || !contenido.Contains("Guardar y cerrar pedido") || !contenido.Contains("Elegir origen"))
            throw new Exception("El borrador anticipado debe abrir Mi pedido y destacar la revisión de origen y cantidad.");
        if (!contenido.Contains("Compra excepcional") || !contenido.Contains("De 10 a 6"))
            throw new Exception("La fila del pedido debe permitir marcar excepciones y consultar el motivo de la sugerencia.");
        Console.WriteLine("OK: cobertura plegable, revisión manual, motivo y compra excepcional en la pantalla real.");
        trabajo.Estado.Corte = trabajo.Estado.Corte with { Estado = "Cerrado" };
        contenido = await Renderizar();
        if (!contenido.Contains("Borrador para el próximo pedido") || contenido.Contains("pedido-tabla-wrap") || !contenido.Contains("historial"))
            throw new Exception("Un Excel cerrado debe mostrar las anotaciones futuras y el historial, sin la lista para volver a pedir.");
        Console.WriteLine("OK: borrador futuro, filas amarillas y estado cerrado sin revisión del inventario anterior.");
        string bootstrap = await File.ReadAllTextAsync(Path.Combine(raiz, "PedidoMercaderia/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css"));
        string estilos = await File.ReadAllTextAsync(Path.Combine(raiz, "PedidoMercaderia/Pages/RevisarPedido.razor.css"));
        string html = "<!doctype html><html lang='es'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>Vista previa · Pedido simplificado</title><style>" + bootstrap + estilos +
            "body{background:#fafbfd;padding:24px;font-family:Arial,sans-serif} .pedido-page{max-width:1200px}" +
            "</style><body>" + contenido + "</body></html>";
        var salida = Path.Combine(raiz, "artifacts", "pruebas");
        Directory.CreateDirectory(salida);
        await File.WriteAllTextAsync(Path.Combine(salida, "vista-previa.html"), html);
        Console.WriteLine("OK: pantalla real renderizada con datos ficticios en artifacts/pruebas/vista-previa.html");
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

sealed class NavegacionPrueba : NavigationManager
{
    public NavegacionPrueba() => Initialize("http://localhost/", "http://localhost/pedido");
    protected override void NavigateToCore(string uri, bool forceLoad) { Uri = ToAbsoluteUri(uri).ToString(); }
}
