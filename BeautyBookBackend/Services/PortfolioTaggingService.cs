using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Shares catalog-before-MUA lock/retry ordering with catalog writers. Never updates specialties.
public sealed class PortfolioTaggingService(ApplicationDbContext db)
{
    private static void Fail(string code, string message, int status = 400) => throw new StyleWriteException(code, message, status);

    public Task<bool> Save(Guid owner, Guid? id, PortfolioCreateRequest request) => new UnifiedStyleService(db).Write(owner, async () => {
        await using var moderation = await ModerationWriteScope.Start(db, owner);
        await EnsureOwner(owner);
        Portfolio p;
        if (id.HasValue)
        {
            p = (await FindLocked(owner, id.Value))!;
            if (p == null) return false;
            await new ModerationService(db).EnsureContent("Portfolio", p.PortfolioId);
            if (request.Tagging?.HasChanges == true && !request.ExpectedVersion.HasValue)
                Fail("PORTFOLIO_VERSION_REQUIRED", "Cần version hiện tại để chỉnh tag.");
            if (request.ExpectedVersion.HasValue && request.ExpectedVersion.Value != p.Version)
                Fail("PORTFOLIO_VERSION_CONFLICT", "Tác phẩm đã thay đổi; hãy tải lại trước khi lưu.", 409);
        }
        else p = new() { PortfolioId = Guid.NewGuid(), MUAId = owner, CreatedAt = DateTime.UtcNow };

        ValidateContent(request);
        if (request.Has(nameof(request.ServiceId)) && request.ServiceId.HasValue &&
            !await db.Services.AnyAsync(s => s.ServiceId == request.ServiceId && s.MUAId == owner))
            Fail("PORTFOLIO_SERVICE_INVALID", "Dịch vụ phải thuộc cùng MUA.");

        // Validate every group before touching any persisted content or link.
        (List<PortfolioStyleLink> Styles, List<PortfolioTermLink> Terms)? replacement =
            request.Tagging?.HasChanges == true ? await Resolve(p, request.Tagging) : null;
        if (replacement != null && id.HasValue)
        {
            db.PortfolioStyleLinks.RemoveRange(p.StyleLinks);
            db.PortfolioTermLinks.RemoveRange(p.TermLinks);
            await db.SaveChangesAsync(); // release unique slots; still inside the encompassing transaction
            p.StyleLinks.Clear(); p.TermLinks.Clear();
        }
        if (request.Has(nameof(request.Title))) p.Title = request.Title;
        if (request.Has(nameof(request.Description))) p.Description = request.Description;
        if (request.Has(nameof(request.ImageUrls))) p.ImageUrls = request.ImageUrls!.ToList();
        if (request.Has(nameof(request.Tags))) p.Tags = request.Tags!.ToList();
        if (request.Has(nameof(request.ServiceId))) p.ServiceId = request.ServiceId;
        if (replacement != null)
        {
            p.StyleLinks = replacement.Value.Styles;
            p.TermLinks = replacement.Value.Terms;
        }
        if (!id.HasValue) db.Portfolios.Add(p);
        else db.Entry(p).Property(x => x.Version).IsModified = true;
        await db.SaveChangesAsync();
        return true;
    });

    public Task<bool> ChangeState(Guid owner, Guid id, Action<Portfolio> change, bool requireContent = true) =>
        new UnifiedStyleService(db).Write(owner, async () => {
            await using var moderation = await ModerationWriteScope.Start(db, owner);
            await EnsureOwner(owner);
            var p = await FindLocked(owner, id);
            if (p == null) return false;
            if (requireContent) await new ModerationService(db).EnsureContent("Portfolio", id);
            change(p);
            await db.SaveChangesAsync();
            return true;
        });

    private async Task EnsureOwner(Guid owner)
    {
        await new UnifiedStyleService(db).EnsureWritable(owner);
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(owner);
    }

