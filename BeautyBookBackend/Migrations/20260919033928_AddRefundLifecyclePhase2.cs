using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundLifecyclePhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    ReasonCode = table.Column<byte>(type: "smallint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastHandledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.RefundId);
                    table.ForeignKey(
                        name: "FK_Refunds_BookingPayments_BookingPaymentId",
                        column: x => x.BookingPaymentId,
                        principalTable: "BookingPayments",
                        principalColumn: "PaymentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Refunds_Users_LastHandledBy",
                        column: x => x.LastHandledBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Refunds_Users_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_BookingId_Status",
                table: "Refunds",
                columns: new[] { "BookingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_BookingPaymentId",
                table: "Refunds",
                column: "BookingPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_LastHandledBy",
                table: "Refunds",
                column: "LastHandledBy");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_RequestedBy",
                table: "Refunds",
                column: "RequestedBy");

            migrationBuilder.Sql(
                """
                INSERT INTO "Refunds" (
                    "RefundId", "BookingId", "BookingPaymentId", "Amount", "Status", "ReasonCode",
                    "Reason", "CreatedAt", "UpdatedAt")
                SELECT
                    md5(bp."PaymentId"::text || ':refund')::uuid,
                    bp."BookingId",
                    bp."PaymentId",
                    bp."Amount",
                    0,
                    5,
                    'Refund pending được chuyển đổi khi triển khai Refund lifecycle.',
                    COALESCE(bp."RefundRequestedAt", bp."UpdatedAt", NOW()),
                    COALESCE(bp."RefundRequestedAt", bp."UpdatedAt", NOW())
                FROM "BookingPayments" bp
                WHERE bp."Status" = 5
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "Refunds" r
                      WHERE r."BookingPaymentId" = bp."PaymentId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Refunds");
        }
    }
}
