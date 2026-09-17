using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    public partial class AddBookingEscrowAmounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "DepositAmount", table: "Bookings", type: "numeric", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "PlatformFeeAmount", table: "Bookings", type: "numeric", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "MuaEscrowAmount", table: "Bookings", type: "numeric", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "RemainingAmount", table: "Bookings", type: "numeric", nullable: false, defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DepositAmount", table: "Bookings");
            migrationBuilder.DropColumn(name: "PlatformFeeAmount", table: "Bookings");
            migrationBuilder.DropColumn(name: "MuaEscrowAmount", table: "Bookings");
            migrationBuilder.DropColumn(name: "RemainingAmount", table: "Bookings");
        }
    }
}
