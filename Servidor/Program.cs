
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using Npgsql;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using Servidor.DatosModelo;
using Servidor.Configuracion;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// LOGGING
// ============================================

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ============================================
// DIRECTORIO DE DATOS
// ============================================

var carpeta = Path.GetFullPath(
    builder.Configuration["Datos"] ??
    Path.Combine(
        builder.Environment.ContentRootPath,
        "Datos"
    )
);

Directory.CreateDirectory(carpeta);

// ============================================
// CLAVE DE INSTALACION
// ============================================

// En Render se obtiene de una variable privada.
// En desarrollo local se conserva el mecanismo anterior.

var claveInstalacion =
    builder.Configuration["Instalacion:Clave"];

if (string.IsNullOrWhiteSpace(claveInstalacion))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Configura Instalacion__Clave como variable privada en el servidor."
        );
    }

    var claveArchivo = Path.Combine(
        carpeta,
        "clave-instalacion.txt"
    );

    if (!File.Exists(claveArchivo))
    {
        File.WriteAllText(
            claveArchivo,
            Convert.ToHexString(
                RandomNumberGenerator.GetBytes(24)
            )
        );
    }

    claveInstalacion =
        File.ReadAllText(claveArchivo).Trim();
}

// ============================================
// POSTGRESQL - NEON
// ============================================

// ConexionPedidos obtiene la cadena desde
// IConfiguration, incluyendo variables de entorno.

var conexion =
    ConexionPedidos.Obtener(builder.Configuration);

await using var fuente =
    NpgsqlDataSource.Create(conexion);

// ============================================
// CIFRADO DE DATOS
// ============================================

// Conservamos tu implementacion actual.
// Debe verificarse su persistencia en Render.

using var cifradoDatos =
    await ClavesCifrado.Cargar(
        builder.Configuration,
        builder.Environment,
        fuente,
        carpeta
    );

// ============================================
// INYECCION DE DEPENDENCIAS
// ============================================

builder.Services.AddSingleton(fuente);
builder.Services.AddSingleton(cifradoDatos);

builder.Services.AddSingleton<
    IDbContextFactory<PedidosDbContext>,
    FabricaContextos
>();

builder.Services.AddSingleton<Almacen>();

// ============================================
// COMPRESION
// ============================================

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;

    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();

    o.MimeTypes =
        ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "application/wasm",
                "application/octet-stream"
            ]
        );
});

builder.Services.Configure<
    BrotliCompressionProviderOptions
>(o => o.Level = CompressionLevel.Fastest);

builder.Services.Configure<
    GzipCompressionProviderOptions
>(o => o.Level = CompressionLevel.Fastest);

// ============================================
// PROTECCION DE DATOS
// ============================================

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(
            Path.Combine(carpeta, "claves")
        )
    );

builder.Services.AddSingleton<
    IPasswordHasher<string>,
    PasswordHasher<string>
>();

builder.Services.AddTransient<CuentaDesarrollo>();

// ============================================
// ANTIFORGERY
// ============================================

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Strict;
});

// ============================================
// AUTENTICACION
// ============================================

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme
    )
    .AddCookie(o =>
    {
        o.Cookie.Name = "Pedidos.Sesion";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;

        o.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;

        o.Events.OnRedirectToLogin = c =>
        {
            c.Response.StatusCode = 401;
            return Task.CompletedTask;
        };

        o.Events.OnRedirectToAccessDenied = c =>
        {
            c.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// ============================================
// LIMITACION DE PETICIONES
// ============================================

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;

    o.AddPolicy("acceso", c =>
        RateLimitPartition.GetFixedWindowLimiter(
            c.Connection.RemoteIpAddress?.ToString()
                ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );
});

// ============================================
// KESTREL
// ============================================

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize =
        40 * 1024 * 1024;
});

// ============================================
// CONSTRUIR APLICACION
// ============================================

var app = builder.Build();

await app.Services
    .GetRequiredService<Almacen>()
    .Inicializar();

await app.Services
    .GetRequiredService<CuentaDesarrollo>()
    .Inicializar();

// ============================================
// HTTPS
// ============================================

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// ============================================
// FRONTEND BLAZOR
// ============================================

app.UseResponseCompression();

app.UseBlazorFrameworkFiles();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(
            ".css",
            StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl =
                "no-cache";
        }
    }
});

