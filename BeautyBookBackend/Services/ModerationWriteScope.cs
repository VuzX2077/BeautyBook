using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BeautyBookBackend.Services;

// Serialize block decisions with UGC writes; hold actor rows against account deletion.
// This lock is separate from financial/provisioning locks and never calls a provider.
internal sealed class ModerationWriteScope : IAsyncDisposable
{
    private readonly IDbContextTransaction? transaction;
    private ModerationWriteScope(IDbContextTransaction? transaction) => this.transaction = transaction;
    public static async Task<ModerationWriteScope> Start(ApplicationDbContext db, params Guid[] users)
    {
        if (!db.Database.IsNpgsql()) return new(null);
        var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        var scope = new ModerationWriteScope(transaction);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724266524669010)");
            foreach (var id in users.Distinct().OrderBy(x => x))
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"UserId\"={id} FOR SHARE");
            return scope;
        }
        catch { await scope.DisposeAsync(); throw; }
    }
    public async Task Commit() { if (transaction != null) await transaction.CommitAsync(); }
    public async ValueTask DisposeAsync() { if (transaction != null) await transaction.DisposeAsync(); }
}
