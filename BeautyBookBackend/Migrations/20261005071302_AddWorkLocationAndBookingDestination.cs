using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLocationAndBookingDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowCustomerVisit",
                table: "MakeupArtistProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WorkLocationAddress",
                table: "MakeupArtistProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkLocationName",
                table: "MakeupArtistProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceLocationName",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceLocationType",
                table: "Bookings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowCustomerVisit",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "WorkLocationAddress",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "WorkLocationName",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "ServiceLocationName",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ServiceLocationType",
                table: "Bookings");
        }
    }
}
