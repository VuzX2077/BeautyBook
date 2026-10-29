using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class MuaMonetizationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MuaMonetizationAccounts",
                columns: table => new
                {
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cohort = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivationPurchaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivationRevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstPublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaMonetizationAccounts", x => x.MuaId);
                    table.CheckConstraint("CK_MuaMonetizationAccounts_Cohort", "\"Cohort\" IN ('LegacyWaiver','RolloutWaiver','PaidActivation')");
                    table.CheckConstraint("CK_MuaMonetizationAccounts_PaidActivation", "(\"ActivatedAt\" IS NULL AND \"ActivationPurchaseReference\" IS NULL) OR (\"Cohort\" = 'PaidActivation' AND \"ActivatedAt\" IS NOT NULL AND length(trim(\"ActivationPurchaseReference\")) > 0 AND \"ActivationPurchaseReference\" IS NOT NULL)");
                    table.CheckConstraint("CK_MuaMonetizationAccounts_Revocation", "\"ActivationRevokedAt\" IS NULL OR (\"ActivatedAt\" IS NOT NULL AND \"ActivationRevokedAt\" >= \"ActivatedAt\")");
                    table.ForeignKey(
                        name: "FK_MuaMonetizationAccounts_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaVisibilityGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaVisibilityGrants", x => x.Id);
                    table.CheckConstraint("CK_MuaVisibilityGrants_Interval", "\"EndsAt\" > \"StartsAt\"");
                    table.CheckConstraint("CK_MuaVisibilityGrants_Kind", "\"Kind\" IN ('FirstActivationGift','Paid','Compensation')");
                    table.CheckConstraint("CK_MuaVisibilityGrants_Revocation", "(\"RevokedAt\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"RevokedAt\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL AND length(trim(\"RevocationReason\")) > 0)");
                    table.CheckConstraint("CK_MuaVisibilityGrants_Source", "length(trim(\"SourceReference\")) > 0");
                    table.ForeignKey(
                        name: "FK_MuaVisibilityGrants_MuaMonetizationAccounts_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MuaMonetizationAccounts",
                        principalColumn: "MuaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MuaMonetizationAccounts_ActivationPurchaseReference",
                table: "MuaMonetizationAccounts",
                column: "ActivationPurchaseReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaMonetizationAccounts_Cohort_EnrolledAt",
                table: "MuaMonetizationAccounts",
                columns: new[] { "Cohort", "EnrolledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaVisibilityGrants_MuaId_EndsAt",
                table: "MuaVisibilityGrants",
                columns: new[] { "MuaId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaVisibilityGrants_SourceReference",
                table: "MuaVisibilityGrants",
                column: "SourceReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MuaVisibilityGrants_FirstGift",
                table: "MuaVisibilityGrants",
                column: "MuaId",
                unique: true,
                filter: "\"Kind\" = 'FirstActivationGift'");

            // Preserve the existing account-deletion/media writer coverage contract.
            // Both tables use MuaId, which the existing owner guard recognizes.
            migrationBuilder.Sql("""
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaMonetizationAccounts"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaMonetizationAccounts"
                    FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "MuaVisibilityGrants"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "MuaVisibilityGrants"
                    FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();

                CREATE FUNCTION public.mua_monetization_history_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF TG_TABLE_NAME = 'MuaMonetizationAccounts' THEN
                        IF NEW."MuaId" IS DISTINCT FROM OLD."MuaId"
                            OR NEW."Cohort" IS DISTINCT FROM OLD."Cohort"
                            OR NEW."PolicyVersion" IS DISTINCT FROM OLD."PolicyVersion"
                            OR NEW."EnrolledAt" IS DISTINCT FROM OLD."EnrolledAt"
                            OR (OLD."FirstPublishedAt" IS NOT NULL AND NEW."FirstPublishedAt" IS DISTINCT FROM OLD."FirstPublishedAt") THEN
                            RAISE EXCEPTION 'Monetization enrollment history cannot be rewritten' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF NEW."Id" IS DISTINCT FROM OLD."Id"
                            OR NEW."MuaId" IS DISTINCT FROM OLD."MuaId"
                            OR NEW."Kind" IS DISTINCT FROM OLD."Kind"
                            OR NEW."SourceReference" IS DISTINCT FROM OLD."SourceReference"
                            OR NEW."StartsAt" IS DISTINCT FROM OLD."StartsAt"
                            OR NEW."EndsAt" IS DISTINCT FROM OLD."EndsAt"
                            OR NEW."CreatedAt" IS DISTINCT FROM OLD."CreatedAt"
                            OR (OLD."RevokedAt" IS NOT NULL AND NEW."RevokedAt" IS DISTINCT FROM OLD."RevokedAt") THEN
                            RAISE EXCEPTION 'Visibility history cannot be rewritten; use a compensation grant' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $guard$;
                CREATE TRIGGER mua_monetization_history_guard BEFORE UPDATE ON "MuaMonetizationAccounts"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_monetization_history_guard();
                CREATE TRIGGER mua_monetization_history_guard BEFORE UPDATE ON "MuaVisibilityGrants"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_monetization_history_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaVisibilityGrants");

            migrationBuilder.DropTable(
                name: "MuaMonetizationAccounts");

            migrationBuilder.Sql("DROP FUNCTION public.mua_monetization_history_guard();");
        }
    }
}
