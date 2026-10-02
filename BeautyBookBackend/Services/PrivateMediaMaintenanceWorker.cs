using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// A persisted queue in the existing PostgreSQL database works on Render Free.
// A restarted instance resumes Running jobs; advisory locks prevent two workers.
public sealed class PrivateMediaMaintenanceWorker(IServiceScopeFactory scopes, ILogger<PrivateMediaMaintenanceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning("Private media worker unavailable ({FailureType}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
    public async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        var locked = false;
        try
        {
            locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(724266524669003) AS \"Value\"").SingleAsync(ct);
            if (!locked) return;
            var job = await db.PrivateMediaJobs.Where(x => x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (job == null) return;
            job.Status = "Running"; job.StartedAt ??= DateTime.UtcNow; job.Attempts++;
            await db.SaveChangesAsync(ct);
            try
            {
                var maintenance = scope.ServiceProvider.GetRequiredService<VerificationMediaMaintenance>();
                // Recheck all current references immediately before destructive cleanup.
                if (job.Action == "cleanup-legacy") await maintenance.RunAsync("audit", ct);
                await maintenance.RunAsync(job.Action, ct);
                job.Status = "Succeeded";
                job.Result = job.Action == "cleanup-legacy" ? "Đã xử lý bản public cũ đủ điều kiện. Cần kiểm tra CDN cache và truy cập anonymous." : "Tác vụ hoàn tất. Kiểm tra dữ liệu và ứng dụng trước bước tiếp theo.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                job.Status = "Failed";
                job.Result = ex is InvalidOperationException ? "Tác vụ dừng vì cấu hình private, checksum hoặc reference cần xử lý. Không chạy cleanup khi chưa giải quyết. Kiểm tra log tổng hợp và hồ sơ admin." : "Tác vụ gặp lỗi lưu trữ/kết nối. Kiểm tra cấu hình và chạy lại; công cụ hỗ trợ retry.";
                log.LogWarning("Private media job {JobId} stopped ({FailureType}).", job.Id, ex.GetType().Name);
            }
            job.CompletedAt = DateTime.UtcNow; job.IsActive = false;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            try { if (locked) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(724266524669003)", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
