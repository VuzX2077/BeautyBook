using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStatusToBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "PaymentStatus",
                table: "Bookings",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.Sql("""
                UPDATE "Bookings" b
                SET "PaymentStatus" = 1
                WHERE EXISTS (
                    SELECT 1
                    FROM "WalletTransactions" wt
                    WHERE wt."TransactionType" = 2
                        AND wt."Amount" < 0
                        AND wt."Description" IS NOT NULL
                        AND wt."Description" LIKE ('%' || substring(b."BookingId"::text, 1, 8) || '%')
                );
                """);

            migrationBuilder.Sql("""
                UPDATE "Bookings" b
                SET "PaymentStatus" = 2
                WHERE EXISTS (
                    SELECT 1
                    FROM "WalletTransactions" wt
                    WHERE wt."TransactionType" = 2
                        AND wt."Amount" > 0
                        AND wt."Description" IS NOT NULL
                        AND wt."Description" LIKE ('%' || substring(b."BookingId"::text, 1, 8) || '%')
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Bookings");
        }
    }
}
