using Microsoft.JSInterop;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
int comprobaciones = 0;
void Verificar(bool condicion, string caso)
{
    if (!condicion) throw new Exception(caso);
    comprobaciones++;
    Console.WriteLine($"OK: {caso}");
}

ProductoInventario Producto(decimal local, decimal matriz, decimal ebenezer, decimal bame) => new()
{
    Codigo = "CABLE-01", Descripcion = "Cable", UnidadMedida = "METRO",
    Existencias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["B4"] = local, ["B1"] = matriz, ["B40"] = ebenezer, ["B39"] = bame
    }
};

var configuracion = new ConfiguracionBodegas();
var filtros = new FiltrosPedido();
bool Candidato(ProductoInventario producto) => ReglasAbastecimiento.EsOportunidad(producto, configuracion, filtros);
Verificar(configuracion.OrigenesHabilitados.ToHashSet().SetEquals(["B1", "B40"]), "Solo Matriz y Ebenezer son orígenes por defecto");
Verificar(!Candidato(Producto(0, 0, 0, 1000)), "Stock solo en Bame no genera oportunidades");
Verificar(Candidato(Producto(10, 100, 200, 1000)), "Cable con 10 en B4 y stock en los centros sí aparece");
Verificar(Candidato(Producto(0, 2, 0, 0)), "Sin stock en B4 y total nacional menor que 20 sí aparece");
Verificar(Candidato(Producto(20, 10, 0, 0)), "Tener 20 en B4 no descarta automáticamente el producto");
Verificar(!Candidato(Producto(0, 1, 1, 1000)), "No se ofrece stock reservado");
filtros.ReservaCentro = 0;
Verificar(Candidato(Producto(0, 1, 0, 0)), "Reserva cero permite pedir la última unidad");
filtros.ReservaCentro = 1;
var cable = Producto(10, 100, 200, 1000);
decimal participacion = ReglasAbastecimiento.Participacion(cable, configuracion);
cable.Existencias["B39"] = 1000000;
Verificar(ReglasAbastecimiento.Participacion(cable, configuracion) == participacion, "Bame no cambia el porcentaje ni la prioridad");
cable.Existencias["B2"] = 50000;
Verificar(ReglasAbastecimiento.Participacion(cable, configuracion) == participacion, "Otras sucursales no cambian el porcentaje");
Verificar(Candidato(Producto(50, 2, 2, 1000000)), "Ningún porcentaje oculta productos disponibles");
filtros.UsarMaximoB4 = true;
Verificar(!Candidato(cable), "El máximo de B4 se aplica solo si está activado");
filtros.UsarMaximoB4 = false;
filtros.MinimoTransferible = 10;
Verificar(!Candidato(Producto(0, 6, 6, 1000)), "El mínimo transferible se cumple en un centro, no sumando ambos");
filtros.MinimoTransferible = 1;
configuracion.ProductosNoSugerir.Add(cable.Codigo);
Verificar(!Candidato(cable), "No sugerir excluye el producto");
configuracion.ProductosNoSugerir.Clear();
Verificar(Candidato(cable), "Reactivar recupera el producto");

var pedido = new PedidoService();
Verificar(pedido.Agregar(cable, configuracion, 20, 1), "Se puede solicitar 20 unidades aunque B4 tenga 10");
Verificar(pedido.Obtener(cable.Codigo)!.BodegaOrigen == "B40", "Elige Ebenezer por mayor disponibilidad, no Bame");
Verificar(!pedido.Agregar(cable, configuracion, 20, 1), "No duplica un producto ya añadido");
Verificar(!pedido.CambiarCantidad(cable, 200), "Editar no permite consumir la reserva");
Verificar(pedido.CambiarCantidad(cable, 199), "Permite exactamente el stock transferible");
Verificar(!pedido.CambiarCantidad(cable, 0), "Rechaza cantidad cero");
Verificar(!pedido.CambiarCantidad(cable, -1), "Rechaza cantidad negativa");
Verificar(!pedido.CambiarBodega(cable, "B39", configuracion), "No permite cambiar origen a Bame");
Verificar(pedido.CambiarBodega(cable, "B1", configuracion), "Permite cambiar a Matriz");
Verificar(pedido.Obtener(cable.Codigo)!.CantidadSolicitada == 99, "Cambiar de centro ajusta cantidad respetando reserva");
cable.Existencias["B1"] = 1;
Verificar(pedido.ValidarBorrador([cable], configuracion) > 0, "Revalida al agotarse la disponibilidad del origen");
Verificar(pedido.Obtener(cable.Codigo)!.BodegaOrigen == "B40", "Reasigna a un centro permitido con disponibilidad");
configuracion.BodegasOrigen.Remove("B40");
pedido.ValidarBorrador([cable], configuracion);
Verificar(pedido.TotalProductos == 0, "Quita el producto si ningún centro habilitado puede abastecerlo");
configuracion.BodegasOrigen.Add("B39");
Verificar(!pedido.Agregar(Producto(0, 0, 0, 1000), configuracion, 10, 0), "Incluso una configuración manipulada no permite pedir a Bame");
pedido.Omitidos.Add("CABLE-01");
pedido.Limpiar();
Verificar(pedido.Omitidos.Count == 0, "Un inventario nuevo reinicia los omitidos temporales");

configuracion = new ConfiguracionBodegas();
var decimalProducto = Producto(0, 1.5m, 0, 0);
Verificar(pedido.Agregar(decimalProducto, configuracion, 0.5m, 1), "Permite cantidades fraccionarias");
Verificar(pedido.TotalUnidades == 0.5m, "Conserva la precisión decimal");

var js = new AlmacenamientoFalso("""
    {"BodegaLocal":"B4","BodegasOrigen":["B1","B39","B40"],"ProductosNoSugerir":["cable-01"]}
    """);
var servicio = new ConfiguracionService(js);
await servicio.CargarAsync();
Verificar(servicio.Actual.BodegasOrigen.SetEquals(["B1", "B40"]), "Migra configuración antigua eliminando B39");
Verificar(servicio.Actual.ProductosNoSugerir.Contains("CABLE-01"), "La migración conserva exclusiones sin distinguir mayúsculas");
await servicio.GuardarAsync();
Verificar(js.Json is not null && !js.Json.Contains("B39"), "Guardar persiste solo orígenes autorizados");
Verificar(servicio.ObtenerNombreBodega("B40") == "Ebenezer", "B40 se identifica como Ebenezer");
Verificar(servicio.ObtenerNombreBodega("B39") == "Bame", "B39 se identifica como Bame");

PruebasActividad.Ejecutar(Verificar);
PruebasExportacion.Ejecutar(Verificar);
Console.WriteLine($"{comprobaciones} comprobaciones correctas.");

if (args.Length == 2 && args[0] == "--vista")
    await VistaPrevia.Generar(Path.GetFullPath(args[1]));

sealed class AlmacenamientoFalso(string? json) : IJSRuntime
{
    public string? Json { get; private set; } = json;
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "localStorage.getItem") return ValueTask.FromResult((TValue)(object)Json!);
        if (identifier == "localStorage.setItem") Json = (string?)args![1];
        return ValueTask.FromResult(default(TValue)!);
    }
}
