using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Servidor.Configuracion;

namespace Servidor.DatosModelo;

// Las herramientas de EF crean el contexto sin arrancar el servidor ni cargar claves.
public sealed class FabricaDiseno : IDesignTimeDbContextFactory<PedidosDbContext>
{
    public PedidosDbContext CreateDbContext(string[] args)
    {
        var raiz = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(raiz, "Servidor"))) raiz = Path.Combine(raiz, "Servidor");
        var entorno = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var configuracion = new ConfigurationBuilder().SetBasePath(raiz)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{entorno}.json", optional: true)
            .AddEnvironmentVariables().AddCommandLine(args).Build();

        var opciones = new DbContextOptionsBuilder<PedidosDbContext>()
            .UseNpgsql(ConexionPedidos.Obtener(configuracion)).Options;
        return new PedidosDbContext(opciones);
    }
}
