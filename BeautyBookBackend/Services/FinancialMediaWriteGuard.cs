using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public static class FinancialMediaWriteGuard
{
    public static async Task VerifyCoverageAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid
            JOIN pg_namespace n ON n.oid=r.relnamespace WHERE n.nspname='public'
            AND r.relname IN ('BankAccounts','Payouts','Refunds') AND t.tgname='financial_media_write_guard'
            AND t.tgenabled='O' AND NOT t.tgisinternal
            """).SingleAsync(ct);
        if (count != 3) throw new InvalidOperationException("Financial reference writer guard is incomplete.");
    }
}
