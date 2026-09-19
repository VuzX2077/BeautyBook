using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCancellationRefundPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "CancellationActor",
                table: "Bookings",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancellationAppointmentAtUtc",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationPolicyRule",
                table: "Bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Bookings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationRefundAmount",
                table: "Bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationRefundPercentage",
                table: "Bookings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledBy",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_PositiveAmount",
                table: "Refunds",
                sql: "\"Amount\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CancelledBy",
                table: "Bookings",
                column: "CancelledBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Users_CancelledBy",
                table: "Bookings",
                column: "CancelledBy",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Users_CancelledBy",
                table: "Bookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_PositiveAmount",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CancelledBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationActor",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationAppointmentAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationPolicyRule",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationRefundAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationRefundPercentage",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Bookings");
        }
    }
}
