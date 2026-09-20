using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRefundDestinationAndPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "Refunds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DestinationAccountName",
                table: "Refunds",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationAccountNumber",
                table: "Refunds",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationBankBin",
                table: "Refunds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationBankName",
                table: "Refunds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DestinationCapturedAt",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProviderState",
                table: "Refunds",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAt",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPayoutId",
                table: "Refunds",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderReferenceId",
                table: "Refunds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankBin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBankAccounts_Users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ProviderReferenceId",
                table: "Refunds",
                column: "ProviderReferenceId",
                unique: true,
                filter: "\"ProviderReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankAccounts_CustomerId_IsActive",
                table: "CustomerBankAccounts",
                columns: new[] { "CustomerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_CustomerBankAccounts_Default",
                table: "CustomerBankAccounts",
                column: "CustomerId",
                unique: true,
                filter: "\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerBankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_ProviderReferenceId",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationAccountName",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationAccountNumber",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationBankBin",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationBankName",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DestinationCapturedAt",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "LastProviderState",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ProviderPayoutId",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ProviderReferenceId",
                table: "Refunds");
        }
    }
}
