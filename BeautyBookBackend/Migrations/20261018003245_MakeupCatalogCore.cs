using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class MakeupCatalogCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "MakeupStyles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "MakeupStyles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "MakeupStyles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "MakeupStyles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "MakeupStyles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "MakeupStyles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "LegacyUnknown");

            migrationBuilder.AddColumn<string>(
                name: "SystemDefinition",
                table: "MakeupStyles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "MakeupStyles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "MakeupStyles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "MakeupCatalogChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MakeupCatalogChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MakeupCatalogTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    BroaderTermId = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MakeupCatalogTerms", x => x.Id);
                    table.UniqueConstraint("AK_MakeupCatalogTerms_Id_Kind", x => new { x.Id, x.Kind });
                    table.CheckConstraint("CK_CatalogTerm_Archive", "\"ArchivedAt\" IS NULL OR NOT \"IsActive\"");
                    table.CheckConstraint("CK_CatalogTerm_Intensity", "\"Kind\" <> 'intensity' OR \"Key\" IN ('light','medium','bold')");
                    table.CheckConstraint("CK_CatalogTerm_Kind", "\"Kind\" IN ('occasion','intensity','color-tone','effect','occasion-group','effect-group')");
                    table.ForeignKey(
                        name: "FK_MakeupCatalogTerms_MakeupCatalogTerms_BroaderTermId",
                        column: x => x.BroaderTermId,
                        principalTable: "MakeupCatalogTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MakeupCatalogTerms_MakeupCatalogTerms_GroupId",
                        column: x => x.GroupId,
                        principalTable: "MakeupCatalogTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MakeupStyleTermLinks",
                columns: table => new
                {
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    TermId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MakeupStyleTermLinks", x => new { x.StyleId, x.TermId });
                    table.CheckConstraint("CK_StyleTerm_Kind", "\"Kind\" IN ('occasion','intensity','color-tone','effect')");
                    table.ForeignKey(
                        name: "FK_MakeupStyleTermLinks_MakeupCatalogTerms_TermId_Kind",
                        columns: x => new { x.TermId, x.Kind },
                        principalTable: "MakeupCatalogTerms",
                        principalColumns: new[] { "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MakeupStyleTermLinks_MakeupStyles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "MakeupStyles",
                        principalColumn: "StyleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyles_IsActive_SortOrder",
                table: "MakeupStyles",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyles_Key",
                table: "MakeupStyles",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogChanges_BatchId",
                table: "MakeupCatalogChanges",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogChanges_Subject_CreatedAt",
                table: "MakeupCatalogChanges",
                columns: new[] { "Subject", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogTerms_BroaderTermId",
                table: "MakeupCatalogTerms",
                column: "BroaderTermId");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogTerms_GroupId",
                table: "MakeupCatalogTerms",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogTerms_Kind_IsActive_SortOrder",
                table: "MakeupCatalogTerms",
                columns: new[] { "Kind", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogTerms_Kind_Key",
                table: "MakeupCatalogTerms",
                columns: new[] { "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MakeupCatalogTerms_Kind_NormalizedLabel",
                table: "MakeupCatalogTerms",
                columns: new[] { "Kind", "NormalizedLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyleTermLinks_StyleId_Kind",
                table: "MakeupStyleTermLinks",
                columns: new[] { "StyleId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'intensity'");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyleTermLinks_TermId_Kind",
                table: "MakeupStyleTermLinks",
                columns: new[] { "TermId", "Kind" });

            // New catalog writers participate in the existing deletion barrier.
            migrationBuilder.Sql("""
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MakeupCatalogTerms"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MakeupStyleTermLinks"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MakeupCatalogChanges"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MakeupCatalogTerms"
                    FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MakeupStyleTermLinks"
                    FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MakeupCatalogChanges"
                    FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                CREATE FUNCTION public.bbook_catalog_identity_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF TG_TABLE_NAME = 'MakeupStyles' THEN
                        IF (OLD."Key" IS NOT NULL AND NEW."Key" IS DISTINCT FROM OLD."Key") OR
                           (OLD."SystemDefinition" IS NOT NULL AND NEW."SystemDefinition" IS DISTINCT FROM OLD."SystemDefinition") THEN
                            RAISE EXCEPTION 'Catalog identity/baseline is immutable' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF NEW."Key" IS DISTINCT FROM OLD."Key" OR NEW."Kind" IS DISTINCT FROM OLD."Kind" THEN
                            RAISE EXCEPTION 'Catalog term identity is immutable' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $guard$;
                CREATE TRIGGER bbook_catalog_identity_guard BEFORE UPDATE ON "MakeupStyles"
                    FOR EACH ROW EXECUTE FUNCTION public.bbook_catalog_identity_guard();
                CREATE TRIGGER bbook_catalog_identity_guard BEFORE UPDATE ON "MakeupCatalogTerms"
                    FOR EACH ROW EXECUTE FUNCTION public.bbook_catalog_identity_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down is destructive after adoption; never use it against live catalog data.
            migrationBuilder.Sql("""
                DROP TRIGGER bbook_catalog_identity_guard ON "MakeupStyles";
                DROP TRIGGER bbook_catalog_identity_guard ON "MakeupCatalogTerms";
                DROP FUNCTION public.bbook_catalog_identity_guard();
                """);
            migrationBuilder.DropTable(
                name: "MakeupCatalogChanges");

            migrationBuilder.DropTable(
                name: "MakeupStyleTermLinks");

            migrationBuilder.DropTable(
                name: "MakeupCatalogTerms");

            migrationBuilder.DropIndex(
                name: "IX_MakeupStyles_IsActive_SortOrder",
                table: "MakeupStyles");

            migrationBuilder.DropIndex(
                name: "IX_MakeupStyles_Key",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "SystemDefinition",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MakeupStyles");
        }
    }
}
