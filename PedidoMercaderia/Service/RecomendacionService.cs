using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

public static class RecomendacionService
{
    public static List<PropuestaPedido> Crear(IReadOnlyList<ProductoInventario> actual,
        IReadOnlyList<ProductoInventario>? anterior, IReadOnlyList<ProductoInventario>? ultimoPedido,
        ISet<string> conocidosAntes, ISet<string> pendientes, ConfiguracionBodegas configuracion,
        IReadOnlyList<ProductoFrecuente>? frecuentes = null)
    {
        var previos = anterior?.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase) ?? new();
        var pedidos = ultimoPedido?.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase) ?? new();
        var resultado = new List<PropuestaPedido>();
        var historial = frecuentes?.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase) ?? new();
        foreach (var p in actual)
        {
            if (configuracion.ProductosNoSugerir.Contains(p.Codigo) || pendientes.Contains(p.Codigo)) continue;
            // Si faltan columnas en un corte no interpretamos su ausencia como stock cero.
            if (!p.Existencias.ContainsKey(configuracion.BodegaLocal)) continue;
            var origen = configuracion.OrigenesHabilitados.Select(b => (Bodega: b, Disponible: ReglasAbastecimiento.Disponible(p, b, 1)))
                .OrderByDescending(x => x.Disponible).ThenBy(x => x.Bodega).FirstOrDefault();
            if (origen.Disponible <= 0) continue;
            previos.TryGetValue(p.Codigo, out var previo);
            pedidos.TryGetValue(p.Codigo, out var pedido);
            decimal local = p.ObtenerStock(configuracion.BodegaLocal);
            decimal? antes = previo?.Existencias.ContainsKey(configuracion.BodegaLocal) == true ? previo.ObtenerStock(configuracion.BodegaLocal) : null;
            decimal? alPedir = pedido?.Existencias.ContainsKey(configuracion.BodegaLocal) == true ? pedido.ObtenerStock(configuracion.BodegaLocal) : null;
            // Una referencia vieja de otro pedido no debe inflar la reposición de esta semana.
            decimal baja = Math.Max(0, (antes ?? local) - local);
            historial.TryGetValue(p.Codigo, out var frecuente);
            bool nuevo = anterior is not null && !conocidosAntes.Contains(p.Codigo);
            bool regresoCentro = previo is not null && configuracion.OrigenesHabilitados.All(b => previo.Existencias.ContainsKey(b))
                && configuracion.OrigenesHabilitados.All(b => ReglasAbastecimiento.Disponible(previo, b, 1) <= 0);
            string tipo, motivo; decimal cantidad; bool rapido;
            if (nuevo)
            { tipo = "Nuevo"; motivo = "Código no visto en cargas anteriores. Revisar si conviene probarlo."; cantidad = 3; rapido = false; }
            else if (baja > 0)
            { tipo = local <= 0 ? "Agotado" : "Bajó stock"; motivo = $"{configuracion.Nombre(configuracion.BodegaLocal)} pasó de {antes:0.##} a {local:0.##}: bajó {baja:0.##}. Se propone reponer esa diferencia, limitada al disponible del centro."; cantidad = baja; rapido = true; }
            else if (local <= 0 && previo is not null)
            { tipo = "Sin stock"; motivo = "Ya estaba sin stock en el corte anterior. Revisar manualmente: no hay una bajada nueva que justifique repetirlo."; cantidad = 3; rapido = false; }
            else if (regresoCentro)
            { tipo = "Disponible otra vez"; motivo = "Volvió a estar disponible en los centros autorizados. Revisar antes de pedir."; cantidad = 3; rapido = false; }
            else continue;
            if (frecuente is not null) motivo += $" Aparece en {frecuente.Pedidos} pedidos de los últimos 90 días ({frecuente.Cantidad:0.##} unidades solicitadas).";
            var prioridad = (tipo == "Agotado" ? 1000 : rapido ? 500 : 0) + Math.Min(frecuente?.Pedidos ?? 0, 100);
            resultado.Add(new(p.Codigo, p.Descripcion, p.Marca, tipo, motivo, local, antes, alPedir,
                origen.Bodega, Math.Min(cantidad, origen.Disponible), rapido, frecuente?.Pedidos ?? 0, frecuente?.Cantidad ?? 0, prioridad));
        }
        return resultado.OrderByDescending(x => x.Prioridad).ThenBy(x => x.StockActual).ThenBy(x => x.Codigo).ToList();
    }

    public static List<CambioInventario> Comparar(IReadOnlyList<ProductoInventario> actual,
        IReadOnlyList<ProductoInventario>? anterior, ISet<string> conocidos, ConfiguracionBodegas config)
    {
        if (anterior is null) return [];
        var previos = anterior.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        var actuales = actual.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        var cambios = new List<CambioInventario>();
        foreach (var p in actual)
        {
            previos.TryGetValue(p.Codigo, out var previo);
            decimal? antes = previo?.Existencias.ContainsKey(config.BodegaLocal) == true ? previo.ObtenerStock(config.BodegaLocal) : null;
            decimal? ahora = p.Existencias.ContainsKey(config.BodegaLocal) ? p.ObtenerStock(config.BodegaLocal) : null;
            var tipo = !conocidos.Contains(p.Codigo) ? "Nuevo" : previo is null ? "Volvió al Excel"
                : antes > 0 && ahora <= 0 ? "Agotado" : ahora < antes ? "Bajó stock" : ahora > antes ? "Subió stock" : "";
            if (tipo == "") continue;
            cambios.Add(new(p.Codigo, p.Descripcion, tipo, antes, ahora,
                config.OrigenesHabilitados.ToDictionary(b => b, b => ReglasAbastecimiento.Disponible(p, b, 1))));
        }
        foreach (var p in anterior.Where(p => !actuales.ContainsKey(p.Codigo)))
            cambios.Add(new(p.Codigo, p.Descripcion, "Ausente del Excel", p.Existencias.ContainsKey(config.BodegaLocal) ? p.ObtenerStock(config.BodegaLocal) : null, null, new()));
        return cambios.OrderBy(c => c.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
