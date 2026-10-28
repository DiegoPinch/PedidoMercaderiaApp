# Comprobaciones de pedidos

Desde la raíz del proyecto:

```powershell
dotnet restore Tests/Reglas/Reglas.csproj --configfile Tests/Reglas/NuGet.Config
dotnet run --project Tests/Reglas/Reglas.csproj --no-restore
dotnet build PedidoMercaderia.sln --no-restore
```

El ejecutable comprueba las reglas con datos ficticios: B1 y B40 como únicos orígenes,
exclusión de Bame, reserva en altas y modificaciones, cantidades fraccionarias,
revalidación del pedido y compatibilidad con configuraciones antiguas. También prueba la importación
del reporte B4, códigos con caracteres de control, filas duplicadas, días inválidos,
coincidencias entre archivos, agotados y excepciones manuales.
La exportación se valida con OpenXML y ExcelDataReader: cinco columnas, códigos
con ceros iniciales, texto que comienza por `=`, cantidades decimales, comentarios,
orígenes permitidos, disponibilidad y rechazo de pedidos vacíos o incompletos.
No requiere paquetes de pruebas externos.

Para comprobar el módulo de descarga sin abrir el navegador:

```powershell
node Tests/descarga.test.mjs
```

Para generar una vista previa estática de la pantalla real:

```powershell
dotnet run --project Tests/Reglas/Reglas.csproj --no-restore -- --vista .
```

Genera `Tests/vista-previa.html` con tres productos ficticios. Es una captura de HTML
del componente Razor y sus estilos: los botones de pedido no son interactivos en ese
archivo. `LibroPrueba.cs` genera libros Excel ficticios en memoria para probar los servicios
reales; no modifica los datos de la aplicación. La opción `--vista` comprueba también
la pantalla con el filtro de actividad apagado, encendido y con recuperación manual.

## Revisión manual

- La vista inicial muestra Producto, Tengo, Matriz, Ebenezer, Pedir y las acciones.
- Opciones avanzadas y Ver detalle empiezan cerrados.
- Solo los que no tengo limita a productos con stock local cero o negativo.
- Los ajustes numéricos avanzados se aplican al pulsar Aplicar cambios.
- Los filtros anteriores por porcentaje ya no ocultan productos.
- Agregar mueve el producto a Mi pedido; allí se puede cambiar cantidad y origen.
- En Mi pedido, escribe un comentario opcional debajo del producto (hasta 300 caracteres).
- Descargar Excel incluye todo el pedido, incluso si la búsqueda muestra solo una parte.
- El archivo tiene código, descripción, desde, cantidad y comentario; se puede adjuntar al correo.
- Más permite omitir temporalmente o marcar que no se vende. Otras listas permite recuperarlo.
- Importar inventario ofrece un segundo Excel opcional de B4: COD_ITEM, STOCK y DIAS_SIN_ACTIVIDAD.
- Indica la fecha de corte del reporte. Los días se utilizan tal como estaban en ese corte; no se incrementan automáticamente.
- En Por revisar, activa Apartar productos con stock y sin movimiento; elige 30, 90, 180 o 365 días.
- Solo se apartan productos con stock positivo tanto en el inventario actual como en el reporte B4.
- Sin movimiento en B4 permite agregarlos directamente o recuperarlos mediante Incluir esta vez.
- No se alteran los productos ya añadidos al pedido. Los datos desconocidos, inválidos o duplicados no se usan para apartar.
- Cargar otro reporte reinicia las excepciones de actividad. Cargar otro inventario reinicia el pedido y elimina el reporte de actividad.

El pedido, el inventario y el reporte de actividad todavía están en memoria: recargar la página los reinicia.
La configuración y las exclusiones permanentes se guardan en el navegador.
