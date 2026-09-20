using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260920143000_BackfillAwaitingRefundDestinations")]
    public class BackfillAwaitingRefundDestinations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Refunds" AS r
                SET "DestinationBankBin" = bank."BankBin",
                    "DestinationBankName" = bank."BankName",
                    "DestinationAccountNumber" = bank."AccountNumber",
                    "DestinationAccountName" = bank."AccountHolderName",
                    "DestinationCapturedAt" = NOW(),
                    "Status" = 0,
                    "UpdatedAt" = NOW()
                FROM "Bookings" AS b
                JOIN LATERAL (
                    SELECT c."BankBin", c."BankName", c."AccountNumber", c."AccountHolderName"
                    FROM "CustomerBankAccounts" AS c
                    WHERE c."CustomerId" = b."CustomerId"
                      AND c."IsActive" = TRUE
                      AND c."IsDefault" = TRUE
                    ORDER BY c."UpdatedAt" DESC
                    LIMIT 1
                ) AS bank ON TRUE
                WHERE r."BookingId" = b."BookingId"
                  AND r."Status" = 5;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destination snapshots are business records and must not be erased on rollback.
        }
    }
}
