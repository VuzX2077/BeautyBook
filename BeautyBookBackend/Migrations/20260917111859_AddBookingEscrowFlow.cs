using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingEscrowFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerConfirmationDeadline",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepositPaidAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositRate",
                table: "Bookings",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MuaPayoutAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeeAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitingCustomerAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "DepositRate" = 1.0000,
                    "DepositAmount" = "TotalAmount",
                    "RemainingAmount" = 0,
                    "PlatformFeeAmount" = LEAST(10000, "TotalAmount"),
                    "MuaPayoutAmount" = GREATEST(0, "TotalAmount" - 10000),
                    "UpdatedAt" = COALESCE("CreatedAt", CURRENT_TIMESTAMP);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CustomerConfirmationDeadline",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepositPaidAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DepositRate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DisputedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MuaPayoutAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformFeeAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "WaitingCustomerAt",
                table: "Bookings");
        }
    }
}
