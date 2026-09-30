using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class HardenRealtimeChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Messages" SET "Content" = LEFT("Content", 2000)
                WHERE LENGTH("Content") > 2000;

                WITH ranked AS (
                    SELECT "ChatRoomId",
                           FIRST_VALUE("ChatRoomId") OVER (
                               PARTITION BY "CustomerId", "MUAId"
                               ORDER BY "CreatedAt", "ChatRoomId") AS keeper
                    FROM "ChatRooms"
                ), duplicates AS (
                    SELECT "ChatRoomId", keeper FROM ranked WHERE "ChatRoomId" <> keeper
                )
                UPDATE "Messages" AS message
                SET "ChatRoomId" = duplicate.keeper
                FROM duplicates AS duplicate
                WHERE message."ChatRoomId" = duplicate."ChatRoomId";

                WITH ranked AS (
                    SELECT "ChatRoomId",
                           ROW_NUMBER() OVER (
                               PARTITION BY "CustomerId", "MUAId"
                               ORDER BY "CreatedAt", "ChatRoomId") AS position
                    FROM "ChatRooms"
                )
                DELETE FROM "ChatRooms" AS room
                USING ranked
                WHERE room."ChatRoomId" = ranked."ChatRoomId" AND ranked.position > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatRoomId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_CustomerId",
                table: "ChatRooms");

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Messages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatRoomId_SentAt_MessageId",
                table: "Messages",
                columns: new[] { "ChatRoomId", "SentAt", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_CustomerId_MUAId",
                table: "ChatRooms",
                columns: new[] { "CustomerId", "MUAId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ChatRoomId_SentAt_MessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_ChatRooms_CustomerId_MUAId",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Messages");

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Messages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChatRoomId",
                table: "Messages",
                column: "ChatRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_CustomerId",
                table: "ChatRooms",
                column: "CustomerId");
        }
    }
}
