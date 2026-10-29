using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAiLifetimeAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAiPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OrderCode = table.Column<long>(type: "bigint", nullable: false),
                    Channel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentLinkId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAiPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAiPurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON \"CustomerAiPurchases\" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();");
            migrationBuilder.Sql("CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON \"CustomerAiPurchases\" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();");
            migrationBuilder.CreateIndex(
                name: "IX_CustomerAiPurchases_OrderCode",
                table: "CustomerAiPurchases",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAiPurchases_UserId_IdempotencyKey",
                table: "CustomerAiPurchases",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAiPurchases");
        }
    }
}
