using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Servidor.DatosModelo;

[Table("usuarios"), PrimaryKey(nameof(Id))]
public sealed class Usuario
{
    [Column("id")] public string Id { get; set; } = "";
    [Column("usuario_cifrado")] public byte[] UsuarioCifrado { get; set; } = [];
    [Column("nombre_cifrado")] public byte[] NombreCifrado { get; set; } = [];
    [Column("rol_cifrado")] public byte[] RolCifrado { get; set; } = [];
    [Column("hash_cifrado")] public byte[] HashCifrado { get; set; } = [];
}

[Table("bodegas"), PrimaryKey(nameof(Id))]
public sealed class Bodega
{
    [Column("id")] public string Id { get; set; } = "";
    [Column("codigo_cifrado")] public byte[] CodigoCifrado { get; set; } = [];
    [Column("nombre_cifrado")] public byte[] NombreCifrado { get; set; } = [];
}

[Table("trabajo"), PrimaryKey(nameof(Id))]
public sealed class Trabajo
{
    [Column("id")] public int Id { get; set; }
    [Column("revision")] public long Revision { get; set; }
    [Column("configuracion_cifrada")] public byte[] ConfiguracionCifrada { get; set; } = [];
}

[Table("reportes_actividad"), PrimaryKey(nameof(BodegaId))]
public sealed class ReporteActividadGuardado
{
    [Column("bodega_id")] public string BodegaId { get; set; } = "";
    [Column("contenido_cifrado")] public byte[] ContenidoCifrado { get; set; } = [];
}

[Table("cortes"), PrimaryKey(nameof(Id))]
public sealed class Corte
{
    [Column("id")] public long Id { get; set; }
    [Column("fecha_indice")] public string FechaIndice { get; set; } = "";
    [Column("autor_id")] public string AutorId { get; set; } = "";
    [Column("resumen_cifrado")] public byte[] ResumenCifrado { get; set; } = [];
    [Column("metadata_cifrada")] public byte[] MetadataCifrada { get; set; } = [];
}

[Table("productos"), PrimaryKey(nameof(Id))]
public sealed class Producto
{
    [Column("id")] public string Id { get; set; } = "";
    [Column("codigo_cifrado")] public byte[] CodigoCifrado { get; set; } = [];
    [Column("descripcion_cifrada")] public byte[] DescripcionCifrada { get; set; } = [];
    [Column("primer_corte_id")] public long PrimerCorteId { get; set; }
    [Column("ultimo_corte_id")] public long? UltimoCorteId { get; set; }
    [Column("anterior_corte_id")] public long? AnteriorCorteId { get; set; }
    [Column("atributos_cifrados")] public byte[]? AtributosCifrados { get; set; }
}

[Table("existencias"), PrimaryKey(nameof(ProductoId), nameof(BodegaId))]
public sealed class Existencia
{
    [Column("producto_id")] public string ProductoId { get; set; } = "";
    [Column("bodega_id")] public string BodegaId { get; set; } = "";
    [Column("corte_id")] public long CorteId { get; set; }
    [Column("cantidad_cifrada")] public byte[] CantidadCifrada { get; set; } = [];
    [Column("corte_anterior_id")] public long? CorteAnteriorId { get; set; }
    [Column("cantidad_anterior_cifrada")] public byte[]? CantidadAnteriorCifrada { get; set; }
}

[Table("pedidos"), PrimaryKey(nameof(Id))]
public sealed class Pedido
{
    [Column("id")] public long Id { get; set; }
    [Column("autor_id")] public string AutorId { get; set; } = "";
    [Column("destino_id")] public string DestinoId { get; set; } = "";
    [Column("corte_id")] public long CorteId { get; set; }
    [Column("resumen_cifrado")] public byte[] ResumenCifrado { get; set; } = [];
    [Column("estado_indice")] public string EstadoIndice { get; set; } = "";
    [Column("cerrado_corte_id")] public long? CerradoCorteId { get; set; }
}

[Table("pedido_detalles"), PrimaryKey(nameof(PedidoId), nameof(ProductoId))]
public sealed class PedidoLinea
{
    [Column("excepcional_cifrado")] public byte[]? ExcepcionalCifrado { get; set; }
    [Column("pedido_id")] public long PedidoId { get; set; }
    [Column("producto_id")] public string ProductoId { get; set; } = "";
    [Column("origen_id")] public string OrigenId { get; set; } = "";
    [Column("cantidad_cifrada")] public byte[] CantidadCifrada { get; set; } = [];
    [Column("reserva_cifrada")] public byte[] ReservaCifrada { get; set; } = [];
    [Column("descripcion_cifrada")] public byte[] DescripcionCifrada { get; set; } = [];
    [Column("comentario_cifrado")] public byte[] ComentarioCifrado { get; set; } = [];
}

[Table("borrador_detalles"), PrimaryKey(nameof(TrabajoId), nameof(ProductoId))]
public sealed class BorradorLinea
{
    [Column("excepcional_cifrado")] public byte[]? ExcepcionalCifrado { get; set; }
    [Column("trabajo_id")] public int TrabajoId { get; set; }
    [Column("producto_id")] public string ProductoId { get; set; } = "";
    [Column("origen_id")] public string? OrigenId { get; set; }
    [Column("revision_cifrada")] public byte[]? RevisionCifrada { get; set; }
    [Column("cantidad_cifrada")] public byte[] CantidadCifrada { get; set; } = [];
    [Column("reserva_cifrada")] public byte[] ReservaCifrada { get; set; } = [];
    [Column("descripcion_cifrada")] public byte[] DescripcionCifrada { get; set; } = [];
    [Column("comentario_cifrado")] public byte[] ComentarioCifrado { get; set; } = [];
}

[Table("auditoria"), PrimaryKey(nameof(Id))]
public sealed class Evento
{
    [Column("id")] public long Id { get; set; }
    [Column("usuario_id")] public string UsuarioId { get; set; } = "";
    [Column("evento_cifrado")] public byte[] EventoCifrado { get; set; } = [];
}

[Table("cifrado_control"), PrimaryKey(nameof(Id))]
public sealed class ControlCifrado
{
    [Column("id")] public int Id { get; set; }
    [Column("verificador")] public byte[] Verificador { get; set; } = [];
}

[Table("perfiles_reposicion"), PrimaryKey(nameof(BodegaId), nameof(ProductoId))]
public sealed class PerfilReposicionGuardado
{
    [Column("bodega_id")] public string BodegaId { get; set; } = "";
    [Column("producto_id")] public string ProductoId { get; set; } = "";
    [Column("contenido_cifrado")] public byte[] ContenidoCifrado { get; set; } = [];
}

[Table("analitica_trabajo"), PrimaryKey(nameof(TrabajoId))]
public sealed class AnaliticaTrabajo
{
    [Column("trabajo_id")] public int TrabajoId { get; set; }
    [Column("contenido_cifrado")] public byte[] ContenidoCifrado { get; set; } = [];
}


