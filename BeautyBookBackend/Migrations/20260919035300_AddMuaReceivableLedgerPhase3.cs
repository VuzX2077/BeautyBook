using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaReceivableLedgerPhase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MuaReceivables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformFeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FrozenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaReceivables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaReceivables_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MuaReceivables_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MuaReceivables_BookingId",
                table: "MuaReceivables",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaReceivables_MuaId_Status",
                table: "MuaReceivables",
                columns: new[] { "MuaId", "Status" });

            // Backfill only when the historical wallet ledger proves a deterministic
            // Booking -> MUA wallet -> gross/fee/net mapping. Ambiguous rows remain in
            // WalletTransactions for manual reconciliation; no amount is inferred.
            migrationBuilder.Sql(
                """
                INSERT INTO "MuaReceivables" (
                    "Id", "BookingId", "MuaId", "GrossAmount", "PlatformFeeAmount", "NetAmount",
                    "Status", "CreatedAt", "PaidOutAt", "ReversedAt", "UpdatedAt")
                SELECT
                    md5(b."BookingId"::text || ':mua-receivable')::uuid,
                    b."BookingId", b."MUAId", b."DepositAmount", b."PlatformFeeAmount", b."MuaPayoutAmount",
                    CASE WHEN EXISTS (
                        SELECT 1 FROM "Refunds" r
                        WHERE r."BookingId" = b."BookingId" AND r."Status" = 3
                    ) THEN 5 ELSE 4 END,
                    COALESCE(b."CompletedAt", e."CreatedAt"),
                    CASE WHEN NOT EXISTS (
                        SELECT 1 FROM "Refunds" r
                        WHERE r."BookingId" = b."BookingId" AND r."Status" = 3
                    ) THEN COALESCE(b."CompletedAt", e."CreatedAt") ELSE NULL END,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM "Refunds" r
                        WHERE r."BookingId" = b."BookingId" AND r."Status" = 3
                    ) THEN COALESCE((SELECT max(r."CompletedAt") FROM "Refunds" r WHERE r."BookingId" = b."BookingId" AND r."Status" = 3), b."UpdatedAt") ELSE NULL END,
                    GREATEST(b."UpdatedAt", e."CreatedAt")
                FROM "Bookings" b
                JOIN "WalletTransactions" e
                  ON e."ReferenceId" = b."BookingId"
                 AND e."ReferenceType" = 'Booking'
                 AND e."TransactionType" = 3
                JOIN "Wallets" w ON w."WalletId" = e."WalletId" AND w."UserId" = b."MUAId"
                WHERE b."Status" IN (2, 10)
                  AND e."Amount" = b."DepositAmount"
                  AND EXISTS (
                      SELECT 1 FROM "BookingPayments" bp
                      WHERE bp."BookingId" = b."BookingId" AND bp."Status" IN (2, 6))
                  AND (SELECT count(*) FROM "WalletTransactions" x
                       WHERE x."ReferenceId" = b."BookingId" AND x."ReferenceType" = 'Booking'
                         AND x."TransactionType" = 3) = 1
                  AND (
                      (b."PlatformFeeAmount" = 0 AND NOT EXISTS (
                          SELECT 1 FROM "WalletTransactions" c
                          WHERE c."ReferenceId" = b."BookingId" AND c."ReferenceType" = 'Booking' AND c."TransactionType" = 4))
                      OR
                      (b."PlatformFeeAmount" > 0
                       AND (SELECT count(*) FROM "WalletTransactions" c
                            WHERE c."ReferenceId" = b."BookingId" AND c."ReferenceType" = 'Booking'
                              AND c."TransactionType" = 4 AND c."Amount" = -b."PlatformFeeAmount") = 1)
                  )
                  AND b."DepositAmount" - b."PlatformFeeAmount" = b."MuaPayoutAmount";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaReceivables");
        }
    }
}
