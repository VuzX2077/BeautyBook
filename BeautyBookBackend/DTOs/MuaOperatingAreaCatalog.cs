using System.Text.Json;

namespace BeautyBookBackend.DTOs;

internal static class MuaOperatingAreaCatalog
{
    private static readonly Lazy<JsonDocument> Catalog = new(() =>
        JsonDocument.Parse(typeof(MuaOperatingAreaCatalog).Assembly.GetManifestResourceStream("BeautyBookBackend.Data.vietnamAreas.json")!));

    public static bool IsValid(int provinceCode, int? districtCode, string city, string? district)
    {
        foreach (var province in Catalog.Value.RootElement.EnumerateArray())
        {
            if (province.GetProperty("code").GetInt32() != provinceCode) continue;
            if (province.GetProperty("name").GetString() != city) return false;
            return districtCode.HasValue && province.GetProperty("districts").EnumerateArray().Any(item =>
                item.GetProperty("code").GetInt32() == districtCode.Value && item.GetProperty("name").GetString() == district);
        }
        return false;
    }
}
