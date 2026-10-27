
using System.Globalization;
using ExcelDataReader;
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services
{
    public class ExcelImportacionService
    {
        public ResultadoImportacion Importar(
            Stream archivo,
            string nombreArchivo)
        {
            if (!archivo.CanRead || !archivo.CanSeek)
                throw new InvalidDataException(
                    "No se puede leer el archivo seleccionado.");

            archivo.Position = 0;

            using var lector = ExcelReaderFactory.CreateReader(archivo);

            do
            {
                var resultado = new ResultadoImportacion
                {
                    NombreArchivo = nombreArchivo,
                    NombreHoja = lector.Name ?? "Hoja sin nombre"
                };

                if (!LeerHoja(lector, resultado))
                    continue;

                if (resultado.Productos.Count == 0)
                    throw new InvalidDataException(
                        "La hoja no contiene productos válidos.");

                return resultado;
            }
            while (lector.NextResult());

            throw new InvalidDataException(
                "No se encontraron las columnas necesarias " +
                "para importar el inventario.");
        }

        private static bool LeerHoja(
            IExcelDataReader lector,
            ResultadoImportacion resultado)
        {
            Columnas? columnas = null;
            int filasIniciales = 0;

            var codigos = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            // Solo almacenaremos existencias distintas de cero.
            while (lector.Read())
            {
                if (columnas is null)
                {
                    filasIniciales++;

                    columnas = Columnas.Detectar(lector);

                    if (columnas is not null)
                    {
                        resultado.BodegasDetectadas = columnas.Bodegas
                            .Select(b => b.Codigo)
                            .ToList();
                    }
                    else if (filasIniciales >= 50)
                    {
                        return false;
                    }

                    continue;
                }

                string codigo = LimpiarCodigo(
                    ObtenerTexto(lector, columnas.Codigo));

                string descripcion = ObtenerTexto(
                    lector, columnas.Descripcion).Trim();

                if (string.IsNullOrWhiteSpace(codigo) &&
                    string.IsNullOrWhiteSpace(descripcion))
                {
                    continue;
                }

                resultado.FilasLeidas++;

                if (string.IsNullOrWhiteSpace(codigo) ||
                    string.IsNullOrWhiteSpace(descripcion) ||
                    !codigos.Add(codigo))
                {
                    resultado.FilasOmitidas++;
                    continue;
                }

                var existencias = new Dictionary<string, decimal>();

                bool filaValida = true;
                decimal totalCalculado = 0;

                foreach (var bodega in columnas.Bodegas)
                {
                    object? valor = ObtenerValor(
                        lector, bodega.Indice);

                    if (!IntentarNumero(valor, out decimal cantidad))
                    {
                        filaValida = false;
                        break;
                    }

                    totalCalculado += cantidad;

                    if (cantidad != 0)
                    {
                        existencias.Add(bodega.Codigo, cantidad);
                    }
                }

                if (!filaValida)
                {
                    resultado.FilasOmitidas++;
                    codigos.Remove(codigo);
                    continue;
                }

                var producto = new ProductoInventario
                {
                    Codigo = codigo,

                    Descripcion = descripcion,

                    Referencia = LimpiarCodigo(
                        ObtenerTexto(lector, columnas.Referencia)),

                    Marca = ObtenerTexto(
                        lector, columnas.Marca).Trim(),

                    Grupo = ObtenerTexto(
                        lector, columnas.Grupo).Trim(),

                    Subgrupo = ObtenerTexto(
                        lector, columnas.Subgrupo).Trim(),

                    TipoItem = ObtenerTexto(
                        lector, columnas.TipoItem).Trim(),

                    UnidadMedida = ObtenerTexto(
                        lector, columnas.UnidadMedida).Trim(),

                    UltimoProveedor = ObtenerTexto(
                        lector, columnas.Proveedor).Trim(),

                    Precio = ObtenerNumeroOpcional(
                        lector, columnas.Precio),

                    StockMinimoSistema = ObtenerNumeroOpcional(
                        lector, columnas.StockMinimo),

                    StockMaximoSistema = ObtenerNumeroOpcional(
                        lector, columnas.StockMaximo),

                    Existencias = existencias
                };

                object? valorTotal = ObtenerValor(
                    lector, columnas.Total);

                if (valorTotal is not null &&
                    IntentarNumero(valorTotal, out decimal total))
                {
                    producto.TotalReportado = total;

                    if (Math.Abs(total - totalCalculado) > 0.01m)
                    {
                        resultado.DiferenciasTotales++;
                    }
                }
                else
                {
                    producto.TotalReportado = totalCalculado;
                }

                resultado.Productos.Add(producto);
            }

            return columnas is not null;
        }

        private static object? ObtenerValor(
            IExcelDataReader lector,
            int indice)
        {
            if (indice < 0 || indice >= lector.FieldCount)
                return null;

            return lector.GetValue(indice);
        }

        private static string ObtenerTexto(
            IExcelDataReader lector,
            int indice)
        {
            object? valor = ObtenerValor(lector, indice);

            if (valor is null || valor is DBNull)
                return string.Empty;

            return Convert.ToString(
                valor, CultureInfo.InvariantCulture) ?? "";
        }

        private static decimal ObtenerNumeroOpcional(
            IExcelDataReader lector,
            int indice)
        {
            object? valor = ObtenerValor(lector, indice);

            return IntentarNumero(valor, out decimal numero)
                ? numero
                : 0;
        }

        private static bool IntentarNumero(
            object? valor,
            out decimal numero)
        {
            numero = 0;

            if (valor is null || valor is DBNull)
                return true;

            // Evita convertir cada cantidad numérica a texto.
            switch (valor)
            {
                case decimal d:
                    numero = d;
                    return true;

                case int i:
                    numero = i;
                    return true;

                case long l:
                    numero = l;
                    return true;

                case double d:
                    if (!double.IsFinite(d))
                        return false;

                    try
                    {
                        numero = Convert.ToDecimal(d);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }

                case float f:
                    if (!float.IsFinite(f))
                        return false;

                    try
                    {
                        numero = Convert.ToDecimal(f);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
            }

            string texto = Convert.ToString(
                valor, CultureInfo.InvariantCulture)?.Trim() ?? "";

            if (texto.Length == 0)
                return true;

            if (decimal.TryParse(
                texto,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out numero))
            {
                return true;
            }

            if (decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.GetCultureInfo("es-EC"),
                out numero))
            {
                return true;
            }

            return decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out numero);
        }

        private static string LimpiarCodigo(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return "";

            // Limpia los caracteres de control del sistema.
            return new string(texto
                .Where(c => !char.IsControl(c))
                .ToArray()).Trim();
        }

        private sealed class Columnas
        {
            public int Codigo { get; private set; }
            public int Descripcion { get; private set; }
            public int Referencia { get; private set; }
            public int Marca { get; private set; }
            public int Grupo { get; private set; }
            public int Subgrupo { get; private set; }
            public int TipoItem { get; private set; }
            public int UnidadMedida { get; private set; }
            public int Proveedor { get; private set; }
            public int Precio { get; private set; }
            public int StockMinimo { get; private set; }
            public int StockMaximo { get; private set; }
            public int Total { get; private set; }

            public List<(string Codigo, int Indice)> Bodegas
            { get; private set; } = new();

            public static Columnas? Detectar(
                IExcelDataReader lector)
            {
                var mapa = new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < lector.FieldCount; i++)
                {
                    object? valor = lector.GetValue(i);

                    if (valor is not string nombre)
                        continue;

                    nombre = nombre.Trim()
                        .Replace('\u00A0', ' ')
                        .Replace(' ', '_')
                        .ToUpperInvariant();

                    if (nombre.Length > 0)
                        mapa.TryAdd(nombre, i);
                }

                if (!mapa.ContainsKey("COD_ITEM") ||
                    !mapa.ContainsKey("DESCRIPCION") ||
                    !mapa.ContainsKey("MARCA"))
                {
                    return null;
                }

                var bodegas = new List<(
                    string Codigo, int Indice, int Numero)>();

                foreach (var columna in mapa)
                {
                    string nombre = columna.Key;

                    if (!nombre.StartsWith("B") ||
                        !int.TryParse(nombre[1..], out int numero) ||
                        numero < 1 || numero > 45)
                    {
                        continue;
                    }

                    bodegas.Add((
                        nombre,
                        columna.Value,
                        numero));
                }

                if (bodegas.Count == 0)
                    return null;

                int Buscar(string nombre)
                {
                    return mapa.TryGetValue(nombre, out int indice)
                        ? indice
                        : -1;
                }

                return new Columnas
                {
                    Codigo = Buscar("COD_ITEM"),
                    Descripcion = Buscar("DESCRIPCION"),
                    Referencia = Buscar("REFERENCIA"),
                    Marca = Buscar("MARCA"),
                    Grupo = Buscar("NOMBRE_GRUPO"),
                    Subgrupo = Buscar("NOMBRE_SUBGRUPO"),
                    TipoItem = Buscar("TIPO_ITEM"),
                    UnidadMedida = Buscar("NOM_UNIDAD_MEDIDA"),
                    Proveedor = Buscar("ULT.PROVEEDOR"),
                    Precio = Buscar("PRECIO"),
                    StockMinimo = Buscar("STOCK_MINIMO"),
                    StockMaximo = Buscar("STOCK_MAXIMO"),
                    Total = Buscar("TOTAL_CANTIDAD"),

                    Bodegas = bodegas
                        .OrderBy(b => b.Numero)
                        .Select(b => (b.Codigo, b.Indice))
                        .ToList()
                };
            }
        }
    }
}
