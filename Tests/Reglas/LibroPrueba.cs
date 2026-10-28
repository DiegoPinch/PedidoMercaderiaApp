using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

// Excel mínimo con datos sintéticos; no lee ni carga archivos del usuario.
public static class LibroPrueba
{
    public static MemoryStream Crear(params object?[][] filas)
    {
        var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
        {
            void Agregar(string nombre, string contenido)
            {
                using var escritor = new StreamWriter(zip.CreateEntry(nombre).Open());
                escritor.Write(contenido);
            }
            Agregar("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Agregar("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Agregar("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Prueba" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Agregar("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var datos = new XElement(ns + "sheetData");
            for (int r = 0; r < filas.Length; r++)
            {
                var fila = new XElement(ns + "row", new XAttribute("r", r + 1));
                for (int c = 0; c < filas[r].Length; c++)
                {
                    object? valor = filas[r][c];
                    if (valor is null) continue;
                    var celda = new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + c)}{r + 1}"));
                    if (valor is string texto)
                    {
                        celda.Add(new XAttribute("t", "inlineStr"));
                        celda.Add(new XElement(ns + "is", new XElement(ns + "t", texto)));
                    }
                    else celda.Add(new XElement(ns + "v", Convert.ToString(valor, CultureInfo.InvariantCulture)));
                    fila.Add(celda);
                }
                datos.Add(fila);
            }
            Agregar("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", datos).ToString());
        }
        memoria.Position = 0;
        return memoria;
    }
}
