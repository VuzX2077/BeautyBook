using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    /// <inheritdoc />
    public partial class MuaWebCommerceAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedCommerceEmail",
                table: "Users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PortfolioPromotionConsent",
                table: "MuaCommerceOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "MuaCommerceAttempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationStartedAt",
                table: "MuaCommerceAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MuaCommerceIngress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderCode = table.Column<long>(type: "bigint", nullable: false),
                    AmountVnd = table.Column<long>(type: "bigint", nullable: false),
                    PaymentLinkId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceIngress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FirstSendAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveryState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RecipientHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceJobs", x => x.Id);
                    table.CheckConstraint("CK_MuaCommerceJobs_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_MuaCommerceJobs_State", "\"State\" IN ('Pending','Processing','Done','ManualReview')");
                });

            migrationBuilder.CreateTable(
                name: "MuaCommerceOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmailSnapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaCommerceOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaCommerceOffers_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaFacebookTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleStartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Draft = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    PublishedUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revisions = table.Column<int>(type: "integer", nullable: false),
                    RevisionRequest = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RemedyNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaFacebookTasks", x => x.Id);
                    table.CheckConstraint("CK_MuaFacebookTask_Revisions", "\"Revisions\" BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_MuaFacebookTask_State", "\"State\" IN ('Preparing','Scheduled','Published','NeedsContent','RevisionRequested')");
                    table.ForeignKey(
                        name: "FK_MuaFacebookTasks_MuaCommerceOrders_OrderId_MuaId",
                        columns: x => new { x.OrderId, x.MuaId },
                        principalTable: "MuaCommerceOrders",
                        principalColumns: new[] { "Id", "MuaId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MuaFacebookTasks_MuaVisibilityGrants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "MuaVisibilityGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaSponsoredEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MuaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaSponsoredEvents", x => x.Id);
                    table.CheckConstraint("CK_MuaSponsoredEvents_Kind", "\"Kind\" IN ('Impression','ProfileView')");
                    table.ForeignKey(
                        name: "FK_MuaSponsoredEvents_MakeupArtistProfiles_MuaId",
                        column: x => x.MuaId,
                        principalTable: "MakeupArtistProfiles",
                        principalColumn: "MUAId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuaWebSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CsrfToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuaWebSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuaWebSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders",
                sql: "\"Origin\" IN ('IndependentWebsite','AdminApproval','ExternalRequest')");

            foreach (var table in new[] { "MuaCommerceIngress", "MuaCommerceJobs", "MuaCommerceOffers", "MuaFacebookTasks", "MuaSponsoredEvents", "MuaWebSessions" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();");
                migrationBuilder.Sql($"CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();");
            }
            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceIngress_EventKey",
                table: "MuaCommerceIngress",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceJobs_DedupKey",
                table: "MuaCommerceJobs",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceJobs_State_AvailableAt",
                table: "MuaCommerceJobs",
                columns: new[] { "State", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaCommerceOffers_MuaId_EvidenceReference",
                table: "MuaCommerceOffers",
                columns: new[] { "MuaId", "EvidenceReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaFacebookTasks_GrantId",
                table: "MuaFacebookTasks",
                column: "GrantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaFacebookTasks_OrderId_MuaId",
                table: "MuaFacebookTasks",
                columns: new[] { "OrderId", "MuaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaFacebookTasks_State_DueAt",
                table: "MuaFacebookTasks",
                columns: new[] { "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaSponsoredEvents_MuaId_CreatedAt",
                table: "MuaSponsoredEvents",
                columns: new[] { "MuaId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MuaWebSessions_ExpiresAt",
                table: "MuaWebSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MuaWebSessions_TokenHash",
                table: "MuaWebSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuaWebSessions_UserId",
                table: "MuaWebSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MuaCommerceIngress");

            migrationBuilder.DropTable(
                name: "MuaCommerceJobs");

            migrationBuilder.DropTable(
                name: "MuaCommerceOffers");

            migrationBuilder.DropTable(
                name: "MuaFacebookTasks");

            migrationBuilder.DropTable(
                name: "MuaSponsoredEvents");

            migrationBuilder.DropTable(
                name: "MuaWebSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "VerifiedCommerceEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PortfolioPromotionConsent",
                table: "MuaCommerceOrders");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "MuaCommerceAttempts");

            migrationBuilder.DropColumn(
                name: "CreationStartedAt",
                table: "MuaCommerceAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MuaCommerceOrders_Origin",
                table: "MuaCommerceOrders",
                sql: "\"Origin\" = 'IndependentWebsite'");
        }
    }
}
