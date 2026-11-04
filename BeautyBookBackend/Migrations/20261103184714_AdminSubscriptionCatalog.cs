using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AdminSubscriptionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_MuaFacebookTasks_GrantId", table: "MuaFacebookTasks");
            migrationBuilder.CreateIndex(name: "IX_MuaFacebookTasks_GrantId_CycleStartsAt", table: "MuaFacebookTasks", columns: new[] { "GrantId", "CycleStartsAt" }, unique: true);
            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Catalog",
                table: "MuaCommerceOrders");

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "MuaCommerceOrders",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PlanVersion",
                table: "MuaCommerceOrders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "MuaCommerceOrders",
                type: "text",
                nullable: false,
                defaultValue: "BBook Visibility 30 ngày");

            migrationBuilder.AddColumn<int>(
                name: "AmountVnd",
                table: "CustomerAiPurchases",
                type: "integer",
                nullable: false,
                defaultValue: 20000);

            migrationBuilder.AddColumn<int>(
                name: "PlanVersion",
                table: "CustomerAiPurchases",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ProductId",
                table: "CustomerAiPurchases",
                type: "text",
                nullable: false,
                defaultValue: "bbook_customer_ai_lifetime_v1");

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "CustomerAiPurchases",
                type: "text",
                nullable: false,
                defaultValue: "AI quét mặt trọn đời");

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: false),
                    AfterJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AmountVnd = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "AmountVnd", "Days", "Description", "Name", "Role", "State", "Version" },
                values: new object[,]
                {
                    { "bbook_customer_ai_lifetime_v1", 20000, null, "", "AI quét mặt trọn đời", "Customer", "Published", 1 },
                    { "bbook_visibility_30d_v1", 75000, 30, "", "BBook Visibility 30 ngày", "MUA", "Published", 1 }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Catalog",
                table: "MuaCommerceOrders",
                sql: "\"AmountVnd\" >= 1000 AND \"DurationDays\" > 0");

            migrationBuilder.Sql("""
                UPDATE "MuaCommerceOrders" SET "ProductName" = 'Kích hoạt MUA (gói cũ)' WHERE "ProductId" = 'bbook_mua_activation_v1';
                CREATE FUNCTION public.subscription_snapshot_guard() RETURNS trigger LANGUAGE plpgsql AS $guard$
                BEGIN
                    IF ROW(to_jsonb(NEW)->'ProductId',to_jsonb(NEW)->'ProductName',to_jsonb(NEW)->'AmountVnd',to_jsonb(NEW)->'PlanVersion',to_jsonb(NEW)->'DurationDays')
                        IS DISTINCT FROM ROW(to_jsonb(OLD)->'ProductId',to_jsonb(OLD)->'ProductName',to_jsonb(OLD)->'AmountVnd',to_jsonb(OLD)->'PlanVersion',to_jsonb(OLD)->'DurationDays') THEN
                        RAISE EXCEPTION 'Purchased plan snapshot is immutable' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $guard$;
                CREATE TRIGGER subscription_snapshot_guard BEFORE UPDATE ON "MuaCommerceOrders" FOR EACH ROW EXECUTE FUNCTION public.subscription_snapshot_guard();
                CREATE TRIGGER subscription_snapshot_guard BEFORE UPDATE ON "CustomerAiPurchases" FOR EACH ROW EXECUTE FUNCTION public.subscription_snapshot_guard();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanChanges_PlanId_CreatedAt",
                table: "SubscriptionPlanChanges",
                columns: new[] { "PlanId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER subscription_snapshot_guard ON \"MuaCommerceOrders\"; DROP TRIGGER subscription_snapshot_guard ON \"CustomerAiPurchases\"; DROP FUNCTION public.subscription_snapshot_guard();");
            // Rollback requires reconciling multi-cycle tasks and newly priced orders first.
            migrationBuilder.DropIndex(name: "IX_MuaFacebookTasks_GrantId_CycleStartsAt", table: "MuaFacebookTasks");
            migrationBuilder.CreateIndex(name: "IX_MuaFacebookTasks_GrantId", table: "MuaFacebookTasks", column: "GrantId", unique: true);
            migrationBuilder.DropTable(
                name: "SubscriptionPlanChanges");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Catalog",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "PlanVersion",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "AmountVnd",
                table: "CustomerAiPurchases");

            migrationBuilder.DropColumn(
                name: "PlanVersion",
                table: "CustomerAiPurchases");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "CustomerAiPurchases");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "CustomerAiPurchases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Catalog",
                table: "MuaCommerceOrders",
                sql: "(\"ProductId\" = 'bbook_mua_activation_v1' AND \"AmountVnd\" = 100000) OR (\"ProductId\" = 'bbook_visibility_30d_v1' AND \"AmountVnd\" = 75000)");
        }
    }
}
