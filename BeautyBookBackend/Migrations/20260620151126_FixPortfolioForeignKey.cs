using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class FixPortfolioForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Portfolios_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "Portfolios");

            migrationBuilder.DropIndex(
                name: "IX_Portfolios_MakeupArtistProfileMUAId",
                table: "Portfolios");

            migrationBuilder.DropColumn(
                name: "MakeupArtistProfileMUAId",
                table: "Portfolios");

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_MUAId",
                table: "Portfolios",
                column: "MUAId");

            migrationBuilder.AddForeignKey(
                name: "FK_Portfolios_MakeupArtistProfiles_MUAId",
                table: "Portfolios",
                column: "MUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Portfolios_MakeupArtistProfiles_MUAId",
                table: "Portfolios");

            migrationBuilder.DropIndex(
                name: "IX_Portfolios_MUAId",
                table: "Portfolios");

            migrationBuilder.AddColumn<Guid>(
                name: "MakeupArtistProfileMUAId",
                table: "Portfolios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_MakeupArtistProfileMUAId",
                table: "Portfolios",
                column: "MakeupArtistProfileMUAId");

            migrationBuilder.AddForeignKey(
                name: "FK_Portfolios_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "Portfolios",
                column: "MakeupArtistProfileMUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId");
        }
    }
}