    private async Task<Portfolio?> FindLocked(Guid owner, Guid id)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Portfolios\" WHERE \"PortfolioId\"={id} AND \"MUAId\"={owner} FOR UPDATE");
        // A scoped context can already track the entry. Reload scalar state before checking version.
        var tracked = db.ChangeTracker.Entries<Portfolio>().SingleOrDefault(x => x.Entity.PortfolioId == id);
        if (tracked != null) await tracked.ReloadAsync();
        return await db.Portfolios.WithTagging().SingleOrDefaultAsync(x => x.PortfolioId == id && x.MUAId == owner);
    }

    private static void ValidateContent(PortfolioCreateRequest r)
    {
        if (r.Has(nameof(r.ImageUrls)) && (r.ImageUrls == null || r.ImageUrls.Any(string.IsNullOrWhiteSpace)))
            Fail("PORTFOLIO_IMAGES_INVALID", "Danh sách ảnh không hợp lệ.");
        if (r.Has(nameof(r.Tags)) && (r.Tags == null || r.Tags.Any(x => x == null)))
            Fail("PORTFOLIO_LEGACY_TAGS_INVALID", "Tags phải là danh sách; dùng [] để xóa.");
        if (r.Description?.Length > 2000) Fail("PORTFOLIO_DESCRIPTION_INVALID", "Mô tả tối đa 2000 ký tự.");
        if (r.ExpectedVersion is <= 0) Fail("PORTFOLIO_VERSION_INVALID", "Version không hợp lệ.");
    }

    private async Task<(List<PortfolioStyleLink> Styles, List<PortfolioTermLink> Terms)> Resolve(Portfolio p, PortfolioTaggingRequest r)
    {
        var primary = r.Has(nameof(r.PrimaryStyleId)) ? r.PrimaryStyleId : p.StyleLinks.SingleOrDefault(x => x.Slot == 0)?.StyleId;
        var secondary = r.Has(nameof(r.SecondaryStyleIds)) ? List(r.SecondaryStyleIds, 2) : p.StyleLinks.Where(x => x.Slot > 0).OrderBy(x => x.Slot).Select(x => x.StyleId).ToList();
        if (secondary.Count > 0 && primary == null) Fail("PORTFOLIO_PRIMARY_REQUIRED", "Chọn phong cách chính trước phong cách phụ.");
        if (primary.HasValue && secondary.Contains(primary.Value)) Fail("PORTFOLIO_TAG_DUPLICATE", "Phong cách chính không được trùng phong cách phụ.");
        var selectedStyles = new List<(int Id, short Slot)>();
        if (primary.HasValue) selectedStyles.Add((primary.Value, 0));
        selectedStyles.AddRange(secondary.Select((value, index) => (value, (short)(index + 1))));
        var styleIds = selectedStyles.Select(x => x.Id).ToList();
        var styles = await db.MakeupStyles.Where(x => styleIds.Contains(x.StyleId)).ToDictionaryAsync(x => x.StyleId);
        foreach (var selected in selectedStyles)
        {
            if (!styles.TryGetValue(selected.Id, out var style)) Fail("PORTFOLIO_STYLE_INVALID", "Phong cách không tồn tại.");
            if ((!style!.IsActive || style.ArchivedAt != null) &&
                !p.StyleLinks.Any(x => x.StyleId == selected.Id && (x.Slot == 0) == (selected.Slot == 0)))
                Fail("PORTFOLIO_TAG_INACTIVE", "Không được thêm hoặc đổi vai trò phong cách ngừng dùng.");
        }

        List<string> Keys(string kind) => p.TermLinks.Where(x => x.Kind == kind).OrderBy(x => x.Slot).Select(x => x.Term.Key).ToList();
        var groups = new Dictionary<string, List<string>> {
            ["occasion"] = r.Has(nameof(r.OccasionKeys)) ? List(r.OccasionKeys, 3) : Keys("occasion"),
            ["color-tone"] = r.Has(nameof(r.ColorToneKeys)) ? List(r.ColorToneKeys, 3) : Keys("color-tone"),
            ["effect"] = r.Has(nameof(r.EffectKeys)) ? List(r.EffectKeys, 4) : Keys("effect"),
            ["intensity"] = r.Has(nameof(r.IntensityKey)) ? (r.IntensityKey == null ? [] : [r.IntensityKey]) : Keys("intensity")
        };
        if (groups["intensity"].Any(x => !MakeupCatalogService.Intensities.Contains(x)))
            Fail("PORTFOLIO_INTENSITY_INVALID", "Mức độ chỉ có light/medium/bold.");
        var keys = groups.Values.SelectMany(x => x).Distinct().ToList();
        var terms = await db.MakeupCatalogTerms.Include(x => x.Group).Where(x => keys.Contains(x.Key)).ToListAsync();
        var termLinks = new List<PortfolioTermLink>();
        foreach (var (kind, values) in groups)
        {
            for (short slot = 0; slot < values.Count; slot++)
            {
                var term = terms.SingleOrDefault(x => x.Kind == kind && x.Key == values[slot]);
                if (term == null) Fail("PORTFOLIO_TERM_INVALID", "Tag không tồn tại hoặc sai nhóm.");
                if (!PortfolioTaggingProjection.Selectable(term!) && !p.TermLinks.Any(x => x.TermId == term!.Id && x.Kind == kind))
                    Fail("PORTFOLIO_TAG_INACTIVE", "Không được thêm tag ngừng dùng.");
                termLinks.Add(new() { PortfolioId = p.PortfolioId, TermId = term!.Id, Kind = kind, Slot = slot, Term = term });
            }
        }
        return (selectedStyles.Select(x => new PortfolioStyleLink { PortfolioId = p.PortfolioId, StyleId = x.Id, Slot = x.Slot, Style = styles[x.Id] }).ToList(), termLinks);
    }

    private static List<T> List<T>(List<T>? values, int max)
    {
        if (values == null) Fail("PORTFOLIO_TAG_LIST_NULL", "Dùng [] để xóa nhóm; danh sách không nhận null.");
        if (values!.Count > max) Fail("PORTFOLIO_TAG_LIMIT", $"Nhóm này tối đa {max} lựa chọn.");
        if (values.Distinct().Count() != values.Count) Fail("PORTFOLIO_TAG_DUPLICATE", "Không được chọn tag trùng.");
        return values;
    }
}
