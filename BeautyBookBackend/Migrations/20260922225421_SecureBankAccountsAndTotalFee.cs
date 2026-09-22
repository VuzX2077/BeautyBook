using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class SecureBankAccountsAndTotalFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationQrCodeUrl",
                table: "Refunds",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrlSnapshot",
                table: "Payouts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAt",
                table: "MuaBankAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "MuaBankAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "BANK");

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrl",
                table: "MuaBankAccounts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAt",
                table: "CustomerBankAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "CustomerBankAccounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "BANK");

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrl",
                table: "CustomerBankAccounts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinancialPolicyVersion",
                table: "Bookings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "V1_DEPOSIT_FEE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationQrCodeUrl",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "QrCodeUrlSnapshot",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "QrCodeUrl",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "CustomerBankAccounts");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "CustomerBankAccounts");

            migrationBuilder.DropColumn(
                name: "QrCodeUrl",
                table: "CustomerBankAccounts");

            migrationBuilder.DropColumn(
                name: "FinancialPolicyVersion",
                table: "Bookings");
        }
    }
}
