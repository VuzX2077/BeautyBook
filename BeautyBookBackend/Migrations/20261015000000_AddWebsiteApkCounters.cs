using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeautyBookBackend.Migrations;

// Operational analytics only; not part of the domain entity model.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261015000000_AddWebsiteApkCounters")]
public sealed class AddWebsiteApkCounters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE "WebsiteApkClicks" (
          "EventId" uuid PRIMARY KEY,
          "ReceivedAt" timestamptz NOT NULL,
          "Day" date NOT NULL
        );
        CREATE INDEX "IX_WebsiteApkClicks_Day" ON "WebsiteApkClicks" ("Day");
        CREATE TABLE "WebsiteCounterSources" (
          "Name" text PRIMARY KEY,
          "StartedAt" timestamptz NULL
        );
        CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "WebsiteApkClicks"
          FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
        CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "WebsiteApkClicks"
          FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
        CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "WebsiteCounterSources"
          FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
        CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "WebsiteCounterSources"
          FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
        INSERT INTO "WebsiteCounterSources" ("Name") VALUES ('apk');
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE "WebsiteApkClicks";
        DROP TABLE "WebsiteCounterSources";
        """);
}
