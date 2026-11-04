using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

public sealed class TrabajoService
{
    private readonly HttpClient http;
    private readonly PedidoService pedido;
    private readonly InventarioService inventario;
    private readonly ConfiguracionService configuracion;
    private readonly ActividadService actividad;
    private readonly IJSRuntime js;
    private readonly SemaphoreSlim puerta = new(1);
    private string? token;
    private bool pendiente;
    private List<ItemPedido> baseBorrador = new();
    private static List<ItemPedido> Copiar(IEnumerable<ItemPedido> items) => items.Select(i => i.Copiar()).ToList();
    private Task guardado = Task.CompletedTask;
    public UsuarioSesion? Usuario { get; private set; }
    public EstadoTrabajo Estado { get; private set; } = new();
    public bool RequiereConfiguracion { get; private set; }
    public bool Ocupado { get; private set; }
    public bool Guardando { get; private set; }
    public string? ErrorGuardado { get; private set; }
    public int Carga { get; private set; }
    public event Action? Cambiado;

    public TrabajoService(HttpClient http, PedidoService pedido, InventarioService inventario,
        ConfiguracionService configuracion, ActividadService actividad, IJSRuntime js)
    {
        this.http = http; this.pedido = pedido; this.inventario = inventario; this.configuracion = configuracion; this.actividad = actividad; this.js = js;
        pedido.Cambiado += PedidoCambiado;
        pedido.EsperarGuardado = EsperarGuardado;
        configuracion.GuardarCompartido = GuardarConfiguracion;
    }
    public async Task Iniciar()
    {
        var sesion = await http.GetFromJsonAsync<SesionRespuesta>("api/auth/sesion") ?? throw new InvalidOperationException("No se pudo leer la sesión.");
        Usuario = sesion.Usuario; RequiereConfiguracion = sesion.RequiereConfiguracion;
        token = null;
        if (Usuario is not null) await Cargar();
    }
    public async Task Entrar(AccesoSolicitud acceso)
    {
        Usuario = await Enviar<UsuarioSesion>("api/auth/entrar", acceso);
        RequiereConfiguracion = false;
        token = null;
        await Cargar();
    }
    public async Task Configurar(AccesoSolicitud acceso)
    { await Enviar<object>("api/auth/configurar", acceso); await Entrar(acceso); }
    public async Task Salir()
    {
        await EsperarGuardado();
        await Enviar<object>("api/auth/salir", new { });
        Usuario = null; token = null; pedido.Compartido = false; pedido.Restaurar([]); pedido.Omitidos.Clear(); inventario.Restaurar(null); actividad.Limpiar(); configuracion.Restaurar(new()); Estado = new(); Cambiado?.Invoke();
    }
    private async Task<T?> Enviar<T>(string ruta, object datos, HttpMethod? metodo = null)
    {
        token ??= (await http.GetFromJsonAsync<Dictionary<string, string>>("api/auth/token"))!["token"];
        using var solicitud = new HttpRequestMessage(metodo ?? HttpMethod.Post, ruta) { Content = JsonContent.Create(datos) };
        solicitud.Headers.Add("X-CSRF-TOKEN", token);
        using var respuesta = await http.SendAsync(solicitud);
        await ComprobarRespuesta(respuesta);
        if (typeof(T) == typeof(object)) return default;
        return await respuesta.Content.ReadFromJsonAsync<T>();
    }
    private static async Task ComprobarRespuesta(HttpResponseMessage respuesta)
    {
        if (!respuesta.IsSuccessStatusCode)
        {
            string mensaje = respuesta.StatusCode switch
            { System.Net.HttpStatusCode.Unauthorized => "Usuario o contraseña incorrectos, o sesión vencida. Vuelve a entrar.", System.Net.HttpStatusCode.Forbidden => "No tienes permiso para esta operación.", System.Net.HttpStatusCode.TooManyRequests => "Demasiados intentos. Espera un minuto.", _ => "No se pudo guardar. Comprueba la conexión." };
            try { mensaje = (await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>())?.GetValueOrDefault("mensaje") ?? mensaje; } catch (JsonException) { }
            throw new InvalidOperationException(mensaje);
        }
    }
    public async Task<byte[]> DescargarExcel(long? pedidoId = null)
    {
        if (!pedidoId.HasValue) await EsperarGuardado();
        var ruta = pedidoId.HasValue ? $"api/trabajo/pedidos/{pedidoId.Value}/excel" : $"api/trabajo/borrador/excel?revision={Estado.Revision}";
        using var respuesta = await http.GetAsync(ruta);
        await ComprobarRespuesta(respuesta);
        return await respuesta.Content.ReadAsByteArrayAsync();
    }
    private async Task Cargar()
    {
        var conocido = Estado.Inventario is not null ? Estado.Corte?.Id : null;
        var ruta = "api/trabajo/?compacto=true" + (conocido.HasValue ? $"&inventarioConocido={conocido.Value}" : "");
        var nuevo = await http.GetFromJsonAsync<EstadoTrabajo>(ruta) ?? throw new InvalidOperationException("No se pudo cargar el trabajo.");
        if (nuevo.InventarioSinCambios)
        {
            if (Estado.Inventario is null || conocido != nuevo.Corte?.Id)
                throw new InvalidOperationException("No se pudo actualizar el inventario. Vuelve a cargar la página.");
            nuevo.Inventario = Estado.Inventario;
        }
        if (Estado.Corte?.Id != nuevo.Corte?.Id || Estado.Configuracion.BodegaLocal != nuevo.Configuracion.BodegaLocal) { actividad.Limpiar(); pedido.Omitidos.Clear(); }
        actividad.BodegaLocal = nuevo.Configuracion.BodegaLocal;
        actividad.Restaurar(nuevo.Actividad, nuevo.Inventario?.Productos ?? []);
        Estado = nuevo; inventario.Restaurar(nuevo.Inventario); pedido.Restaurar(nuevo.Borrador);
        baseBorrador = Copiar(nuevo.Borrador);
        pedido.Compartido = true; configuracion.Restaurar(nuevo.Configuracion); Carga++;
        ErrorGuardado = null; pendiente = false;
        await js.InvokeVoidAsync("pedidoPendiente", false);
        Cambiado?.Invoke();
    }
    public async Task Actualizar(bool descartar = false)
    {
        if (!descartar) await EsperarGuardado(); else await guardado;
        await puerta.WaitAsync(); Ocupado = true; Cambiado?.Invoke();
        try { await Cargar(); } finally { Ocupado = false; puerta.Release(); Cambiado?.Invoke(); }
    }
    private void PedidoCambiado()
    {
        if (Usuario is null || !pedido.Compartido) return;
        pendiente = true;
        if (!Guardando && ErrorGuardado is null) guardado = GuardarLoop();
    }
    private async Task GuardarLoop()
    {
        Guardando = true; Cambiado?.Invoke();
        try
        {
            await js.InvokeVoidAsync("pedidoPendiente", true);
            // Agrupar varias casillas o pulsaciones de comentario en una sola escritura.
            await Task.Delay(250);
            while (pendiente)
            {
                await puerta.WaitAsync();
                try
                {
                    pendiente = false;
                    var copia = Copiar(pedido.Items);
                    var respuesta = await Enviar<BorradorRespuesta>("api/trabajo/borrador", new GuardarBorrador(Estado.Revision, copia, Estado.Corte?.Id ?? 0, Copiar(baseBorrador)), HttpMethod.Put);
                    Estado.Revision = respuesta!.Revision;
                    baseBorrador = Copiar(respuesta.Items);
                    var combinado = respuesta.Items.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
                    var enviados = copia.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
                    var locales = pedido.Items.ToDictionary(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase);
                    // Conservar nuevas pulsaciones hechas mientras la petición estaba viajando.
                    foreach (var codigo in enviados.Keys.Union(locales.Keys, StringComparer.OrdinalIgnoreCase))
                    {
                        var antes = enviados.GetValueOrDefault(codigo); var ahora = locales.GetValueOrDefault(codigo);
                        if (ItemPedido.MismoContenido(antes, ahora)) continue;
                        if (ahora is null) combinado.Remove(codigo); else combinado[codigo] = ahora;
                    }
                    pedido.Restaurar(combinado.Values);
                }
                finally { puerta.Release(); }
            }
            await js.InvokeVoidAsync("pedidoPendiente", false);
        }
        catch (Exception ex) { ErrorGuardado = ex.Message; pendiente = true; }
        finally { Guardando = false; Cambiado?.Invoke(); }
    }
    public async Task EsperarGuardado()
    {
        await guardado;
        if (ErrorGuardado is not null) throw new InvalidOperationException(ErrorGuardado);
    }
    public async Task Reintentar()
    { if (Guardando) return; ErrorGuardado = null; guardado = GuardarLoop(); await guardado; }
    private async Task Ejecutar(Func<Task> accion)
    {
        await EsperarGuardado(); await puerta.WaitAsync(); Ocupado = true; Cambiado?.Invoke();
        try { await accion(); await Cargar(); }
        finally { Ocupado = false; puerta.Release(); Cambiado?.Invoke(); }
    }
    public Task Importar(ResultadoImportacion resultado, DateOnly fecha) => Ejecutar(async () =>
        await Enviar<RevisionRespuesta>("api/trabajo/inventarios", new ImportarCorte(Estado.Revision, fecha, resultado)));
    public Task GuardarActividad(ReporteActividad? reporte, long corteId, string bodega) => Ejecutar(async () =>
        await Enviar<RevisionRespuesta>("api/trabajo/actividad",
            new GuardarReporteActividad(Estado.Revision, corteId, bodega, reporte), HttpMethod.Put));
    public Task Generar(bool nuevos) => Ejecutar(async () => await Enviar<RevisionRespuesta>("api/trabajo/generar", new GenerarPropuesta(Estado.Revision, nuevos)));
    public Task AnularPreparacion() => Ejecutar(async () => await Enviar<RevisionRespuesta>("api/trabajo/anular-preparacion", new AccionTrabajo(Estado.Revision)));
    public Task Realizar() => Ejecutar(async () => await Enviar<RevisionRespuesta>("api/trabajo/realizar", new AccionTrabajo(Estado.Revision)));
    public Task CambiarEstado(long id, string estado) => Ejecutar(async () => await Enviar<RevisionRespuesta>($"api/trabajo/pedidos/{id}", new CambiarEstadoPedido(Estado.Revision, estado), HttpMethod.Put));
    private Task GuardarConfiguracion(ConfiguracionBodegas config) => Ejecutar(async () =>
        await Enviar<RevisionRespuesta>("api/trabajo/configuracion", new GuardarConfiguracion(Estado.Revision, config), HttpMethod.Put));
    public Task<PedidoDetalle?> Detalle(long id) => http.GetFromJsonAsync<PedidoDetalle>($"api/trabajo/pedidos/{id}");
    public Task<PaginaHistorial?> Historial(string codigo, string estado, string desde, string hasta, int pagina = 1)
    {
        var parametros = new Dictionary<string, string> { ["codigo"] = codigo, ["estado"] = estado, ["desde"] = desde, ["hasta"] = hasta, ["pagina"] = pagina.ToString() };
        var query = string.Join("&", parametros.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return http.GetFromJsonAsync<PaginaHistorial>("api/trabajo/historial?" + query);
    }
    public Task<List<UsuarioSesion>?> Usuarios() => http.GetFromJsonAsync<List<UsuarioSesion>>("api/usuarios");
    public async Task CrearUsuario(NuevoUsuario usuario) => await Enviar<object>("api/usuarios", usuario);
}
