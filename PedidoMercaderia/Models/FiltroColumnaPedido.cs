namespace PedidoMercaderia.Models;

public record ValorColumnaPedido(string Clave, string Texto, decimal? Numero = null);

public class FiltroColumnaPedido
{
    public string Texto { get; set; } = "";
    public string Condicion { get; set; } = "todos";
    public decimal? Desde { get; set; }
    public decimal? Hasta { get; set; }
    // null significa todos; un conjunto vacío significa ninguno.
    public HashSet<string>? Valores { get; set; }
    public bool Activo => !string.IsNullOrWhiteSpace(Texto) || Condicion != "todos" || Valores is not null;

    public FiltroColumnaPedido Copiar() => new()
    {
        Texto = Texto, Condicion = Condicion, Desde = Desde, Hasta = Hasta,
        Valores = Valores is null ? null : new(Valores, StringComparer.OrdinalIgnoreCase)
    };

    public bool Coincide(ValorColumnaPedido valor)
    {
        if (!valor.Texto.Contains(Texto.Trim(), StringComparison.OrdinalIgnoreCase)
            || (Valores is not null && !Valores.Contains(valor.Clave))) return false;
        if (Condicion == "todos") return true;
        if (valor.Numero is not decimal numero || Desde is not decimal desde) return false;
        return Condicion switch
        {
            "igual" => numero == desde,
            "distinto" => numero != desde,
            "mayor" => numero > desde,
            "menor" => numero < desde,
            "minimo" => numero >= desde,
            "maximo" => numero <= desde,
            "entre" => Hasta is decimal hasta && numero >= desde && numero <= hasta,
            _ => false
        };
    }
}
