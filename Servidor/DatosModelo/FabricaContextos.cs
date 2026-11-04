using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Servidor.DatosModelo;

public sealed class FabricaContextos(NpgsqlDataSource fuente) : IDbContextFactory<PedidosDbContext>
{
    private readonly DbContextOptions<PedidosDbContext> opciones = new DbContextOptionsBuilder<PedidosDbContext>()
        .UseNpgsql(fuente).Options;

    public PedidosDbContext CreateDbContext() => new(opciones);
}
