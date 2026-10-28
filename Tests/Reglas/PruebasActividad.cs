using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

public static class PruebasActividad
{
    public static void Ejecutar(Action<bool, string> verificar)
    {
        var fecha = new DateOnly(2026, 10, 27);
        var actividad = new ActividadService();
        var pedido = new PedidoService();
        var inventario = new InventarioService(new ExcelImportacionService(), pedido, actividad);
        using var baseExcel = LibroPrueba.Crear(
            ["COD_ITEM", "DESCRIPCION", "MARCA", "B4", "B1", "B40"],
            ["ANTIGUO", "Producto antiguo", "Marca", 5, 100, 200],
            ["AGOTADO", "Agotado", "Marca", 0, 20, 0],
            ["RECIENTE", "Reciente", "Marca", 8, 10, 5],
            ["DUP", "Duplicado", "Marca", 5, 10, 0],
            ["INVALIDO", "Dato inválido", "Marca", 5, 10, 0],
            ["SIN-DATO", "Sin dato", "Marca", 5, 10, 0],
            ["SIN-STOCK-REPORTE", "Stock discrepante", "Marca", 5, 10, 0]);
        inventario.Importar(baseExcel, "Base");
        ProductoInventario Producto(string codigo) => inventario.Productos.Single(p => p.Codigo == codigo);
        using var reporte = LibroPrueba.Crear(
            ["Reporte B4"],
            ["COD_ITEM", "STOCK", "DIAS_SIN_ACTIVIDAD", "ULTIMO_MOVIMIENTO"],
            ["_x0002_ANTIGUO", "5,00", "180.00", "30/4/2026"],
            ["AGOTADO", 5, 400, null],
            ["RECIENTE", 8, "22,00", "5/10/2026"],
            ["DUP", 5, 200, null],
            ["dup", 5, 1, null],
            ["INVALIDO", 5, -10, null],
            ["SIN-STOCK-REPORTE", 0, 300, null],
            ["NO-EXISTE", 5, 100, null]);
        pedido.Agregar(Producto("ANTIGUO"), new ConfiguracionBodegas(), 10, 1);
        actividad.Importar(reporte, "Movimientos B4", fecha, inventario.Productos);
        verificar(pedido.TotalProductos == 1, "Cargar movimientos conserva el pedido existente");
        verificar(actividad.Obtener("antiguo")?.DiasSinActividad == 180, "Limpia caracteres de control y cruza códigos sin distinguir mayúsculas");
        verificar(actividad.DebeApartar(Producto("ANTIGUO"), 180), "Aparta al alcanzar exactamente 180 días y tener stock en ambos reportes");
        verificar(!actividad.DebeApartar(Producto("ANTIGUO"), 365), "Cambiar el umbral recupera los productos con menos días");
        verificar(!actividad.DebeApartar(Producto("AGOTADO"), 180), "No aparta productos agotados en el inventario actual");
        verificar(!actividad.DebeApartar(Producto("SIN-STOCK-REPORTE"), 180), "No aparta si el reporte de actividad no tenía stock");
        verificar(!actividad.DebeApartar(Producto("RECIENTE"), 180), "Conserva productos de movimiento reciente");
        verificar(actividad.Obtener("RECIENTE")?.UltimoMovimiento == new DateOnly(2026, 10, 5), "Interpreta fechas como día/mes/año");
        verificar(!actividad.DebeApartar(Producto("SIN-DATO"), 180), "Un código ausente no se interpreta como falta de ventas");
        verificar(actividad.Obtener("DUP") is null && actividad.Obtener("INVALIDO") is null, "Duplicados y días inválidos no pueden ocultar productos");
        verificar(actividad.Actual!.FilasOmitidas == 3 && actividad.Actual.Coincidencias == 4, "Informa filas omitidas y coincidencias reales");
        actividad.Incluir("ANTIGUO");
        verificar(!actividad.DebeApartar(Producto("ANTIGUO"), 180), "Incluir esta vez recupera un producto sin cambiar el reporte");
        using var nuevoReporte = new MemoryStream(reporte.ToArray());
        actividad.Importar(nuevoReporte, "Nuevo corte", fecha, inventario.Productos);
        verificar(actividad.DebeApartar(Producto("ANTIGUO"), 180), "Reemplazar el reporte reinicia las excepciones temporales");
        var previo = actividad.Actual;
        using var ajeno = LibroPrueba.Crear(["COD_ITEM", "STOCK", "DIAS_SIN_ACTIVIDAD"], ["OTRO", 2, 200]);
        try { actividad.Importar(ajeno, "Ajeno", fecha, inventario.Productos); verificar(false, "Debió rechazar códigos ajenos"); }
        catch (InvalidDataException) { verificar(ReferenceEquals(previo, actividad.Actual), "Un reporte sin coincidencias conserva el anterior"); }
        using var columnasIncorrectas = LibroPrueba.Crear(["COD_ITEM", "STOCK", "OTRA"], ["ANTIGUO", 2, 200]);
        try { actividad.Importar(columnasIncorrectas, "Incompleto", fecha, inventario.Productos); verificar(false, "Debió rechazar cabeceras incorrectas"); }
        catch (InvalidDataException) { verificar(ReferenceEquals(previo, actividad.Actual), "Un reporte con columnas incorrectas conserva el anterior"); }
        using var ambiguos = LibroPrueba.Crear(
            ["COD_ITEM", "STOCK", "DIAS_SIN_ACTIVIDAD", "ULTIMO_MOVIMIENTO"],
            ["ANTIGUO", 5, 180, null], ["AGOTADO", 5, null, null],
            ["RECIENTE", 5, 0.5, null], ["INVALIDO", 5, 200, "28/10/2026"]);
        actividad.Importar(ambiguos, "Datos incompletos", fecha, inventario.Productos);
        verificar(actividad.Obtener("AGOTADO") is null && actividad.Obtener("RECIENTE") is null && actividad.Obtener("INVALIDO") is null,
            "Días vacíos, fraccionarios y fechas futuras quedan sin información");
        using var nuevaBase = new MemoryStream(baseExcel.ToArray());
        inventario.Importar(nuevaBase, "Inventario nuevo");
        verificar(!actividad.TieneReporte && pedido.TotalProductos == 0, "Un inventario nuevo reinicia pedido y reporte de actividad juntos");
    }
}
