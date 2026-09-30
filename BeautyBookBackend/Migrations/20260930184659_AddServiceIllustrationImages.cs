using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceIllustrationImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ImageUrls",
                table: "Services",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
            migrationBuilder.Sql("UPDATE \"Services\" SET \"ImageUrls\" = ARRAY[\"ImageUrl\"] WHERE \"ImageUrl\" IS NOT NULL AND \"ImageUrl\" <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrls",
                table: "Services");
        }
    }
}
