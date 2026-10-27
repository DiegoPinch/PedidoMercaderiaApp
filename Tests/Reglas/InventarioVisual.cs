using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services;

// Inventario ficticio para renderizar la pantalla sin cargar archivos ni datos del usuario.
public class InventarioService
{
    public IReadOnlyList<ProductoInventario> Productos { get; } =
    [
        new() { Codigo = "JR-DF308", Descripcion = "PAR DE VIDEO BALUM HD CON CONECTORES RJ45 · TECLAM", Marca = "TECLAM",
            Existencias = new() { ["B4"] = 0, ["B1"] = 524, ["B40"] = 255, ["B39"] = 0 } },
        new() { Codigo = "CABLE-01", Descripcion = "Cable de red UTP categoría 6", Marca = "TECLAM", UnidadMedida = "Metro",
            Existencias = new() { ["B4"] = 10, ["B1"] = 100, ["B40"] = 200, ["B39"] = 500 } },
        new() { Codigo = "CONECTOR-02", Descripcion = "Conector RJ45 para cable de red", Marca = "TECLAM",
            Existencias = new() { ["B4"] = 20, ["B1"] = 40, ["B40"] = 60 } }
    ];
    public bool TieneInventario => Productos.Count > 0;
}
