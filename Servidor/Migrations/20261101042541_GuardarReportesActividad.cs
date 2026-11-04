using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servidor.Migrations
{
    /// <inheritdoc />
    public partial class GuardarReportesActividad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reportes_actividad",
                columns: table => new
                {
                    bodega_id = table.Column<string>(type: "text", nullable: false),
                    contenido_cifrado = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reportes_actividad", x => x.bodega_id);
                    table.ForeignKey(
                        name: "FK_reportes_actividad_bodegas_bodega_id",
                        column: x => x.bodega_id,
                        principalTable: "bodegas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reportes_actividad");
        }
    }
}
