using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public sealed record PlaceSuggestion(string Id, string Label);
public sealed record SelectedLocation(string Label, double Latitude, double Longitude, int? ProvinceCode, string? AreaId);
public sealed class LocationUnavailableException(string message) : Exception(message);

public class LocationService(HttpClient client, IConfiguration configuration)
{
    public bool Configured => !string.IsNullOrWhiteSpace(configuration["Maps:ServerApiKey"]);
    private string Key => Configured ? configuration["Maps:ServerApiKey"]! : throw new LocationUnavailableException("Tìm địa chỉ chưa được cấu hình. Bạn vẫn có thể chọn khu vực hoặc dùng GPS.");
    public async Task<List<PlaceSuggestion>> SearchAsync(string text, string session, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete");
        request.Headers.Add("X-Goog-Api-Key", Key);
        request.Content = JsonContent.Create(new { input = text, includedRegionCodes = new[] { "vn" }, languageCode = "vi", sessionToken = session });
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new LocationUnavailableException("Không thể tìm địa chỉ lúc này. Vui lòng thử lại.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<PlaceSuggestion>();
        if (json.RootElement.TryGetProperty("suggestions", out var rows))
            foreach (var row in rows.EnumerateArray())
                if (row.TryGetProperty("placePrediction", out var p)) results.Add(new(p.GetProperty("placeId").GetString()!, p.GetProperty("text").GetProperty("text").GetString()!));
        return results;
    }
    public async Task<SelectedLocation> SelectAsync(string id, string session, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(id)}?languageCode=vi&sessionToken={Uri.EscapeDataString(session)}");
        request.Headers.Add("X-Goog-Api-Key", Key);
        request.Headers.Add("X-Goog-FieldMask", "formattedAddress,location,addressComponents");
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new LocationUnavailableException("Không thể xác định địa điểm đã chọn.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var loc = root.GetProperty("location");
        var names = root.TryGetProperty("addressComponents", out var parts) ? parts.EnumerateArray().Select(x => x.GetProperty("longText").GetString()!).ToList() : new();
        var matched = Match(names);
        return new(root.GetProperty("formattedAddress").GetString()!, loc.GetProperty("latitude").GetDouble(), loc.GetProperty("longitude").GetDouble(), matched.Province, matched.Area);
    }
    public async Task<SelectedLocation> ReverseAsync(double lat, double lng, CancellationToken ct)
    {
        var coordinates = lat.ToString(CultureInfo.InvariantCulture) + "," + lng.ToString(CultureInfo.InvariantCulture);
        using var response = await client.GetAsync($"https://maps.googleapis.com/maps/api/geocode/json?latlng={coordinates}&language=vi&key={Uri.EscapeDataString(Key)}", ct);
        if (!response.IsSuccessStatusCode) throw new LocationUnavailableException("Không thể tra khu vực. Bạn vẫn có thể chọn thủ công.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (root.GetProperty("status").GetString() != "OK") throw new LocationUnavailableException("Chưa tìm được tên khu vực tại vị trí này.");
        var rows = root.GetProperty("results");
        var names = rows.EnumerateArray().SelectMany(x => x.GetProperty("address_components").EnumerateArray()).Select(x => x.GetProperty("long_name").GetString()!).ToList();
        var matched = Match(names);
        return new(rows[0].GetProperty("formatted_address").GetString()!, lat, lng, matched.Province, matched.Area);
    }
    public static string Normalize(string text)
    {
        var value = new string(text.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Replace('đ','d');
        foreach (var prefix in new[] { "thanh pho ", "tinh ", "quan ", "huyen ", "phuong ", "xa ", "thi tran ", "thi xa ", "tp. " }) if (value.StartsWith(prefix)) { value = value[prefix.Length..]; break; }
        return value.Trim();
    }
    private static (int? Province, string? Area) Match(List<string> names)
    {
        var values = names.Select(Normalize).ToHashSet();
        var province = OperatingAreas.Catalog.Provinces.FirstOrDefault(p => values.Contains(Normalize(p.Name)))
            ?? OperatingAreas.Catalog.Provinces.FirstOrDefault(p => p.Areas.Any(a => a.LegacyProvinceName != null && values.Contains(Normalize(a.LegacyProvinceName))));
        var matches = province?.Areas.Where(a => values.Contains(Normalize(a.Name))).ToList() ?? new();
        var exact = matches.Where(a => names.Any(n => n.Equals(a.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var area = exact.Count == 1 ? exact[0] : matches.Count == 1 ? matches[0] : null;
        return (province?.Code, area?.Id);
    }
}
