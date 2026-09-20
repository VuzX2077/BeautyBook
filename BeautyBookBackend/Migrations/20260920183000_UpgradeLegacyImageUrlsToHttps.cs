using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BeautyBookBackend.Data;

#nullable disable

namespace BeautyBookBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260920183000_UpgradeLegacyImageUrlsToHttps")]
    public partial class UpgradeLegacyImageUrlsToHttps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string oldOrigin = "http://beautybook-13zj.onrender.com/";
            const string newOrigin = "https://beautybook-13zj.onrender.com/";

            foreach (var (table, column) in new[]
            {
                ("Users", "AvatarUrl"),
                ("Services", "ImageUrl"),
                ("Reviews", "ImageUrl"),
                ("Messages", "ImageUrl"),
                ("Products", "ImageUrl")
            })
            {
                migrationBuilder.Sql($$"""
                    UPDATE "{{table}}"
                    SET "{{column}}" = replace("{{column}}", '{{oldOrigin}}', '{{newOrigin}}')
                    WHERE "{{column}}" LIKE '{{oldOrigin}}%';
                    """);
            }

            migrationBuilder.Sql($$"""
                UPDATE "Portfolios"
                SET "ImageUrls" = ARRAY(
                    SELECT replace(url, '{{oldOrigin}}', '{{newOrigin}}')
                    FROM unnest("ImageUrls") AS url
                )
                WHERE EXISTS (
                    SELECT 1 FROM unnest("ImageUrls") AS url
                    WHERE url LIKE '{{oldOrigin}}%'
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // HTTPS URLs remain valid and must not be downgraded to cleartext HTTP.
        }
    }
}
