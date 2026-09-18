using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260918033000_AddPortfolioServiceAndRichChat")]
    public class AddPortfolioServiceAndRichChat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "ServiceId", table: "Portfolios", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ImageUrl", table: "Messages", type: "character varying(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ReplyToMessageId", table: "Messages", type: "uuid", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_Portfolios_ServiceId", table: "Portfolios", column: "ServiceId");
            migrationBuilder.CreateIndex(name: "IX_Messages_ReplyToMessageId", table: "Messages", column: "ReplyToMessageId");
            migrationBuilder.AddForeignKey(name: "FK_Portfolios_Services_ServiceId", table: "Portfolios", column: "ServiceId", principalTable: "Services", principalColumn: "ServiceId", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_Messages_Messages_ReplyToMessageId", table: "Messages", column: "ReplyToMessageId", principalTable: "Messages", principalColumn: "MessageId", onDelete: ReferentialAction.SetNull);
            migrationBuilder.CreateTable(
                name: "MessageReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReactions", x => x.Id);
                    table.ForeignKey("FK_MessageReactions_Messages_MessageId", x => x.MessageId, "Messages", "MessageId", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_MessageReactions_MessageId_UserId", table: "MessageReactions", columns: new[] { "MessageId", "UserId" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MessageReactions");
            migrationBuilder.DropForeignKey(name: "FK_Portfolios_Services_ServiceId", table: "Portfolios");
            migrationBuilder.DropForeignKey(name: "FK_Messages_Messages_ReplyToMessageId", table: "Messages");
            migrationBuilder.DropIndex(name: "IX_Portfolios_ServiceId", table: "Portfolios");
            migrationBuilder.DropIndex(name: "IX_Messages_ReplyToMessageId", table: "Messages");
            migrationBuilder.DropColumn(name: "ServiceId", table: "Portfolios");
            migrationBuilder.DropColumn(name: "ImageUrl", table: "Messages");
            migrationBuilder.DropColumn(name: "ReplyToMessageId", table: "Messages");
        }
    }
}
