using System.Reflection;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;

public static class PruebasFiltrosColumnas
{
    public static void Ejecutar(Action<bool, string> verificar)
    {
        var rango = new FiltroColumnaPedido { Condicion = "entre", Desde = .5m, Hasta = 10 };
        verificar(rango.Coincide(new(".5", "0,5", .5m)) && rango.Coincide(new("10", "10", 10))
            && !rango.Coincide(new("0", "0", 0)) && !rango.Coincide(new("", "—")), "Los rangos incluyen ambos límites y distinguen bodegas desactivadas de stock cero");
        var ninguno = new FiltroColumnaPedido { Valores = new(StringComparer.OrdinalIgnoreCase) };
        verificar(!ninguno.Coincide(new("A", "A")), "Desmarcar todos los valores muestra cero resultados");
        var texto = new FiltroColumnaPedido { Texto = "cable", Valores = new(StringComparer.OrdinalIgnoreCase) { "0001" } };
        verificar(texto.Coincide(new("0001", "0001 · CABLE UTP")) && !texto.Coincide(new("1", "1 · CABLE")), "El filtro combina texto y selección exacta conservando ceros iniciales");
        var copia = texto.Copiar(); copia.Valores!.Clear();
        verificar(texto.Valores!.Count == 1, "Cancelar una edición no modifica la selección aplicada");

        // Usa los mismos métodos de la página para verificar la combinación antes de paginar.
        var pedido = new PedidoService();
        var actividad = new ActividadService();
        var inventario = new InventarioService(new ExcelImportacionService(), pedido, actividad);
        var configuracion = new ConfiguracionService();
        configuracion.Restaurar(new() { BodegaLocal = "B4", BodegasOrigen = new(["B1", "B40"]) });
        var filas = new List<object?[]> { new object?[] { "COD_ITEM", "DESCRIPCION", "MARCA", "B4", "B1", "B40" } };
        filas.AddRange(Enumerable.Range(0, 35).Select(n => new object?[] { $"P{n:00}", $"Cable {n}", "Marca", n, n + 1, 100 }));
        using var excel = LibroPrueba.Crear(filas.ToArray());
        inventario.Importar(excel, "Prueba de filtros");
        var tipo = typeof(PruebasFiltrosColumnas).Assembly.GetTypes().Single(t => t.Name == "RevisarPedido");
        var pantalla = Activator.CreateInstance(tipo)!;
        const BindingFlags acceso = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        foreach (var (nombre, servicio) in new (string, object)[] { ("Inventario", inventario), ("Pedido", pedido), ("Actividad", actividad), ("Configuracion", configuracion) })
            tipo.GetProperty(nombre, acceso)!.SetValue(pantalla, servicio);
        object? Invocar(string nombre, params object?[] argumentos) => tipo.GetMethod(nombre, acceso)!.Invoke(pantalla, argumentos);
        void Campo(string nombre, object valor) => tipo.GetField(nombre, acceso)!.SetValue(pantalla, valor);
        T Leer<T>(string nombre) => (T)tipo.GetField(nombre, acceso)!.GetValue(pantalla)!;
        List<ProductoInventario> Resultados() => (List<ProductoInventario>)Invocar("ObtenerFiltrados", new object?[] { null })!;
        void Filtrar(string columna, string condicion, string desde, string hasta = "")
        {
            Invocar("AbrirFiltroColumna", columna);
            Leer<FiltroColumnaPedido>("filtroEnEdicion").Condicion = condicion;
            Campo("numeroDesde", desde); Campo("numeroHasta", hasta);
            Invocar("AplicarFiltroColumna");
        }
        Filtrar("local", "minimo", "30");
        verificar(Resultados().Count == 5 && Resultados()[0].Codigo == "P30", "Filtra todo el inventario, incluidos productos fuera de la primera página");
        Filtrar("B1", "maximo", "32");
        verificar(Resultados().Select(p => p.Codigo).SequenceEqual(new[] { "P30", "P31", "P32" }), "Combina Tengo y Matriz usando la disponibilidad que muestra la tabla, descontando reserva");
        Invocar("AbrirFiltroColumna", "local");
        verificar(Leer<List<ValorColumnaPedido>>("valoresColumna").Count == 33, "Al editar una columna ofrece sus otros valores respetando los filtros de las demás columnas");
        Invocar("CerrarFiltroColumna");
        Filtrar("local", "entre", "10", "2");
        verificar(Leer<string>("errorFiltroColumna").Length > 0 && Resultados().Count == 3, "Un rango invertido conserva el filtro anterior y muestra un error");
        Invocar("CerrarFiltroColumna");
        Invocar("AbrirFiltroColumna", "local"); Invocar("OrdenarColumna", true);
        verificar(Resultados().Select(p => p.Codigo).SequenceEqual(new[] { "P32", "P31", "P30" }), "Ordena las cantidades numéricamente y mantiene los filtros combinados");
        Invocar("LimpiarFiltrosColumnas");
        verificar(Resultados().Count == 35 && Leer<int>("pagina") == 1, "Limpiar recupera los productos y vuelve a la primera página");
        var producto = inventario.Productos.First(p => p.Codigo == "P30");
        pedido.Agregar(producto, configuracion.Actual, 20, 2);
        Invocar("MostrarMiPedido");
        Filtrar("pedir", "igual", "20");
        verificar(Resultados().Single().Codigo == "P30", "Pedir filtra la cantidad elegida del pedido");
        Invocar("AbrirFiltroColumna", "origen");
        Leer<FiltroColumnaPedido>("filtroEnEdicion").Valores = new(StringComparer.OrdinalIgnoreCase) { "B1" };
        Invocar("AplicarFiltroColumna");
        verificar(Resultados().Count == 0 && pedido.TotalProductos == 1, "Filtrar por origen oculta filas sin quitar artículos del pedido");
        verificar(PedidoExportacionService.Crear(pedido.Items, inventario.Productos, configuracion.Actual).Length > 0, "El pedido completo sigue exportándose aunque la tabla filtrada esté vacía");
        Invocar("MostrarOportunidades");
        verificar(Leer<Dictionary<string, FiltroColumnaPedido>>("filtrosColumnas").Count == 0 && Resultados().Count == 34, "Cambiar de lista elimina filtros de columnas que podrían ocultar productos inesperadamente");
        configuracion.Actual.BodegasOrigen.Remove("B1");
        Filtrar("B1", "igual", "0");
        verificar(Resultados().Count == 34, "Un centro retirado ya no tiene columna ni aplica filtros invisibles");
        Campo("recomendaciones", new Dictionary<string, PropuestaPedido>(StringComparer.OrdinalIgnoreCase)
        {
            ["P31"] = new("P31", "Cable 31", "Marca", "Bajó stock", "Bajó de 33 a 31", 31, 33, null, "B40", 2, true),
            ["P32"] = new("P32", "Cable 32", "Marca", "Bajó stock", "Bajó de 36 a 32", 32, 36, null, "B40", 4, true)
        });
        Invocar("MostrarRecomendados");
        verificar(Resultados().Select(p => p.Codigo).SequenceEqual(new[] { "P31", "P32" }), "La tabla puede mostrar solo las reposiciones calculadas desde el último Excel");
        var recomendado = inventario.Productos.Single(p => p.Codigo == "P31");
        verificar((decimal)Invocar("CantidadParaAgregar", recomendado)! == 2, "La cantidad inicial usa la bajada calculada en lugar de una cantidad fija");
        Invocar("PrepararCantidad", recomendado, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "7" });
        Invocar("CambiarComentario", recomendado.Codigo, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Para cliente: entregar completo" });
        PedidoMercaderia.Models.ConfiguracionBodegas configActual = configuracion.Actual;
        configActual.ProductosNoSugerir.Add("P32");
        Campo("marcaSeleccionada", "Otra marca");
        verificar(Resultados().Count == 0, "Los filtros de marca siguen funcionando en la lista de recomendados");
        ((Task)Invocar("GenerarBorrador")!).GetAwaiter().GetResult();
        verificar(pedido.Obtener("P31")?.CantidadSolicitada == 7 && pedido.Obtener("P30")?.CantidadSolicitada == 20 && !pedido.Contiene("P32"),
            "El borrador usa todas las marcas, respeta cantidades editadas y exclusiones y conserva el pedido anterior");
        ((Task)Invocar("GenerarBorrador")!).GetAwaiter().GetResult();
        verificar(pedido.TotalProductos == 2, "Generar de nuevo no duplica los artículos del borrador");
        verificar(pedido.Obtener("P31")?.Comentario == "Para cliente: entregar completo", "Generar el borrador conserva el comentario escrito antes de agregar");
        var individual = inventario.Productos.Single(p => p.Codigo == "P10");
        Invocar("CambiarComentario", individual.Codigo, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Reservar para instalación" });
        Invocar("MostrarNuevos");
        Invocar("MostrarOportunidades");
        verificar((string)Invocar("ComentarioPara", individual.Codigo)! == "Reservar para instalación", "Cambiar de lista conserva la nota pendiente sin agregar el producto");
        string? comentarioAlNotificar = null;
        pedido.Cambiado += () => comentarioAlNotificar = pedido.Obtener(individual.Codigo)?.Comentario;
        Invocar("AgregarUno", individual);
        verificar(comentarioAlNotificar == "Reservar para instalación", "El guardado recibe el comentario desde la primera notificación al agregar");
        Invocar("CambiarComentario", individual.Codigo, new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "" });
        verificar(pedido.Obtener(individual.Codigo)?.Comentario == "", "También se puede borrar el comentario de un artículo agregado");
        Invocar("CambiarComentario", "P11", new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Línea once" });
        Invocar("CambiarComentario", "P12", new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Línea doce" });
        Leer<HashSet<string>>("seleccionados").UnionWith(["P11", "P12"]);
        Invocar("AgregarMarcados");
        verificar(pedido.Obtener("P11")?.Comentario == "Línea once" && pedido.Obtener("P12")?.Comentario == "Línea doce", "Agregar marcados conserva la nota propia de cada fila");
        Invocar("Quitar", "P11"); Invocar("AgregarUno", inventario.Productos.Single(p => p.Codigo == "P11"));
        verificar(pedido.Obtener("P11")?.Comentario == "", "Quitar y agregar nuevamente no recupera una nota descartada");
    }
    public static void VerificarTablaInventario(Action<bool, string> verificar)
    {
        var tipo = typeof(PruebasFiltrosColumnas).Assembly.GetTypes().Single(t => t.Name == "TablaInventario");
        var tabla = Activator.CreateInstance(tipo)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var config = new ConfiguracionService();
        config.Restaurar(new() { BodegaLocal = "B7", NombresBodegas = new() { ["B7"] = "Ambato" } });
        tipo.GetProperty("Configuracion", flags)!.SetValue(tabla, config);
        var inventario = new ResultadoImportacion { BodegasDetectadas = ["B7", "B88"], Productos = Enumerable.Range(0, 51)
            .Select(n => new ProductoInventario { Codigo = $"INV-{n:D3}", Descripcion = n == 50 ? "Cable especial" : "Equipo", Marca = n == 50 ? "MarcaFinal" : "General",
                Existencias = new() { ["B7"] = n, ["B88"] = 100 } }).ToList() };
        void Cargar(ResultadoImportacion datos) { tipo.GetProperty("Inventario")!.SetValue(tabla, datos); tipo.GetMethod("OnParametersSet", flags)!.Invoke(tabla, null); }
        T Leer<T>(string nombre) => (T)tipo.GetField(nombre, flags)!.GetValue(tabla)!;
        void Buscar(string texto) => tipo.GetMethod("Buscar", flags)!.Invoke(tabla, [new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = texto }]);
        Cargar(inventario);
        verificar(Leer<string>("bodega") == "B7" && Leer<List<ProductoInventario>>("filtrados").Count == 51, "La consulta de inventario empieza en la sucursal y conserva todos los productos");
        tipo.GetField("pagina", flags)!.SetValue(tabla, 2);
        Buscar(" inv-050 ");
        verificar(Leer<int>("pagina") == 1 && Leer<List<ProductoInventario>>("filtrados").Single().Codigo == "INV-050", "Busca en todo el inventario, incluidos productos fuera de la primera página");
        Buscar("ESPECIAL");
        verificar(Leer<List<ProductoInventario>>("filtrados").Single().Codigo == "INV-050", "Busca por descripción sin distinguir mayúsculas");
        Buscar("marcafinal");
        verificar(Leer<List<ProductoInventario>>("filtrados").Count == 1, "Permite localizar productos por marca");
        Buscar("inexistente");
        verificar(Leer<List<ProductoInventario>>("filtrados").Count == 0 && inventario.Productos.Count == 51, "Una búsqueda vacía no modifica el inventario");
        void Campo(string nombre, object valor) => tipo.GetField(nombre, flags)!.SetValue(tabla, valor);
        void Filtrar() => tipo.GetMethod("Filtrar", flags)!.Invoke(tabla, null);
        Buscar(""); Campo("marca", "General"); Campo("stock", "sin"); Filtrar();
        verificar(Leer<List<ProductoInventario>>("filtrados").Single().Codigo == "INV-000", "Inventario combina marca y falta de stock sin modificar los productos");
        Campo("bodega", "B88"); Filtrar();
        verificar(Leer<List<ProductoInventario>>("filtrados").Count == 0, "Cambiar de bodega recalcula el filtro de existencias");
        Campo("bodega", "B7"); Campo("marca", ""); Campo("stock", ""); Campo("orden", "stock"); Campo("descendente", true); Filtrar();
        verificar(Leer<List<ProductoInventario>>("filtrados").First().Codigo == "INV-050" && Leer<int>("pagina") == 1, "Ordena cantidades numéricamente antes de paginar el inventario");
        Campo("marca", "General"); Campo("stock", "con"); Filtrar();
        Cargar(new() { BodegasDetectadas = ["B2"], Productos = [new() { Codigo = "NUEVO" }] });
        verificar(Leer<string>("bodega") == "B2" && Leer<string>("busqueda") == "" && Leer<List<ProductoInventario>>("filtrados").Single().Codigo == "NUEVO", "Otro Excel reinicia búsqueda, página y bodega sin mostrar productos anteriores");
        verificar(Leer<string>("marca") == "" && Leer<string>("stock") == "" && Leer<string>("orden") == "codigo" && !Leer<bool>("descendente"), "Otro Excel limpia también los filtros de marca, existencias y orden");
    }

}
