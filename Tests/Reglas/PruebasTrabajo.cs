using System.Net;
using System.Net.Http.Json;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

static class PruebasTrabajo
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        using var servidor = new ServidorFalso();
        using var http = new HttpClient(servidor) { BaseAddress = new("http://localhost") };
        var pedido = new PedidoService();
        var actividad = new ActividadService();
        var configuracion = new ConfiguracionService();
        var inventario = new InventarioService(new ExcelImportacionService(), pedido, actividad);
        var trabajo = new TrabajoService(http, pedido, inventario, configuracion, actividad, new AlmacenamientoFalso(null));
        await trabajo.Entrar(new("prueba", "solo-prueba"));
        verificar(servidor.Rutas.Count == 3 && !servidor.Rutas.Any(r => r.Contains("sesion")) && trabajo.Usuario?.Usuario == "prueba",
            "Entrar utiliza la sesión devuelta por el login sin otra consulta de autenticación");
        var inicial = inventario.ResultadoActual;
        await trabajo.Actualizar();
        verificar(ReferenceEquals(inicial, inventario.ResultadoActual) && trabajo.Estado.Revision == 2
            && servidor.Rutas.Last().Contains("inventarioConocido=1"),
            "Actualizar conserva el inventario conocido y renueva la revisión del equipo");
        await trabajo.Actualizar();
        verificar(!ReferenceEquals(inicial, inventario.ResultadoActual) && inventario.Productos.Single().ObtenerStock("B4") == 2
            && trabajo.Estado.Corte?.Id == 2,
            "Cuando cambia el Excel se reemplaza el stock local sin conservar cantidades anteriores");
        var original = new ItemPedido { CodigoProducto = "P", CantidadSolicitada = 3 };
        var copia = original.Copiar();
        copia.CantidadSolicitada = 4;
        verificar(original.CantidadSolicitada == 3 && !ItemPedido.MismoContenido(original, copia),
            "La copia de una línea aísla las ediciones durante un guardado");
        verificar((await trabajo.DescargarExcel()).SequenceEqual(new byte[] { 80, 75 })
            && servidor.Rutas.Last() == "/api/trabajo/borrador/excel?revision=3",
            "La descarga pide al servidor el Excel de la revisión mostrada");
        await trabajo.DescargarExcel(42);
        verificar(servidor.Rutas.Last() == "/api/trabajo/pedidos/42/excel",
            "El Excel histórico se descarga directamente por su pedido");
    }

    private sealed class ServidorFalso : HttpMessageHandler
    {
        public List<string> Rutas { get; } = [];
        private int lecturas;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var ruta = request.RequestUri!.PathAndQuery;
            Rutas.Add(ruta);
            if (ruta.Contains("/excel"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([80, 75]) });
            object contenido;
            if (ruta == "/api/auth/token") contenido = new { token = "prueba" };
            else if (ruta == "/api/auth/entrar") contenido = new UsuarioSesion("prueba", "Prueba", "Colaborador");
            else if (ruta.StartsWith("/api/trabajo/?compacto=true"))
            {
                lecturas++;
                contenido = new EstadoTrabajo
                {
                    Revision = lecturas,
                    Corte = new(lecturas == 3 ? 2 : 1, new(2026, 10, 1), "prueba.xlsx", "prueba", 1),
                    InventarioSinCambios = lecturas == 2,
                    Inventario = lecturas == 2 ? null : new()
                    {
                        BodegasDetectadas = ["B4"],
                        Productos = [new() { Codigo = "P", Existencias = new() { ["B4"] = lecturas == 3 ? 2 : 10 } }]
                    }
                };
            }
            else throw new Exception("Petición inesperada: " + ruta);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(contenido, contenido.GetType()) });
        }
    }
}
