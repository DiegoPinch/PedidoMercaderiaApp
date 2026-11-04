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

Genera `artifacts/pruebas/vista-previa.html` con tres productos ficticios. Es una captura de HTML
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
- Pulsa ▾ en un encabezado para filtrar esa columna y ordenar de menor a mayor o de A a Z.
- Puedes combinar condiciones numéricas (igual, mayor, menor, entre) y marcar valores concretos.
- En Código / descripción puedes filtrar por texto o buscar valores y elegir Solo estos resultados.
- Aplicar filtro afecta a todos los resultados antes de paginar; Cerrar descarta los cambios del panel.
- Los filtros activos se muestran sobre la tabla y se limpian al cambiar de lista.
- Filtrar no quita productos de Mi pedido y no limita el contenido del Excel exportado.
- Descargar Excel incluye todo el pedido, incluso si la búsqueda muestra solo una parte.
- El archivo tiene código, descripción, desde, cantidad y comentario; se puede adjuntar al correo.
- Más permite omitir temporalmente o marcar que no se vende. Otras listas permite recuperarlo.
- Importar inventario ofrece un segundo Excel opcional de B4: COD_ITEM, STOCK y DIAS_SIN_ACTIVIDAD.
- Indica la fecha de corte del reporte. Los días se utilizan tal como estaban en ese corte; no se incrementan automáticamente.
- En Por revisar, activa Apartar productos con stock y sin movimiento; elige 30, 90, 180 o 365 días.
- Solo se apartan productos con stock positivo tanto en el inventario actual como en el reporte B4.
- Sin movimiento en B4 permite agregarlos directamente o recuperarlos mediante Incluir esta vez.
- No se alteran los productos ya añadidos al pedido. Los datos desconocidos, inválidos o duplicados no se usan para apartar.
- Cargar otro reporte reinicia las excepciones de actividad. En el modo compartido, cargar otro inventario conserva el pedido y el último reporte de movimientos de la sucursal.

El pedido, los inventarios, la configuración y las exclusiones se guardan ahora en el servidor.
El reporte de movimientos se carga en Preparar pedido y se guarda cifrado por sucursal. Las pruebas del servicio antiguo de importación verifican también su funcionamiento aislado, pero la interfaz compartida utiliza Analizar y luego TrabajoService.Importar.

Pruebas del servidor (PostgreSQL real en un esquema temporal, nunca tablas reales). Configura primero `PEDIDOS_TEST_CONNECTION` con la conexión de tu base de pruebas; no se utiliza la conexión de la aplicación:

```powershell
dotnet run --project Tests/Servidor/PruebasServidor.csproj
```

Pruebas HTTP: configurar `ConnectionStrings__Pedidos` con una base o esquema de prueba aislado y `CuentaDesarrollo__Habilitada=false` para probar el registro inicial. Preparar allí las tablas del modelo EF e iniciar el servidor con `--Datos` apuntando a una carpeta temporal nueva, y ejecutar `python Tests/servidor_http.py http://127.0.0.1:5189 RUTA_DATOS_TEMPORAL`. El servidor ya no crea ni migra tablas automáticamente.
Verifican cuentas, cookies, protección CSRF, permisos, dos usuarios, cargas, generación y registro del pedido. No ejecutarlas contra la base de trabajo real.

El servidor verifica AES-GCM, nonce aleatorio, rechazo de manipulación, separación de contextos, clave equivocada y persistencia cifrada en tablas creadas desde el modelo EF. Las pruebas usan claves aleatorias en memoria. Para comprobar solo el algoritmo sin PostgreSQL: `dotnet run --project Tests/Servidor/PruebasServidor.csproj -- --cifrado`.

El banco de pruebas genera las tablas directamente desde `PedidosDbContext` exclusivamente en su esquema temporal. Verifica el rechazo del arranque sin esquema, la inicialización sin migraciones automáticas, historial por código/estado/fechas, frecuencia de pedidos en 90 días, agotados, artículos nuevos sin centros disponibles y cantidades calculadas entre cortes consecutivos. No crea archivos de migración en el proyecto.

Para comprobar sin PostgreSQL que la cuenta de prueba se bloquea en producción y cuando está desactivada: `dotnet run --project Tests/Servidor/PruebasServidor.csproj -- --cuenta-desarrollo`. La suite completa también verifica creación concurrente única, contraseña verificable y conservación de cuentas existentes.

El ciclo de pedidos se verifica con `dotnet run --project Tests/Servidor/PruebasServidor.csproj -- --ciclo`: cierre único, bloqueo de importación abierta, borrador futuro sin origen, conservación y revisión obligatoria al cargar, catálogo sin duplicados, historial descargable y anulación. La suite completa incluye estas comprobaciones. Las pruebas Razor verifican el estado cerrado y las filas amarillas.

La prueba `--consolidacion` recrea el esquema anterior, carga fichas y existencias cifradas, aplica el cambio de modelo EF con `ConsolidacionInventario.Antes/Despues` y comprueba stock actual/anterior, borrador, pedidos históricos y cantidades de filas estables después de varias cargas. No crea archivos de migración ni utiliza el esquema de trabajo real.

Las suites también comprueban la actualización condicional del inventario, igualdad de existencias en respuestas compactas, copias aisladas del borrador, escritura exclusiva de las filas modificadas y descarga desde el servidor con control de revisión. El cliente prueba el cambio de Excel y el ingreso sin una segunda consulta de sesión.

`dotnet run --project Tests/Servidor/PruebasServidor.csproj -- --reposicion` verifica cobertura y disponibilidad, insuficiencia de datos, ausencias y anomalías, preferencias acotadas sin compras excepcionales, perfiles limitados, reutilización del análisis, invalidación al configurar, cierre/anulación y cifrado. La suite completa incluye estas pruebas en un esquema temporal aislado.
