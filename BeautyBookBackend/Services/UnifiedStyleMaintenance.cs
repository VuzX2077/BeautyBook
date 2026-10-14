using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed record LegacyStyleMapping(Guid ServiceId, string LegacyTag, int StyleId);
public sealed record ReviewedStyleName(int StyleId, string ExpectedName);
public sealed record LegacyTagAudit(Guid ServiceId, Guid MuaId, string Tag, IReadOnlyList<int> ExactCandidateIds);
public sealed record StyleNameCollision(string Key, IReadOnlyList<int> StyleIds);
public sealed record StyleOverflow(Guid Id, int Count);
public sealed record StyleCatalogAudit(int StyleId, string? Name, bool IsActive, string NormalizedKey);
public sealed record StyleAuditReport(IReadOnlyList<LegacyTagAudit> Tags, IReadOnlyList<StyleNameCollision> Collisions,
    IReadOnlyList<int> InvalidNames, IReadOnlyList<StyleOverflow> MuaOverflow, IReadOnlyList<StyleOverflow> ServiceOverflow,
    IReadOnlyList<LegacyStyleMapping> MissingSpecialties, IReadOnlyList<StyleCatalogAudit> Catalog);

// Explicit operator utility, never invoked by startup or a schema migration.
public sealed class UnifiedStyleMaintenance(ApplicationDbContext db)
{
    private sealed record StyleLinkAudit(Guid ServiceId, int StyleId, Guid MuaId);
    public async Task<StyleAuditReport> Audit()
    {
        // Explicit projections keep this report usable BEFORE the expansion migration.
        var catalog = await db.MakeupStyles.AsNoTracking().Select(x => new { x.StyleId, x.Name, x.IsActive }).ToListAsync();
        var services = await db.Services.AsNoTracking().Select(x => new { x.ServiceId, x.MUAId, x.Tags }).ToListAsync();
        var muaLinks = await db.MUAStyles.AsNoTracking().ToListAsync();
        var expanded = !db.Database.IsNpgsql() || await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"ServiceStyles\"') IS NOT NULL AS \"Value\"").SingleAsync();
        var serviceLinks = expanded ? await db.ServiceStyles.AsNoTracking().Select(x => new StyleLinkAudit(x.ServiceId, x.StyleId, x.Service.MUAId)).ToListAsync() : [];
        var names = catalog.GroupBy(x => UnifiedStyleService.NameKey(x.Name)).ToDictionary(x => x.Key, x => x.ToList());
        var invalid = catalog.Where(x => UnifiedStyleService.DisplayName(x.Name).Length is 0 or > 100 || x.Name?.Any(char.IsControl) == true).Select(x => x.StyleId).ToList();
        return new(
            services.SelectMany(x => x.Tags.Select(tag => new LegacyTagAudit(x.ServiceId, x.MUAId, tag,
                names.TryGetValue(UnifiedStyleService.NameKey(tag), out var matches) ? matches.Select(s => s.StyleId).ToList() : []))).ToList(),
            names.Where(x => x.Value.Count > 1).Select(x => new StyleNameCollision(x.Key, x.Value.Select(s => s.StyleId).ToList())).ToList(), invalid,
            muaLinks.GroupBy(x => x.MUAId).Where(x => x.Count() > UnifiedStyleService.MuaLimit).Select(x => new StyleOverflow(x.Key, x.Count())).ToList(),
            services.Select(x => new StyleOverflow(x.ServiceId, Math.Max(x.Tags.Distinct().Count(), serviceLinks.Count(s => s.ServiceId == x.ServiceId))))
                .Where(x => x.Count > UnifiedStyleService.ServiceLimit).ToList(),
            serviceLinks.Where(x => !muaLinks.Any(m => m.MUAId == x.MuaId && m.StyleId == x.StyleId))
                .Select(x => new LegacyStyleMapping(x.ServiceId, "", x.StyleId)).ToList(),
            catalog.Select(x => new StyleCatalogAudit(x.StyleId, x.Name, x.IsActive, UnifiedStyleService.NameKey(x.Name))).ToList());
    }

    public Task<bool> Apply(IReadOnlyList<ReviewedStyleName> reviewedNames, IReadOnlyList<LegacyStyleMapping> mappings) =>
        new UnifiedStyleService(db).Write(Guid.Empty, async () =>
        {
            if (mappings.GroupBy(x => new { x.ServiceId, x.LegacyTag }).Any(x => x.Select(m => m.StyleId).Distinct().Count() > 1))
                throw new StyleWriteException("STYLE_BACKFILL_CONFLICT", "Một tag cũ không được ánh xạ sang nhiều ID khác nhau.", 409);
            var catalog = await db.MakeupStyles.ToListAsync();
            foreach (var review in reviewedNames)
            {
                var style = catalog.SingleOrDefault(x => x.StyleId == review.StyleId);
                if (style == null || style.Name != review.ExpectedName)
                    throw new StyleWriteException("STYLE_BACKFILL_STALE", "Danh mục đã thay đổi sau khi review.", 409);
                var name = UnifiedStyleService.ValidateName(style.Name);
                var key = UnifiedStyleService.NameKey(name);
                if (catalog.Any(x => x.StyleId != style.StyleId && UnifiedStyleService.NameKey(x.Name) == key))
                    throw new StyleWriteException("STYLE_NAME_AMBIGUOUS", "Xử lý tên trùng trước khi backfill khóa chuẩn hóa.", 409);
                style.NormalizedName = key;
            }
            var services = await db.Services.Include(x => x.Styles).ThenInclude(x => x.MakeupStyle)
                .Where(x => mappings.Select(m => m.ServiceId).Contains(x.ServiceId)).ToListAsync();
            var specialties = await db.MUAStyles.ToListAsync();
            foreach (var mapping in mappings.Distinct())
            {
                var service = services.SingleOrDefault(x => x.ServiceId == mapping.ServiceId);
                var style = catalog.SingleOrDefault(x => x.StyleId == mapping.StyleId);
                if (service == null || style == null || !service.Tags.Contains(mapping.LegacyTag))
                {
                    // A completed mapping remains a no-op when its original legacy spelling was replaced.
                    if (!(service != null && style != null && service.Styles.Any(x => x.StyleId == style.StyleId) && service.Tags.Contains(style.Name!)))
                        throw new StyleWriteException("STYLE_BACKFILL_STALE", "Tag/dịch vụ đã thay đổi sau khi review.", 409);
                }
                if (!service.Styles.Any(x => x.StyleId == style.StyleId))
                    service.Styles.Add(new ServiceStyle { Service = service, ServiceId = service.ServiceId, MakeupStyle = style, StyleId = style.StyleId });
                if (!specialties.Any(x => x.MUAId == service.MUAId && x.StyleId == style.StyleId))
                {
                    var link = new MUAStyle { MUAId = service.MUAId, StyleId = style.StyleId };
                    db.MUAStyles.Add(link); specialties.Add(link);
                }
                service.Tags = service.Tags.Select(x => x == mapping.LegacyTag ? style.Name! : x).Distinct().ToList();
            }
            // Historical over-limit/inactive values are retained and reported, never trimmed.
            await db.SaveChangesAsync(); return true;
        });
}
