using PedidoMercaderia.Models;
using PedidoMercaderia.Services;
using Servidor;
using Servidor.Configuracion;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.EntityFrameworkCore;

int n = 0;
void Verificar(bool condicion, string mensaje) { if (!condicion) throw new Exception(mensaje); Console.WriteLine("OK: " + mensaje); n++; }
async Task Rechaza(Func<Task> accion, string mensaje) { bool rechazado = false; try { await accion(); } catch (Exception ex) when (ex is InvalidOperationException or ConflictoException) { rechazado = true; } Verificar(rechazado, mensaje); }
ProductoInventario P(string codigo, decimal local, decimal matriz = 50, decimal ebe = 100) => new()
{ Codigo = codigo, Descripcion = "Producto " + codigo, Marca = "Prueba", Existencias = new() { ["B4"] = local, ["B1"] = matriz, ["B40"] = ebe, ["B39"] = 9999 } };
ResultadoImportacion Corte(params ProductoInventario[] productos) => new() { NombreArchivo = "ejemplo.xlsx", BodegasDetectadas = ["B4", "B1", "B40"], Productos = productos.ToList() };
if (args.Contains("--cifrado"))
{
    PruebasCifrado.VerificarAlgoritmo(Verificar);
    Console.WriteLine($"{n} comprobaciones de cifrado correctas.");
    return;
}
if (args.Contains("--ciclo"))
{
    await PruebasCicloPedidos.Ejecutar(Verificar);
    Console.WriteLine($"{n} comprobaciones del ciclo correctas.");
    return;
}
if (args.Contains("--consolidacion"))
{
    await PruebasConsolidacion.Ejecutar(Verificar);
    Console.WriteLine($"{n} comprobaciones de consolidación correctas.");
    return;
}
if (args.Contains("--reposicion"))
{
    await PruebasReposicion.Ejecutar(Verificar);
    Console.WriteLine($"{n} comprobaciones de reposición correctas.");
    return;
}
if (args.Contains("--cuenta-desarrollo"))
{
    var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CuentaDesarrollo:Habilitada"] = "true" }).Build();
    var entorno = new HostingEnvironment { EnvironmentName = "Production" };
    // Un almacén ausente hace fallar la prueba si se intenta acceder a datos.
    var inicializador = new CuentaDesarrollo(null!, entorno, configuracion, new PasswordHasher<string>());
    await inicializador.Inicializar();
    Verificar(true, "Producción nunca intenta crear la cuenta de prueba, aunque esté habilitada");
    entorno.EnvironmentName = "Development";
    configuracion["CuentaDesarrollo:Habilitada"] = "false";
    await inicializador.Inicializar();
    Verificar(true, "La cuenta de desarrollo desactivada no accede a la base");
    configuracion["CuentaDesarrollo:Habilitada"] = "true";
    await Rechaza(() => inicializador.Inicializar(), "Rechaza credenciales de prueba incompletas antes de acceder a la base");
    return;
}
await using var banco = await BancoPruebas.Crear();
using var cifrado = new CifradoDatos(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
using var db = new Almacen(banco.Fabrica, cifrado);
await Rechaza(() => db.Inicializar(), "El arranque sin migración falla sin crear las tablas automáticamente");
await banco.CrearModeloEf();
await db.Inicializar();
Verificar((await db.SinUsuarios()), "Una instalación nueva requiere administrador");
var hashInicial = new PasswordHasher<string>().HashPassword("admin", "Prueba12345!");
var creacionesIniciales = await Task.WhenAll(Enumerable.Range(0, 4)
    .Select(_ => db.CrearAdministradorSiVacio("admin", "Administrador", hashInicial)));
Verificar(creacionesIniciales.Count(x => x) == 1, "Varios arranques concurrentes crean un único administrador inicial");
Verificar(new PasswordHasher<string>().VerifyHashedPassword("admin", (await db.Usuario("admin"))!.Value.Hash, "Prueba12345!") != PasswordVerificationResult.Failed,
    "El administrador inicial permite comprobar la contraseña tras descifrar el hash");
Verificar(!await db.CrearAdministradorSiVacio("otro", "Otra cuenta", "otro-hash") && (await db.Usuario("admin"))!.Value.Hash == hashInicial,
    "Reiniciar no crea otra cuenta de prueba ni reemplaza la contraseña existente");
await Rechaza(async () => await db.CrearUsuario("otro", "Otro", "hash", true), "No permite un segundo registro de administrador inicial");
await db.CrearUsuario("ana", "Ana", "hash-ficticio", false);
Verificar((await db.Usuario("ANA"))!.Value.Usuario.Rol == "Colaborador", "Usuarios personales y nombres de acceso sin distinguir mayúsculas");
var primero = Corte(P("BAJA", 10), P("IGUAL", 8), P("CERO", 5), P("REGRESA", 5, 0, 0), P("REAPARECE", 7));
await db.Importar(new(0, new(2026, 10, 1), primero), "admin");
        await BancoPruebas.ConfigurarB4(db);
var estado = (await db.Estado());
Verificar(estado.Propuestas.Count == 0, "La primera carga no inventa productos nuevos ni consumo histórico");
var linea = new ItemPedido { CodigoProducto = "BAJA", BodegaOrigen = "B40", CantidadSolicitada = 3, ReservaCentro = 1, Comentario = "Comentario del equipo" };
await db.Guardar(new(estado.Revision, [linea], estado.Corte!.Id), "ana");
var segundo = Corte(P("BAJA", 6), P("IGUAL", 8), P("CERO", 0), P("REGRESA", 5), P("NUEVO", 0), P("SOLO_BAME", 0, 0, 0));
await BancoPruebas.PrepararOtraCarga(db);
estado = (await db.Estado()); await db.Importar(new(estado.Revision, new(2026, 10, 8), segundo), "admin");
estado = (await db.Estado());
Verificar(estado.Borrador.Single().Comentario == "Comentario del equipo", "Importar otra semana conserva el pedido y sus comentarios");
Verificar(estado.Propuestas.Single(p => p.Codigo == "BAJA").Cantidad == 4, "La bajada sugiere reponer la diferencia observada");
Verificar(!estado.Propuestas.Any(p => p.Codigo == "IGUAL"), "Stock positivo sin cambios queda fuera de recomendaciones");
Verificar(estado.Propuestas.Single(p => p.Codigo == "NUEVO").Tipo == "Nuevo" && !estado.Propuestas.Single(p => p.Codigo == "NUEVO").Rapido, "Los códigos nuevos se muestran como prueba opcional");
Verificar(estado.Propuestas.Single(p => p.Codigo == "REGRESA").Tipo == "Disponible otra vez", "Detecta reposición en centros autorizados");
Verificar(!estado.Propuestas.Any(p => p.Codigo == "SOLO_BAME"), "Bame nunca abastece una recomendación");
var revisionVieja = estado.Revision;
await db.Generar(new(estado.Revision, false), "ana");
estado = (await db.Estado());
Verificar(estado.Borrador.Count == 2 && estado.Borrador.Single(i => i.CodigoProducto == "BAJA").CantidadSolicitada == 3, "Generar añade agotados y conserva cantidades ya editadas sin duplicar");
await Rechaza(async () => await db.Guardar(new(revisionVieja, [], estado.Corte!.Id), "admin"), "Un segundo usuario no puede sobrescribir una versión más reciente");
await BancoPruebas.ConfirmarBorrador(db); estado = await db.Estado();
await db.Realizar(estado.Revision, "ana"); estado = (await db.Estado());
Verificar(estado.Borrador.Count == 0 && estado.Historial.Single().Autor == "ana", "Registrar realizado conserva autor y vacía solo el borrador confirmado");
Verificar(!estado.Propuestas.Any(p => p.Codigo is "BAJA" or "CERO"), "Solicitudes pendientes no vuelven a sugerirse");
var historico = (await db.Detalle(estado.Historial.Single().Id));
Verificar(historico.Items.Any(i => i.Comentario == "Comentario del equipo" && i.DescripcionProducto == "Producto BAJA"), "Historial conserva descripciones y comentarios originales");
Verificar(PedidoExportacionService.CrearHistorico(historico.Items).Length > 0, "Permite descargar el pedido histórico sin depender del stock actual");
Verificar(!estado.PedidoAbierto && historico.Resumen.Estado == "Guardado", "Guardar cierra el ciclo sin requerir recepción");
estado = (await db.Estado());
Verificar(!estado.Propuestas.Any(p => p.Codigo is "BAJA" or "CERO"), "Un pedido guardado no vuelve a sugerir con el mismo Excel");
var tercero = Corte(P("BAJA", 6), P("IGUAL", 8), P("REAPARECE", 7), P("NUEVO", 0));
await db.Importar(new(estado.Revision, new(2026, 10, 15), tercero), "ana"); estado = (await db.Estado());
Verificar(!estado.Propuestas.Any(p => p.Codigo == "REAPARECE" && p.Tipo == "Nuevo"), "Un código que reaparece no se confunde con uno nunca visto");
Verificar(!estado.Propuestas.Any(p => p.Codigo == "BAJA"), "No insiste en reponer cuando el stock positivo se mantuvo igual esta semana");
await Rechaza(async () => await db.Importar(new(estado.Revision, new(2026, 10, 8), segundo), "ana"), "Rechaza cortes fuera de orden sin reemplazar el actual");
await Rechaza(async () => await db.Importar(new(estado.Revision, new(2026, 10, 15), tercero), "ana"), "Repetir una carga no duplica la misma fecha");
await Rechaza(async () => await db.Guardar(new(estado.Revision, [new() { CodigoProducto = "BAJA", BodegaOrigen = "B39", CantidadSolicitada = 1 }], estado.Corte!.Id), "ana"), "El servidor también rechaza pedidos a Bame");
estado.Configuracion.ProductosNoSugerir.Add("NUEVO");
await db.Configurar(new(estado.Revision, estado.Configuracion), "ana"); estado = (await db.Estado());
Verificar(!estado.Propuestas.Any(p => p.Codigo == "NUEVO"), "Las exclusiones compartidas impiden sugerencias automáticas");
using var otraInstancia = new Almacen(banco.Fabrica, cifrado);
await otraInstancia.Inicializar();
var reiniciado = await otraInstancia.Estado();
Verificar(reiniciado.Cortes.Count == 3 && reiniciado.Historial.Count == 1 && reiniciado.Configuracion.ProductosNoSugerir.Contains("NUEVO"), "La base conserva cortes, historial y exclusiones al reiniciar el servidor");
var revisionComun = reiniciado.Revision;
var l1 = new ItemPedido { CodigoProducto = "BAJA", BodegaOrigen = "B1", CantidadSolicitada = 2 };
var l2 = new ItemPedido { CodigoProducto = "IGUAL", BodegaOrigen = "B40", CantidadSolicitada = 3 };
await db.Guardar(new(revisionComun, [l1], reiniciado.Corte!.Id, []), "ana");
await db.Guardar(new(revisionComun, [l2], reiniciado.Corte.Id, []), "admin");
Verificar((await db.Estado()).Borrador.Count == 2, "Dos personas agregan productos diferentes y se combinan sin perder líneas");
var otraCantidad = new ItemPedido { CodigoProducto = "BAJA", BodegaOrigen = "B1", CantidadSolicitada = 8 };
await Rechaza(async () => await db.Guardar(new(revisionComun, [otraCantidad], reiniciado.Corte.Id, []), "admin"), "Cambios concurrentes sobre la misma línea requieren resolución");
estado = await db.Estado();
await BancoPruebas.PrepararOtraCarga(db); estado = await db.Estado();
var reloj = System.Diagnostics.Stopwatch.StartNew();
await db.Importar(new(estado.Revision, new(2026, 10, 22), Corte(Enumerable.Range(0, 10000).Select(i => P($"VOLUMEN-{i:D5}", 10)).ToArray())), "admin");
Console.WriteLine($"MEDICIÓN local: importar 10.000 productos: {reloj.ElapsedMilliseconds} ms");
estado = await db.Estado();
await db.Guardar(new(estado.Revision, [], estado.Corte!.Id), "admin");
estado = await db.Estado();
var revisionParalela = estado.Revision;
var corteParalelo = estado.Corte!.Id;
reloj.Restart();
await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
    (i % 2 == 0 ? db : otraInstancia).Guardar(new(revisionParalela,
        [new() { CodigoProducto = $"VOLUMEN-{i:D5}", BodegaOrigen = "B1", CantidadSolicitada = 2 }], corteParalelo, []), "ana")));
