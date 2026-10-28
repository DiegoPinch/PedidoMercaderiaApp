namespace PedidoMercaderia.Models;

public class ActividadProducto
{
    public string Codigo { get; init; } = "";
    public decimal StockB4 { get; init; }
    public int DiasSinActividad { get; init; }
    public DateOnly? UltimoMovimiento { get; init; }
}

public class ReporteActividad
{
    public string NombreArchivo { get; init; } = "";
    public string NombreHoja { get; init; } = "";
    public DateOnly FechaReporte { get; init; }
    public Dictionary<string, ActividadProducto> Productos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int FilasLeidas { get; set; }
    public int FilasOmitidas => FilasLeidas - Productos.Count;
    public int Coincidencias { get; set; }
}
