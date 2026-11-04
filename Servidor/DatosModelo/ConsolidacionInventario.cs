using Microsoft.EntityFrameworkCore.Migrations;

namespace Servidor.DatosModelo;

/// <summary>Traslado transaccional para la migración a catálogo único. No se ejecuta al iniciar la aplicación.</summary>
public static class ConsolidacionInventario
{
    // Llamar al principio de Up, antes de las operaciones que genere EF.
    public static void Antes(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TEMP TABLE consolidar_productos ON COMMIT DROP AS
        SELECT p.id, ficha.corte_id AS ultimo_corte_id, ficha.atributos_cifrados,
            (SELECT pc.corte_id FROM productos_por_corte pc
             WHERE pc.producto_id=p.id AND pc.corte_id=(SELECT id FROM cortes ORDER BY id DESC OFFSET 1 LIMIT 1)) AS anterior_corte_id
        FROM productos p
        LEFT JOIN LATERAL (SELECT corte_id, atributos_cifrados FROM productos_por_corte
            WHERE producto_id=p.id ORDER BY corte_id DESC LIMIT 1) ficha ON true;

        CREATE TEMP TABLE consolidar_existencias ON COMMIT DROP AS
        SELECT DISTINCT ON (e.producto_id,e.bodega_id)
            e.producto_id,e.bodega_id,e.corte_id,e.cantidad_cifrada,
            anterior.corte_id AS corte_anterior_id,anterior.cantidad_cifrada AS cantidad_anterior_cifrada
        FROM existencias e
        LEFT JOIN existencias anterior ON anterior.producto_id=e.producto_id AND anterior.bodega_id=e.bodega_id
            AND anterior.corte_id=(SELECT id FROM cortes ORDER BY id DESC OFFSET 1 LIMIT 1)
        WHERE e.corte_id IN (SELECT id FROM cortes ORDER BY id DESC LIMIT 2)
        ORDER BY e.producto_id,e.bodega_id,e.corte_id DESC;

        DELETE FROM existencias;
        """);

    // Llamar al final de Up, después de las operaciones que genere EF.
    public static void Despues(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE productos p SET ultimo_corte_id=t.ultimo_corte_id,
            anterior_corte_id=t.anterior_corte_id,atributos_cifrados=t.atributos_cifrados
        FROM consolidar_productos t WHERE p.id=t.id;

        INSERT INTO existencias(producto_id,bodega_id,corte_id,cantidad_cifrada,corte_anterior_id,cantidad_anterior_cifrada)
        SELECT producto_id,bodega_id,corte_id,cantidad_cifrada,corte_anterior_id,cantidad_anterior_cifrada
        FROM consolidar_existencias;

        DROP TABLE consolidar_existencias;
        DROP TABLE consolidar_productos;
        """);
}
