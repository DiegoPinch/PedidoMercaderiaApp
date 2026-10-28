using System.Globalization;
using ExcelDataReader;
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

public class ActividadService
{
    public ReporteActividad? Actual { get; private set; }
    public bool TieneReporte => Actual is not null;
    private readonly HashSet<string> _incluir = new(StringComparer.OrdinalIgnoreCase);

    public ActividadProducto? Obtener(string codigo) =>
        Actual is not null && Actual.Productos.TryGetValue(NormalizarCodigo(codigo), out var dato) ? dato : null;

    public bool DebeApartar(ProductoInventario producto, int dias)
    {
        var dato = Obtener(producto.Codigo);
        // Un agotado puede no moverse porque no hay qué vender. Ante datos contradictorios, conservarlo.
        return dias > 0 && dato is not null && !_incluir.Contains(NormalizarCodigo(producto.Codigo))
            && producto.ObtenerStock("B4") > 0 && dato.StockB4 > 0 && dato.DiasSinActividad >= dias;
    }

    public void Incluir(string codigo) => _incluir.Add(NormalizarCodigo(codigo));

    public void Limpiar()
    {
        Actual = null;
        _incluir.Clear();
    }

    public void Importar(Stream archivo, string nombre, DateOnly fechaReporte,
        IReadOnlyList<ProductoInventario> inventario)
    {
        if (inventario.Count == 0)
            throw new InvalidDataException("Primero importa el inventario de bodegas.");
        if (!archivo.CanRead || !archivo.CanSeek)
            throw new InvalidDataException("No se puede leer el reporte de B4.");
        archivo.Position = 0;
        using var lector = ExcelReaderFactory.CreateReader(archivo);
        var codigosInventario = inventario.Select(p => NormalizarCodigo(p.Codigo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool encontroCabecera = false;
        do
        {
            Dictionary<string, int>? columnas = null;
            var duplicados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultado = new ReporteActividad
            {
                NombreArchivo = nombre, NombreHoja = lector.Name ?? "", FechaReporte = fechaReporte
            };
            int fila = 0;
            while (lector.Read())
            {
                fila++;
                if (columnas is null)
                {
                    var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < lector.FieldCount; i++)
                    {
                        string cabecera = Texto(lector.GetValue(i)).Trim().Replace('\u00a0', ' ').Replace(' ', '_');
                        if (cabecera.Length > 0) mapa.TryAdd(cabecera, i);
                    }
                    if (mapa.ContainsKey("COD_ITEM") && mapa.ContainsKey("STOCK") && mapa.ContainsKey("DIAS_SIN_ACTIVIDAD"))
                    {
                        columnas = mapa;
                        encontroCabecera = true;
                    }
                    else if (fila >= 50) break;
                    continue;
                }

                string codigo = NormalizarCodigo(Texto(lector.GetValue(columnas["COD_ITEM"])));
                object? stockValor = lector.GetValue(columnas["STOCK"]);
                object? diasValor = lector.GetValue(columnas["DIAS_SIN_ACTIVIDAD"]);
                if (codigo.Length == 0 && string.IsNullOrWhiteSpace(Texto(stockValor)) && string.IsNullOrWhiteSpace(Texto(diasValor)))
                    continue;
                resultado.FilasLeidas++;
                if (codigo.Length == 0) continue;
                // Los códigos repetidos son ambiguos: ninguno debe ocultar un producto por error.
                if (!duplicados.Add(codigo))
                {
                    resultado.Productos.Remove(codigo);
                    continue;
                }
                if (!Numero(stockValor, out decimal stock) || !Numero(diasValor, out decimal dias)
                    || dias < 0 || dias > int.MaxValue || decimal.Truncate(dias) != dias)
                    continue;

                DateOnly? ultimo = null;
                if (columnas.TryGetValue("ULTIMO_MOVIMIENTO", out int indice))
                {
                    object? valor = lector.GetValue(indice);
                    if (valor is DateTime fecha) ultimo = DateOnly.FromDateTime(fecha);
                    else if (DateOnly.TryParseExact(Texto(valor).Trim(),
                        ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var fechaTexto)) ultimo = fechaTexto;
                    // Una fecha futura respecto al corte es contradictoria; conservar el ítem como sin información.
                    if (ultimo > fechaReporte) continue;
                }

                resultado.Productos.Add(codigo, new ActividadProducto
                {
                    Codigo = codigo, StockB4 = stock, DiasSinActividad = (int)dias, UltimoMovimiento = ultimo
                });
            }
            if (resultado.Productos.Count == 0) continue;
            resultado.Coincidencias = resultado.Productos.Keys.Count(codigosInventario.Contains);
            if (resultado.Coincidencias == 0) continue;
            // Sustituir solo después de leer y validar: un archivo fallido no borra el reporte anterior.
            Actual = resultado;
            _incluir.Clear();
            return;
        } while (lector.NextResult());

        throw new InvalidDataException(encontroCabecera
            ? "El reporte no contiene datos válidos que coincidan con el inventario. Revisa los códigos, el stock y los días sin actividad."
            : "Faltan las columnas COD_ITEM, STOCK y DIAS_SIN_ACTIVIDAD del reporte de B4.");
    }

    private static string NormalizarCodigo(string valor) => new string(valor.Where(c => !char.IsControl(c)).ToArray()).Trim();
    private static string Texto(object? valor) => Convert.ToString(valor, CultureInfo.InvariantCulture) ?? "";

    private static bool Numero(object? valor, out decimal numero)
    {
        numero = 0;
        string texto = Texto(valor).Trim();
        if (texto.Length == 0) return false;
        return decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out numero)
            || decimal.TryParse(texto, NumberStyles.Number, CultureInfo.GetCultureInfo("es-EC"), out numero);
    }
}