var tiempoParalelo = reloj.ElapsedMilliseconds;
estado = await db.Estado();
Verificar(estado.Borrador.Count == 20 && estado.Revision == revisionParalela + 20,
    "20 guardados simultáneos desde dos instancias conservan todas las líneas y revisiones");
Console.WriteLine($"MEDICIÓN local: 20 guardados concurrentes con catálogo de 10.000 productos: {tiempoParalelo} ms");
var revAntesFallo = estado.Revision;
await Rechaza(async () => await db.Guardar(new(revAntesFallo,
    [new() { CodigoProducto = "VOLUMEN-00001", BodegaOrigen = "B1", CantidadSolicitada = 9999 }], corteParalelo), "ana"), "Una cantidad inválida falla dentro de la transacción");
var despuesFallo = await otraInstancia.Estado();
Verificar(despuesFallo.Revision == revAntesFallo && despuesFallo.Borrador.Count == 20,
    "Un error revierte íntegramente el guardado y mantiene la revisión anterior");
await using (var lectura = banco.Fabrica.CreateDbContext())
{
    var antes = await lectura.Borrador.AsNoTracking().ToDictionaryAsync(x => x.ProductoId, x => x.ComentarioCifrado);
    var editados = despuesFallo.Borrador.Select(i => i.Copiar()).ToList();
    editados[0].Comentario = "Solo cambia esta fila";
    editados[0].BodegaOrigen = "B40";
    await db.Guardar(new(despuesFallo.Revision, editados, corteParalelo, despuesFallo.Borrador), "ana");
    var despues = await lectura.Borrador.AsNoTracking().ToDictionaryAsync(x => x.ProductoId, x => x.ComentarioCifrado);
    Verificar(despues.Count == 20 && antes.Count(x => x.Value.SequenceEqual(despues[x.Key])) == 19,
        "Editar una fila no reescribe ni vuelve a cifrar las otras 19");
    Verificar((await db.Estado()).Borrador.Single(i => i.CodigoProducto == editados[0].CodigoProducto).Comentario == editados[0].Comentario,
        "El cambio de origen conserva un contexto de cifrado válido");
}
var completo = await db.Estado();
Verificar((await db.ExportarBorrador(completo.Revision)).Length > 0
    && (await db.Estado()).Revision == completo.Revision,
    "El servidor genera el Excel sin cerrar ni modificar el pedido");