// ============================================
// MIDDLEWARE
// ============================================

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl =
            "no-store";

        if (context.Request.Method is not
            ("GET" or "HEAD" or "OPTIONS"))
        {
            try
            {
                await context.RequestServices
                    .GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = 400;

                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        mensaje =
                            "La sesión de seguridad cambió. Vuelve a iniciar sesión."
                    }
                );

                return;
            }
        }
    }

    try
    {
        await next();
    }
    catch (ConflictoException ex)
    {
        context.Response.StatusCode = 409;

        await context.Response.WriteAsJsonAsync(
            new { mensaje = ex.Message }
        );
    }
    catch (InvalidOperationException ex)
    {
        context.Response.StatusCode = 400;

        await context.Response.WriteAsJsonAsync(
            new { mensaje = ex.Message }
        );
    }
    catch (PostgresException ex)
        when (ex.SqlState ==
            PostgresErrorCodes.UniqueViolation)
    {
        context.Response.StatusCode = 409;

        await context.Response.WriteAsJsonAsync(
            new
            {
                mensaje =
                    "Ese usuario o corte ya existe. Actualiza la información."
            }
        );
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
    {
        context.Response.StatusCode = 409;

        await context.Response.WriteAsJsonAsync(
            new
            {
                mensaje =
                    "Ese usuario o corte ya existe. Actualiza la información."
            }
        );
    }
});

// ============================================
// AUTENTICACION - TOKEN
// ============================================

app.MapGet("/api/auth/token",
    (HttpContext c, IAntiforgery a) =>
        new
        {
            token =
                a.GetAndStoreTokens(c).RequestToken
        }
);

// ============================================
// AUTENTICACION - SESION
// ============================================

app.MapGet("/api/auth/sesion",
    async (HttpContext c, Almacen db) =>
        new SesionRespuesta(
            c.User.Identity?.IsAuthenticated == true
                ? (await db.Usuario(
                    c.User.Identity.Name!
                ))?.Usuario
                : null,
            await db.SinUsuarios()
        )
);

// ============================================
// VALIDACION DE CUENTAS
// ============================================

void ValidarCuenta(
    string usuario,
    string nombre,
    string clave)
{
    if (
        !Regex.IsMatch(
            usuario,
            "^[a-zA-Z0-9._-]{3,40}$"
        )
        || nombre.Trim().Length is < 2 or > 80
        || clave.Length is < 10 or > 128
    )
    {
        throw new InvalidOperationException(
            "Usa un usuario de 3–40 letras/números, un nombre de 2–80 caracteres y una contraseña de 10–128 caracteres."
        );
    }
}

// ============================================
// CONFIGURACION INICIAL
// ============================================

app.MapPost("/api/auth/configurar",
    async (
        AccesoSolicitud s,
        Almacen db,
        IPasswordHasher<string> hash) =>
    {
        // Impedir la configuracion si ya hay usuarios.
        if (!await db.SinUsuarios())
        {
            return Results.Conflict(
                new
                {
                    mensaje =
                        "La configuración inicial ya fue completada."
                }
            );
        }

        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(
                s.ClaveInstalacion),
            System.Text.Encoding.UTF8.GetBytes(
                claveInstalacion)))
        {
            return Results.BadRequest(
                new
                {
                    mensaje =
                        "Clave de instalación incorrecta."
                }
            );
        }

        ValidarCuenta(
            s.Usuario,
            s.Nombre,
            s.Clave
        );

        await db.CrearUsuario(
            s.Usuario,
            s.Nombre,
            hash.HashPassword(
                s.Usuario,
                s.Clave
            ),
            true
        );

        return Results.Ok();
    }
).RequireRateLimiting("acceso");

// ============================================
// INICIO DE SESION
// ============================================

app.MapPost("/api/auth/entrar",
    async (
        AccesoSolicitud s,
        HttpContext c,
        Almacen db,
        IPasswordHasher<string> hash) =>
    {
        if (s.Usuario.Length > 80 ||
            s.Clave.Length > 128)
        {
            return Results.Unauthorized();
        }

        var registro =
            await db.Usuario(s.Usuario);

        if (registro is null ||
            hash.VerifyHashedPassword(
                s.Usuario,
                registro.Value.Hash,
                s.Clave
            ) == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        var u = registro.Value.Usuario;

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.Name,
                        u.Usuario
                    ),
                    new Claim(
                        ClaimTypes.Role,
                        u.Rol
                    )
                },
                CookieAuthenticationDefaults
                    .AuthenticationScheme
            )
        );

        await c.SignInAsync(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            principal
        );

        return Results.Ok(u);
    }
).RequireRateLimiting("acceso");

