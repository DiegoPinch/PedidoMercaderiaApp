using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servidor.Migrations
{
    /// <inheritdoc />
    public partial class InventarioActual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DatosModelo.ConsolidacionInventario.Antes(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_existencias_productos_por_corte_corte_id_producto_id",
                table: "existencias");

            migrationBuilder.DropTable(
                name: "productos_por_corte");

            migrationBuilder.DropPrimaryKey(
                name: "PK_existencias",
                table: "existencias");

            migrationBuilder.AddColumn<long>(
                name: "anterior_corte_id",
                table: "productos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "atributos_cifrados",
                table: "productos",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ultimo_corte_id",
                table: "productos",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "cantidad_anterior_cifrada",
                table: "existencias",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "corte_anterior_id",
                table: "existencias",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_existencias",
                table: "existencias",
                columns: new[] { "producto_id", "bodega_id" });

            migrationBuilder.CreateIndex(
                name: "IX_productos_anterior_corte_id",
                table: "productos",
                column: "anterior_corte_id");

            migrationBuilder.CreateIndex(
                name: "IX_productos_ultimo_corte_id",
                table: "productos",
                column: "ultimo_corte_id");

            migrationBuilder.AddForeignKey(
                name: "FK_existencias_productos_producto_id",
                table: "existencias",
                column: "producto_id",
                principalTable: "productos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            DatosModelo.ConsolidacionInventario.Despues(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("La consolidación descartó el historial de inventarios. Para volver al esquema anterior, restaura el respaldo previo.");
        }
    }
}
