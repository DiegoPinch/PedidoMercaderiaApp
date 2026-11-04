using System.Security.Cryptography;
using Npgsql;

namespace Servidor;

public static class ClavesCifrado
{
    public static async Task<CifradoDatos> Cargar(IConfiguration configuracion, IHostEnvironment entorno, NpgsqlDataSource fuente, string carpeta)
    {
        var externa = configuracion["Cifrado:ClaveBase64"];
        byte[] clave;
        if (!string.IsNullOrWhiteSpace(externa)) clave = Convert.FromBase64String(externa);
        else
        {
            if (!entorno.IsDevelopment() || !OperatingSystem.IsWindows())
                throw new InvalidOperationException("Configura Cifrado__ClaveBase64 con una clave de 32 bytes guardada en el gestor de secretos del servidor.");
            var directorio = Path.Combine(carpeta, "cifrado");
            var archivo = Path.Combine(directorio, "maestra.dpapi");
            if (!File.Exists(archivo))
            {
                await using var c = fuente.CreateCommand("SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='cifrado_control')");
                if ((bool)(await c.ExecuteScalarAsync())!)
                {
                    // Una migración crea tablas vacías antes del primer arranque.
                    await using var datos = fuente.CreateCommand("SELECT EXISTS(SELECT 1 FROM cifrado_control) OR EXISTS(SELECT 1 FROM usuarios) OR EXISTS(SELECT 1 FROM trabajo) OR EXISTS(SELECT 1 FROM bodegas)");
                    if ((bool)(await datos.ExecuteScalarAsync())!)
                        throw new InvalidOperationException("Falta la clave de una base ya cifrada. Restaura maestra.dpapi; no se generará otra clave.");
                }
                Directory.CreateDirectory(directorio);
                var nueva = RandomNumberGenerator.GetBytes(32);
                try
                {
                    // CreateNew evita reemplazar una clave creada simultáneamente por otro proceso.
                    using var salida = new FileStream(archivo, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    salida.Write(ProtectedData.Protect(nueva, null, DataProtectionScope.CurrentUser));
                    salida.Flush(true);
                }
                catch (IOException) when (File.Exists(archivo)) { }
                finally { CryptographicOperations.ZeroMemory(nueva); }
            }
            clave = ProtectedData.Unprotect(File.ReadAllBytes(archivo), null, DataProtectionScope.CurrentUser);
        }
        try { return new CifradoDatos(clave); }
        finally { CryptographicOperations.ZeroMemory(clave); }
    }
}
