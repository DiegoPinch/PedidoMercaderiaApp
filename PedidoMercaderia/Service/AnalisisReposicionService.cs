using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

/// <summary>Estimaciones explicables de bajadas netas, nunca ventas inferidas.</summary>
public static class AnalisisReposicionService
{
    public static PropuestaPedido Ajustar(PropuestaPedido propuesta, PerfilReposicion perfil,
        PoliticaReposicion politica, decimal disponible)
    {
        if (!politica.UsarTendencia || propuesta.Tipo is not ("Agotado" or "Bajó stock")) return propuesta;
        var muestras = perfil.Observaciones.OrderBy(o => o.Fecha).ToList();
        if (propuesta.StockActual < 0)
            return propuesta with { Rapido = false, Confianza = "Revisar", Motivo = "El Excel tiene stock negativo. Revisa el dato antes de reponer." };
        if (muestras.Count >= 2 && muestras[^1].Fecha.DayNumber - muestras[^2].Fecha.DayNumber > 31)
            return propuesta with { Rapido = false, Confianza = "Revisar", Motivo = "Pasó más de un mes entre las últimas cargas. Revisa esta bajada antes de usarla como reposición habitual." };
        var intervalos = new List<(decimal Baja, int Dias, bool Agotado)>();
        for (int i = 1; i < muestras.Count; i++)
        {
            var antes = muestras[i - 1]; var ahora = muestras[i];
            var dias = ahora.Fecha.DayNumber - antes.Fecha.DayNumber;
            // Una reposición, ausencia o período sin existencias no revela la demanda.
            if (dias is < 1 or > 31 || antes.Stock is null || ahora.Stock is null
                || antes.Stock <= 0 || ahora.Stock < 0 || ahora.Stock > antes.Stock) continue;
            intervalos.Add((antes.Stock.Value - ahora.Stock.Value, dias, ahora.Stock == 0));
        }
        if (intervalos.Count < 3 || intervalos.Count(i => i.Baja > 0) < 2)
            return propuesta with { Motivo = propuesta.Motivo + " Historial insuficiente para estimar cobertura; se usa solo la bajada reciente.", Intervalos = intervalos.Count };
        var ultimo = intervalos[^1];
        var positivas = intervalos.Take(intervalos.Count - 1).Where(i => i.Baja > 0).Select(i => i.Baja / i.Dias).Order().ToList();
        if (positivas.Count >= 2 && ultimo.Baja / ultimo.Dias > positivas[positivas.Count / 2] * 3)
            return propuesta with { Rapido = false, Confianza = "Revisar", Intervalos = intervalos.Count,
                Motivo = "Bajada inusual frente a tu historial. Puede ser una transferencia o compra excepcional; revisa antes de pedir." };
        decimal suma = 0, diasPonderados = 0;
        for (int i = 0; i < intervalos.Count; i++)
        { suma += intervalos[i].Baja * (i + 1); diasPonderados += intervalos[i].Dias * (i + 1); }
        var diaria = suma / diasPonderados;
        var cobertura = politica.DiasEntrePedidos + politica.DiasTraslado + politica.DiasMargen;
        var objetivo = diaria * cobertura;
        var necesidad = Math.Max(0, objetivo - Math.Max(0, propuesta.StockActual));
        var decisiones = perfil.Decisiones.Where(d => d.Tipo is "Aceptada" or "Ajustada" && d.Sugerida > 0 && d.Elegida > 0).TakeLast(6).ToList();
        string ajuste = "";
        if (decisiones.Count >= 3)
        {
            // Preferencia de compra acotada; no se utiliza como una medida de ventas.
            var factor = Math.Clamp(decisiones.Average(d => d.Elegida / d.Sugerida), 0.5m, 1.5m);
            necesidad *= factor;
            if (Math.Abs(factor - 1) >= .05m) ajuste = $" Ajustada según tus últimas {decisiones.Count} decisiones habituales (factor {factor:0.##}).";
        }
        var cantidad = Math.Min(disponible, Math.Ceiling(necesidad));
        var motivo = $"Bajada neta estimada: {diaria:0.##} unidades/día en {intervalos.Count} intervalos. Cobertura: {cobertura} días ({politica.DiasEntrePedidos} entre pedidos + {politica.DiasTraslado} de traslado + {politica.DiasMargen} de margen). Tienes {propuesta.StockActual:0.##}; sugerimos {cantidad:0.##}, limitada al centro. No equivale a ventas." + ajuste;
        if (cantidad == 0) motivo = "Tu stock cubre el período configurado según las bajadas observadas. No hace falta reponer ahora. " + motivo;
        return propuesta with { Cantidad = cantidad, Rapido = cantidad > 0, BajadaDiaria = diaria,
            Intervalos = intervalos.Count, Confianza = intervalos.Any(i => i.Agotado) ? "Limitada por agotados" : "Con historial", Motivo = motivo };
    }
}
