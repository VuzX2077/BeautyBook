using System.Text;
using System.Text.RegularExpressions;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class MakeupStyleConflictException() : Exception("Tên phong cách đã tồn tại. Hãy chọn tên khác.");
public sealed class AdminMakeupStyleService(ApplicationDbContext db)
{
    public static string NormalizeName(string? name) => Regex.Replace((name ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
    public static string? Validate(AdminMakeupStyleWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Nhập tên phong cách.";
        if (request.Name.Length > 100 || NormalizeName(request.Name).Length > 100) return "Tên phong cách tối đa 100 ký tự.";
        if (request.Description?.Length > 255) return "Mô tả tối đa 255 ký tự.";
        return null;
    }
    private static AdminMakeupStyleDto Map(MakeupStyle style) => new(style.StyleId, style.Name, style.Description, style.IsActive, style.CreatedAt);
    public async Task<AdminMakeupStylePage> List(int page, int pageSize, string? search, string status, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue || !new[] { "all", "active", "inactive" }.Contains(status))
            throw new ArgumentException("Trang, số dòng hoặc trạng thái không hợp lệ.");
        var query = db.MakeupStyles.AsNoTracking();
        if (status != "all") query = query.Where(x => x.IsActive == (status == "active"));
        var term = search?.Trim().ToLower();
        if (!string.IsNullOrEmpty(term)) query = query.Where(x => x.Name != null && x.Name.ToLower().Contains(term));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.StyleId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AdminMakeupStyleDto(x.StyleId, x.Name, x.Description, x.IsActive, x.CreatedAt)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }
    public async Task<AdminMakeupStyleDto?> Detail(int id, CancellationToken ct = default)
    {
        var style = await db.MakeupStyles.AsNoTracking().SingleOrDefaultAsync(x => x.StyleId == id, ct);
        return style == null ? null : Map(style);
    }
    public async Task<AdminMakeupStyleDto?> Save(int? id, AdminMakeupStyleWriteRequest request, Guid actor, CancellationToken ct = default)
    {
        if (Validate(request) is { } error) throw new ArgumentException(error);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(actor);
        // Read after the shared lock, so concurrent create/update sees the committed name.
        var style = id.HasValue ? await db.MakeupStyles.SingleOrDefaultAsync(x => x.StyleId == id, ct) : new MakeupStyle();
        if (style == null) return null;
        var name = NormalizeName(request.Name);
        var folded = name.ToLower();
        if (await db.MakeupStyles.AnyAsync(x => x.StyleId != (id ?? 0) && x.Name != null && x.Name.ToLower() == folded, ct))
            throw new MakeupStyleConflictException();
        style.Name = name;
        style.Description = request.Description?.Trim();
        if (!id.HasValue) db.MakeupStyles.Add(style);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(style);
    }
    public async Task<AdminMakeupStyleDto?> SetStatus(int id, bool active, Guid actor, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(actor);
        var style = await db.MakeupStyles.SingleOrDefaultAsync(x => x.StyleId == id, ct);
        if (style == null) return null;
        style.IsActive = active; // Preserve all MUAStyle links; never delete catalog records.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(style);
    }
    private Task Lock(CancellationToken ct) => db.Database.IsNpgsql()
        // Same lock as legacy MuaService.CreateStyleAsync, including select-or-create.
        ? db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('makeup-style:create', 0))", ct)
        : Task.CompletedTask;
}
