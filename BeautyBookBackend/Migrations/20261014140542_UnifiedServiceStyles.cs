using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedServiceStyles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreateRequestHash",
                table: "Services",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "MakeupStyles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceStyles",
                columns: table => new
                {
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceStyles", x => new { x.ServiceId, x.StyleId });
                    table.ForeignKey(
                        name: "FK_ServiceStyles_MakeupStyles_StyleId",
                        column: x => x.StyleId,
                        principalTable: "MakeupStyles",
                        principalColumn: "StyleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceStyles_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "ServiceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MakeupStyles_NormalizedName",
                table: "MakeupStyles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceStyles_StyleId",
                table: "ServiceStyles",
                column: "StyleId");

            // New tables must participate in the existing deletion writer guard.
            // The shared function already resolves a ServiceId to its MUA owner.
            migrationBuilder.Sql("""
                CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "ServiceStyles"
                    FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceStyles");

            migrationBuilder.DropIndex(
                name: "IX_MakeupStyles_NormalizedName",
                table: "MakeupStyles");

            migrationBuilder.DropColumn(
                name: "CreateRequestHash",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "MakeupStyles");
        }
    }
}
