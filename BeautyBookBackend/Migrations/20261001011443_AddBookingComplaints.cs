using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingComplaints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestedOutcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResponseDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovedRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingComplaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingComplaints_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ImageUrls = table.Column<List<string>>(type: "text[]", nullable: false),
                    Internal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintMessages_BookingComplaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "BookingComplaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingComplaints_BookingId",
                table: "BookingComplaints",
                column: "BookingId",
                unique: true,
                filter: "\"IsOpen\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_BookingComplaints_Status_CreatedAt",
                table: "BookingComplaints",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintMessages_ComplaintId_CreatedAt",
                table: "ComplaintMessages",
                columns: new[] { "ComplaintId", "CreatedAt" });

            migrationBuilder.Sql("""
                INSERT INTO "BookingComplaints" ("Id", "BookingId", "Category", "Description", "RequestedOutcome", "Status", "IsOpen", "CreatedAt", "UpdatedAt", "ResponseDeadline")
                SELECT gen_random_uuid(), "BookingId", 'Other', left(coalesce(nullif("DisputeReason", ''), 'Khiếu nại được chuyển từ luồng booking trước đây.'), 2000), 'Support', 'Submitted', TRUE,
                    coalesce("DisputedAt", "UpdatedAt"), NOW(), NOW() + interval '24 hours'
                FROM "Bookings" WHERE "Status" = 9;
                INSERT INTO "ComplaintMessages" ("Id", "ComplaintId", "AuthorId", "AuthorRole", "Kind", "Body", "ImageUrls", "Internal", "CreatedAt")
                SELECT gen_random_uuid(), c."Id", b."CustomerId", 'Customer', 'Submitted', c."Description", ARRAY[]::text[], FALSE, c."CreatedAt"
                FROM "BookingComplaints" c JOIN "Bookings" b ON b."BookingId" = c."BookingId";
                UPDATE "MuaReceivables" r SET "Status" = 0, "AvailableAt" = b."CompletedAt" + interval '48 hours', "UpdatedAt" = NOW()
                FROM "Bookings" b WHERE r."BookingId" = b."BookingId" AND r."Status" = 1
                    AND b."Status" IN (2, 10) AND b."CompletedAt" > NOW() - interval '48 hours';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplaintMessages");

            migrationBuilder.DropTable(
                name: "BookingComplaints");
        }
    }
}
