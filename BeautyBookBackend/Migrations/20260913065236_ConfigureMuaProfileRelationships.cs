using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureMuaProfileRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MUAStyles_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "MUAStyles");

            migrationBuilder.DropForeignKey(
                name: "FK_MUAStyles_MakeupStyles_MakeupStyleStyleId",
                table: "MUAStyles");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Services_MakeupArtistProfileMUAId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_MUAStyles_MakeupArtistProfileMUAId",
                table: "MUAStyles");

            migrationBuilder.DropIndex(
                name: "IX_MUAStyles_MakeupStyleStyleId",
                table: "MUAStyles");

            migrationBuilder.DropColumn(
                name: "MakeupArtistProfileMUAId",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "MakeupArtistProfileMUAId",
                table: "MUAStyles");

            migrationBuilder.DropColumn(
                name: "MakeupStyleStyleId",
                table: "MUAStyles");

            migrationBuilder.CreateIndex(
                name: "IX_Services_MUAId",
                table: "Services",
                column: "MUAId");

            migrationBuilder.CreateIndex(
                name: "IX_MUAStyles_StyleId",
                table: "MUAStyles",
                column: "StyleId");

            migrationBuilder.AddForeignKey(
                name: "FK_MUAStyles_MakeupArtistProfiles_MUAId",
                table: "MUAStyles",
                column: "MUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MUAStyles_MakeupStyles_StyleId",
                table: "MUAStyles",
                column: "StyleId",
                principalTable: "MakeupStyles",
                principalColumn: "StyleId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_MakeupArtistProfiles_MUAId",
                table: "Services",
                column: "MUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MUAStyles_MakeupArtistProfiles_MUAId",
                table: "MUAStyles");

            migrationBuilder.DropForeignKey(
                name: "FK_MUAStyles_MakeupStyles_StyleId",
                table: "MUAStyles");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_MakeupArtistProfiles_MUAId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Services_MUAId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_MUAStyles_StyleId",
                table: "MUAStyles");

            migrationBuilder.AddColumn<Guid>(
                name: "MakeupArtistProfileMUAId",
                table: "Services",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MakeupArtistProfileMUAId",
                table: "MUAStyles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MakeupStyleStyleId",
                table: "MUAStyles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_MakeupArtistProfileMUAId",
                table: "Services",
                column: "MakeupArtistProfileMUAId");

            migrationBuilder.CreateIndex(
                name: "IX_MUAStyles_MakeupArtistProfileMUAId",
                table: "MUAStyles",
                column: "MakeupArtistProfileMUAId");

            migrationBuilder.CreateIndex(
                name: "IX_MUAStyles_MakeupStyleStyleId",
                table: "MUAStyles",
                column: "MakeupStyleStyleId");

            migrationBuilder.AddForeignKey(
                name: "FK_MUAStyles_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "MUAStyles",
                column: "MakeupArtistProfileMUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId");

            migrationBuilder.AddForeignKey(
                name: "FK_MUAStyles_MakeupStyles_MakeupStyleStyleId",
                table: "MUAStyles",
                column: "MakeupStyleStyleId",
                principalTable: "MakeupStyles",
                principalColumn: "StyleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Services_MakeupArtistProfiles_MakeupArtistProfileMUAId",
                table: "Services",
                column: "MakeupArtistProfileMUAId",
                principalTable: "MakeupArtistProfiles",
                principalColumn: "MUAId");
        }
    }
}
