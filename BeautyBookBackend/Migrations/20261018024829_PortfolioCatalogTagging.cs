using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioCatalogTagging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Portfolios",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "PortfolioStyleLinks",
                columns: table => new
                {
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioStyleLinks", x => new { x.PortfolioId, x.StyleId });
                    table.CheckConstraint("CK_PortfolioStyle_Slot", "\"Slot\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_PortfolioStyleLinks_MakeupStyles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "MakeupStyles",
                        principalColumn: "StyleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioStyleLinks_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "PortfolioId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioTermLinks",
                columns: table => new
                {
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Slot = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTermLinks", x => new { x.PortfolioId, x.TermId });
                    table.CheckConstraint("CK_PortfolioTerm_SlotKind", "(\"Kind\" = 'occasion' AND \"Slot\" BETWEEN 0 AND 2) OR (\"Kind\" = 'intensity' AND \"Slot\" = 0) OR (\"Kind\" = 'color-tone' AND \"Slot\" BETWEEN 0 AND 2) OR (\"Kind\" = 'effect' AND \"Slot\" BETWEEN 0 AND 3)");
                    table.ForeignKey(
                        name: "FK_PortfolioTermLinks_MakeupCatalogTerms_TermId_Kind",
                        columns: x => new { x.TermId, x.Kind },
                        principalTable: "MakeupCatalogTerms",
                        principalColumns: new[] { "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioTermLinks_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "PortfolioId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioStyleLinks_PortfolioId_Slot",
                table: "PortfolioStyleLinks",
                columns: new[] { "PortfolioId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioStyleLinks_StyleId_PortfolioId",
                table: "PortfolioStyleLinks",
                columns: new[] { "StyleId", "PortfolioId" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTermLinks_PortfolioId_Kind_Slot",
                table: "PortfolioTermLinks",
                columns: new[] { "PortfolioId", "Kind", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTermLinks_TermId_Kind",
                table: "PortfolioTermLinks",
                columns: new[] { "TermId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTermLinks_TermId_PortfolioId",
                table: "PortfolioTermLinks",
                columns: new[] { "TermId", "PortfolioId" });

            migrationBuilder.Sql("""
                CREATE FUNCTION public.portfolio_styles_require_primary() RETURNS trigger LANGUAGE plpgsql AS $body$
                DECLARE current_id uuid;
                BEGIN
                  FOR current_id IN SELECT DISTINCT id FROM unnest(ARRAY[
                    CASE WHEN TG_OP <> 'INSERT' THEN OLD."PortfolioId" END,
                    CASE WHEN TG_OP <> 'DELETE' THEN NEW."PortfolioId" END]) AS ids(id) WHERE id IS NOT NULL LOOP
                    -- Serialize final-state checks; parent may have been physically deleted by cascade.
                    PERFORM 1 FROM "Portfolios" WHERE "PortfolioId" = current_id FOR UPDATE;
                    IF EXISTS (SELECT 1 FROM "PortfolioStyleLinks" WHERE "PortfolioId" = current_id AND "Slot" > 0)
                       AND NOT EXISTS (SELECT 1 FROM "PortfolioStyleLinks" WHERE "PortfolioId" = current_id AND "Slot" = 0) THEN
                      RAISE EXCEPTION 'Secondary portfolio styles require a primary style'
                        USING ERRCODE = '23514', CONSTRAINT = 'CK_PortfolioStyle_PrimaryRequired';
                    END IF;
                  END LOOP;
                  RETURN NULL;
                END $body$;
                CREATE CONSTRAINT TRIGGER portfolio_styles_require_primary
                  AFTER INSERT OR UPDATE OR DELETE ON "PortfolioStyleLinks"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                  EXECUTE FUNCTION public.portfolio_styles_require_primary();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "PortfolioStyleLinks"
                  FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "PortfolioTermLinks"
                  FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "PortfolioStyleLinks"
                  FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "PortfolioTermLinks"
                  FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioStyleLinks");

            migrationBuilder.DropTable(
                name: "PortfolioTermLinks");

            migrationBuilder.Sql("DROP FUNCTION public.portfolio_styles_require_primary();");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Portfolios");
        }
    }
}
