using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddExplorePagingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Services_IsActive_Price_ServiceId",
                table: "Services",
                columns: new[] { "IsActive", "Price", "ServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_IsHidden_CreatedAt_PortfolioId",
                table: "Portfolios",
                columns: new[] { "IsHidden", "CreatedAt", "PortfolioId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupArtistProfiles_Status_VerificationStatus_MUAId",
                table: "MakeupArtistProfiles",
                columns: new[] { "Status", "VerificationStatus", "MUAId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Services_IsActive_Price_ServiceId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Portfolios_IsHidden_CreatedAt_PortfolioId",
                table: "Portfolios");

            migrationBuilder.DropIndex(
                name: "IX_MakeupArtistProfiles_Status_VerificationStatus_MUAId",
                table: "MakeupArtistProfiles");
        }
    }
}
