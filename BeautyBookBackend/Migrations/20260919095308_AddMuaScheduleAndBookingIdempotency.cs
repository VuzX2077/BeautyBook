using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaScheduleAndBookingIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_CustomerId",
                table: "Bookings");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MuaTimeOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MUAId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaTimeOffs", x => x.Id);
                    table.CheckConstraint("CK_MuaTimeOffs_ValidRange", "\"StartAt\" < \"EndAt\"");
                    table.ForeignKey(
                        name: "FK_MuaTimeOffs_MakeupArtistProfiles_MUAId",
                        column: x => x.MUAId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MuaWorkingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MUAId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaWorkingSchedules", x => x.Id);
                    table.CheckConstraint("CK_MuaWorkingSchedules_ValidRange", "\"StartTime\" >= INTERVAL '0' AND \"EndTime\" <= INTERVAL '1 day' AND \"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_MuaWorkingSchedules_MakeupArtistProfiles_MUAId",
                        column: x => x.MUAId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CustomerId_IdempotencyKey",
                table: "Bookings",
                columns: new[] { "CustomerId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MuaTimeOffs_MUAId_StartAt_EndAt",
                table: "MuaTimeOffs",
                columns: new[] { "MUAId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaWorkingSchedules_MUAId_DayOfWeek_StartTime_EndTime",
                table: "MuaWorkingSchedules",
                columns: new[] { "MUAId", "DayOfWeek", "StartTime", "EndTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaTimeOffs");

            migrationBuilder.DropTable(
                name: "MuaWorkingSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CustomerId_IdempotencyKey",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CustomerId",
                table: "Bookings",
                column: "CustomerId");
        }
    }
}
