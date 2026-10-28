export async function descargarPedido(nombre, referencia) {
    const contenido = await referencia.arrayBuffer();
    const archivo = new Blob([contenido], {
        type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    });
    const url = URL.createObjectURL(archivo);
    const enlace = document.createElement("a");
    try {
        enlace.href = url;
        enlace.download = nombre;
        document.body.appendChild(enlace);
        enlace.click();
    } finally {
        enlace.remove();
        // Dar tiempo al navegador para comenzar la descarga antes de liberar el archivo.
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }
}
