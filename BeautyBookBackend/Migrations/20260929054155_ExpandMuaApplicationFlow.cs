using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class ExpandMuaApplicationFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "MakeupArtistProfiles",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "CertificateUrls",
                table: "MakeupArtistProfiles",
                type: "text[]",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"MakeupArtistProfiles\" SET \"CertificateUrls\" = ARRAY[]::text[] WHERE \"CertificateUrls\" IS NULL;");

            migrationBuilder.AlterColumn<List<string>>(
                name: "CertificateUrls",
                table: "MakeupArtistProfiles",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(List<string>),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityBackUrl",
                table: "MakeupArtistProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityFrontUrl",
                table: "MakeupArtistProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortraitUrl",
                table: "MakeupArtistProfiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionDetailsJson",
                table: "MakeupArtistProfiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "CertificateUrls",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "IdentityBackUrl",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "IdentityFrontUrl",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "PortraitUrl",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "RejectionDetailsJson",
                table: "MakeupArtistProfiles");
        }
    }
}
