using System.Text.Json.Serialization;

namespace BeautyBookBackend.DTOs;

// Setters run for explicit JSON null as well as values; omitted properties never run.
public abstract class PresenceAwareRequest
{
    private readonly HashSet<string> supplied = new(StringComparer.Ordinal);
    public bool Has(string property) => supplied.Contains(property);
    protected T Supplied<T>(T value, string property) { supplied.Add(property); return value; }
}

public sealed class PortfolioTaggingRequest : PresenceAwareRequest
{
    private int? primaryStyleId;
    private List<int>? secondaryStyleIds;
    private List<string>? occasionKeys, colorToneKeys, effectKeys;
    private string? intensityKey;
    public int? PrimaryStyleId { get => primaryStyleId; set => primaryStyleId = Supplied(value, nameof(PrimaryStyleId)); }
    public List<int>? SecondaryStyleIds { get => secondaryStyleIds; set => secondaryStyleIds = Supplied(value, nameof(SecondaryStyleIds)); }
    public List<string>? OccasionKeys { get => occasionKeys; set => occasionKeys = Supplied(value, nameof(OccasionKeys)); }
    public string? IntensityKey { get => intensityKey; set => intensityKey = Supplied(value, nameof(IntensityKey)); }
    public List<string>? ColorToneKeys { get => colorToneKeys; set => colorToneKeys = Supplied(value, nameof(ColorToneKeys)); }
    public List<string>? EffectKeys { get => effectKeys; set => effectKeys = Supplied(value, nameof(EffectKeys)); }
    [JsonIgnore] public bool HasChanges => Has(nameof(PrimaryStyleId)) || Has(nameof(SecondaryStyleIds)) || Has(nameof(OccasionKeys)) ||
        Has(nameof(IntensityKey)) || Has(nameof(ColorToneKeys)) || Has(nameof(EffectKeys));
}

public sealed record PortfolioStyleReference(int StyleId, string? Key, string? Name, bool IsActive, DateTime? ArchivedAt, bool IsSelectable);
public sealed record PortfolioTermReference(int Id, string Kind, string Key, string Label, int? GroupId, bool IsActive, DateTime? ArchivedAt, bool IsSelectable);
public sealed class PortfolioTaggingDto
{
    public PortfolioStyleReference? PrimaryStyle { get; init; }
    public List<PortfolioStyleReference> SecondaryStyles { get; init; } = [];
    public List<PortfolioTermReference> Occasions { get; init; } = [];
    public PortfolioTermReference? Intensity { get; init; }
    public List<PortfolioTermReference> ColorTones { get; init; } = [];
    public List<PortfolioTermReference> Effects { get; init; } = [];
}
