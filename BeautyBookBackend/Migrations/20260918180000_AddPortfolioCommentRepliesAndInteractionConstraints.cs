using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BeautyBookBackend.Data;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260918180000_AddPortfolioCommentRepliesAndInteractionConstraints")]
    public partial class AddPortfolioCommentRepliesAndInteractionConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCommentId",
                table: "PortfolioComments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DELETE FROM "PortfolioLikes" a USING "PortfolioLikes" b
                WHERE a."PortfolioId" = b."PortfolioId" AND a."UserId" = b."UserId" AND a."Id" > b."Id";
                DELETE FROM "PortfolioSaves" a USING "PortfolioSaves" b
                WHERE a."PortfolioId" = b."PortfolioId" AND a."UserId" = b."UserId" AND a."Id" > b."Id";
                """);

            migrationBuilder.CreateIndex(name: "IX_PortfolioLikes_PortfolioId_UserId", table: "PortfolioLikes", columns: new[] { "PortfolioId", "UserId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PortfolioSaves_PortfolioId_UserId", table: "PortfolioSaves", columns: new[] { "PortfolioId", "UserId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PortfolioComments_ParentCommentId", table: "PortfolioComments", column: "ParentCommentId");
            migrationBuilder.AddForeignKey(name: "FK_PortfolioComments_PortfolioComments_ParentCommentId", table: "PortfolioComments", column: "ParentCommentId", principalTable: "PortfolioComments", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_PortfolioComments_PortfolioComments_ParentCommentId", table: "PortfolioComments");
            migrationBuilder.DropIndex(name: "IX_PortfolioComments_ParentCommentId", table: "PortfolioComments");
            migrationBuilder.DropIndex(name: "IX_PortfolioLikes_PortfolioId_UserId", table: "PortfolioLikes");
            migrationBuilder.DropIndex(name: "IX_PortfolioSaves_PortfolioId_UserId", table: "PortfolioSaves");
            migrationBuilder.DropColumn(name: "ParentCommentId", table: "PortfolioComments");
        }
    }
}
