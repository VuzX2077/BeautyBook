using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class MakeupCatalogService(ApplicationDbContext db)
{
    public static readonly string[] Kinds = ["occasion", "intensity", "color-tone", "effect", "occasion-group", "effect-group"];
    public static readonly string[] Intensities = ["light", "medium", "bold"];
    public static bool ValidKey(string? key) => key is { Length: > 0 and <= 100 } && Regex.IsMatch(key, "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    public static void Fail(string code, string message, int status = 400) => throw new StyleWriteException(code, message, status);
    public static void CheckVersion(long actual, long? expected) { if (expected.HasValue && actual != expected) Fail("CATALOG_VERSION_CONFLICT", "Danh mục đã thay đổi; hãy tải lại trước khi lưu.", 409); }
    public static CatalogTermDto Map(MakeupCatalogTerm t) => new(t.Id, t.Kind, t.Key, t.Label, t.Description, t.GroupId, t.BroaderTermId, t.SortOrder, t.IsActive, t.ArchivedAt, t.Version);
    public async Task EnsureAdmin(Guid actor)
    {
        if (!await db.Users.AnyAsync(x => x.UserId == actor && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null))
            Fail("CATALOG_ADMIN_REQUIRED", "Chỉ Admin được quản lý danh mục.", 403);
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(actor);
    }
    public void Log(Guid? actor, string action, string subject, object? before, object? after, string? hash = null, Guid? batch = null) =>
        db.MakeupCatalogChanges.Add(new() { ActorId = actor, Action = action, Subject = subject,
            BeforeJson = before == null ? null : JsonSerializer.Serialize(before), AfterJson = after == null ? null : JsonSerializer.Serialize(after), SourceHash = hash, BatchId = batch });

    public async Task<MakeupCatalogDto> Public(CancellationToken ct = default)
    {
        var terms = await db.MakeupCatalogTerms.AsNoTracking().OrderBy(x => x.Kind).ThenBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var byId = terms.ToDictionary(x => x.Id);
        bool Visible(MakeupCatalogTerm t) => t.IsActive && t.ArchivedAt == null &&
            (!t.GroupId.HasValue || byId.TryGetValue(t.GroupId.Value, out var g) && g.IsActive && g.ArchivedAt == null);
        var active = terms.Where(Visible).Select(Map).ToList(); var ids = active.Select(x => x.Id).ToHashSet();
        var styles = await db.MakeupStyles.AsNoTracking().Where(x => x.IsActive && x.ArchivedAt == null).OrderBy(x => x.SortOrder).ThenBy(x => x.StyleId).ToListAsync(ct);
        var links = await db.MakeupStyleTermLinks.AsNoTracking().Include(x => x.Term).ToListAsync(ct);
        var rows = styles.Select(s => new CatalogStyleDto(s.StyleId, s.Key, s.Name, s.Description, s.SortOrder, s.IsActive,
            links.Where(x => x.StyleId == s.StyleId && ids.Contains(x.TermId)).OrderBy(x => x.Term.SortOrder).ThenBy(x => x.TermId).Select(x => Map(x.Term)).ToList())).ToList();
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { rows, active, capabilities = new CatalogCapabilities() }))));
        return new(version, rows, active);
    }
    public async Task<CatalogUsage> StyleUsage(int id, CancellationToken ct = default) => new(
        await db.MUAStyles.Where(x => x.StyleId == id).Select(x => x.MUAId).Distinct().CountAsync(ct),
        await db.ServiceStyles.Where(x => x.StyleId == id).Select(x => x.ServiceId).Distinct().CountAsync(ct), null,
        await db.MakeupStyleTermLinks.CountAsync(x => x.StyleId == id, ct), 0);
    public async Task<CatalogUsage> TermUsage(int id, CancellationToken ct = default)
    {
        var terms = db.MakeupCatalogTerms.Where(x => x.Id == id || x.GroupId == id).Select(x => x.Id);
        var styles = db.MakeupStyleTermLinks.Where(x => terms.Contains(x.TermId)).Select(x => x.StyleId);
        return new(await db.MUAStyles.Where(x => styles.Contains(x.StyleId)).Select(x => x.MUAId).Distinct().CountAsync(ct),
            await db.ServiceStyles.Where(x => styles.Contains(x.StyleId)).Select(x => x.ServiceId).Distinct().CountAsync(ct), null,
            await styles.Distinct().CountAsync(ct), await db.MakeupCatalogTerms.CountAsync(x => x.GroupId == id || x.BroaderTermId == id, ct));
    }

    public async Task<CatalogTermPage> List(string? kind, string? search, string status, int? groupId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue || !new[] { "all", "active", "inactive", "archived" }.Contains(status) || kind != null && !Kinds.Contains(kind))
            Fail("CATALOG_QUERY_INVALID", "Bộ lọc hoặc phân trang không hợp lệ.");
        var query = db.MakeupCatalogTerms.AsNoTracking();
        if (kind != null) query = query.Where(x => x.Kind == kind);
        if (groupId.HasValue) query = query.Where(x => x.GroupId == groupId);
        if (status == "active") query = query.Where(x => x.IsActive && x.ArchivedAt == null);
        if (status == "inactive") query = query.Where(x => !x.IsActive && x.ArchivedAt == null);
        if (status == "archived") query = query.Where(x => x.ArchivedAt != null);
        if (!string.IsNullOrWhiteSpace(search)) { var key = UnifiedStyleService.NameKey(search); query = query.Where(x => x.NormalizedLabel.Contains(key) || x.Key.Contains(key)); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(Map).ToList(), total, page, pageSize);
    }
    public Task<CatalogTermDto> SaveTerm(int? id, CatalogTermWriteRequest r, Guid actor) => new UnifiedStyleService(db).Write(actor, async () => {
        await EnsureAdmin(actor);
        var term = id.HasValue ? await db.MakeupCatalogTerms.SingleOrDefaultAsync(x => x.Id == id) : new MakeupCatalogTerm();
        if (term == null) { Fail("CATALOG_NOT_FOUND", "Không tìm thấy mục danh mục.", 404); return null!; }
        CheckVersion(term.Version, r.ExpectedVersion); var before = id.HasValue ? Map(term) : null;
        if (id.HasValue && (r.Key != null && r.Key != term.Key || r.Kind.Length > 0 && r.Kind != term.Kind)) Fail("CATALOG_KEY_IMMUTABLE", "Không được đổi key hoặc loại danh mục.");
        var kind = id.HasValue ? term.Kind : r.Kind; var key = id.HasValue ? term.Key : r.Key;
        if (!Kinds.Contains(kind) || !ValidKey(key) || kind == "intensity" && !Intensities.Contains(key!)) Fail("CATALOG_KEY_INVALID", "Key hoặc loại danh mục không được hỗ trợ.");
        var label = UnifiedStyleService.ValidateName(r.Label); var normalized = UnifiedStyleService.NameKey(label);
        if (r.Description?.Length > 2000 || r.SortOrder is < 0 or > 100000) Fail("CATALOG_INPUT_INVALID", "Mô tả hoặc thứ tự không hợp lệ.");
        if (await db.MakeupCatalogTerms.AnyAsync(x => x.Id != term.Id && x.Kind == kind && (x.Key == key || x.NormalizedLabel == normalized))) Fail("CATALOG_DUPLICATE", "Key hoặc tên đã tồn tại trong danh mục.", 409);
        await ValidateParents(term, kind, r.GroupId, r.BroaderTermId);
        term.Kind = kind; term.Key = key!; term.Label = label; term.NormalizedLabel = normalized; term.Description = r.Description?.Trim();
        term.GroupId = r.GroupId; term.BroaderTermId = r.BroaderTermId; term.SortOrder = r.SortOrder;
        if (!id.HasValue) db.MakeupCatalogTerms.Add(term);
        await db.SaveChangesAsync(); Log(actor, id.HasValue ? "term-update" : "term-create", $"term:{term.Id}", before, Map(term)); await db.SaveChangesAsync();
        return Map(term);
    });
    private async Task ValidateParents(MakeupCatalogTerm term, string kind, int? groupId, int? broaderId)
    {
        if (groupId.HasValue)
        {
            var group = await db.MakeupCatalogTerms.SingleOrDefaultAsync(x => x.Id == groupId);
            var expected = kind == "occasion" ? "occasion-group" : kind == "effect" ? "effect-group" : "";
            if (group == null || group.Kind != expected || group.Id == term.Id || (!group.IsActive || group.ArchivedAt != null) && groupId != term.GroupId) Fail("CATALOG_GROUP_INVALID", "Nhóm không hợp lệ hoặc đã ngừng dùng.");
        }
        if (kind is "occasion" or "effect" && !groupId.HasValue) Fail("CATALOG_GROUP_REQUIRED", "Vui lòng chọn nhóm.");
        if (broaderId.HasValue)
        {
            if (kind != "occasion") Fail("CATALOG_BROADER_INVALID", "Chỉ dịp có phân cấp mở rộng.");
            var all = await db.MakeupCatalogTerms.Where(x => x.Kind == "occasion").ToDictionaryAsync(x => x.Id);
            var seen = new HashSet<int>(); var current = broaderId;
            while (current.HasValue)
            {
                if (current == term.Id || !seen.Add(current.Value) || !all.TryGetValue(current.Value, out var parent)) { Fail("CATALOG_CYCLE", "Dịp cha không hợp lệ hoặc tạo vòng lặp."); return; }
                if (current == broaderId && (!parent.IsActive || parent.ArchivedAt != null) && broaderId != term.BroaderTermId) Fail("CATALOG_BROADER_INVALID", "Dịp cha đã ngừng dùng.");
                current = parent.BroaderTermId;
            }
        }
    }
    public Task<CatalogTermDto> SetTermStatus(int id, CatalogLifecycleRequest r, Guid actor, bool archive = false) => new UnifiedStyleService(db).Write(actor, async () => {
        await EnsureAdmin(actor); var term = await db.MakeupCatalogTerms.SingleOrDefaultAsync(x => x.Id == id);
        if (term == null) { Fail("CATALOG_NOT_FOUND", "Không tìm thấy mục danh mục.", 404); return null!; }
        CheckVersion(term.Version, r.ExpectedVersion);
        if (!archive && !r.IsActive.HasValue) Fail("CATALOG_STATUS_REQUIRED", "Chọn trạng thái.");
        if (!archive && r.IsActive == true && term.ArchivedAt != null) Fail("CATALOG_ARCHIVED", "Mục đã lưu trữ không thể được kích hoạt lại bằng đổi trạng thái.", 409);
        var before = Map(term); term.IsActive = archive ? false : r.IsActive!.Value; if (archive) term.ArchivedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(); Log(actor, archive ? "term-archive" : "term-status", $"term:{id}", before, new { Item = Map(term), r.Reason }); await db.SaveChangesAsync(); return Map(term);
    });
    public async Task ReplaceMetadata(MakeupStyle style, IReadOnlyList<int> ids)
    {
        if (ids.Count > 100 || ids.Distinct().Count() != ids.Count) Fail("CATALOG_METADATA_INVALID", "Metadata trùng hoặc quá dài.");
        var links = await db.MakeupStyleTermLinks.Where(x => x.StyleId == style.StyleId).ToListAsync();
        var terms = await db.MakeupCatalogTerms.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (terms.Count != ids.Count || terms.Any(x => x.Kind.EndsWith("-group")) || terms.Count(x => x.Kind == "intensity") > 1) Fail("CATALOG_METADATA_INVALID", "Metadata không hợp lệ.");
        var groups = await db.MakeupCatalogTerms.Where(x => terms.Select(t => t.GroupId).Contains(x.Id)).ToListAsync();
        foreach (var term in terms.Where(x => !links.Any(l => l.TermId == x.Id)))
            if (!term.IsActive || term.ArchivedAt != null || term.GroupId.HasValue && !groups.Any(g => g.Id == term.GroupId && g.IsActive && g.ArchivedAt == null)) Fail("CATALOG_ITEM_INACTIVE", "Không được thêm metadata đã ngừng dùng.", 409);
        db.MakeupStyleTermLinks.RemoveRange(links.Where(x => !ids.Contains(x.TermId)));
        foreach (var term in terms.Where(x => !links.Any(l => l.TermId == x.Id))) db.MakeupStyleTermLinks.Add(new() { StyleId = style.StyleId, TermId = term.Id, Kind = term.Kind });
    }
}
