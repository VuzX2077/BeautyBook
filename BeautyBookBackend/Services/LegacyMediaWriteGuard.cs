using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Covers direct SQL and every server writer, including arrays/JSON. Fail cleanup
// closed if a future textual table was added without the database guard.
public static class LegacyMediaWriteGuard
{
    public static async Task VerifyCoverageAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var missing = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM (
              SELECT DISTINCT table_name FROM information_schema.columns
              WHERE table_schema='public' AND data_type IN ('text','character varying','json','jsonb','ARRAY')
                AND table_name NOT IN ('VerificationMedia','PrivateMediaJobs','__EFMigrationsHistory')
            ) c WHERE NOT EXISTS (
              SELECT 1 FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
              WHERE n.nspname='public' AND r.relname=c.table_name AND t.tgname='private_media_write_guard'
                AND t.tgenabled='O' AND NOT t.tgisinternal
            )
            """).SingleAsync(ct);
        if (missing != 0) throw new InvalidOperationException("Private media database writer guard is incomplete.");
    }

}
