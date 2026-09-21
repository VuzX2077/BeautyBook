using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaSpecialtiesAndSocialProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "MakeupStyles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "MakeupStyles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "MakeupArtistProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "MakeupArtistProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyles_Name",
                table: "MakeupStyles",
                column: "Name",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "MakeupStyles" ("Name", "Description", "IsActive", "CreatedAt")
                VALUES
                    ('Cô dâu', NULL, TRUE, CURRENT_TIMESTAMP),
                    ('Sự kiện', NULL, TRUE, CURRENT_TIMESTAMP),
                    ('Korean', NULL, TRUE, CURRENT_TIMESTAMP),
                    ('Natural', NULL, TRUE, CURRENT_TIMESTAMP),
                    ('Douyin', NULL, TRUE, CURRENT_TIMESTAMP),
                    ('Kỷ yếu', NULL, TRUE, CURRENT_TIMESTAMP)
                ON CONFLICT ("Name") DO NOTHING;

                INSERT INTO "MakeupStyles" ("Name", "Description", "IsActive", "CreatedAt")
                SELECT DISTINCT BTRIM(value), NULL, TRUE, CURRENT_TIMESTAMP
                FROM "MakeupArtistProfiles" profile
                CROSS JOIN LATERAL regexp_split_to_table(COALESCE(profile."Specialization", ''), ',') AS value
                WHERE BTRIM(value) <> ''
                ON CONFLICT ("Name") DO NOTHING;

                INSERT INTO "MUAStyles" ("MUAId", "StyleId")
                SELECT DISTINCT profile."MUAId", style."StyleId"
                FROM "MakeupArtistProfiles" profile
                CROSS JOIN LATERAL regexp_split_to_table(COALESCE(profile."Specialization", ''), ',') AS value
                JOIN "MakeupStyles" style ON LOWER(style."Name") = LOWER(BTRIM(value))
                WHERE BTRIM(value) <> ''
                ON CONFLICT ("MUAId", "StyleId") DO NOTHING;

                UPDATE "MakeupArtistProfiles"
                SET "InstagramUrl" = "SocialLinks"
                WHERE "SocialLinks" IS NOT NULL
                  AND LOWER("SocialLinks") LIKE '%instagram.com%';

                UPDATE "MakeupArtistProfiles"
                SET "FacebookUrl" = "SocialLinks"
                WHERE "SocialLinks" IS NOT NULL
                  AND LOWER("SocialLinks") LIKE '%facebook.com%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MakeupStyles_Name",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "MakeupArtistProfiles");

        }
    }
}
