using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaOnboardingAreaAndExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "MakeupArtistProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DistrictCode",
                table: "MakeupArtistProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExperienceLevel",
                table: "MakeupArtistProfiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "MakeupArtistProfiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "MakeupArtistProfiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvinceCode",
                table: "MakeupArtistProfiles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "District",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "DistrictCode",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "ExperienceLevel",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "ProvinceCode",
                table: "MakeupArtistProfiles");
        }
    }
}
