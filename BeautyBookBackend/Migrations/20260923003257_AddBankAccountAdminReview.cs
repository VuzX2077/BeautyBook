using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountAdminReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "MuaBankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "MuaBankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "MuaBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "APPROVED");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CustomerBankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                table: "CustomerBankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "CustomerBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "APPROVED");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "MuaBankAccounts");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CustomerBankAccounts");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "CustomerBankAccounts");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "CustomerBankAccounts");
        }
    }
}
