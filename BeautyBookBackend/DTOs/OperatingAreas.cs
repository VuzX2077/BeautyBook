using System.Text.Json;

namespace BeautyBookBackend.DTOs;

public sealed record OperatingAreaOption(string Id, string Name, string Kind, string? LegacyProvinceName);
public sealed record OperatingProvince(int Code, string Name, List<int> LegacyProvinceCodes, List<OperatingAreaOption> Areas);
public sealed record OperatingCatalog(string Version, string Source, List<OperatingProvince> Provinces);

public static class OperatingAreas
{
    public static readonly OperatingCatalog Catalog = JsonSerializer.Deserialize<OperatingCatalog>(
        typeof(OperatingAreas).Assembly.GetManifestResourceStream("BeautyBookBackend.Data.operatingAreas.json")!,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static OperatingProvince? Province(int? code) => Catalog.Provinces.FirstOrDefault(p => p.Code == code);
    public static int? FromLegacyProvince(int? code) => Catalog.Provinces.FirstOrDefault(p => p.LegacyProvinceCodes.Contains(code ?? -1))?.Code;
    public static bool IsValid(int? provinceCode, IReadOnlyCollection<string>? ids) =>
        ids is { Count: > 0 and <= 100 } && ids.Distinct().Count() == ids.Count &&
        Province(provinceCode) is { } province && ids.All(id => province.Areas.Any(a => a.Id == id));
}
