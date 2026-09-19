using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaPayoutPhase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MuaBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_MuaBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaBankAccounts_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LastHandledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    Provider = table.Column<byte>(type: "smallint", nullable: false),
                    BankCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankNameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountNumberSnapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountHolderNameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessingAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconciledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payouts_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payouts_Users_LastHandledBy",
                        column: x => x.LastHandledBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Payouts_Users_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayoutItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaReceivableId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutItems_MuaReceivables_MuaReceivableId",
                        column: x => x.MuaReceivableId,
                        principalTable: "MuaReceivables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutItems_Payouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "Payouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MuaBankAccounts_MuaId_IsActive",
                table: "MuaBankAccounts",
                columns: new[] { "MuaId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_MuaBankAccounts_Default",
                table: "MuaBankAccounts",
                column: "MuaId",
                unique: true,
                filter: "\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutItems_PayoutId_MuaReceivableId",
                table: "PayoutItems",
                columns: new[] { "PayoutId", "MuaReceivableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PayoutItems_ActiveReceivable",
                table: "PayoutItems",
                column: "MuaReceivableId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_LastHandledBy",
                table: "Payouts",
                column: "LastHandledBy");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_MuaId_IdempotencyKey",
                table: "Payouts",
                columns: new[] { "MuaId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_MuaId_Status",
                table: "Payouts",
                columns: new[] { "MuaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_RequestedBy",
                table: "Payouts",
                column: "RequestedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaBankAccounts");

            migrationBuilder.DropTable(
                name: "PayoutItems");

            migrationBuilder.DropTable(
                name: "Payouts");
        }
    }
}
