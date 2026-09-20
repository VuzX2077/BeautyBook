using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BeautyBookBackend.Data;

#nullable disable
namespace BeautyBookBackend.Migrations;

[Migration("20260920170000_AddAdminNotificationCampaigns")]
[DbContext(typeof(ApplicationDbContext))]
public partial class AddAdminNotificationCampaigns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "NotificationCampaigns", columns: table => new { Id = table.Column<Guid>(type: "uuid", nullable: false), Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false), Audience = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false), Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true), CreatedByAdminId = table.Column<Guid>(type: "uuid", nullable: false), RecipientCount = table.Column<int>(type: "integer", nullable: false), Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false), CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false) }, constraints: table => { table.PrimaryKey("PK_NotificationCampaigns", x => x.Id); table.ForeignKey("FK_NotificationCampaigns_Users_CreatedByAdminId", x => x.CreatedByAdminId, "Users", "UserId", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.AddColumn<Guid>(name: "CampaignId", table: "AppNotifications", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_NotificationCampaigns_CreatedAt", table: "NotificationCampaigns", column: "CreatedAt");
        migrationBuilder.CreateIndex(name: "IX_NotificationCampaigns_CreatedByAdminId", table: "NotificationCampaigns", column: "CreatedByAdminId");
        migrationBuilder.CreateIndex(name: "IX_NotificationCampaigns_IdempotencyKey", table: "NotificationCampaigns", column: "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppNotifications_CampaignId", table: "AppNotifications", column: "CampaignId");
        migrationBuilder.AddForeignKey(name: "FK_AppNotifications_NotificationCampaigns_CampaignId", table: "AppNotifications", column: "CampaignId", principalTable: "NotificationCampaigns", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_AppNotifications_NotificationCampaigns_CampaignId", table: "AppNotifications");
        migrationBuilder.DropTable(name: "NotificationCampaigns");
        migrationBuilder.DropIndex(name: "IX_AppNotifications_CampaignId", table: "AppNotifications");
        migrationBuilder.DropColumn(name: "CampaignId", table: "AppNotifications");
    }
}
