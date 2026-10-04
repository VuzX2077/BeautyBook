using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUgcModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentReports_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentReports_Users_ReviewedBy",
                        column: x => x.ReviewedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentReports_Users_TargetOwnerId",
                        column: x => x.TargetOwnerId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerId, x.BlockedId });
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockedId",
                        column: x => x.BlockedId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBlocks_Users_BlockerId",
                        column: x => x.BlockerId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterId_TargetType_TargetId",
                table: "ContentReports",
                columns: new[] { "ReporterId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReviewedBy",
                table: "ContentReports",
                column: "ReviewedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_Status_CreatedAt",
                table: "ContentReports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_TargetOwnerId",
                table: "ContentReports",
                column: "TargetOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_TargetType_TargetId_Status",
                table: "ContentReports",
                columns: new[] { "TargetType", "TargetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedId",
                table: "UserBlocks",
                column: "BlockedId");
            // Add guards for new tables without replacing the existing deletion/financial guard.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.ugc_owner_write_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
                DECLARE data jsonb := to_jsonb(NEW); ids uuid[]; id uuid;
                        deleting text := current_setting('bbook.deletion_owner', true);
                BEGIN
                  IF NOT pg_try_advisory_xact_lock_shared(724266524669002) THEN
                    RAISE EXCEPTION 'Account/media operation active; retry later' USING ERRCODE='55000';
                  END IF;
                  IF TG_TABLE_NAME='UserBlocks' THEN
                    ids := ARRAY[(data->>'BlockerId')::uuid,(data->>'BlockedId')::uuid];
                    IF ids[1]=ids[2] THEN RAISE EXCEPTION 'Cannot block self' USING ERRCODE='23514'; END IF;
                  ELSE
                    ids := ARRAY[(data->>'ReporterId')::uuid,(data->>'TargetOwnerId')::uuid,(data->>'ReviewedBy')::uuid];
                  END IF;
                  FOR id IN SELECT DISTINCT value FROM unnest(ids) AS u(value) WHERE value IS NOT NULL ORDER BY value LOOP
                    PERFORM 1 FROM "Users" WHERE "UserId"=id FOR SHARE;
                  END LOOP;
                  IF (deleting IS NULL OR deleting='') AND EXISTS(SELECT 1 FROM "Users" u WHERE u."UserId"=ANY(ids) AND u."DeletedAt" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Deleted account cannot receive new UGC data' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $guard$;
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "UserBlocks" FOR EACH ROW EXECUTE FUNCTION public.ugc_owner_write_guard();
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "ContentReports" FOR EACH ROW EXECUTE FUNCTION public.ugc_owner_write_guard();
                CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "ContentReports" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropTable(
                name: "UserBlocks");
            migrationBuilder.Sql("DROP FUNCTION public.ugc_owner_write_guard();");
        }
    }
}
