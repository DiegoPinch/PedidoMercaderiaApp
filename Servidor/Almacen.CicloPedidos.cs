using Microsoft.EntityFrameworkCore;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using System.Data;
using Servidor.DatosModelo;

namespace Servidor;

public sealed partial class Almacen
{
    public async Task<byte[]> ExportarBorrador(long revision)
    {
        await using var db = await Abrir();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var estado = await Estado(db, false);
        if (estado.Revision != revision)
            throw new ConflictoException("El pedido cambió. Actualiza desde el equipo y revisa sus cantidades antes de descargarlo.");
        ExigirPedidoAbierto(estado);
        var archivo = PedidoExportacionService.Crear(estado.Borrador, estado.Inventario!.Productos, estado.Configuracion);
        await tx.CommitAsync();
        return archivo;
    }

    private async Task CambiarCiclo(PedidosDbContext db, CorteResumen corte, string estado)
    {
        var contenido = cifrado.Cifrar(corte with { Estado = estado }, $"cortes/{corte.Id}/resumen");
        await db.Cortes.Where(c => c.Id == corte.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.ResumenCifrado, contenido));
    }

    public Task<long> AnularPreparacion(long revision, string usuario) => Mutar(revision, usuario,
        "Anular preparación y conservar borrador", async (db, estado) =>
        {
            if (!estado.PedidoAbierto) throw new InvalidOperationException("No hay una preparación abierta.");
            foreach (var item in estado.Borrador) item.RequiereRevision = true;
            await GuardarLineas(db, estado.Borrador);
            await CambiarCiclo(db, estado.Corte!, "Anulado");
        });

    private static void ExigirPedidoAbierto(EstadoTrabajo estado)
    {
        if (!estado.PedidoAbierto)
            throw new InvalidOperationException("Carga un nuevo Excel para preparar otro pedido. Puedes anotar productos en el borrador para la próxima compra.");
    }
}
