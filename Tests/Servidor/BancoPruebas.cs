using Npgsql;
using Microsoft.EntityFrameworkCore;

// Cada ejecución crea y elimina exclusivamente su esquema, nunca una base del usuario.
sealed class BancoPruebas : IAsyncDisposable
{
    private readonly string esquema = "pruebas_" + Guid.NewGuid().ToString("N");
    private readonly NpgsqlDataSource administrador;
    public NpgsqlDataSource Fuente { get; private set; } = null!;
    public Servidor.DatosModelo.FabricaContextos Fabrica => new(Fuente);
    private BancoPruebas(string conexion) { administrador = NpgsqlDataSource.Create(conexion); }
    public static async Task<BancoPruebas> Crear()
    {
        var conexion = Environment.GetEnvironmentVariable("PEDIDOS_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(conexion))
            throw new InvalidOperationException("Configura PEDIDOS_TEST_CONNECTION con una base de pruebas antes de ejecutar estas comprobaciones.");
        var banco = new BancoPruebas(conexion);
        await using var cmd = banco.administrador.CreateCommand($"CREATE SCHEMA {banco.esquema}");
        await cmd.ExecuteNonQueryAsync();
        var opciones = new NpgsqlConnectionStringBuilder(conexion) { SearchPath = banco.esquema };
        banco.Fuente = NpgsqlDataSource.Create(opciones.ConnectionString);
        return banco;
    }
    public async ValueTask DisposeAsync()
    {
        await Fuente.DisposeAsync();
        await using var cmd = administrador.CreateCommand($"DROP SCHEMA {esquema} CASCADE");
        await cmd.ExecuteNonQueryAsync();
        await administrador.DisposeAsync();
    }

    public static async Task ConfigurarB4(Servidor.Almacen db)
    {
        var estado = await db.Estado();
        estado.Configuracion.BodegaLocal = "B4";
        estado.Configuracion.BodegasOrigen = new(["B1", "B40"]);
        await db.Configurar(new(estado.Revision, estado.Configuracion), "admin");
    }

    public static async Task PrepararOtraCarga(Servidor.Almacen db)
    {
        var estado = await db.Estado();
        if (estado.PedidoAbierto) await db.AnularPreparacion(estado.Revision, "admin");
    }
    public static async Task ConfirmarBorrador(Servidor.Almacen db)
    {
        var estado = await db.Estado();
        foreach (var item in estado.Borrador) item.RequiereRevision = false;
        await db.Guardar(new(estado.Revision, estado.Borrador, estado.Corte!.Id), "admin");
    }

    public async Task CrearModeloEf()
    {
        // Solo en el esquema aleatorio de pruebas. La aplicación no ejecuta DDL.
        await using var db = Fabrica.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
    }
}
