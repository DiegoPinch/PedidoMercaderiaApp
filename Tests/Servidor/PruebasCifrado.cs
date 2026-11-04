using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using PedidoMercaderia.Models;
using Servidor;

static class PruebasCifrado
{
    public static void VerificarAlgoritmo(Action<bool, string> verificar)
    {
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        var a = cifrado.Cifrar("dato confidencial", "tabla/1/campo");
        var b = cifrado.Cifrar("dato confidencial", "tabla/1/campo");
        verificar(!a.SequenceEqual(b), "Cifrar el mismo valor usa un nonce nuevo y produce resultados distintos");
        verificar(cifrado.Leer<string>(a, "tabla/1/campo") == "dato confidencial", "AES-GCM recupera el valor original");
        bool cambiado = false;
        a[^1] ^= 1;
        try { cifrado.Leer<string>(a, "tabla/1/campo"); } catch (CryptographicException) { cambiado = true; }
        verificar(cambiado, "El cifrado rechaza contenido manipulado");
        bool intercambiado = false;
        try { cifrado.Leer<string>(b, "tabla/2/campo"); } catch (CryptographicException) { intercambiado = true; }
        verificar(intercambiado, "El cifrado impide trasladar el valor a otra fila o campo");
        verificar(cifrado.Indice("producto", "abc") == cifrado.Indice("producto", "ABC") &&
            cifrado.Indice("producto", "ABC") != cifrado.Indice("usuario", "ABC"), "Índices protegidos distinguen dominios y normalizan códigos");
    }

    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        VerificarAlgoritmo(verificar);
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        await using var banco = await BancoPruebas.Crear();
        await banco.CrearModeloEf();
        using var db = new Almacen(banco.Fabrica, cifrado);
        await db.Inicializar();
        await db.CrearUsuario("admin", "Nombre confidencial", new PasswordHasher<string>().HashPassword("admin", "Prueba-segura-2026"), true);
        var inventario = new ResultadoImportacion { NombreArchivo = "privado.xlsx", BodegasDetectadas = ["B4", "B1", "B40"],
            Productos = [new() { Codigo = "SECRETO-001", Descripcion = "Producto reservado", Existencias = new() { ["B4"] = 6, ["B1"] = 30, ["B40"] = 50 } }] };
        await db.Importar(new(0, new(2026, 10, 1), inventario), "admin");
        await BancoPruebas.ConfigurarB4(db);
        var e = await db.Estado();
        var items = new List<ItemPedido> { new() { CodigoProducto = "SECRETO-001", BodegaOrigen = "B1", CantidadSolicitada = 3, DescripcionProducto = "Producto reservado", Comentario = "Comentario privado" } };
        await db.Guardar(new(e.Revision, items, e.Corte!.Id), "admin");
        e = await db.Estado();
        await db.Realizar(e.Revision, "admin");
        e = await db.Estado();
        var detalle = await db.Detalle(e.Historial.Single().Id);
        verificar(e.Inventario!.Productos.Single().ObtenerStock("B4") == 6 && detalle.Items.Single().Comentario == "Comentario privado" &&
            detalle.Items.Single().CantidadSolicitada == 3, "El esquema EF conserva inventario y detalles cifrados del pedido");
        var cuenta = (await db.Usuario("ADMIN"))!.Value;
        verificar(new PasswordHasher<string>().VerifyHashedPassword("admin", cuenta.Hash, "Prueba-segura-2026") != PasswordVerificationResult.Failed,
            "El hash cifrado permite comprobar la contraseña del usuario");
        await using (var c = banco.Fuente.CreateCommand("""
            SELECT (SELECT count(*) FROM productos)=1 AND (SELECT count(*) FROM bodegas)=3
              AND (SELECT count(*) FROM existencias)=3 AND (SELECT count(*) FROM pedido_detalles)=1
            """)) verificar((bool)(await c.ExecuteScalarAsync())!, "EF crea las tablas relacionales separadas con sus datos cifrados");
        await using (var c = banco.Fuente.CreateCommand("SELECT usuario_cifrado,nombre_cifrado,hash_cifrado FROM usuarios"))
        await using (var r = await c.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            verificar(Enumerable.Range(0, 3).All(i => !Encoding.UTF8.GetString(r.GetFieldValue<byte[]>(i)).Contains("admin", StringComparison.OrdinalIgnoreCase)),
                "PostgreSQL recibe datos de usuario cifrados, incluido el hash de contraseña");
        }
        await using (var c = banco.Fuente.CreateCommand("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema=current_schema() AND table_name <> '__EFMigrationsHistory'
              AND data_type NOT IN ('bytea','bigint','integer')
              AND column_name NOT IN ('id','producto_id','bodega_id','origen_id','destino_id','autor_id','usuario_id','fecha_indice','estado_indice')
            """)) verificar((long)(await c.ExecuteScalarAsync())! == 0, "No quedan columnas de negocio en texto claro: solo cifrado y claves técnicas");
        using var incorrecta = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        using var sinClave = new Almacen(banco.Fabrica, incorrecta);
        bool rechazada = false;
        try { await sinClave.Inicializar(); } catch (CryptographicException) { rechazada = true; }
        verificar(rechazada && (await db.Estado()).Revision == e.Revision, "Una clave equivocada bloquea el arranque sin modificar los datos");

    }
}

