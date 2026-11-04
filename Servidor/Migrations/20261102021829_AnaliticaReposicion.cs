using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servidor.Migrations
{
    /// <inheritdoc />
    public partial class AnaliticaReposicion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "excepcional_cifrado",
                table: "pedido_detalles",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "excepcional_cifrado",
                table: "borrador_detalles",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "analitica_trabajo",
                columns: table => new
                {
                    trabajo_id = table.Column<int>(type: "integer", nullable: false),
                    contenido_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analitica_trabajo", x => x.trabajo_id);
                    table.ForeignKey(
                        name: "FK_analitica_trabajo_trabajo_trabajo_id",
                        column: x => x.trabajo_id,
                        principalTable: "trabajo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "perfiles_reposicion",
                columns: table => new
                {
                    bodega_id = table.Column<string>(type: "text", nullable: false),
                    producto_id = table.Column<string>(type: "text", nullable: false),
                    contenido_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perfiles_reposicion", x => new { x.bodega_id, x.producto_id });
                    table.ForeignKey(
                        name: "FK_perfiles_reposicion_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_perfiles_reposicion_productos_producto_id",
                        column: x => x.producto_id,
                        principalTable: "productos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_perfiles_reposicion_producto_id",
                table: "perfiles_reposicion",
                column: "producto_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analitica_trabajo");

            migrationBuilder.DropTable(
                name: "perfiles_reposicion");

            migrationBuilder.DropColumn(
                name: "excepcional_cifrado",
                table: "pedido_detalles");

            migrationBuilder.DropColumn(
                name: "excepcional_cifrado",
                table: "borrador_detalles");
        }
    }
}
