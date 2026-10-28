import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// Comprueba el puente de descarga sin abrir un navegador ni enviar archivos.
const codigo = await readFile(new URL('../PedidoMercaderia/wwwroot/js/descargarPedido.js', import.meta.url), 'utf8');
const { descargarPedido } = await import(`data:text/javascript;base64,${Buffer.from(codigo).toString('base64')}`);
let archivo, temporizador, agregado = false, retirado = false, clic = false, revocado = false;
const enlace = { click() { clic = true; assert.ok(agregado); assert.equal(revocado, false); }, remove() { retirado = true; } };
globalThis.document = { createElement(tipo) { assert.equal(tipo, 'a'); return enlace; }, body: { appendChild() { agregado = true; } } };
globalThis.URL.createObjectURL = blob => { archivo = blob; return 'blob:pedido-prueba'; };
globalThis.URL.revokeObjectURL = url => { assert.equal(url, 'blob:pedido-prueba'); revocado = true; };
globalThis.setTimeout = (funcion, plazo) => { assert.equal(plazo, 60000); temporizador = funcion; };
await descargarPedido('Pedido_B4.xlsx', { arrayBuffer: async () => new Uint8Array([80, 75, 3, 4]).buffer });
assert.equal(enlace.download, 'Pedido_B4.xlsx');
assert.equal(enlace.href, 'blob:pedido-prueba');
assert.equal(archivo.type, 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
assert.deepEqual([...new Uint8Array(await archivo.arrayBuffer())], [80, 75, 3, 4]);
assert.ok(clic && retirado && !revocado);
temporizador(); assert.ok(revocado);
await assert.rejects(descargarPedido('error.xlsx', { arrayBuffer: async () => { throw new Error('fallo de lectura'); } }), /fallo de lectura/);
console.log('OK: descarga con nombre, contenido y tipo Excel; limpieza diferida y errores de lectura.');
