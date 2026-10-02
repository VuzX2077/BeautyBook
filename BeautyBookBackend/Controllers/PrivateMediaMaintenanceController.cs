using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/private-media/jobs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PrivateMediaMaintenanceController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.PrivateMediaJobs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync());
    public sealed class JobRequest
    {
        public string Action { get; set; } = "audit";
        public bool ConfirmPrivateBackup { get; set; }
        public bool ConfirmLegacyDeletion { get; set; }
    }
    [HttpPost]
    public async Task<IActionResult> Start(JobRequest request)
    {
        if (request.Action is not ("audit" or "migrate" or "cleanup-legacy" or "cleanup-orphans")) return BadRequest(new { Message = "Tác vụ không hợp lệ." });
        if (request.Action == "migrate" && !request.ConfirmPrivateBackup) return BadRequest(new { Message = "Cần xác nhận đã chuẩn bị backup riêng tư trước khi chuyển dữ liệu." });
        if (request.Action == "cleanup-legacy" && !request.ConfirmLegacyDeletion) return BadRequest(new { Message = "Cần xác nhận đã kiểm chứng bản private trước khi xóa bản public cũ." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724266524669004)");
        if (await db.PrivateMediaJobs.AnyAsync(x => x.IsActive)) return Conflict(new { Message = "Đã có tác vụ đang chạy. Theo dõi trạng thái trước khi tạo tác vụ mới." });
        if (request.Action is "migrate" or "cleanup-legacy")
        {
            var audit = await db.PrivateMediaJobs.AsNoTracking().Where(x => x.Action == "audit").OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
            if (audit?.Status != "Succeeded") return BadRequest(new { Message = "Cần chạy audit thành công trước thao tác này." });
            if (request.Action == "cleanup-legacy" && !await db.PrivateMediaJobs.AnyAsync(x => x.Action == "migrate" && x.Status == "Succeeded"))
                return BadRequest(new { Message = "Cần chuyển dữ liệu thành công và kiểm chứng trước khi dọn bản cũ." });
        }
        var job = new PrivateMediaJob { Id = Guid.NewGuid(), RequestedBy = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Action = request.Action, CreatedAt = DateTime.UtcNow };
        db.PrivateMediaJobs.Add(job); await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Accepted(new { job.Id, job.Status });
    }
}
