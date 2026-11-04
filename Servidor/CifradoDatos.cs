using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Servidor;

/// <summary>Cifrado autenticado en la aplicación; PostgreSQL nunca recibe la clave.</summary>
public sealed class CifradoDatos : IDisposable
{
    private readonly byte[] clave;
    private readonly byte[] claveIndices;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public CifradoDatos(byte[] clave)
    {
        if (clave.Length != 32) throw new ArgumentException("La clave de cifrado debe tener 32 bytes.");
        this.clave = clave.ToArray();
        claveIndices = HKDF.DeriveKey(HashAlgorithmName.SHA256, clave, 32, info: Encoding.UTF8.GetBytes("Pedidos/indices/v1"));
    }

    public string Indice(string dominio, string valor) => Convert.ToHexString(
        HMACSHA256.HashData(claveIndices, Encoding.UTF8.GetBytes(dominio + "\0" + valor.Trim().ToUpperInvariant())));

    // Formato: versión | nonce aleatorio 96 bits | tag 128 bits | texto cifrado.
    // El contexto enlaza el valor a su tabla, fila y campo e impide intercambiarlos.
    public byte[] Cifrar<T>(T valor, string contexto)
    {
        var texto = JsonSerializer.SerializeToUtf8Bytes(valor, Json);
        var salida = new byte[29 + texto.Length];
        salida[0] = 1;
        RandomNumberGenerator.Fill(salida.AsSpan(1, 12));
        try
        {
            using var aes = new AesGcm(clave, 16);
            aes.Encrypt(salida.AsSpan(1, 12), texto, salida.AsSpan(29), salida.AsSpan(13, 16), Encoding.UTF8.GetBytes(contexto));
            return salida;
        }
        finally { CryptographicOperations.ZeroMemory(texto); }
    }

    public T Leer<T>(byte[] valor, string contexto)
    {
        if (valor.Length < 29 || valor[0] != 1) throw new CryptographicException("Formato de cifrado desconocido.");
        var texto = new byte[valor.Length - 29];
        try
        {
            using var aes = new AesGcm(clave, 16);
            aes.Decrypt(valor.AsSpan(1, 12), valor.AsSpan(29), valor.AsSpan(13, 16), texto, Encoding.UTF8.GetBytes(contexto));
            return JsonSerializer.Deserialize<T>(texto, Json)!;
        }
        finally { CryptographicOperations.ZeroMemory(texto); }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(clave);
        CryptographicOperations.ZeroMemory(claveIndices);
    }
}
