using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using Servidor;
using System.Security.Cryptography;
using System.Text;

static class PruebasMovimientos
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        await using var banco = await BancoPruebas.Crear();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var db = new Almacen(banco.Fabrica, cifrado);
        await banco.CrearModeloEf(); await db.Inicializar();
        await db.CrearUsuario("admin", "Administrador", "hash", true);
        ResultadoImportacion Inventario() => new() { NombreArchivo = "stock.xlsx", BodegasDetectadas = ["B4", "B1", "B40"],
            Productos = [new() { Codigo = "PRODUCTO-PRUEBA", Existencias = new() { ["B4"] = 5, ["B1"] = 10, ["B40"] = 20 } }] };
        ReporteActividad Reporte(int dia, int dias = 200) => new() { NombreArchivo = "movimientos.xlsx", FechaReporte = new(2026, 10, dia), FilasLeidas = 1,
            Productos = new() { ["PRODUCTO-PRUEBA"] = new() { Codigo = "PRODUCTO-PRUEBA", StockB4 = 5, DiasSinActividad = dias } } };
        async Task Rechaza(Func<Task> accion, string mensaje)
        {
            bool rechazo = false;
            try { await accion(); } catch (Exception e) when (e is InvalidOperationException or ConflictoException) { rechazo = true; }
            verificar(rechazo, mensaje);
        }
        await db.Importar(new(0, new(2026, 10, 1), Inventario()), "admin");
        await BancoPruebas.ConfigurarB4(db);
        var estado = await db.Estado();
        verificar(estado.Actividad is null, "Sin reporte cargado no se inventan movimientos");
        await db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B4", Reporte(1)), "admin");
        using var otraInstancia = new Almacen(banco.Fabrica, cifrado);
        estado = await otraInstancia.Estado();
        verificar(estado.Actividad?.Productos["producto-prueba"].DiasSinActividad == 200 && estado.Actividad.Coincidencias == 1,
            "Otra instancia recupera el último reporte con sus productos y coincidencias");
        await using (var ef = banco.Fabrica.CreateDbContext())
        {
            var fila = await ef.ReportesActividad.SingleAsync();
            verificar(!Encoding.UTF8.GetString(fila.ContenidoCifrado).Contains("PRODUCTO-PRUEBA")
                && !Encoding.UTF8.GetString(fila.ContenidoCifrado).Contains("movimientos.xlsx") && fila.BodegaId != "B4",
                "Códigos, nombres y datos del reporte se guardan cifrados");
        }
        await BancoPruebas.PrepararOtraCarga(db); estado = await db.Estado();
        await db.Importar(new(estado.Revision, new(2026, 10, 8), Inventario()), "admin");
        estado = await db.Estado();
        verificar(estado.Actividad?.FechaReporte == new DateOnly(2026, 10, 1), "Otro inventario conserva el reporte sin inventar días adicionales");
        await Rechaza(() => db.GuardarActividad(new(estado.Revision, estado.Corte!.Id - 1, "B4", Reporte(8)), "admin"), "Rechaza un reporte enviado sobre un inventario reemplazado");
        await Rechaza(() => db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B1", Reporte(8)), "admin"), "No guarda movimientos en otra sucursal por error");
        await db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B4", Reporte(8, 207)), "admin");
        estado = await db.Estado();
        await Rechaza(() => db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B4", Reporte(1)), "admin"), "Un reporte anterior no reemplaza al más reciente");
        var invalido = Reporte(9); invalido.Productos.Clear();
        await Rechaza(() => db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B4", invalido), "admin"), "Rechaza reportes vacíos sin perder el anterior");
        verificar((await db.Estado()).Actividad?.Productos["PRODUCTO-PRUEBA"].DiasSinActividad == 207, "Un reemplazo fallido conserva los datos guardados");
        estado.Configuracion.BodegaLocal = "B1"; estado.Configuracion.BodegasOrigen = ["B40"];
        await db.Configurar(new(estado.Revision, estado.Configuracion), "admin");
        estado = await db.Estado();
        verificar(estado.Actividad is null, "Cambiar sucursal no mezcla reportes de otras bodegas");
        await db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B1", Reporte(8, 30)), "admin");
        estado = await db.Estado(); estado.Configuracion.BodegaLocal = "B4";
        await db.Configurar(new(estado.Revision, estado.Configuracion), "admin");
        estado = await db.Estado();
        verificar(estado.Actividad?.Productos["PRODUCTO-PRUEBA"].DiasSinActividad == 207, "Volver a la sucursal recupera su propio último reporte");
        await db.GuardarActividad(new(estado.Revision, estado.Corte!.Id, "B4", null), "admin");
        verificar((await otraInstancia.Estado()).Actividad is null, "Quitar el reporte persiste al recargar desde otra instancia");
        await using var final = banco.Fabrica.CreateDbContext();
        verificar(await final.ReportesActividad.CountAsync() == 1, "Quitar un reporte conserva el de la otra sucursal");
    }
}
