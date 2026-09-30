using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMuaOperatingAreasAndConfirmedLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OperatingLocationConfirmed",
                table: "MakeupArtistProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OperatingLocationLabel",
                table: "MakeupArtistProfiles",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatingProvinceCode",
                table: "MakeupArtistProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PublicMeetingPoint",
                table: "MakeupArtistProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MuaOperatingAreas",
                columns: table => new
                {
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AreaId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaOperatingAreas", x => new { x.MuaId, x.AreaId });
                    table.ForeignKey(
                        name: "FK_MuaOperatingAreas_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupArtistProfiles_OperatingProvinceCode_Status_Verificat~",
                table: "MakeupArtistProfiles",
                columns: new[] { "OperatingProvinceCode", "Status", "VerificationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaOperatingAreas_AreaId",
                table: "MuaOperatingAreas",
                column: "AreaId");
            migrationBuilder.Sql("""
                UPDATE "MakeupArtistProfiles" SET "OperatingProvinceCode" = CASE "ProvinceCode" WHEN 1 THEN 1 WHEN 4 THEN 4 WHEN 2 THEN 8 WHEN 8 THEN 8 WHEN 11 THEN 11 WHEN 12 THEN 12 WHEN 14 THEN 14 WHEN 10 THEN 15 WHEN 15 THEN 15 WHEN 6 THEN 19 WHEN 19 THEN 19 WHEN 20 THEN 20 WHEN 22 THEN 22 WHEN 24 THEN 24 WHEN 27 THEN 24 WHEN 17 THEN 25 WHEN 25 THEN 25 WHEN 26 THEN 25 WHEN 30 THEN 31 WHEN 31 THEN 31 WHEN 33 THEN 33 WHEN 34 THEN 33 WHEN 35 THEN 37 WHEN 36 THEN 37 WHEN 37 THEN 37 WHEN 38 THEN 38 WHEN 40 THEN 40 WHEN 42 THEN 42 WHEN 44 THEN 44 WHEN 45 THEN 44 WHEN 46 THEN 46 WHEN 48 THEN 48 WHEN 49 THEN 48 WHEN 51 THEN 51 WHEN 62 THEN 51 WHEN 52 THEN 52 WHEN 64 THEN 52 WHEN 56 THEN 56 WHEN 58 THEN 56 WHEN 54 THEN 66 WHEN 66 THEN 66 WHEN 60 THEN 68 WHEN 67 THEN 68 WHEN 68 THEN 68 WHEN 70 THEN 75 WHEN 75 THEN 75 WHEN 74 THEN 79 WHEN 77 THEN 79 WHEN 79 THEN 79 WHEN 72 THEN 80 WHEN 80 THEN 80 WHEN 82 THEN 82 WHEN 87 THEN 82 WHEN 83 THEN 86 WHEN 84 THEN 86 WHEN 86 THEN 86 WHEN 89 THEN 91 WHEN 91 THEN 91 WHEN 92 THEN 92 WHEN 93 THEN 92 WHEN 94 THEN 92 WHEN 95 THEN 96 WHEN 96 THEN 96 ELSE NULL END;
                INSERT INTO "MuaOperatingAreas" ("MuaId", "AreaId") SELECT "MUAId", 'legacy-district:' || "DistrictCode"::text FROM "MakeupArtistProfiles" WHERE "DistrictCode" IS NOT NULL AND "OperatingProvinceCode" IS NOT NULL ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaOperatingAreas");

            migrationBuilder.DropIndex(
                name: "IX_MakeupArtistProfiles_OperatingProvinceCode_Status_Verificat~",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "OperatingLocationConfirmed",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "OperatingLocationLabel",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "OperatingProvinceCode",
                table: "MakeupArtistProfiles");

            migrationBuilder.DropColumn(
                name: "PublicMeetingPoint",
                table: "MakeupArtistProfiles");
        }
    }
}
