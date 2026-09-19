using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    public partial class AddDirectBookingPayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentExpiresAt", table: "Bookings",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingPayments",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<byte>(type: "smallint", nullable: false),
                    ProviderOrderCode = table.Column<long>(type: "bigint", nullable: false),
                    ProviderPaymentLinkId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CheckoutUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QrCode = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RawWebhookPayload = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPayments", x => x.PaymentId);
                    table.ForeignKey("FK_BookingPayments_Bookings_BookingId", x => x.BookingId,
                        "Bookings", "BookingId", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_BookingPayments_Users_CustomerId", x => x.CustomerId,
                        "Users", "UserId", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex("IX_BookingPayments_BookingId_Status", "BookingPayments", new[] { "BookingId", "Status" });
            migrationBuilder.CreateIndex("IX_BookingPayments_CustomerId", "BookingPayments", "CustomerId");
            migrationBuilder.CreateIndex("IX_BookingPayments_ProviderOrderCode", "BookingPayments", "ProviderOrderCode", unique: true);
            migrationBuilder.CreateIndex("IX_BookingPayments_ProviderPaymentLinkId", "BookingPayments", "ProviderPaymentLinkId", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BookingPayments");
            migrationBuilder.DropColumn(name: "PaymentExpiresAt", table: "Bookings");
        }
    }
}
