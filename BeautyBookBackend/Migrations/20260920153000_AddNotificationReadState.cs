using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BeautyBookBackend.Data;

#nullable disable

namespace BeautyBookBackend.Migrations;

[Migration("20260920153000_AddNotificationReadState")]
[DbContext(typeof(ApplicationDbContext))]
public partial class AddNotificationReadState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "ReadAt", table: "AppNotifications", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_AppNotifications_UserId_ReadAt_CreatedAt", table: "AppNotifications", columns: new[] { "UserId", "ReadAt", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AppNotifications_UserId_ReadAt_CreatedAt", table: "AppNotifications");
        migrationBuilder.DropColumn(name: "ReadAt", table: "AppNotifications");
    }
}
