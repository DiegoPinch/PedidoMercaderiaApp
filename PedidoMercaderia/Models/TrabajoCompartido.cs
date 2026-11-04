namespace PedidoMercaderia.Models;

public record UsuarioSesion(string Usuario, string Nombre, string Rol, string Sucursal = "B4");
public record AccesoSolicitud(string Usuario, string Clave, string Nombre = "", string ClaveInstalacion = "");
public record NuevoUsuario(string Usuario, string Nombre, string Clave);
public record SesionRespuesta(UsuarioSesion? Usuario, bool RequiereConfiguracion);
public record CorteResumen(long Id, DateOnly Fecha, string Archivo, string Autor, int Productos, string Estado = "Abierto");
public record PedidoResumen(long Id, DateTime Fecha, string Autor, string Estado, int Productos, long CorteId, string Destino = "B4");
public record PedidoDetalle(PedidoResumen Resumen, List<ItemPedido> Items);
public record PropuestaPedido(string Codigo, string Descripcion, string Marca, string Tipo, string Motivo,
    decimal StockActual, decimal? StockAnterior, decimal? StockUltimoPedido, string Origen, decimal Cantidad, bool Rapido,
    int VecesPedido90Dias = 0, decimal CantidadPedida90Dias = 0, int Prioridad = 0,
    string Confianza = "Inicial", decimal? BajadaDiaria = null, int Intervalos = 0);
public record ProductoFrecuente(string Codigo, string Descripcion, int Pedidos, decimal Cantidad, DateTime UltimoPedido,
    decimal? StockActual, bool Pendiente, bool Excluido);
public record CambioInventario(string Codigo, string Descripcion, string Tipo, decimal? Antes, decimal? Ahora,
    Dictionary<string, decimal> Centros);
public record PaginaHistorial(List<PedidoResumen> Pedidos, int Total, int Pagina, int PorPagina);
public class EstadoTrabajo
{
    public long Revision { get; set; }
    public ResultadoImportacion? Inventario { get; set; }
    public bool InventarioSinCambios { get; set; }
    public ResumenAprendizaje Aprendizaje { get; set; } = new();
    public CorteResumen? Corte { get; set; }
    public bool PedidoAbierto => Corte is not null && Corte.Estado == "Abierto";
    public ReporteActividad? Actividad { get; set; }
    public List<ItemPedido> Borrador { get; set; } = new();
    public ConfiguracionBodegas Configuracion { get; set; } = new();
    public List<PropuestaPedido> Propuestas { get; set; } = new();
    public List<CorteResumen> Cortes { get; set; } = new();
    public List<PedidoResumen> Historial { get; set; } = new();
    public List<ProductoFrecuente> MasPedidos { get; set; } = new();
    public List<CambioInventario> Cambios { get; set; } = new();
    public int DiasHistorial { get; set; } = 90;
}
public record GuardarBorrador(long Revision, List<ItemPedido> Items, long CorteId, List<ItemPedido>? BaseItems = null);
public record BorradorRespuesta(long Revision, List<ItemPedido> Items);
public record GuardarConfiguracion(long Revision, ConfiguracionBodegas Configuracion);
public record ImportarCorte(long Revision, DateOnly Fecha, ResultadoImportacion Inventario);
public record GuardarReporteActividad(long Revision, long CorteId, string Bodega, ReporteActividad? Reporte);
public record AccionTrabajo(long Revision);
public record GenerarPropuesta(long Revision, bool IncluirNuevos);
public record CambiarEstadoPedido(long Revision, string Estado);
public record RevisionRespuesta(long Revision);
