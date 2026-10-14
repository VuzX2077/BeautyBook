using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Services;

public sealed class AdvisorStyleMappingOptions
{
    public List<AdvisorStyleMappingEntry> Entries { get; set; } = new();
}
public sealed class AdvisorStyleMappingEntry
{
    public string StyleKey { get; set; } = "";
    public List<int> StyleIds { get; set; } = new();
    public bool Approved { get; set; }
}
public sealed record AdvisorStyleMappingDto(string StyleKey, IReadOnlyList<int> StyleIds);
public sealed class AdvisorStyleMappings(ApplicationDbContext db, IOptions<AdvisorStyleMappingOptions> options)
{
    public async Task<IReadOnlyList<AdvisorStyleMappingDto>> Read(CancellationToken ct)
    {
        // Deployment-specific IDs are operator-reviewed configuration, never inferred from names.
        var entries = options.Value.Entries.Where(x => x.Approved &&
            System.Text.RegularExpressions.Regex.IsMatch(x.StyleKey, "^[a-z]+(?:-[a-z]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
            x.StyleKey.Length <= 100 && x.StyleIds.Count is > 0 and <= 20 && x.StyleIds.All(id => id > 0))
            .GroupBy(x => x.StyleKey, StringComparer.Ordinal).Where(x => x.Count() == 1).Select(x => x.Single()).ToList();
        var ids = entries.SelectMany(x => x.StyleIds).Distinct().ToArray();
        var active = await db.MakeupStyles.AsNoTracking().Where(x => x.IsActive && ids.Contains(x.StyleId))
            .Select(x => x.StyleId).ToListAsync(ct);
        return entries.Select(x => new AdvisorStyleMappingDto(x.StyleKey, x.StyleIds.Where(active.Contains).Distinct().Order().ToArray()))
            .Where(x => x.StyleIds.Count > 0).OrderBy(x => x.StyleKey).ToList();
    }
}
