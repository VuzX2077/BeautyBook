using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BeautyBookBackend.Migrations;

// Repair the verified coverage gap introduced when the feedback tables were added.
// Keep this separate from AI counters so operators can review both changes independently.
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261010001000_AddFeedbackPrivateMediaGuards")]
public sealed class AddFeedbackPrivateMediaGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON public."UserFeedbacks"
            FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
        CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON public."FeedbackEvents"
            FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER private_media_write_guard ON public."FeedbackEvents";
        DROP TRIGGER private_media_write_guard ON public."UserFeedbacks";
        """);
}