// ============================================
// CERRAR SESION
// ============================================

app.MapPost("/api/auth/salir",
    async (HttpContext c) =>
    {
        await c.SignOutAsync();
        return Results.Ok();
    }
).RequireAuthorization();

// ============================================
// API DE TRABAJO
// ============================================

var api = app.MapGroup("/api/trabajo")
    .RequireAuthorization();

api.MapGet("/",
    (Almacen db, long? inventarioConocido, bool? compacto) =>
        db.Estado(
            inventarioConocido,
            compacto ?? false
        )
);

api.MapPut("/borrador",
    (GuardarBorrador s, Almacen db, HttpContext c) =>
        db.Guardar(
            s,
            c.User.Identity!.Name!
        )
);

api.MapPut("/configuracion",
    async (
        GuardarConfiguracion s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.Configurar(
                    s,
                    c.User.Identity!.Name!
                )
            )
);

api.MapPost("/inventarios",
    async (
        ImportarCorte s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.Importar(
                    s,
                    c.User.Identity!.Name!
                )
            )
);

api.MapPut("/actividad",
    async (
        GuardarReporteActividad s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.GuardarActividad(
                    s,
                    c.User.Identity!.Name!
                )
            )
);

api.MapPost("/generar",
    async (
        GenerarPropuesta s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.Generar(
                    s,
                    c.User.Identity!.Name!
                )
            )
);

api.MapPost("/anular-preparacion",
    async (
        AccionTrabajo s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.AnularPreparacion(
                    s.Revision,
                    c.User.Identity!.Name!
                )
            )
);

api.MapPost("/realizar",
    async (
        AccionTrabajo s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.Realizar(
                    s.Revision,
                    c.User.Identity!.Name!
                )
            )
);

// ============================================
// PEDIDOS
// ============================================

api.MapGet("/pedidos/{id:long}",
    (long id, Almacen db) =>
        db.Detalle(id)
);

api.MapGet("/borrador/excel",
    async (long revision, Almacen db) =>
        Results.File(
            await db.ExportarBorrador(revision),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Pedido.xlsx"
        )
);

api.MapGet("/pedidos/{id:long}/excel",
    async (long id, Almacen db) =>
        Results.File(
            PedidoExportacionService.CrearHistorico(
                (await db.Detalle(id)).Items
            ),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Pedido_{id}.xlsx"
        )
);

api.MapGet("/historial",
    (
        Almacen db,
        string? codigo,
        string? estado,
        DateOnly? desde,
        DateOnly? hasta,
        int? pagina) =>
            db.Historial(
                codigo,
                estado,
                desde,
                hasta,
                pagina ?? 1
            )
);

api.MapPut("/pedidos/{id:long}",
    async (
        long id,
        CambiarEstadoPedido s,
        Almacen db,
        HttpContext c) =>
            new RevisionRespuesta(
                await db.CambiarEstado(
                    id,
                    s,
                    c.User.Identity!.Name!
                )
            )
);

// ============================================
// USUARIOS
// ============================================

app.MapGet("/api/usuarios",
    (Almacen db) =>
        db.Usuarios()
).RequireAuthorization(
    p => p.RequireRole("Administrador")
);

app.MapPost("/api/usuarios",
    async (
        NuevoUsuario s,
        Almacen db,
        IPasswordHasher<string> hash) =>
    {
        ValidarCuenta(
            s.Usuario,
            s.Nombre,
            s.Clave
        );

        await db.CrearUsuario(
            s.Usuario,
            s.Nombre,
            hash.HashPassword(
                s.Usuario,
                s.Clave
            ),
            false
        );

        return Results.Ok();
    }
).RequireAuthorization(
    p => p.RequireRole("Administrador")
);

// ============================================
// FRONTEND BLAZOR - FALLBACK
// ============================================

app.MapFallbackToFile("index.html");

// ============================================
// EJECUCION
// ============================================

app.Run();

public partial class Program { }
