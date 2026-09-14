using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletTopUps",
                columns: table => new
                {
                    TopUpId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Provider = table.Column<byte>(type: "smallint", nullable: false),
                    ProviderOrderCode = table.Column<long>(type: "bigint", nullable: false),
                    ProviderPaymentLinkId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QrCode = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RawWebhookPayload = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTopUps", x => x.TopUpId);
                    table.ForeignKey(
                        name: "FK_WalletTopUps_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTopUps_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUps_ProviderOrderCode",
                table: "WalletTopUps",
                column: "ProviderOrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUps_ProviderPaymentLinkId",
                table: "WalletTopUps",
                column: "ProviderPaymentLinkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUps_UserId",
                table: "WalletTopUps",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUps_WalletId",
                table: "WalletTopUps",
                column: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTopUps");
        }
    }
}
