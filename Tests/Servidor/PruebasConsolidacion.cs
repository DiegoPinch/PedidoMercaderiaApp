using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using Servidor.DatosModelo;
using System.Security.Cryptography;

static class PruebasConsolidacion
{
    public static async Task Ejecutar(Action<bool, string> verificar)
    {
        await using var banco = await BancoPruebas.Crear();
        using var cifrado = new CifradoDatos(RandomNumberGenerator.GetBytes(32));
        await using var ef = banco.Fabrica.CreateDbContext();
        // Recrear exclusivamente en el esquema aleatorio el modelo que tiene instalada la aplicación anterior.
        var script = ef.GetService<IMigrator>().GenerateScript(toMigration: "20261101160729_ciclopedidos");
        await ef.Database.ExecuteSqlRawAsync(script);
        using var almacen = new Almacen(banco.Fabrica, cifrado);
        // Sembrar con las columnas del esquema antiguo; el arranque actual exige sus migraciones nuevas.
        ef.Set<Servidor.DatosModelo.ControlCifrado>().Add(new() { Id = 1, Verificador = cifrado.Cifrar("Pedidos/v2", "control/1") });
        ef.Trabajo.Add(new() { Id = 1, ConfiguracionCifrada = cifrado.Cifrar(new ConfiguracionBodegas(), "trabajo/1/configuracion") });
        await ef.SaveChangesAsync(); ef.ChangeTracker.Clear();
        await almacen.CrearUsuario("admin", "Admin", "hash", true);
        var autor = cifrado.Indice("usuario", "admin");
        string Pid(string codigo) => cifrado.Indice("producto", codigo);
        string Bid(string codigo) => cifrado.Indice("bodega", codigo);
        async Task Sql(string sql, params (string, object)[] parametros)
        {
            await using var cmd = banco.Fuente.CreateCommand(sql);
            foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach (var b in new[] { "B4", "B1", "B40" })
            await Sql("INSERT INTO bodegas(id,codigo_cifrado,nombre_cifrado) VALUES(@id,@c,@n)", ("id", Bid(b)),
                ("c", cifrado.Cifrar(b, $"bodegas/{Bid(b)}/codigo")), ("n", cifrado.Cifrar(b, $"bodegas/{Bid(b)}/nombre")));
        var config = new ConfiguracionBodegas { BodegaLocal = "B4", BodegasOrigen = ["B1", "B40"] };
        await Sql("UPDATE trabajo SET configuracion_cifrada=@c", ("c", cifrado.Cifrar(config, "trabajo/1/configuracion")));
        for (int corte = 1; corte <= 3; corte++)
        {
            var codigos = corte == 1 ? new[] { "A", "B", "C" } : corte == 2 ? new[] { "A", "B" } : new[] { "A", "NUEVO" };
            var resumen = new CorteResumen(corte, new(2026, 10, corte), $"archivo{corte}.xlsx", "admin", codigos.Length);
            await Sql("INSERT INTO cortes(id,fecha_indice,autor_id,resumen_cifrado,metadata_cifrada) VALUES(@id,@f,@a,@r,@m)",
                ("id", (long)corte), ("f", cifrado.Indice("fecha", resumen.Fecha.ToString("yyyy-MM-dd"))), ("a", autor),
                ("r", cifrado.Cifrar(resumen, $"cortes/{corte}/resumen")),
                ("m", cifrado.Cifrar(new ResultadoImportacion { NombreArchivo = resumen.Archivo, BodegasDetectadas = ["B4", "B1", "B40"] }, $"cortes/{corte}/metadata")));
            foreach (var codigo in codigos)
            {
                var pid = Pid(codigo);
                var ficha = new ProductoInventario { Codigo = codigo, Descripcion = $"Descripción {codigo} carga {corte}", Marca = "Marca", TipoItem = "Tipo", Precio = 12.5m };
                await Sql("INSERT INTO productos(id,codigo_cifrado,descripcion_cifrada,primer_corte_id) VALUES(@id,@c,@d,@f) ON CONFLICT(id) DO NOTHING",
                    ("id", pid), ("c", cifrado.Cifrar(codigo, $"productos/{pid}/codigo")), ("d", cifrado.Cifrar(ficha.Descripcion, $"productos/{pid}/descripcion")), ("f", (long)corte));
                await Sql("INSERT INTO productos_por_corte(corte_id,producto_id,atributos_cifrados) VALUES(@c,@p,@a)",
                    ("c", (long)corte), ("p", pid), ("a", cifrado.Cifrar(ficha, $"productos_por_corte/{corte}/{pid}")));
                foreach (var b in new[] { "B4", "B1", "B40" })
                {
                    decimal cantidad = b == "B4" ? 12 - corte * 2 : 50;
                    await Sql("INSERT INTO existencias(corte_id,producto_id,bodega_id,cantidad_cifrada) VALUES(@c,@p,@b,@q)",
                        ("c", (long)corte), ("p", pid), ("b", Bid(b)), ("q", cifrado.Cifrar(cantidad, $"existencias/{corte}/{pid}/{Bid(b)}")));
                }
            }
        }
        await Sql("SELECT setval(pg_get_serial_sequence('cortes','id'),3)");
        var pidA = Pid("A"); var origen = Bid("B1");
        ef.Pedidos.Add(new() { Id = 1, AutorId = autor, DestinoId = Bid("B4"), CorteId = 1, EstadoIndice = cifrado.Indice("estado", "Guardado"),
            ResumenCifrado = cifrado.Cifrar(new PedidoResumen(1, DateTime.UtcNow, "admin", "Guardado", 1, 1), "pedidos/1/resumen") });
        await ef.SaveChangesAsync(); ef.ChangeTracker.Clear();
        await Sql("INSERT INTO pedido_detalles(pedido_id,producto_id,origen_id,cantidad_cifrada,reserva_cifrada,descripcion_cifrada,comentario_cifrado) VALUES(1,@p,@o,@c,@r,@d,@m)",
            ("p", pidA), ("o", origen), ("c", cifrado.Cifrar(7m, $"pedido_detalles/1/{pidA}/{origen}/cantidad")),
            ("r", cifrado.Cifrar(1m, $"pedido_detalles/1/{pidA}/{origen}/reserva")), ("d", cifrado.Cifrar("Descripción original del pedido", $"pedido_detalles/1/{pidA}/{origen}/descripcion")),
            ("m", cifrado.Cifrar("Comentario original", $"pedido_detalles/1/{pidA}/{origen}/comentario")));
        await Sql("INSERT INTO borrador_detalles(trabajo_id,producto_id,origen_id,cantidad_cifrada,reserva_cifrada,descripcion_cifrada,comentario_cifrado,revision_cifrada) VALUES(1,@p,NULL,@c,@r,@d,@m,@v)",
            ("p", pidA), ("c", cifrado.Cifrar(9m, $"borrador_detalles/1/{pidA}//cantidad")), ("r", cifrado.Cifrar(1m, $"borrador_detalles/1/{pidA}//reserva")),
            ("d", cifrado.Cifrar("A", $"borrador_detalles/1/{pidA}//descripcion")), ("m", cifrado.Cifrar("Anotación", $"borrador_detalles/1/{pidA}//comentario")),
            ("v", cifrado.Cifrar(true, $"borrador_detalles/1/{pidA}//revision")));

        var viejo = ef.GetService<IModelRuntimeInitializer>().Initialize(new Servidor.Migrations.ciclopedidos().TargetModel, designTime: true);
        var modelo = ef.GetService<IDesignTimeModel>().Model;
        var cambios = ef.GetService<IMigrationsModelDiffer>().GetDifferences(viejo.GetRelationalModel(), modelo.GetRelationalModel());
        var migracion = new MigrationBuilder(ef.Database.ProviderName!);
        ConsolidacionInventario.Antes(migracion);
        foreach (var cambio in cambios) migracion.Operations.Add(cambio);
        ConsolidacionInventario.Despues(migracion);
        var comandos = ef.GetService<IMigrationsSqlGenerator>().Generate(migracion.Operations, modelo);
        await using (var tx = await ef.Database.BeginTransactionAsync())
        {
            await ef.Database.ExecuteSqlRawAsync(comandos[0].CommandText);
            await tx.RollbackAsync();
        }
        await using (var consulta = banco.Fuente.CreateCommand("SELECT count(*) FROM existencias"))
            verificar((long)(await consulta.ExecuteScalarAsync())! == 21, "Si el traslado falla, la transacción conserva todas las existencias originales");
        await using (var tx = await ef.Database.BeginTransactionAsync())
        {
            foreach (var comando in comandos) await ef.Database.ExecuteSqlRawAsync(comando.CommandText);
            await tx.CommitAsync();
        }
        verificar(!modelo.GetEntityTypes().Any(t => t.GetTableName() == "productos_por_corte") && await ef.Productos.CountAsync() == 4,
            "La migración elimina productos_por_corte y conserva un catálogo único, incluidos productos ausentes");
        verificar(await ef.Existencias.CountAsync() == 9, "La migración conserva una sola fila por producto y bodega de las dos últimas referencias");
        var estado = await almacen.Estado();
        verificar(estado.Inventario!.Productos.Count == 2 && estado.Inventario.Productos.Single(p => p.Codigo == "A").ObtenerStock("B4") == 6
            && estado.Inventario.Productos.Single(p => p.Codigo == "A").Descripcion == "Descripción A carga 3",
            "El traslado conserva el último stock y los atributos cifrados del producto");
        verificar(estado.Propuestas.Single(p => p.Codigo == "A").Cantidad == 2 && estado.Cambios.Any(c => c.Codigo == "B" && c.Ahora is null),
            "La referencia anterior permite recomendar bajadas y distinguir productos ausentes sin historial de fichas");
        verificar(estado.Cambios.Any(c => c.Codigo == "NUEVO" && c.Tipo == "Nuevo") && estado.Borrador.Single().RequiereRevision
            && estado.Borrador.Single().CantidadSolicitada == 9, "El traslado conserva novedades y borrador pendiente de revisión");
        var pedido = await almacen.Detalle(1);
        verificar(pedido.Items.Single().CantidadSolicitada == 7 && pedido.Items.Single().Comentario == "Comentario original"
            && PedidoExportacionService.CrearHistorico(pedido.Items).Length > 0, "Los pedidos y su Excel histórico sobreviven a la consolidación");
        for (int n = 4; n <= 8; n++)
        {
            await BancoPruebas.PrepararOtraCarga(almacen); estado = await almacen.Estado();
            await almacen.Importar(new(estado.Revision, new(2026, 10, n), new() { NombreArchivo = "siguiente.xlsx", BodegasDetectadas = ["B4", "B1", "B40"],
                Productos = [new() { Codigo = "A", Descripcion = "Actualizada", Existencias = new() { ["B4"] = 5, ["B1"] = 50, ["B40"] = 50 } },
                    new() { Codigo = "B", Existencias = new() { ["B4"] = 1, ["B1"] = 50, ["B40"] = 50 } }] }), "admin");
            if (n == 4) verificar(!(await almacen.Estado()).Propuestas.Any(p => p.Codigo == "B" && p.Rapido), "Reaparecer después de faltar no inventa una bajada de stock");
        }
        verificar(await ef.Productos.CountAsync() == 4 && await ef.Existencias.CountAsync() == 6, "Cinco Excel adicionales no multiplican productos ni existencias");
        using var reiniciado = new Almacen(banco.Fabrica, cifrado);
        estado = await reiniciado.Estado();
        verificar(estado.Inventario!.Productos.Single(p => p.Codigo == "A").Descripcion == "Actualizada" && !estado.Propuestas.Any(p => p.Rapido),
            "Reiniciar conserva el catálogo actualizado y evita reposiciones con stock estable");
    }
}
