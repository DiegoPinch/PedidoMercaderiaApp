using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ExcelDataReader;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

public static class PruebasExportacion
{
    public static void Ejecutar(Action<bool, string> verificar)
    {
        var configuracion = new ConfiguracionBodegas();
        var productos = new[]
        {
            new ProductoInventario { Codigo = "000123", Descripcion = "CABLE UTP INTERIOR CAT5E 100% COBRE BLANCO 100M TECLAM", Existencias = new() { ["B1"] = 100, ["B40"] = 200 } },
            new ProductoInventario { Codigo = "=COD-02", Descripcion = "Conector RJ45 & adaptador <especial>", Existencias = new() { ["B1"] = 40 } }
        };
        var pedido = new PedidoService();
        pedido.Agregar(productos[0], configuracion, 10, 1);
        pedido.Agregar(productos[1], configuracion, .5m, 0);
        pedido.Obtener("000123")!.Comentario = "Enviar rollos completos. Coordinar entrega con la sucursal.";
        pedido.CambiarCantidad(productos[0], 20);
        pedido.CambiarBodega(productos[0], "B1", configuracion);
        verificar(pedido.Obtener("000123")!.Comentario.StartsWith("Enviar"), "Cambiar cantidad y origen conserva el comentario");
        pedido.CambiarBodega(productos[0], "B40", configuracion);
        pedido.Obtener("=COD-02")!.Comentario = "=1+1";
        byte[] contenido = PedidoExportacionService.Crear(pedido.Items, productos, configuracion);
        using (var documento = SpreadsheetDocument.Open(new MemoryStream(contenido), false))
        {
            var errores = new OpenXmlValidator().Validate(documento).ToList();
            verificar(errores.Count == 0, "Excel válido según OpenXML: " + string.Join("; ", errores.Select(e => e.Description)));
            var hoja = documento.WorkbookPart!.WorksheetParts.Single().Worksheet!;
            var filas = hoja.GetFirstChild<SheetData>()!.Elements<Row>().ToList();
            verificar(filas.Count == 3 && filas.All(r => r.Elements<Cell>().Count() == 5), "Exporta únicamente cinco columnas y todos los artículos pedidos");
            verificar(filas[0].Elements<Cell>().Select(c => c.InnerText).SequenceEqual(new[] { "Código", "Descripción", "Desde", "Cantidad", "Comentario" }), "Encabezados claros y sin datos extra");
            verificar(!hoja.Descendants<CellFormula>().Any(), "Códigos y comentarios que comienzan por = se exportan como texto");
            verificar(hoja.Descendants<Pane>().Single().State?.Value == PaneStateValues.Frozen && hoja.GetFirstChild<AutoFilter>()!.Reference == "A1:E3", "Congela encabezados y permite filtrar todo el pedido");
            verificar(hoja.Descendants<Cell>().Single(c => c.CellReference == "A3").InnerText == "000123", "Conserva los ceros iniciales del código");
        }
        using (var lector = ExcelReaderFactory.CreateReader(new MemoryStream(contenido)))
        {
            lector.Read(); lector.Read();
            verificar(lector.GetString(0) == "=COD-02" && lector.GetDouble(3) == .5 && lector.GetString(4) == "=1+1", "Otro lector recupera texto literal y cantidad decimal numérica");
            lector.Read();
            verificar(lector.GetString(0) == "000123", "Un lector independiente conserva el código completo con ceros iniciales");
            verificar(lector.GetString(2) == "B40 · Ebenezer" && lector.GetString(4).StartsWith("Enviar"), "Exporta el origen elegido y el comentario de cada fila");
        }
        void Rechaza(Action accion, string nombre)
        {
            try { accion(); verificar(false, nombre); }
            catch (InvalidOperationException) { verificar(true, nombre); }
        }
        Rechaza(() => PedidoExportacionService.Crear([], productos, configuracion), "Impide exportar pedidos vacíos");
        pedido.Obtener("000123")!.BodegaOrigen = "B39";
        Rechaza(() => PedidoExportacionService.Crear(pedido.Items, productos, configuracion), "Impide exportar productos desde Bame");
        pedido.Obtener("000123")!.BodegaOrigen = "B40";
        pedido.Obtener("000123")!.CantidadSolicitada = 201;
        Rechaza(() => PedidoExportacionService.Crear(pedido.Items, productos, configuracion), "Impide exportar cantidades superiores a la disponibilidad");
        Rechaza(() => PedidoExportacionService.Crear(pedido.Items, [], configuracion), "No omite silenciosamente un producto ausente del inventario");
        pedido.Quitar("000123"); pedido.Agregar(productos[0], configuracion, 10);
        verificar(pedido.Obtener("000123")!.Comentario == "", "Quitar y volver a agregar no recupera comentarios anteriores");
        if (Environment.GetEnvironmentVariable("PEDIDO_EXPORT_SAMPLE") is string ruta)
            File.WriteAllBytes(ruta, contenido);
    }
}
