using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaFollows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MuaFollows",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaFollows", x => new { x.UserId, x.MuaId });
                    table.CheckConstraint("CK_MuaFollows_NoSelfFollow", "\"UserId\" <> \"MuaId\"");
                    table.ForeignKey(
                        name: "FK_MuaFollows_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MuaFollows_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MuaFollows_MuaId_CreatedAt",
                table: "MuaFollows",
                columns: new[] { "MuaId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaFollows_UserId_CreatedAt",
                table: "MuaFollows",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaFollows");
        }
    }
}
