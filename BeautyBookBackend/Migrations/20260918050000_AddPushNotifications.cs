using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using BeautyBookBackend.Data;

#nullable disable
namespace BeautyBookBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260918050000_AddPushNotifications")]
public partial class AddPushNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "DevicePushTokens", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), UserId = table.Column<Guid>(type: "uuid", nullable: false),
            ExpoPushToken = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false), Platform = table.Column<string>(type: "text", nullable: false), DeviceName = table.Column<string>(type: "text", nullable: true),
            IsActive = table.Column<bool>(type: "boolean", nullable: false), CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_DevicePushTokens", x => x.Id); table.ForeignKey("FK_DevicePushTokens_Users_UserId", x => x.UserId, "Users", "UserId", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex(name: "IX_DevicePushTokens_ExpoPushToken", table: "DevicePushTokens", column: "ExpoPushToken", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DevicePushTokens_UserId", table: "DevicePushTokens", column: "UserId");

        migrationBuilder.CreateTable(name: "AppNotifications", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), UserId = table.Column<Guid>(type: "uuid", nullable: false), BookingId = table.Column<Guid>(type: "uuid", nullable: true),
            Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false), Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false), DataJson = table.Column<string>(type: "text", nullable: true),
            ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true), AttemptCount = table.Column<int>(type: "integer", nullable: false), Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), LastError = table.Column<string>(type: "text", nullable: true), CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_AppNotifications", x => x.Id); table.ForeignKey("FK_AppNotifications_Bookings_BookingId", x => x.BookingId, "Bookings", "BookingId", onDelete: ReferentialAction.Cascade); table.ForeignKey("FK_AppNotifications_Users_UserId", x => x.UserId, "Users", "UserId", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex(name: "IX_AppNotifications_BookingId_UserId_Type", table: "AppNotifications", columns: new[] { "BookingId", "UserId", "Type" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppNotifications_Status_ScheduledAt", table: "AppNotifications", columns: new[] { "Status", "ScheduledAt" });
        migrationBuilder.CreateIndex(name: "IX_AppNotifications_UserId", table: "AppNotifications", column: "UserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("AppNotifications"); migrationBuilder.DropTable("DevicePushTokens"); }
}
