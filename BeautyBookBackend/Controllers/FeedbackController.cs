using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[Authorize, ApiController, Route("api")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeedbackController(ApplicationDbContext db) : ControllerBase
{
    public sealed record CreateRequest(Guid SubmissionId, [Required, StringLength(20)] string Category, [Required, StringLength(2000, MinimumLength = 10)] string Body);
    public sealed record ReviewRequest([Required] string Status, [StringLength(1000)] string Note, [Range(1, int.MaxValue)] int Version);
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static readonly string[] Categories = ["Bug", "Suggestion", "Other"];
    private static readonly string[] Statuses = ["New", "InProgress", "Resolved", "Closed"];

    [HttpPost("feedback"), Authorize(Roles = "Customer,MUA")]
    public async Task<IActionResult> Create(CreateRequest body, CancellationToken ct)
    {
        if (body.SubmissionId == Guid.Empty || !Categories.Contains(body.Category) || body.Body.Trim().Length < 10)
            return BadRequest(new { Message = "Chọn loại phản hồi và nhập nội dung từ 10 đến 2.000 ký tự." });
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == Actor, ct);
        if (actor == null || !actor.IsActive || actor.DeletedAt != null || actor.IsDemoAccount) return Forbid();
        var existing = await db.UserFeedbacks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == Actor && x.SubmissionId == body.SubmissionId, ct);
        if (existing != null) return Ok(new { existing.Id, existing.Status, existing.CreatedAt });
        if (await db.UserFeedbacks.CountAsync(x => x.UserId == Actor && x.CreatedAt >= DateTime.UtcNow.AddHours(-1), ct) >= 10)
            return StatusCode(429, new { Message = "Bạn đã gửi nhiều phản hồi. Vui lòng thử lại sau." });
        var row = new UserFeedback { Id = Guid.NewGuid(), UserId = Actor, SubmissionId = body.SubmissionId, Category = body.Category, Body = body.Body.Trim(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.UserFeedbacks.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) {
            // A retried request may race the original request. The unique key protects against duplicates.
            db.ChangeTracker.Clear();
            existing = await db.UserFeedbacks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == Actor && x.SubmissionId == body.SubmissionId, ct);
            if (existing == null) throw;
            return Ok(new { existing.Id, existing.Status, existing.CreatedAt });
        }
        return Ok(new { row.Id, row.Status, row.CreatedAt });
    }

    [HttpGet("admin/feedback"), Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> List(string? status = null, string? category = null, string? role = null, string? search = null, int page = 1, int pageSize = 20, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || search?.Length > 200 || (!string.IsNullOrEmpty(status) && status != "Open" && !Statuses.Contains(status)) || (!string.IsNullOrEmpty(category) && !Categories.Contains(category)) || (!string.IsNullOrEmpty(role) && role != "Customer" && role != "MUA")) return BadRequest(new { Message = "Bộ lọc phản hồi không hợp lệ." });
        var query = db.UserFeedbacks.AsNoTracking().Where(x => !x.User.IsDemoAccount && x.User.DeletedAt == null);
        if (from.HasValue || to.HasValue) {
            if (!from.HasValue || !to.HasValue || !DashboardDateRange.TryCreate(from.Value, to.Value, out var start, out var end, out _)) return BadRequest(new { Message = "Chọn đủ khoảng ngày hợp lệ, tối đa 366 ngày." });
            query = query.Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        }
        if (status == "Open") query = query.Where(x => x.Status == "New" || x.Status == "InProgress");
        else if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(category)) query = query.Where(x => x.Category == category);
        if (!string.IsNullOrEmpty(role)) { var userRole = Enum.Parse<UserRole>(role); query = query.Where(x => x.User.Role == userRole); }
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Body.ToLower().Contains(term) || (x.User.FullName != null && x.User.FullName.ToLower().Contains(term))); }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Category, x.Body, x.Status, x.CreatedAt, x.UpdatedAt, x.Version, userName = x.User.FullName, role = x.User.Role.ToString() }).ToListAsync(ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("admin/feedback/{id:guid}"), Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var row = await db.UserFeedbacks.AsNoTracking().Where(x => x.Id == id && !x.User.IsDemoAccount && x.User.DeletedAt == null)
            .Select(x => new { x.Id, x.Category, x.Body, x.Status, x.CreatedAt, x.UpdatedAt, x.Version, userName = x.User.FullName, role = x.User.Role.ToString(),
                events = x.Events.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Select(e => new { e.Id, e.Status, e.Note, e.CreatedAt, adminName = e.Admin.FullName }).ToList() }).SingleOrDefaultAsync(ct);
        return row == null ? NotFound() : Ok(row);
    }

    [HttpPost("admin/feedback/{id:guid}/review"), Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Review(Guid id, ReviewRequest body, CancellationToken ct)
    {
        if (!Statuses.Contains(body.Status) || body.Note == null || (body.Status == "Closed" && string.IsNullOrWhiteSpace(body.Note))) return BadRequest(new { Message = "Trạng thái không hợp lệ hoặc thiếu lý do đóng phản hồi." });
        var row = await db.UserFeedbacks.SingleOrDefaultAsync(x => x.Id == id && !x.User.IsDemoAccount && x.User.DeletedAt == null, ct);
        if (row == null) return NotFound();
        if (row.Version != body.Version) return Conflict(new { Message = "Phản hồi đã được admin khác cập nhật. Vui lòng tải lại." });
        row.Status = body.Status; row.UpdatedAt = DateTime.UtcNow; row.Version++;
        db.FeedbackEvents.Add(new FeedbackEvent { Id = Guid.NewGuid(), FeedbackId = row.Id, AdminId = Actor, Status = body.Status, Note = body.Note.Trim(), CreatedAt = row.UpdatedAt });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { Message = "Phản hồi đã thay đổi. Vui lòng tải lại." }); }
        return Ok(new { row.Id, row.Status, row.Version });
    }
}
