using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class HardenBankAccountEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationBankAccountId",
                table: "Refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "VerificationStatus",
                table: "MuaBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PENDING_ADMIN",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "APPROVED");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ActivatedAt",
                table: "MuaBankAccounts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "VerificationStatus",
                table: "CustomerBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PENDING_ADMIN",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "APPROVED");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ActivatedAt",
                table: "CustomerBankAccounts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_DestinationBankAccountId",
                table: "Refunds",
                column: "DestinationBankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_CustomerBankAccounts_DestinationBankAccountId",
                table: "Refunds",
                column: "DestinationBankAccountId",
                principalTable: "CustomerBankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_CustomerBankAccounts_DestinationBankAccountId",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_DestinationBankAccountId",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationBankAccountId",
                table: "Refunds");

            migrationBuilder.AlterColumn<string>(
                name: "VerificationStatus",
                table: "MuaBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "APPROVED",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "PENDING_ADMIN");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ActivatedAt",
                table: "MuaBankAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "VerificationStatus",
                table: "CustomerBankAccounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "APPROVED",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "PENDING_ADMIN");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ActivatedAt",
                table: "CustomerBankAccounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
