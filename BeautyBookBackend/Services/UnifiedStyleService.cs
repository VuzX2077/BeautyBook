using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BeautyBookBackend.Services;

public sealed record StyleRelatedService(Guid ServiceId, string? ServiceName);
public sealed class StyleWriteException(string code, string message, int status = 400,
    IReadOnlyList<int>? styleIds = null, IReadOnlyList<StyleRelatedService>? services = null) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyList<int> StyleIds { get; } = styleIds ?? [];
    public IReadOnlyList<StyleRelatedService> Services { get; } = services ?? [];
}

// Shared by onboarding, profile, services and legacy select-or-create.
// The catalog lock is always acquired BEFORE the MUA lock, including retries.
public sealed class UnifiedStyleService(ApplicationDbContext db)
{
    public const int MuaLimit = 20;
    public const int ServiceLimit = 5;
    private static readonly PartitionedRateLimiter<Guid> CreationBudget = PartitionedRateLimiter.Create<Guid, Guid>(
        actor => RateLimitPartition.GetFixedWindowLimiter(actor, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromHours(1), AutoReplenishment = true, QueueLimit = 0 }));

    public static string DisplayName(string? value)
    {
        try { return Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " "); }
        catch (ArgumentException) { throw new StyleWriteException("STYLE_NAME_INVALID", "Tên phong cách không hợp lệ."); }
    }
    public static string NameKey(string? value) => DisplayName(value).ToLowerInvariant();
    public static string ValidateName(string? value)
    {
        var name = DisplayName(value);
        if (name.Length is 0 or > 100 || name.Any(char.IsControl))
            throw new StyleWriteException("STYLE_NAME_INVALID", "Tên phong cách phải từ 1 đến 100 ký tự.");
        return name;
    }

    public async Task<T> Write<T>(Guid actor, Func<Task<T>> action, Func<T, bool>? commitWhen = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await LockCatalog();
                if (db.Database.IsNpgsql())
                {
                    var key = $"mua-onboarding:{actor:N}";
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
                }
                var result = await action();
                if (commitWhen != null && !commitWhen(result))
                {
                    await tx.RollbackAsync(); db.ChangeTracker.Clear(); return result;
                }
                await tx.CommitAsync(); return result;
            }
            catch (Exception error)
            {
                await tx.RollbackAsync(); db.ChangeTracker.Clear();
                var pg = error as PostgresException ?? error.InnerException as PostgresException;
                var retry = pg?.SqlState is "40001" or "40P01" ||
                    (pg?.SqlState == "23505" && pg.ConstraintName == "IX_MakeupStyles_NormalizedName");
                if (!retry || attempt >= 2) throw;
            }
        }
    }
    public Task LockCatalog() => db.Database.IsNpgsql()
        ? db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('makeup-style:create', 0))")
        : Task.CompletedTask;

    public async Task EnsureWritable(Guid actor, bool requireProfile = true)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor);
        if (user == null || !user.IsActive || user.DeletedAt != null)
            throw new StyleWriteException("MUA_ACCOUNT_UNAVAILABLE", "Tài khoản không được phép cập nhật.", 403);
        if (requireProfile)
        {
            var profile = await db.MakeupArtistProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.MUAId == actor);
            if (profile == null || profile.Status == MuaStatus.Suspended)
                throw new StyleWriteException("MUA_ACCOUNT_UNAVAILABLE", "Hồ sơ không được phép cập nhật.", 403);
        }
    }

    public async Task<MakeupStyle?> FindName(string name, int? exceptId = null)
    {
        var key = NameKey(name);
        // Legacy null keys are deliberately not guessed/backfilled by the schema migration.
        var candidates = await db.MakeupStyles.Where(x => (!exceptId.HasValue || x.StyleId != exceptId.Value) && (x.NormalizedName == key || x.NormalizedName == null)).ToListAsync();
        var matches = candidates.Where(x => NameKey(x.Name) == key).ToList();
        if (matches.Count > 1) throw new StyleWriteException("STYLE_NAME_AMBIGUOUS", "Tên phong cách trùng trong dữ liệu cũ; cần xử lý danh mục.", 409);
        return matches.SingleOrDefault();
    }

    public async Task<MakeupStyle> ResolveNewName(Guid actor, string? rawName, string? description = null)
    {
        var name = ValidateName(rawName);
        // Tracked rows are not the complete catalog: using Local.FirstOrDefault
        // would hide ambiguous legacy names loaded earlier in this request.
        var existing = await FindName(name);
        if (existing != null)
        {
            if (!existing.IsActive) throw new StyleWriteException("STYLE_INACTIVE", "Phong cách đã bị vô hiệu hóa.", 409, [existing.StyleId]);
            return existing;
        }
        using var lease = CreationBudget.AttemptAcquire(actor);
        if (!lease.IsAcquired) throw new StyleWriteException("STYLE_CREATE_RATE_LIMITED", "Bạn đã tạo nhiều phong cách mới; vui lòng thử lại sau.", 429);
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(actor);
        var style = new MakeupStyle { Name = name, NormalizedName = NameKey(name), Description = description?.Trim(), IsActive = true };
        db.MakeupStyles.Add(style);
        // Obtain the generated ID within the enclosing transaction, never a separate commit.
        await db.SaveChangesAsync();
        return style;
    }

    private async Task<List<MakeupStyle>> Resolve(Guid actor, List<int>? ids, List<string>? names, ISet<int> retained)
    {
        if ((ids?.Count ?? 0) > 1000 || (names?.Count ?? 0) > MuaLimit)
            throw new StyleWriteException("STYLE_PAYLOAD_INVALID", "Danh sách phong cách quá dài.");
        var wanted = (ids ?? []).Distinct().ToList();
        var styles = await db.MakeupStyles.Where(x => wanted.Contains(x.StyleId)).ToListAsync();
        if (wanted.Any(x => x <= 0) || styles.Count != wanted.Count)
            throw new StyleWriteException("STYLE_NOT_FOUND", "Phong cách không tồn tại.");
        foreach (var style in styles.Where(x => !x.IsActive && !retained.Contains(x.StyleId)))
            throw new StyleWriteException("STYLE_INACTIVE", "Không thể thêm phong cách đã bị vô hiệu hóa.", 409, [style.StyleId]);
        foreach (var name in names ?? [])
        {
            var style = await ResolveNewName(actor, name);
            if (!styles.Any(x => x.StyleId == style.StyleId)) styles.Add(style);
        }
        return styles;
    }

    public async Task ReplaceSpecialties(Guid actor, List<int>? ids, List<string>? names)
    {
        if (ids == null && names == null) return;
        var links = await db.MUAStyles.Where(x => x.MUAId == actor).ToListAsync();
        var current = links.Select(x => x.StyleId).ToHashSet();
        // Names alone add to the current profile; explicit IDs define a replacement.
        var styles = await Resolve(actor, ids ?? current.ToList(), names, current);
        var wanted = styles.Select(x => x.StyleId).ToHashSet();
        CheckLimit(current, wanted, MuaLimit, "MUA_STYLE_LIMIT_EXCEEDED");
        var removed = current.Except(wanted).ToList();
        if (removed.Count > 0)
        {
            var related = await db.ServiceStyles.Where(x => x.Service.MUAId == actor && removed.Contains(x.StyleId))
                .Select(x => new StyleRelatedService(x.ServiceId, x.Service.ServiceName)).Distinct().ToListAsync();
            if (related.Count > 0) throw new StyleWriteException("STYLE_IN_USE", "Bỏ phong cách khỏi dịch vụ liên quan trước khi bỏ chuyên môn.", 409, removed, related);
        }
        if (wanted.Count == 0) throw new StyleWriteException("MUA_STYLE_REQUIRED", "Vui lòng chọn ít nhất một chuyên môn.");
        db.MUAStyles.RemoveRange(links.Where(x => !wanted.Contains(x.StyleId)));
        foreach (var id in wanted.Except(current)) db.MUAStyles.Add(new MUAStyle { MUAId = actor, StyleId = id });
    }

    public static void CheckLimit(ISet<int> current, ISet<int> wanted, int limit, string code)
    {
        // Grandfather legacy overflow only when retaining/reducing the existing set.
        if (wanted.Count > limit && !(current.Count > limit && wanted.IsSubsetOf(current)))
            throw new StyleWriteException(code, $"Chỉ được chọn tối đa {limit} phong cách.");
    }

    public async Task ApplyService(Guid actor, Models.Service service, ServiceCreateDto request)
    {
        if (request.StyleIds == null && request.NewStyleNames == null && request.Tags == null) return;
        // Old full-form clients resend unchanged text while editing price/name.
        // Do not turn an unrelated edit into an unreviewed backfill or overflow error.
        if (db.Entry(service).State != EntityState.Added && request.StyleIds == null && request.NewStyleNames == null &&
            request.Tags?.Count > 0 && request.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(service.Tags)) return;
        await db.Entry(service).Collection(x => x.Styles).Query().Include(x => x.MakeupStyle).LoadAsync();
        var current = service.Styles.Select(x => x.StyleId).ToHashSet();
        List<MakeupStyle> styles;
        var unknown = new List<string>();
        if (request.StyleIds != null || request.NewStyleNames != null)
        {
            styles = await Resolve(actor, request.StyleIds ?? current.ToList(), request.NewStyleNames, current);
            // Preserve unresolved legacy text until a separately reviewed mapping exists.
            foreach (var raw in service.Tags)
                if (await FindName(raw) == null) unknown.Add(raw);
            if (request.Tags != null)
            {
                var legacyIds = new HashSet<int>();
                foreach (var raw in request.Tags)
                {
                    var match = await FindName(ValidateName(raw));
                    if (match == null || !styles.Any(x => x.StyleId == match.StyleId))
                        throw new StyleWriteException("STYLE_PAYLOAD_CONFLICT", "tags và styleIds không khớp.", 409);
                    legacyIds.Add(match.StyleId);
                }
                if (!legacyIds.SetEquals(styles.Select(x => x.StyleId)))
                    throw new StyleWriteException("STYLE_PAYLOAD_CONFLICT", "tags và styleIds không khớp.", 409);
            }
        }
        else
        {
            styles = [];
            var historicalTagKeys = db.Entry(service).State == EntityState.Added
                ? new HashSet<string>() : service.Tags.Select(NameKey).ToHashSet();
            if (request.Tags!.Count > 1000) throw new StyleWriteException("STYLE_PAYLOAD_INVALID", "Danh sách tag quá dài.");
            foreach (var raw in request.Tags)
            {
                var name = ValidateName(raw);
                var style = await FindName(name);
                if (style == null) { unknown.Add(raw); continue; }
                if (!style.IsActive && !current.Contains(style.StyleId))
                {
                    // Retain pre-backfill inactive text without adding an inactive ID link.
                    if (historicalTagKeys.Contains(NameKey(raw))) { unknown.Add(raw); continue; }
                    throw new StyleWriteException("STYLE_INACTIVE", "Không thể thêm phong cách đã bị vô hiệu hóa.", 409, [style.StyleId]);
                }
                if (!styles.Any(x => x.StyleId == style.StyleId)) styles.Add(style);
            }
            // An old client sending stale names must not silently delete ID links.
            var missing = service.Styles.Where(x => !styles.Any(s => s.StyleId == x.StyleId)).ToList();
            if (unknown.Any(raw => !historicalTagKeys.Contains(NameKey(raw))) && missing.Count > 0)
                throw new StyleWriteException("STYLE_PAYLOAD_CONFLICT", "Tên tag cũ không còn khớp; vui lòng tải lại dịch vụ.", 409);
        }
        var wanted = styles.Select(x => x.StyleId).ToHashSet();
        CheckLimit(current, wanted, ServiceLimit, "SERVICE_STYLE_LIMIT_EXCEEDED");
        var muaLinks = await db.MUAStyles.Where(x => x.MUAId == actor).ToListAsync();
        var muaCurrent = muaLinks.Select(x => x.StyleId).ToHashSet();
        var union = muaCurrent.Union(wanted).ToHashSet();
        CheckLimit(muaCurrent, union, MuaLimit, "MUA_STYLE_LIMIT_EXCEEDED");
        foreach (var style in styles.Where(x => !muaCurrent.Contains(x.StyleId)))
        {
            if (!style.IsActive) throw new StyleWriteException("STYLE_INACTIVE", "Không thể thêm chuyên môn đã bị vô hiệu hóa.", 409, [style.StyleId]);
            db.MUAStyles.Add(new MUAStyle { MUAId = actor, StyleId = style.StyleId });
        }
        var removed = service.Styles.Where(x => !wanted.Contains(x.StyleId)).ToList();
        db.ServiceStyles.RemoveRange(removed);
        foreach (var link in removed) service.Styles.Remove(link);
        foreach (var style in styles.Where(x => !current.Contains(x.StyleId)))
            service.Styles.Add(new ServiceStyle { ServiceId = service.ServiceId, Service = service, StyleId = style.StyleId, MakeupStyle = style });
        service.Tags = styles.Select(x => x.Name!).Concat(unknown).Distinct().ToList();
    }

    public static string RequestHash(ServiceCreateDto request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        request.ServiceName, request.Description, request.Price, request.DurationMinutes, request.ImageUrl,
        request.ImageUrls, request.IsActive, Tags = request.Tags?.OrderBy(x => x).ToArray(),
        StyleIds = request.StyleIds?.Distinct().OrderBy(x => x).ToArray(),
        NewStyleNames = request.NewStyleNames?.Select(NameKey).Distinct().OrderBy(x => x).ToArray()
    }))));

    public static List<ServiceStyleTagDto> StyleTags(Models.Service service) => service.Styles
        .OrderBy(x => x.StyleId).Select(x => new ServiceStyleTagDto(x.StyleId, x.MakeupStyle.Name, x.MakeupStyle.IsActive)).ToList();
    public static List<string> LegacyTags(Models.Service service)
    {
        var keys = service.Styles.Select(x => NameKey(x.MakeupStyle.Name)).ToHashSet();
        return service.Styles.OrderBy(x => x.StyleId).Select(x => x.MakeupStyle.Name!).Concat(service.Tags.Where(x => !keys.Contains(NameKey(x)))).Distinct().ToList();
    }
}
