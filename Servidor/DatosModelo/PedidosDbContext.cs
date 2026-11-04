using Microsoft.EntityFrameworkCore;

namespace Servidor.DatosModelo;

public sealed class PedidosDbContext(DbContextOptions<PedidosDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Bodega> Bodegas => Set<Bodega>();
    public DbSet<ReporteActividadGuardado> ReportesActividad => Set<ReporteActividadGuardado>();
    public DbSet<Trabajo> Trabajo => Set<Trabajo>();
    public DbSet<Corte> Cortes => Set<Corte>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Existencia> Existencias => Set<Existencia>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoLinea> Detalles => Set<PedidoLinea>();
    public DbSet<BorradorLinea> Borrador => Set<BorradorLinea>();
    public DbSet<Evento> Auditoria => Set<Evento>();
    public DbSet<PerfilReposicionGuardado> PerfilesReposicion => Set<PerfilReposicionGuardado>();
    public DbSet<AnaliticaTrabajo> Analiticas => Set<AnaliticaTrabajo>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<PerfilReposicionGuardado>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.BodegaId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PerfilReposicionGuardado>().HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AnaliticaTrabajo>().Property(x => x.TrabajoId).ValueGeneratedNever();
        m.Entity<AnaliticaTrabajo>().HasOne<Trabajo>().WithOne().HasForeignKey<AnaliticaTrabajo>(x => x.TrabajoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ReporteActividadGuardado>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.BodegaId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ControlCifrado>().ToTable(t => t.HasCheckConstraint("control_unico", "id=1"));
        m.Entity<ControlCifrado>().Property(x => x.Id).ValueGeneratedNever();
        m.Entity<Trabajo>().Property(x => x.Id).ValueGeneratedNever();
        m.Entity<Trabajo>().ToTable(t => t.HasCheckConstraint("trabajo_unico", "id=1"));
        m.Entity<Trabajo>().Property(x => x.Revision).IsConcurrencyToken();
        m.Entity<Corte>().HasIndex(x => x.FechaIndice).IsUnique();
        m.Entity<Corte>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.AutorId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Producto>().HasOne<Corte>().WithMany().HasForeignKey(x => x.PrimerCorteId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Producto>().HasIndex(x => x.PrimerCorteId).HasDatabaseName("productos_primer_corte");
        m.Entity<Producto>().HasIndex(x => x.UltimoCorteId);
        m.Entity<Producto>().HasIndex(x => x.AnteriorCorteId);
        m.Entity<Existencia>().HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Existencia>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.BodegaId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Pedido>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.AutorId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Pedido>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.DestinoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Pedido>().HasOne<Corte>().WithMany().HasForeignKey(x => x.CorteId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Pedido>().HasOne<Corte>().WithMany().HasForeignKey(x => x.CerradoCorteId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Pedido>().HasIndex(x => new { x.EstadoIndice, x.CerradoCorteId }).HasDatabaseName("pedidos_estado");
        m.Entity<PedidoLinea>().HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PedidoLinea>().HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PedidoLinea>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.OrigenId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PedidoLinea>().HasIndex(x => new { x.ProductoId, x.PedidoId }).HasDatabaseName("detalle_producto_pedido");
        m.Entity<BorradorLinea>().HasOne<Trabajo>().WithMany().HasForeignKey(x => x.TrabajoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<BorradorLinea>().HasOne<Producto>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<BorradorLinea>().HasOne<Bodega>().WithMany().HasForeignKey(x => x.OrigenId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Evento>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
    }
}

