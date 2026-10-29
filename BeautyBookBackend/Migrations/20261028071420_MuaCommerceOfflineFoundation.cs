using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class MuaCommerceOfflineFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MuaCommerceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AmountVnd = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceOrders", x => x.Id);
                    table.UniqueConstraint("AK_MuaCommerceOrders_Id_MuaId", x => new { x.Id, x.MuaId });
                    table.CheckConstraint("CK_MuaCommerceOrders_Catalog", "(\"ProductId\" = 'bbook_mua_activation_v1' AND \"AmountVnd\" = 100000) OR (\"ProductId\" = 'bbook_visibility_30d_v1' AND \"AmountVnd\" = 75000)");
                    table.CheckConstraint("CK_MuaCommerceOrders_Currency", "\"Currency\" = 'VND'");
                    table.CheckConstraint("CK_MuaCommerceOrders_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_MuaCommerceOrders_Identity", "length(trim(\"IdempotencyKey\")) > 0 AND length(trim(\"TermsVersion\")) > 0");
                    table.CheckConstraint("CK_MuaCommerceOrders_Origin", "\"Origin\" = 'IndependentWebsite'");
                    table.CheckConstraint("CK_MuaCommerceOrders_State", "\"State\" IN ('PaymentPending','PaidAwaitingFulfillment','ManualReview','Fulfilled','Cancelled','Refunded')");
                    table.ForeignKey(
                        name: "FK_MuaCommerceOrders_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCode = table.Column<long>(type: "bigint", nullable: false),
                    Channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentLinkId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceAttempts", x => x.Id);
                    table.CheckConstraint("CK_MuaCommerceAttempts_Reference", "\"OrderCode\" > 0 AND length(trim(\"Channel\")) > 0 AND (\"PaymentLinkId\" IS NULL OR length(trim(\"PaymentLinkId\")) > 0)");
                    table.ForeignKey(
                        name: "FK_MuaCommerceAttempts_MuaCommerceOrders_OrderId_MuaId",
                        columns: x => new { x.OrderId, x.MuaId },
                        principalTable: "MuaCommerceOrders",
                        principalColumns: new[] { "Id", "MuaId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceAudit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaCommerceAudit_MuaCommerceOrders_OrderId_MuaId",
                        columns: x => new { x.OrderId, x.MuaId },
                        principalTable: "MuaCommerceOrders",
                        principalColumns: new[] { "Id", "MuaId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceInbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceInbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaCommerceInbox_MuaCommerceOrders_OrderId_MuaId",
                        columns: x => new { x.OrderId, x.MuaId },
                        principalTable: "MuaCommerceOrders",
                        principalColumns: new[] { "Id", "MuaId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceOutbox", x => x.Id);
                    table.CheckConstraint("CK_MuaCommerceOutbox_Attempts", "\"Attempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_MuaCommerceOutbox_MuaCommerceOrders_OrderId_MuaId",
                        columns: x => new { x.OrderId, x.MuaId },
                        principalTable: "MuaCommerceOrders",
                        principalColumns: new[] { "Id", "MuaId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAttempts_Channel_PaymentLinkId",
                table: "MuaCommerceAttempts",
                columns: new[] { "Channel", "PaymentLinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAttempts_OrderCode",
                table: "MuaCommerceAttempts",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAttempts_OrderId",
                table: "MuaCommerceAttempts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAttempts_OrderId_MuaId",
                table: "MuaCommerceAttempts",
                columns: new[] { "OrderId", "MuaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAudit_OrderId_CreatedAt",
                table: "MuaCommerceAudit",
                columns: new[] { "OrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceAudit_OrderId_MuaId",
                table: "MuaCommerceAudit",
                columns: new[] { "OrderId", "MuaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceInbox_EventKey",
                table: "MuaCommerceInbox",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceInbox_OrderId_MuaId",
                table: "MuaCommerceInbox",
                columns: new[] { "OrderId", "MuaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOrders_MuaId_IdempotencyKey",
                table: "MuaCommerceOrders",
                columns: new[] { "MuaId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOrders_State_CreatedAt",
                table: "MuaCommerceOrders",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_MuaCommerceOrders_Activation",
                table: "MuaCommerceOrders",
                column: "MuaId",
                unique: true,
                filter: "\"ProductId\" = 'bbook_mua_activation_v1' AND \"State\" NOT IN ('Cancelled','Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOutbox_CompletedAt_AvailableAt",
                table: "MuaCommerceOutbox",
                columns: new[] { "CompletedAt", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOutbox_OrderId_Kind",
                table: "MuaCommerceOutbox",
                columns: new[] { "OrderId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOutbox_OrderId_MuaId",
                table: "MuaCommerceOutbox",
                columns: new[] { "OrderId", "MuaId" });

            // All owner-bearing tables preserve the existing deletion/media writer
            // coverage contract. No data is backfilled and no rights are changed.
            foreach (var table in new[] { "MuaCommerceOrders", "MuaCommerceAttempts", "MuaCommerceInbox", "MuaCommerceOutbox", "MuaCommerceAudit" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();");
                migrationBuilder.Sql($"CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();");
            }
            migrationBuilder.Sql("""
                CREATE FUNCTION public.mua_commerce_history_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF TG_TABLE_NAME IN ('MuaCommerceAudit','MuaCommerceInbox') THEN
                        RAISE EXCEPTION 'Commerce audit and receipts are append-only' USING ERRCODE='23514';
                    ELSIF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Commerce financial history cannot be deleted' USING ERRCODE='23514';
                    ELSIF TG_TABLE_NAME = 'MuaCommerceOrders' THEN
                        IF ROW(NEW."Id",NEW."MuaId",NEW."ProductId",NEW."AmountVnd",NEW."Currency",NEW."IdempotencyKey",NEW."TermsVersion",NEW."Origin",NEW."CreatedAt",NEW."ExpiresAt")
                            IS DISTINCT FROM ROW(OLD."Id",OLD."MuaId",OLD."ProductId",OLD."AmountVnd",OLD."Currency",OLD."IdempotencyKey",OLD."TermsVersion",OLD."Origin",OLD."CreatedAt",OLD."ExpiresAt")
                            OR (OLD."VerifiedPaidAt" IS NOT NULL AND NEW."VerifiedPaidAt" IS DISTINCT FROM OLD."VerifiedPaidAt") THEN
                            RAISE EXCEPTION 'Commerce order source is immutable' USING ERRCODE='23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'MuaCommerceAttempts' THEN
                        IF ROW(NEW."Id",NEW."MuaId",NEW."OrderId",NEW."OrderCode",NEW."Channel",NEW."CreatedAt")
                            IS DISTINCT FROM ROW(OLD."Id",OLD."MuaId",OLD."OrderId",OLD."OrderCode",OLD."Channel",OLD."CreatedAt")
                            OR (OLD."PaymentLinkId" IS NOT NULL AND NEW."PaymentLinkId" IS DISTINCT FROM OLD."PaymentLinkId") THEN
                            RAISE EXCEPTION 'Commerce provider reference is immutable' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END $guard$;
                CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceOrders"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
                CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceAttempts"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
                CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceInbox"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
                CREATE TRIGGER mua_commerce_history_guard BEFORE UPDATE OR DELETE ON "MuaCommerceAudit"
                    FOR EACH ROW EXECUTE FUNCTION public.mua_commerce_history_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaCommerceAttempts");

            migrationBuilder.DropTable(
                name: "MuaCommerceAudit");

            migrationBuilder.DropTable(
                name: "MuaCommerceInbox");

            migrationBuilder.DropTable(
                name: "MuaCommerceOutbox");

            migrationBuilder.DropTable(
                name: "MuaCommerceOrders");
            migrationBuilder.Sql("DROP FUNCTION public.mua_commerce_history_guard();");
        }
    }
}
