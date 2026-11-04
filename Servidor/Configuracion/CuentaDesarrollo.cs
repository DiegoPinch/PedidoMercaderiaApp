using Microsoft.AspNetCore.Identity;
using System.Text.RegularExpressions;

namespace Servidor.Configuracion;

public sealed class CuentaDesarrollo(Almacen almacen, IHostEnvironment entorno,
    IConfiguration configuracion, IPasswordHasher<string> hasher)
{
    public async Task Inicializar()
    {
        if (!entorno.IsDevelopment() || !configuracion.GetValue<bool>("CuentaDesarrollo:Habilitada")) return;

        var usuario = configuracion["CuentaDesarrollo:Usuario"] ?? "";
        var nombre = configuracion["CuentaDesarrollo:Nombre"] ?? "";
        var clave = configuracion["CuentaDesarrollo:Clave"] ?? "";
        if (!Regex.IsMatch(usuario, "^[a-zA-Z0-9._-]{3,40}$") || nombre.Trim().Length is < 2 or > 80 || clave.Length is < 10 or > 128)
            throw new InvalidOperationException("La cuenta de desarrollo requiere usuario válido, nombre de 2–80 caracteres y contraseña de 10–128 caracteres.");

        await almacen.CrearAdministradorSiVacio(usuario, nombre, hasher.HashPassword(usuario, clave));
    }
}
