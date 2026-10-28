
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services
{
    public class InventarioService
    {
        private readonly ExcelImportacionService _excelService;
        private readonly PedidoService _pedidoService;
        private readonly ActividadService _actividadService;

        public ResultadoImportacion? ResultadoActual { get; private set; }

        public IReadOnlyList<ProductoInventario> Productos
        {
            get
            {
                if (ResultadoActual is null)
                    return Array.Empty<ProductoInventario>();

                return ResultadoActual.Productos;
            }
        }

        public bool TieneInventario => Productos.Count > 0;

        public InventarioService(
            ExcelImportacionService excelService,
            PedidoService pedidoService,
            ActividadService actividadService)
        {
            _excelService = excelService;
            _pedidoService = pedidoService;
            _actividadService = actividadService;
        }

        public void Importar(Stream archivo, string nombreArchivo)
        {
            var resultado = _excelService.Importar(
                archivo, nombreArchivo);

            ResultadoActual = resultado;

            // Un nuevo inventario inicia un nuevo borrador.
            _pedidoService.Limpiar();
            _actividadService.Limpiar();
        }

        public void Limpiar()
        {
            ResultadoActual = null;
            _pedidoService.Limpiar();
            _actividadService.Limpiar();
        }
    }
}
