using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using System.Security.Cryptography;

static class PruebasCicloPedidos
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        await using var banco = await BancoPruebas.Crear();
        await banco.CrearModeloEf();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var db = new Almacen(banco.Fabrica, cifrado);
        using var otra = new Almacen(banco.Fabrica, cifrado);
        await db.Inicializar(); await db.CrearUsuario("admin", "Administrador", "hash", true);
        await db.CrearUsuario("ana", "Compañera", "hash", false);
        ResultadoImportacion Excel(bool nuevo = false) => new() { NombreArchivo = "semana.xlsx", BodegasDetectadas = ["B4", "B1", "B40"],
            Productos = (nuevo ? new[] { "CABLE", "NUEVO" } : new[] { "CABLE" }).Select(c => new ProductoInventario {
                Codigo = c, Descripcion = nuevo ? "Descripción actualizada" : "Descripción original", Existencias = new() { ["B4"] = nuevo ? 2 : 10, ["B1"] = nuevo ? 4 : 30, ["B40"] = nuevo ? 20 : 0 } }).ToList() };
        async Task Rechaza(Func<Task> accion, string mensaje)
        {
            bool rechazo = false;
            try { await accion(); } catch (Exception e) when (e is InvalidOperationException or ConflictoException) { rechazo = true; }
            verificar(rechazo, mensaje);
        }
        await db.Importar(new(0, new(2026, 10, 1), Excel()), "admin");
        await BancoPruebas.ConfigurarB4(db);
        var e = await db.Estado();
        verificar(e.PedidoAbierto, "Cargar un Excel abre su preparación de pedido");
        await Rechaza(() => db.Importar(new(e.Revision, new(2026, 10, 8), Excel(true)), "ana"), "El servidor bloquea sustituir un Excel con pedido abierto aunque el borrador esté vacío");
        await db.Guardar(new(e.Revision, [new() { CodigoProducto = "CABLE", BodegaOrigen = "B1", CantidadSolicitada = 7, Comentario = "Para cliente" }], e.Corte!.Id), "ana");
        e = await db.Estado(); var revisionAbierta = e.Revision;
        await db.Realizar(e.Revision, "ana"); e = await otra.Estado();
        verificar(!e.PedidoAbierto && e.Borrador.Count == 0 && e.Historial.Single().Estado == "Guardado", "Cerrar guarda el historial, vacía la preparación y no exige recepción");
        var id = e.Historial.Single().Id;
        await Rechaza(() => db.Realizar(e.Revision, "admin"), "Cerrar dos veces no duplica un pedido");
        await Rechaza(() => db.Generar(new(e.Revision, false), "admin"), "No se generan pedidos nuevos con un Excel cerrado");
        await Rechaza(() => db.Guardar(new(revisionAbierta, [new() { CodigoProducto = "CABLE", BodegaOrigen = "B1", CantidadSolicitada = 1 }], e.Corte!.Id, []), "admin"), "Otro compañero con la pantalla vieja no puede reabrir el pedido cerrado al guardar");
        await db.Guardar(new(e.Revision, [new() { CodigoProducto = "CABLE", CantidadSolicitada = 9, ReservaCentro = 1, Comentario = "Solicitado el miércoles", RequiereRevision = true }], e.Corte!.Id), "admin");
        e = await otra.Estado();
        verificar(e.Borrador.Single().RequiereRevision && e.Borrador.Single().BodegaOrigen == "", "El borrador futuro persiste sin inventar una bodega ni limitarse al stock antiguo");
        byte[] codigoAntes;
        await using (var ef = banco.Fabrica.CreateDbContext()) codigoAntes = (await ef.Productos.SingleAsync()).CodigoCifrado;
        await db.Importar(new(e.Revision, new(2026, 10, 8), Excel(true)), "ana"); e = await db.Estado();
        verificar(e.PedidoAbierto && e.Borrador.Single().CantidadSolicitada == 9 && e.Borrador.Single().RequiereRevision,
            "El siguiente Excel conserva la cantidad anticipada y exige revisar la fila amarilla");
        await using (var ef = banco.Fabrica.CreateDbContext())
            verificar(await ef.Productos.CountAsync() == 2 && (await ef.Productos.SingleAsync(p => p.PrimerCorteId == 1)).CodigoCifrado.SequenceEqual(codigoAntes),
                "Importar inserta productos nuevos sin duplicar ni cambiar los identificadores de los existentes");
        verificar(e.Cambios.Any(c => c.Codigo == "NUEVO" && c.Tipo == "Nuevo") && e.Propuestas.Single(p => p.Codigo == "NUEVO").Rapido == false,
            "El código nuevo aparece como opción para revisar, sin agregarlo automáticamente");
        await Rechaza(() => db.Realizar(e.Revision, "admin"), "No se puede cerrar mientras haya filas sin revisar");
        var linea = e.Borrador.Single(); linea.BodegaOrigen = "B1"; linea.RequiereRevision = false;
        await Rechaza(() => db.Guardar(new(e.Revision, [linea], e.Corte!.Id), "admin"), "No se puede confirmar una bodega que ahora tiene stock insuficiente");
        linea.BodegaOrigen = "B40";
        await db.Guardar(new(e.Revision, [linea], e.Corte!.Id), "admin"); e = await db.Estado();
        await db.Realizar(e.Revision, "admin"); e = await db.Estado();
        var historico = await otra.Detalle(id);
        verificar(historico.Items.Single().CantidadSolicitada == 7 && historico.Items.Single().BodegaOrigen == "B1"
            && historico.Items.Single().DescripcionProducto == "Descripción original" && historico.Items.Single().Comentario == "Para cliente"
            && PedidoExportacionService.CrearHistorico(historico.Items).Length > 0,
            "Se descarga el pedido anterior con su descripción, origen, cantidad y comentario originales");
        verificar((await db.Historial(estado: "Guardado")).Total == 2, "El historial muestra todos los pedidos guardados sin depender de recepción");
        var ultimo = e.Historial[0];
        await db.CambiarEstado(ultimo.Id, new(e.Revision, "Cancelado"), "admin"); e = await otra.Estado();
        verificar(e.PedidoAbierto && e.Borrador.Single().CantidadSolicitada == 9 && e.Historial[0].Estado == "Cancelado",
            "Anular el último pedido conserva su historial y recupera la preparación del mismo Excel");
        await db.AnularPreparacion(e.Revision, "admin"); e = await db.Estado();
        verificar(!e.PedidoAbierto && e.Borrador.Single().RequiereRevision, "Anular una preparación permite otro Excel sin perder las anotaciones");
        await db.Importar(new(e.Revision, new(2026, 10, 15), Excel(true)), "admin"); e = await db.Estado();
        await db.CambiarEstado(id, new(e.Revision, "Cancelado"), "admin"); e = await db.Estado();
        verificar(e.Corte!.Fecha == new DateOnly(2026, 10, 15) && e.Borrador.Single().CantidadSolicitada == 9,
            "Anular un pedido antiguo no reemplaza el borrador ni el inventario actual");
    }
}
