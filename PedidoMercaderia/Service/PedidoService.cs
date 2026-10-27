
using PedidoMercaderia.Models;

namespace PedidoMercaderia.Services
{
    public class PedidoService
    {
        private readonly Dictionary<string, ItemPedido> _items =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<ItemPedido> Items => _items.Values;

        public int TotalProductos => _items.Count;

        public decimal TotalUnidades =>
            _items.Values.Sum(x => x.CantidadSolicitada);

        public HashSet<string> Omitidos { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Contiene(string codigo)
        {
            return _items.ContainsKey(codigo);
        }

        public ItemPedido? Obtener(string codigo)
        {
            return _items.TryGetValue(codigo, out var item)
                ? item
                : null;
        }

        public bool Agregar(
            ProductoInventario producto,
            ConfiguracionBodegas configuracion,
            decimal cantidadInicial = 1,
            decimal reservaCentro = 0)
        {
            if (Contiene(producto.Codigo))
                return false;

            if (cantidadInicial <= 0 || reservaCentro < 0)
                return false;

            // Elegir el centro con más stock transferible.
            var origen = configuracion.OrigenesHabilitados
                .Select(codigo => new
                {
                    Codigo = codigo,
                    Stock = producto.ObtenerStock(codigo)
                })
                .Select(x => new
                {
                    x.Codigo,
                    Disponible = ReglasAbastecimiento.Disponible(producto, x.Codigo, reservaCentro)
                })
                .Where(x => x.Disponible > 0)
                .OrderByDescending(x => x.Disponible)
                .ThenBy(x => x.Codigo)
                .FirstOrDefault();

            if (origen is null)
                return false;

            _items.Add(producto.Codigo, new ItemPedido
            {
                CodigoProducto = producto.Codigo,
                BodegaOrigen = origen.Codigo,
                ReservaCentro = reservaCentro,
                CantidadSolicitada = Math.Min(
                    cantidadInicial,
                    origen.Disponible)
            });

            return true;
        }

        public int AgregarVarios(
            IEnumerable<ProductoInventario> productos,
            ConfiguracionBodegas configuracion,
            decimal cantidadInicial,
            decimal reservaCentro)
        {
            int agregados = 0;

            foreach (var producto in productos)
            {
                if (Agregar(
                    producto,
                    configuracion,
                    cantidadInicial,
                    reservaCentro))
                {
                    agregados++;
                }
            }

            return agregados;
        }

        public void Quitar(string codigo)
        {
            _items.Remove(codigo);
        }

        public bool CambiarCantidad(
            ProductoInventario producto,
            decimal nuevaCantidad)
        {
            var item = Obtener(producto.Codigo);

            if (item is null)
                return false;

            decimal disponible = ReglasAbastecimiento.Disponible(
                producto, item.BodegaOrigen, item.ReservaCentro);

            if (nuevaCantidad <= 0 ||
                nuevaCantidad > disponible)
            {
                return false;
            }

            item.CantidadSolicitada = nuevaCantidad;

            return true;
        }

        public bool CambiarBodega(
            ProductoInventario producto,
            string nuevaBodega,
            ConfiguracionBodegas configuracion)
        {
            var item = Obtener(producto.Codigo);

            if (item is null)
                return false;

            if (!configuracion.OrigenesHabilitados.Contains(nuevaBodega, StringComparer.OrdinalIgnoreCase))
                return false;

            decimal disponible = ReglasAbastecimiento.Disponible(producto, nuevaBodega, item.ReservaCentro);

            if (disponible <= 0)
                return false;

            item.BodegaOrigen = nuevaBodega;

            item.CantidadSolicitada = Math.Min(
                item.CantidadSolicitada,
                disponible);

            return true;
        }

        public int ValidarBorrador(
            IEnumerable<ProductoInventario> productos,
            ConfiguracionBodegas configuracion)
        {
            var catalogo = productos.ToDictionary(
                p => p.Codigo,
                StringComparer.OrdinalIgnoreCase);

            int ajustes = 0;

            foreach (var item in _items.Values.ToList())
            {
                if (!catalogo.TryGetValue(
                    item.CodigoProducto, out var producto))
                {
                    Quitar(item.CodigoProducto);
                    ajustes++;
                    continue;
                }

                decimal disponible = ReglasAbastecimiento.Disponible(
                    producto, item.BodegaOrigen, item.ReservaCentro);

                if (!configuracion.OrigenesHabilitados.Contains(
                    item.BodegaOrigen, StringComparer.OrdinalIgnoreCase) || disponible <= 0)
                {
                    var origen = configuracion.OrigenesHabilitados
                        .Select(b => new
                        {
                            Codigo = b,
                            Stock = ReglasAbastecimiento.Disponible(producto, b, item.ReservaCentro)
                        })
                        .Where(x => x.Stock > 0)
                        .OrderByDescending(x => x.Stock)
                        .FirstOrDefault();

                    if (origen is null)
                    {
                        Quitar(item.CodigoProducto);
                        ajustes++;
                        continue;
                    }

                    item.BodegaOrigen = origen.Codigo;
                    disponible = origen.Stock;
                    ajustes++;
                }

                if (item.CantidadSolicitada > disponible)
                {
                    item.CantidadSolicitada = disponible;
                    ajustes++;
                }

                if (item.CantidadSolicitada <= 0)
                {
                    item.CantidadSolicitada = Math.Min(
                        1m, disponible);
                    ajustes++;
                }
            }

            return ajustes;
        }

        public void Limpiar()
        {
            _items.Clear();
            Omitidos.Clear();
        }
    }
}
