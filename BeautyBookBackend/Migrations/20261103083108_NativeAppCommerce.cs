using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class NativeAppCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders");

            migrationBuilder.AddColumn<string>(
                name: "PaymentDetailsJson",
                table: "MuaCommerceAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "CustomerAiPurchases",
                type: "text",
                nullable: false,
                defaultValue: "IndependentWebsite");

            migrationBuilder.AddColumn<string>(
                name: "PaymentDetailsJson",
                table: "CustomerAiPurchases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "CustomerAiPurchases",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders",
                sql: "\"Origin\" IN ('IndependentWebsite','NativeApp','AdminApproval','ExternalRequest')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "PaymentDetailsJson",
                table: "MuaCommerceAttempts");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "CustomerAiPurchases");

            migrationBuilder.DropColumn(
                name: "PaymentDetailsJson",
                table: "CustomerAiPurchases");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "CustomerAiPurchases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders",
                sql: "\"Origin\" IN ('IndependentWebsite','AdminApproval','ExternalRequest')");
        }
    }
}
