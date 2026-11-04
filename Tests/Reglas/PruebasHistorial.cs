using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

static class PruebasHistorial
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        using var servidor = new ServidorHistorial();
        using var http = new HttpClient(servidor) { BaseAddress = new Uri("http://localhost/") };
        var pedido = new PedidoService();
        var actividad = new ActividadService();
        var config = new ConfiguracionService();
        var inventario = new InventarioService(new ExcelImportacionService(), pedido, actividad);
        var trabajo = new TrabajoService(http, pedido, inventario, config, actividad, new AlmacenamientoFalso(null));
        var tipo = typeof(PruebasHistorial).Assembly.GetTypes().Single(t => t.Name == "Resumen");
        var pantalla = Activator.CreateInstance(tipo)!;
        const BindingFlags acceso = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        tipo.GetProperty("Trabajo", acceso)!.SetValue(pantalla, trabajo);
        void Campo(string nombre, object? valor) => tipo.GetField(nombre, acceso)!.SetValue(pantalla, valor);
        T Leer<T>(string nombre) => (T)tipo.GetField(nombre, acceso)!.GetValue(pantalla)!;
        async Task Invocar(string nombre, params object[] args) => await (Task)tipo.GetMethod(nombre, acceso)!.Invoke(pantalla, args)!;

        await Invocar("OnInitializedAsync");
        verificar(Leer<PaginaHistorial>("historial").Total == 1 && trabajo.Estado.Inventario is null,
            "Historial abre directamente los pedidos guardados sin depender de un inventario cargado");
        Campo("porProducto", true); Campo("estadoHistorial", "Cancelado"); Campo("desde", new DateOnly(2026, 11, 1));
        await Invocar("VerHistorialProducto", "000123");
        verificar(!Leer<bool>("porProducto") && servidor.UltimaRuta == "/api/trabajo/historial?codigo=000123&pagina=1",
            "Ver pedidos de un producto conserva ceros y consulta todas las fechas sin filtros anteriores");
        await Invocar("VerPedido", 25L);
        verificar(Leer<PedidoDetalle>("detalle").Items.Single().Comentario == "Para cliente",
            "El detalle histórico recupera el producto, cantidad y comentario guardados");
        trabajo.Estado.MasPedidos.Add(new("000123", "Cable", 2, 10, DateTime.UtcNow, null, false, false));
        trabajo.Estado.MasPedidos.Add(new("OTRO", "Cámara", 1, 1, DateTime.UtcNow, null, false, false));
        Campo("buscarProducto", " cable ");
        var visibles = (IEnumerable<ProductoFrecuente>)tipo.GetProperty("ProductosVisibles", acceso)!.GetValue(pantalla)!;
        verificar(visibles.Single().Codigo == "000123", "Por producto permite buscar por descripción sin modificar los pedidos");
        await Invocar("LimpiarHistorial");
        verificar(servidor.UltimaRuta == "/api/trabajo/historial?pagina=1" && Leer<PedidoDetalle?>("detalle") is null,
            "Limpiar filtros vuelve al historial general y cierra el detalle anterior");
    }

    private sealed class ServidorHistorial : HttpMessageHandler
    {
        public string UltimaRuta { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaRuta = request.RequestUri!.PathAndQuery;
            var resumen = new PedidoResumen(25, DateTime.UtcNow, "Prueba", "Guardado", 1, 1);
            object respuesta = request.RequestUri.AbsolutePath == "/api/trabajo/pedidos/25"
                ? new PedidoDetalle(resumen, [new() { CodigoProducto = "000123", CantidadSolicitada = 5, Comentario = "Para cliente" }])
                : new PaginaHistorial([resumen], 1, 1, 20);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(respuesta, respuesta.GetType()) });
        }
    }
}
