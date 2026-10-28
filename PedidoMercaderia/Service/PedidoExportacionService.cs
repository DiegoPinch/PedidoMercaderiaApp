using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

public static class PedidoExportacionService
{
    public static byte[] Crear(IEnumerable<ItemPedido> pedido, IEnumerable<ProductoInventario> inventario,
        ConfiguracionBodegas configuracion)
    {
        var items = pedido.OrderBy(i => i.BodegaOrigen, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.CodigoProducto, StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) throw new InvalidOperationException("Agrega productos a Mi pedido antes de descargar el Excel.");
        var catalogo = inventario.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!catalogo.TryGetValue(item.CodigoProducto, out var producto)
                || !configuracion.OrigenesHabilitados.Contains(item.BodegaOrigen, StringComparer.OrdinalIgnoreCase)
                || item.CantidadSolicitada <= 0 || item.ReservaCentro < 0
                || item.CantidadSolicitada > ReglasAbastecimiento.Disponible(producto, item.BodegaOrigen, item.ReservaCentro))
                throw new InvalidOperationException($"Revisa la cantidad y el origen de {item.CodigoProducto} antes de exportar.");
        }

        using var archivo = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(archivo, SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook();
            libro.AddNewPart<WorkbookStylesPart>().Stylesheet = CrearEstilos();
            var parte = libro.AddNewPart<WorksheetPart>();
            var datos = new SheetData();
            var cabecera = new Row { RowIndex = 1, Height = 30, CustomHeight = true };
            string[] titulos = ["Código", "Descripción", "Desde", "Cantidad", "Comentario"];
            for (int c = 0; c < titulos.Length; c++) cabecera.Append(Texto($"{(char)('A' + c)}1", titulos[c], 1));
            datos.Append(cabecera);
            uint fila = 2;
            foreach (var item in items)
            {
                var producto = catalogo[item.CodigoProducto];
                var comentario = item.Comentario ?? "";
                uint baseEstilo = fila % 2 == 0 ? 2u : 5u;
                var lineas = Math.Max(Lineas(producto.Descripcion, 53), Math.Max(Lineas(comentario, 36), Lineas(item.CodigoProducto, 25)));
                var row = new Row { RowIndex = fila, Height = Math.Min(409, Math.Max(30, lineas * 15 + 10)), CustomHeight = true };
                row.Append(Texto($"A{fila}", item.CodigoProducto, baseEstilo + 1),
                    Texto($"B{fila}", producto.Descripcion, baseEstilo),
                    Texto($"C{fila}", item.BodegaOrigen.Equals("B1", StringComparison.OrdinalIgnoreCase) ? "B1 · Matriz" : "B40 · Ebenezer", baseEstilo),
                    new Cell { CellReference = $"D{fila}", DataType = CellValues.Number, StyleIndex = baseEstilo + 2,
                        CellValue = new CellValue(item.CantidadSolicitada.ToString(CultureInfo.InvariantCulture)) },
                    Texto($"E{fila}", comentario, baseEstilo));
                datos.Append(row);
                fila++;
            }
            var vista = new SheetView { WorkbookViewId = 0, ShowGridLines = false };
            vista.Append(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            parte.Worksheet = new Worksheet(
                new SheetProperties(new TabColor { Rgb = "FF1D4E78" }, new PageSetupProperties { FitToPage = true }),
                new SheetDimension { Reference = $"A1:E{fila - 1}" },
                new SheetViews(vista),
                new SheetFormatProperties { DefaultRowHeight = 30 },
                new Columns(Columna(1, 28), Columna(2, 58), Columna(3, 22), Columna(4, 14), Columna(5, 40)),
                datos,
                new AutoFilter { Reference = $"A1:E{fila - 1}" },
                new PrintOptions { HorizontalCentered = true },
                new PageMargins { Left = .3, Right = .3, Top = .4, Bottom = .4, Header = .2, Footer = .2 },
                new PageSetup { Orientation = OrientationValues.Landscape, PaperSize = 9, FitToWidth = 1, FitToHeight = 0 });
            libro.Workbook.Append(new Sheets(new Sheet { Name = "Pedido B4", SheetId = 1, Id = libro.GetIdOfPart(parte) }));
            libro.Workbook.Append(new DefinedNames(
                new DefinedName { Name = "_xlnm.Print_Titles", LocalSheetId = 0, Text = "'Pedido B4'!$1:$1" },
                new DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = $"'Pedido B4'!$A$1:$E${fila - 1}" }));
            libro.Workbook.Save();
        }
        return archivo.ToArray();
    }

    private static int Lineas(string texto, int ancho) => (texto ?? "").Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length / (double)ancho)));
    private static Column Columna(uint indice, double ancho) => new() { Min = indice, Max = indice, Width = ancho, CustomWidth = true };

    private static Cell Texto(string referencia, string valor, uint estilo)
    {
        // Texto explícito conserva ceros iniciales y nunca ejecuta códigos o comentarios como fórmulas.
        var limpio = new StringBuilder();
        foreach (var rune in (valor ?? "").EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 or >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
                limpio.Append(rune.ToString());
        return new Cell { CellReference = referencia, DataType = CellValues.InlineString, StyleIndex = estilo,
            InlineString = new InlineString(new Text(limpio.ToString()) { Space = SpaceProcessingModeValues.Preserve }) };
    }

    private static Stylesheet CrearEstilos()
    {
        Font Fuente(string color, bool negrita = false)
        {
            var fuente = new Font();
            if (negrita) fuente.Append(new Bold());
            fuente.Append(new FontSize { Val = 11 }, new Color { Rgb = color }, new FontName { Val = "Arial" });
            return fuente;
        }
        Fill Relleno(string color) => new(new PatternFill(new ForegroundColor { Rgb = color }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid });
        CellFormat Formato(uint font, uint fill, bool numero = false) => new(new Alignment
        {
            Vertical = VerticalAlignmentValues.Center, Horizontal = numero ? HorizontalAlignmentValues.Right : HorizontalAlignmentValues.Left,
            WrapText = true, Indent = 1
        }) { FontId = font, FillId = fill, BorderId = 0, FormatId = 0, NumberFormatId = numero ? 0u : 49u,
            ApplyAlignment = true, ApplyFont = true, ApplyFill = true, ApplyNumberFormat = true };
        var encabezado = Formato(1, 2);
        encabezado.Alignment!.Horizontal = HorizontalAlignmentValues.Center;
        encabezado.Alignment.Indent = 0;
        return new Stylesheet(
            new Fonts(Fuente("FF111111"), Fuente("FFFFFFFF", true), Fuente("FF000000", true)) { Count = 3 },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), Relleno("FF1D4E78"), Relleno("FFF0F5FA")) { Count = 4 },
            new Borders(new Border()) { Count = 1 },
            new CellStyleFormats(new CellFormat()) { Count = 1 },
            new CellFormats(new CellFormat(), encabezado, Formato(0, 0), Formato(2, 0), Formato(2, 0, true),
                Formato(0, 3), Formato(2, 3), Formato(2, 3, true)) { Count = 8 },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    }
}
