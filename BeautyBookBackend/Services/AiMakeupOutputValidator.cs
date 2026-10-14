using System.Text.Json;
using System.Text.RegularExpressions;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public static class AiMakeupOutputValidator
{
    public const string Notice = "Gợi ý từ AI chỉ mang tính tham khảo; ánh sáng và góc chụp có thể ảnh hưởng kết quả. Không phải phép đo khuôn mặt hay tư vấn y khoa.";
    private static readonly string[] Statuses = ["valid", "no_face", "multiple_faces", "blurry", "occluded", "insufficient"];
    private static readonly string[] Features = ["face_contour", "eye_shape", "lip_shape", "lighting"];
    public static object Schema(IReadOnlyList<ExploreStyle> styles) => new {
        type = "object", additionalProperties = false, required = new[] { "imageStatus", "observations", "recommendations" }, properties = new {
            imageStatus = new { type = "string", @enum = Statuses },
            observations = new { type = "array", maxItems = 4, items = new { type = "object", additionalProperties = false, required = new[] { "feature", "description", "certainty" }, properties = new {
                feature = new { type = "string", @enum = Features }, description = new { type = "string" }, certainty = new { type = "string", @enum = new[] { "uncertain", "tentative" } }
            } } },
            recommendations = new { type = "array", maxItems = 3, items = new { type = "object", additionalProperties = false, required = new[] { "styleId", "reason", "makeupTips" }, properties = new {
                styleId = new { type = "integer", @enum = styles.Select(s => s.Id).ToArray() }, reason = new { type = "string" }, makeupTips = new { type = "array", minItems = 1, maxItems = 3, items = new { type = "string" } }
            } } }
        }
    };
    public static AiMakeupResultDto Validate(string json, IReadOnlyList<ExploreStyle> styles)
    {
        try {
            if (json.Length > 16000) throw new JsonException();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
            var root = doc.RootElement; Keys(root, "imageStatus", "observations", "recommendations");
            var status = root.GetProperty("imageStatus").GetString()!;
            if (!Statuses.Contains(status)) throw new JsonException();
            var obs = root.GetProperty("observations"); var rec = root.GetProperty("recommendations");
            if (obs.GetArrayLength() > 4 || (status == "valid" ? rec.GetArrayLength() != 3 : rec.GetArrayLength() != 0 || obs.GetArrayLength() != 0)) throw new JsonException();
            var observations = obs.EnumerateArray().Select(o => {
                Keys(o, "feature", "description", "certainty");
                var feature = o.GetProperty("feature").GetString()!; var certainty = o.GetProperty("certainty").GetString()!;
                if (!Features.Contains(feature) || certainty is not ("uncertain" or "tentative")) throw new JsonException();
                return new AiMakeupObservationDto(feature, Text(o.GetProperty("description")), certainty);
            }).ToArray();
            var recommendations = rec.EnumerateArray().Select(r => {
                Keys(r, "styleId", "reason", "makeupTips");
                var id = r.GetProperty("styleId").GetInt32();
                var style = styles.SingleOrDefault(s => s.Id == id) ?? throw new JsonException();
                var tips = r.GetProperty("makeupTips").EnumerateArray().Select(Text).ToArray();
                if (tips.Length is < 1 or > 3) throw new JsonException();
                return new AiMakeupSuggestionDto(id, style.Name, Text(r.GetProperty("reason")), tips);
            }).ToArray();
            if (recommendations.Select(r => r.StyleId).Distinct().Count() != recommendations.Length) throw new JsonException();
            return new(status, observations, recommendations, Notice);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) {
            throw new AiMakeupException(502, "AI_INVALID_OUTPUT", "AI chưa tạo được gợi ý hợp lệ. Bạn có thể chọn phong cách trong Khám phá.");
        }
    }
    private static void Keys(JsonElement value, params string[] allowed) {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != allowed.Length ||
            value.EnumerateObject().Any(p => !allowed.Contains(p.Name)) || allowed.Any(k => !value.TryGetProperty(k, out _))) throw new JsonException();
    }
    private static string Text(JsonElement value) {
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 400 || text.Any(char.IsControl) ||
            Regex.IsMatch(text, @"https?://|www\.|\b(dân tộc|chủng tộc|giới tính|tuổi|nam giới|nữ giới|bệnh|chẩn đoán|ung thư|trầm cảm|xấu xí|nhan sắc|hấp dẫn|identity|ethnicity|diagnos\w*|attractiv\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new JsonException();
        return text;
    }
}
