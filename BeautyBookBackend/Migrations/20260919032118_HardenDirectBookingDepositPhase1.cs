using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class HardenDirectBookingDepositPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_ReferenceId_TransactionType",
                table: "WalletTransactions");

            migrationBuilder.CreateIndex(
                name: "UX_WalletTransactions_BookingFinancialEffect",
                table: "WalletTransactions",
                columns: new[] { "ReferenceId", "TransactionType" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL AND \"ReferenceType\" = 'Booking' AND \"TransactionType\" IN (3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WalletTransactions_BookingFinancialEffect",
                table: "WalletTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceId_TransactionType",
                table: "WalletTransactions",
                columns: new[] { "ReferenceId", "TransactionType" });
        }
    }
}
