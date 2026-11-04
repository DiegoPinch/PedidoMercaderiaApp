using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Servidor.Migrations
{
    /// <inheritdoc />
    public partial class intialdata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bodegas",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    codigo_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    nombre_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bodegas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cifrado_control",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    verificador = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cifrado_control", x => x.id);
                    table.CheckConstraint("control_unico", "id=1");
                });

            migrationBuilder.CreateTable(
                name: "trabajo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    configuracion_cifrada = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trabajo", x => x.id);
                    table.CheckConstraint("trabajo_unico", "id=1");
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    usuario_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    nombre_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    rol_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    usuario_id = table.Column<string>(type: "text", nullable: false),
                    evento_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria", x => x.id);
                    table.ForeignKey(
                        name: "FK_auditoria_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cortes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fecha_indice = table.Column<string>(type: "text", nullable: false),
                    autor_id = table.Column<string>(type: "text", nullable: false),
                    resumen_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    metadata_cifrada = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cortes", x => x.id);
                    table.ForeignKey(
                        name: "FK_cortes_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    autor_id = table.Column<string>(type: "text", nullable: false),
                    destino_id = table.Column<string>(type: "text", nullable: false),
                    corte_id = table.Column<long>(type: "bigint", nullable: false),
                    resumen_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    estado_indice = table.Column<string>(type: "text", nullable: false),
                    cerrado_corte_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos", x => x.id);
                    table.ForeignKey(
                        name: "FK_pedidos_bodegas_destino_id",
                        column: x => x.destino_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_cortes_cerrado_corte_id",
                        column: x => x.cerrado_corte_id,
                        principalTable: "cortes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_cortes_corte_id",
                        column: x => x.corte_id,
                        principalTable: "cortes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productos",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    codigo_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    descripcion_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    primer_corte_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos", x => x.id);
                    table.ForeignKey(
                        name: "FK_productos_cortes_primer_corte_id",
                        column: x => x.primer_corte_id,
                        principalTable: "cortes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "borrador_detalles",
                columns: table => new
                {
                    trabajo_id = table.Column<int>(type: "integer", nullable: false),
                    producto_id = table.Column<string>(type: "text", nullable: false),
                    origen_id = table.Column<string>(type: "text", nullable: false),
                    cantidad_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    reserva_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    descripcion_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    comentario_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_borrador_detalles", x => new { x.trabajo_id, x.producto_id });
                    table.ForeignKey(
                        name: "FK_borrador_detalles_bodegas_origen_id",
                        column: x => x.origen_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_borrador_detalles_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_borrador_detalles_trabajo_trabajo_id",
                        column: x => x.trabajo_id,
                        principalTable: "trabajo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedido_detalles",
                columns: table => new
                {
                    pedido_id = table.Column<long>(type: "bigint", nullable: false),
                    producto_id = table.Column<string>(type: "text", nullable: false),
                    origen_id = table.Column<string>(type: "text", nullable: false),
                    cantidad_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    reserva_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    descripcion_cifrada = table.Column<byte[]>(type: "bytea", nullable: false),
                    comentario_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_detalles", x => new { x.pedido_id, x.producto_id });
                    table.ForeignKey(
                        name: "FK_pedido_detalles_bodegas_origen_id",
                        column: x => x.origen_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_detalles_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedido_detalles_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productos_por_corte",
                columns: table => new
                {
                    corte_id = table.Column<long>(type: "bigint", nullable: false),
                    producto_id = table.Column<string>(type: "text", nullable: false),
                    atributos_cifrados = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_productos_por_corte", x => new { x.corte_id, x.producto_id });
                    table.ForeignKey(
                        name: "FK_productos_por_corte_cortes_corte_id",
                        column: x => x.corte_id,
                        principalTable: "cortes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_productos_por_corte_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "existencias",
                columns: table => new
                {
                    corte_id = table.Column<long>(type: "bigint", nullable: false),
                    producto_id = table.Column<string>(type: "text", nullable: false),
                    bodega_id = table.Column<string>(type: "text", nullable: false),
                    cantidad_cifrada = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_existencias", x => new { x.corte_id, x.producto_id, x.bodega_id });
                    table.ForeignKey(
                        name: "FK_existencias_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_existencias_productos_por_corte_corte_id_producto_id",
                        columns: x => new { x.corte_id, x.producto_id },
                        principalTable: "productos_por_corte",
                        principalColumns: new[] { "corte_id", "producto_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_usuario_id",
                table: "auditoria",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_borrador_detalles_origen_id",
                table: "borrador_detalles",
                column: "origen_id");

            migrationBuilder.CreateIndex(
                name: "IX_borrador_detalles_producto_id",
                table: "borrador_detalles",
                column: "producto_id");

            migrationBuilder.CreateIndex(
                name: "IX_cortes_autor_id",
                table: "cortes",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "IX_cortes_fecha_indice",
                table: "cortes",
                column: "fecha_indice",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_existencias_bodega_id",
                table: "existencias",
                column: "bodega_id");

            migrationBuilder.CreateIndex(
                name: "detalle_producto_pedido",
                table: "pedido_detalles",
                columns: new[] { "producto_id", "pedido_id" });

            migrationBuilder.CreateIndex(
                name: "IX_pedido_detalles_origen_id",
                table: "pedido_detalles",
                column: "origen_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_autor_id",
                table: "pedidos",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_cerrado_corte_id",
                table: "pedidos",
                column: "cerrado_corte_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_corte_id",
                table: "pedidos",
                column: "corte_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_destino_id",
                table: "pedidos",
                column: "destino_id");

            migrationBuilder.CreateIndex(
                name: "pedidos_estado",
                table: "pedidos",
                columns: new[] { "estado_indice", "cerrado_corte_id" });

            migrationBuilder.CreateIndex(
                name: "productos_primer_corte",
                table: "productos",
                column: "primer_corte_id");

            migrationBuilder.CreateIndex(
                name: "IX_productos_por_corte_producto_id",
                table: "productos_por_corte",
                column: "producto_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "borrador_detalles");

            migrationBuilder.DropTable(
                name: "cifrado_control");

            migrationBuilder.DropTable(
                name: "existencias");

            migrationBuilder.DropTable(
                name: "pedido_detalles");

            migrationBuilder.DropTable(
                name: "trabajo");

            migrationBuilder.DropTable(
                name: "productos_por_corte");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropTable(
                name: "productos");

            migrationBuilder.DropTable(
                name: "bodegas");

            migrationBuilder.DropTable(
                name: "cortes");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