await Rechaza(() => db.ExportarBorrador(completo.Revision - 1), "Exportar una revisión antigua exige revisar los cambios del equipo");
var compacto = await db.Estado(compacto: true);
Verificar(compacto.Inventario!.Productos.Count == completo.Inventario!.Productos.Count
    && compacto.Inventario.Productos.Zip(completo.Inventario.Productos).All(par =>
        par.First.TotalCalculado == par.Second.TotalCalculado && completo.Inventario.BodegasDetectadas.All(b => par.First.ObtenerStock(b) == par.Second.ObtenerStock(b))),
    "La transferencia compacta conserva todas las existencias y totales");
var condicional = await db.Estado(completo.Corte!.Id, true);
Verificar(condicional.Inventario is null && condicional.InventarioSinCambios && condicional.Borrador.Count == 20,
    "Actualizar el mismo Excel envía los cambios del equipo sin repetir el inventario");
Verificar(!(await db.Estado(completo.Corte.Id - 1, true)).InventarioSinCambios
    && (await db.Estado()).Inventario!.Productos[0].Existencias.Count == completo.Inventario.Productos[0].Existencias.Count,
    "Otro corte recibe inventario nuevo y compactar no modifica la caché compartida");
await PruebasCifrado.Ejecutar(Verificar);
await PruebasAnalisis.Ejecutar(Verificar);
await PruebasMovimientos.Ejecutar(Verificar);
await PruebasCicloPedidos.Ejecutar(Verificar);
await PruebasConsolidacion.Ejecutar(Verificar);
await PruebasReposicion.Ejecutar(Verificar);
Console.WriteLine($"{n} comprobaciones de servidor correctas. PostgreSQL real: esquema aislado");
