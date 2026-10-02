using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Only persisted, explicit user/support requests; never scans and deletes live accounts.
public sealed class AccountDeletionWorker(IServiceScopeFactory scopes, AccountConnections connections, ILogger<AccountDeletionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try { await ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning("Account deletion worker unavailable ({FailureType}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
    public async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connected = connections.Users;
        foreach (var id in await db.Users.Where(x => connected.Contains(x.UserId) && (!x.IsActive || x.DeletedAt != null)).Select(x => x.UserId).ToListAsync(ct)) connections.Abort(id);
        var request = await db.AccountDeletionRequests.AsNoTracking().Where(x => x.DatabaseCompletedAt != null && x.Status != "Completed" && x.NextAttemptAt <= DateTime.UtcNow).OrderBy(x => x.NextAttemptAt).FirstOrDefaultAsync(ct);
        if (request == null) return;
        try { await scope.ServiceProvider.GetRequiredService<AccountDeletionStorage>().ProcessAsync(request.UserId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) {
            // New context: don't save tracked partial values after an interrupted DB command.
            await using var recoveryScope = scopes.CreateAsyncScope();
            var recovery = recoveryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await recovery.AccountDeletionRequests.Where(x => x.UserId == request.UserId && x.Status != "Completed")
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, "RetryPending").SetProperty(r => r.ErrorCode, "STORAGE_OR_GUARD_UNAVAILABLE").SetProperty(r => r.NextAttemptAt, DateTime.UtcNow.AddMinutes(5)), ct);
            log.LogWarning("Account deletion storage postponed ({FailureType}).", ex.GetType().Name);
        }
    }
}
