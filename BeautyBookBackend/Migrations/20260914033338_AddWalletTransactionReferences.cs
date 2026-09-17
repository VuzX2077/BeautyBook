using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTransactionReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "WalletTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "WalletTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceId_TransactionType",
                table: "WalletTransactions",
                columns: new[] { "ReferenceId", "TransactionType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletTransactions_ReferenceId_TransactionType",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "WalletTransactions");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "WalletTransactions");
        }
    }
}
