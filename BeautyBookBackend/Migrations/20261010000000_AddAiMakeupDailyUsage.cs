using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeautyBookBackend.Migrations;

// Operational counters accessed through parameterized SQL, intentionally outside EF's domain model.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261010000000_AddAiMakeupDailyUsage")]
public sealed class AddAiMakeupDailyUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE "AiMakeupDailyUsage" (
            "Day" date NOT NULL,
            "UserKey" varchar(64) NOT NULL,
            "Count" integer NOT NULL CHECK ("Count" > 0),
            PRIMARY KEY ("Day", "UserKey")
        );
        CREATE TRIGGER account_deletion_write_guard BEFORE INSERT OR UPDATE ON "AiMakeupDailyUsage"
            FOR EACH ROW EXECUTE FUNCTION public.account_deletion_write_guard();
        CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON "AiMakeupDailyUsage"
            FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE \"AiMakeupDailyUsage\";");
}
