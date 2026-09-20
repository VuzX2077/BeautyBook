using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public class AdminNotificationService : IAdminNotificationService
{
    private static readonly HashSet<string> Audiences = new(StringComparer.OrdinalIgnoreCase) { "All", "Customer", "MUA", "SelectedUsers" };
    private readonly ApplicationDbContext _db;
    public AdminNotificationService(ApplicationDbContext db) => _db = db;

    public async Task<AdminNotificationCampaignDto> CreateAsync(Guid adminId, CreateAdminNotificationRequest request)
    {
        request.Title = request.Title.Trim(); request.Body = request.Body.Trim(); request.Audience = request.Audience.Trim();
        if (request.Title.Length == 0 || request.Body.Length == 0) throw new ArgumentException("Tiêu đề và nội dung là bắt buộc.");
        if (request.IdempotencyKey == Guid.Empty) throw new ArgumentException("IdempotencyKey là bắt buộc.");
        if (!Audiences.Contains(request.Audience)) throw new ArgumentException("Đối tượng nhận không hợp lệ.");
        if (request.UserIds.Distinct().Count() > 100) throw new ArgumentException("Mỗi lần chỉ được chọn tối đa 100 người dùng.");
        if (!string.IsNullOrWhiteSpace(request.Url) && (!request.Url.StartsWith('/') || request.Url.StartsWith("//")))
            throw new ArgumentException("Đường dẫn thông báo phải là đường dẫn nội bộ.");

        var existing = await _db.NotificationCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing != null) return ToDto(existing);

        var users = _db.Users.AsNoTracking().Where(x => x.IsActive && x.DeletedAt == null && x.Role != UserRole.Admin);
        users = request.Audience.ToUpperInvariant() switch
        {
            "CUSTOMER" => users.Where(x => x.Role == UserRole.Customer),
            "MUA" => users.Where(x => x.Role == UserRole.MUA),
            "SELECTEDUSERS" => users.Where(x => request.UserIds.Distinct().Take(100).Contains(x.UserId)),
            _ => users
        };
        var recipientIds = await users.Select(x => x.UserId).ToListAsync();
        if (request.Audience.Equals("SelectedUsers", StringComparison.OrdinalIgnoreCase) && recipientIds.Count == 0)
            throw new ArgumentException("Hãy chọn ít nhất một người dùng đang hoạt động.");

        var now = DateTime.UtcNow;
        var campaign = new NotificationCampaign { Id = Guid.NewGuid(), Title = request.Title, Body = request.Body,
            Audience = request.Audience, Url = request.Url, CreatedByAdminId = adminId, RecipientCount = recipientIds.Count,
            Status = "Completed", IdempotencyKey = request.IdempotencyKey, CreatedAt = now };
        var data = string.IsNullOrWhiteSpace(request.Url) ? null : JsonSerializer.Serialize(new { url = request.Url });
        await using var transaction = await _db.Database.BeginTransactionAsync();
        _db.NotificationCampaigns.Add(campaign);
        _db.AppNotifications.AddRange(recipientIds.Select(userId => new AppNotification { Id = Guid.NewGuid(), UserId = userId,
            CampaignId = campaign.Id, Type = "ADMIN_ANNOUNCEMENT", Title = request.Title, Body = request.Body, DataJson = data,
            ScheduledAt = now, Status = "Pending", CreatedAt = now }));
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(campaign);
    }

    public async Task<PagedResultDto<AdminNotificationCampaignDto>> GetCampaignsAsync(int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = _db.NotificationCampaigns.AsNoTracking().OrderByDescending(x => x.CreatedAt);
        var total = await query.CountAsync();
        var entities = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = entities.Select(ToDto).ToList();
        return new(items, total, page, pageSize);
    }

    public async Task<PagedResultDto<AdminNotificationUserDto>> SearchUsersAsync(string? search, string? role, int page, int pageSize)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = _db.Users.AsNoTracking().Where(x => x.IsActive && x.DeletedAt == null && x.Role != UserRole.Admin);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => (x.FullName ?? "").ToLower().Contains(term) || (x.Email ?? "").ToLower().Contains(term) || (x.PhoneNumber ?? "").Contains(term)); }
        if (Enum.TryParse<UserRole>(role, true, out var parsedRole) && parsedRole != UserRole.Admin) query = query.Where(x => x.Role == parsedRole);
        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminNotificationUserDto(x.UserId, x.FullName ?? "Người dùng", x.Email ?? "", x.Role.ToString(), x.AvatarUrl)).ToListAsync();
        return new(items, total, page, pageSize);
    }

    private static AdminNotificationCampaignDto ToDto(NotificationCampaign x) => new(x.Id, x.Title, x.Body, x.Audience, x.Url, x.RecipientCount, x.Status, x.CreatedAt);
}
