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

    public static void VerificarBorradorSemanal(Action<bool, string> verificar)
    {
        var pedido = new PedidoService();
        var tipo = typeof(PruebasTrabajo).Assembly.GetTypes().Single(t => t.Name == "BorradorProximo");
        var pantalla = Activator.CreateInstance(tipo)!;
        const System.Reflection.BindingFlags acceso = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        tipo.GetProperty("Pedido", acceso)!.SetValue(pantalla, pedido);
        T Campo<T>(string nombre) => (T)tipo.GetField(nombre, acceso)!.GetValue(pantalla)!;
        void Invocar(string nombre, params object[] args) => tipo.GetMethod(nombre, acceso)!.Invoke(pantalla, args);
        var producto = new ProductoInventario { Codigo = "CAMARA-PUNTUAL", Descripcion = "Cámara para cliente", Existencias = new() { ["B1"] = 0, ["B4"] = 0 } };
        Campo<Dictionary<string, string>>("cantidades")[producto.Codigo] = "25";
        Campo<Dictionary<string, string>>("comentarios")[producto.Codigo] = "Cotización del miércoles";
        string? comentarioAlGuardar = null;
        pedido.Cambiado += () => comentarioAlGuardar = pedido.Obtener(producto.Codigo)?.Comentario;
        Invocar("Anotar", producto);
        var item = pedido.Obtener(producto.Codigo)!;
        verificar(item.CantidadSolicitada == 25 && item.BodegaOrigen == "" && item.RequiereRevision,
            "El borrador semanal anota la cantidad solicitada aunque no haya stock, sin asignar un origen antiguo");
        verificar(item.CompraExcepcional && comentarioAlGuardar == "Cotización del miércoles",
            "La primera notificación de guardado ya incluye la nota del cliente y la marca de pedido puntual");
        Invocar("Anotar", producto);
        verificar(pedido.TotalProductos == 1 && item.CantidadSolicitada == 25 && item.Comentario == "Cotización del miércoles",
            "Anotar de nuevo no duplica ni reemplaza una solicitud ya preparada");
        Invocar("Cantidad", item, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "-1" });
        verificar(item.CantidadSolicitada == 25, "Una cantidad inválida no borra la anotación de la semana");
        Invocar("Cantidad", item, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "0.5" });
        verificar(item.CantidadSolicitada == .5m && item.RequiereRevision && item.Comentario == "Cotización del miércoles",
            "Editar admite cantidades fraccionarias y conserva el comentario y la revisión pendiente");
        var otro = new ProductoInventario { Codigo = "OTRO" };
        Campo<Dictionary<string, string>>("cantidades")[otro.Codigo] = "0";
        Invocar("Anotar", otro);
        verificar(!pedido.Contiene(otro.Codigo), "No se anotan productos con cantidad cero");
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
