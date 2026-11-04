namespace Servidor.Configuracion;

public static class ConexionPedidos
{
    public static string Obtener(IConfiguration configuracion)
    {
        var conexion = configuracion.GetConnectionString("Pedidos");
        if (string.IsNullOrWhiteSpace(conexion))
            throw new InvalidOperationException("Configura ConnectionStrings:Pedidos en el servidor o la variable ConnectionStrings__Pedidos antes de continuar.");

        return conexion;
    }
}
