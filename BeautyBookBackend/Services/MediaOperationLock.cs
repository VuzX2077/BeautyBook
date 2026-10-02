using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Same lock as Phase 1 maintenance. Hold across uploads, not only their DB writes.
public sealed class MediaOperationLock(ApplicationDbContext db) : IAsyncDisposable
{
    private bool locked;
    public async Task AcquireAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try {
            var acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock_shared(724266524669002) AS \"Value\"").SingleAsync(ct);
            if (!acquired) throw new InvalidOperationException("Media operation is busy; retry later.");
            locked = true;
        } catch { await db.Database.CloseConnectionAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (!locked) return;
        try { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock_shared(724266524669002)", CancellationToken.None); }
        finally { locked = false; await db.Database.CloseConnectionAsync(); }
    }
}
