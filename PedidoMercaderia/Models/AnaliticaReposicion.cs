namespace PedidoMercaderia.Models;

public sealed class PoliticaReposicion
{
    public int DiasEntrePedidos { get; set; } = 7;
    public int DiasTraslado { get; set; } = 2;
    public int DiasMargen { get; set; } = 2;
    public bool UsarTendencia { get; set; } = true;
    public void Validar()
    {
        if (DiasEntrePedidos is < 1 or > 90 || DiasTraslado is < 0 or > 90 || DiasMargen is < 0 or > 30)
            throw new InvalidOperationException("Revisa los días: entre pedidos 1–90, traslado 0–90 y margen 0–30.");
    }
}

public record ObservacionStock(long CorteId, DateOnly Fecha, decimal? Stock);
public record DecisionReposicion(long CorteId, long? PedidoId, string Tipo, decimal Sugerida, decimal Elegida, DateTime Fecha);
public sealed class PerfilReposicion
{
    public const int MaximoObservaciones = 26;
    public List<ObservacionStock> Observaciones { get; set; } = [];
    public List<DecisionReposicion> Decisiones { get; set; } = [];
    public void Observar(ObservacionStock observacion)
    {
        Observaciones.RemoveAll(o => o.CorteId == observacion.CorteId);
        Observaciones.Add(observacion);
        Observaciones = Observaciones.OrderBy(o => o.Fecha).ThenBy(o => o.CorteId).TakeLast(MaximoObservaciones).ToList();
    }
    public void Registrar(DecisionReposicion decision)
    {
        Decisiones.RemoveAll(d => d.CorteId == decision.CorteId && d.Tipo == decision.Tipo && d.PedidoId == decision.PedidoId);
        Decisiones.Add(decision);
        Decisiones = Decisiones.OrderBy(d => d.Fecha).TakeLast(MaximoObservaciones).ToList();
    }
}

public sealed class ResumenAprendizaje
{
    public int Aceptadas { get; set; }
    public int Ajustadas { get; set; }
    public int Excepcionales { get; set; }
    public int NoSeVenden { get; set; }
    public int ProductosConTendencia { get; set; }
    public DateTime CalculadoUtc { get; set; }
}

public sealed class AnaliticaCalculada
{
    public const int VersionActual = 1;
    public int Version { get; set; } = VersionActual;
    public long? CorteId { get; set; }
    public List<PropuestaPedido> Propuestas { get; set; } = [];
    public List<CambioInventario> Cambios { get; set; } = [];
    public List<ProductoFrecuente> Frecuentes { get; set; } = [];
    public ResumenAprendizaje Aprendizaje { get; set; } = new();
}
