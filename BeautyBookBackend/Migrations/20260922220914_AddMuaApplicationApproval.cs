using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaApplicationApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "MakeupArtistProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "MakeupArtistProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByAdminId",
                table: "MakeupArtistProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "MakeupArtistProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "VerificationStatus",
                table: "MakeupArtistProfiles",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.Sql("UPDATE \"MakeupArtistProfiles\" SET \"VerificationStatus\" = 2, \"ReviewedAt\" = NOW() WHERE \"Status\" = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupArtistProfiles_ReviewedByAdminId",
                table: "MakeupArtistProfiles",
                column: "ReviewedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_MakeupArtistProfiles_VerificationStatus_SubmittedAt",
                table: "MakeupArtistProfiles",
                columns: new[] { "VerificationStatus", "SubmittedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_MakeupArtistProfiles_Users_ReviewedByAdminId",
                table: "MakeupArtistProfiles",
                column: "ReviewedByAdminId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MakeupArtistProfiles_Users_ReviewedByAdminId",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MakeupArtistProfiles_ReviewedByAdminId",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropIndex(
                name: "IX_MakeupArtistProfiles_VerificationStatus_SubmittedAt",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewedByAdminId",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "MakeupArtistProfiles");
        }
    }
}
