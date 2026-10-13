using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeautyBookBackend.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorQuality([property:JsonRequired] bool Frontal, [property:JsonRequired] bool SingleFace, [property:JsonRequired] bool ExpressionLimited);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorBox([property:JsonRequired] double X, [property:JsonRequired] double Y, [property:JsonRequired] double Width, [property:JsonRequired] double Height);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorPose([property:JsonRequired] double Yaw, [property:JsonRequired] double Pitch, [property:JsonRequired] double Roll);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorDetection(int FaceCount, FeatureAdvisorBox BoundingBox, FeatureAdvisorPose Pose);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FaceFeatureSummaryV3(int SchemaVersion, string AnalysisMethod, FeatureAdvisorQuality Quality,
    Dictionary<string, double> Features, string[] Missing, string[] Limitations,
    Dictionary<string, string> FeatureSources, FeatureAdvisorDetection Detection);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorRequest(Guid RequestId, bool Consent, FaceFeatureSummaryV3 Summary);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorRecommendation(string StyleKey, string Title, string ShortDescription, string WhyItFits,
    string[] MakeupFocus, string[] ColorSuggestions, string[] OccasionFit, string[] ServiceQueryKeywords);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FeatureAdvisorResponse(FeatureAdvisorRecommendation[] Recommendations);

public static class FeatureAdvisorContract
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.Strict };
    public static readonly string[] Styles = ["korean", "portrait", "event", "bridal"];
    public static readonly HashSet<string> FeatureNames = new([
        "outlineAspect", "eyeSpacing", "leftEyeWidth", "rightEyeWidth", "leftEyeAspect", "rightEyeAspect",
        "leftBrowLength", "rightBrowLength", "leftBrowArch", "rightBrowArch", "leftBrowEyeGap", "rightBrowEyeGap",
        "noseBridgeLength", "noseBaseWidth", "mouthWidth", "mouthAspect", "upperLipThickness", "lowerLipThickness",
        "lowerFaceHeight", "outlineUpperWidth", "outlineMiddleWidth", "outlineLowerWidth", "chinWidth",
        "leftEyeTilt", "rightEyeTilt", "leftBrowTilt", "rightBrowTilt"
    ], StringComparer.Ordinal);
    public static readonly HashSet<string> MissingNames = new(FeatureNames.Concat(new[] { "leftEyePosition", "rightEyePosition", "eyeWidthDifference", "mouthPosition", "outlineCompactness", "meshProjectedArea", "meshRelativeZSpan" }), StringComparer.Ordinal);
    public static readonly string[] Limitations = [
        "Số đo tương đối 2D của ảnh, chưa hiệu chuẩn; không phải phân loại dáng mặt.",
        "Không đo undertone, giới tính, tình trạng da hoặc độ sâu giải phẫu.",
        "Ánh sáng, che mặt và biểu cảm có thể ảnh hưởng số đo.",
        "Mắt nhắm hoặc chưa rõ: bỏ tỷ lệ độ mở mắt.",
        "Biểu cảm cười: bỏ các tỷ lệ môi nhạy với biểu cảm."
    ];
    private static bool Text(string? value, int max = 500) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl) && !value.Contains("```");
    private static bool List(string[]? values, int maxItems, int maxText) => values is not null && values.Length>=1 && values.Length<=maxItems && values.All(v=>Text(v,maxText));
    public static bool ValidDetection(FeatureAdvisorDetection? d) {
        if(d is null || d.FaceCount!=1 || d.BoundingBox is not {} b || d.Pose is not {} p)return false;
        return new[] {b.X,b.Y,b.Width,b.Height,p.Yaw,p.Pitch,p.Roll}.All(double.IsFinite) &&
            b.X>=0 && b.Y>=0 && b.Width>0 && b.Height>0 && b.X+b.Width<=1.000000001 && b.Y+b.Height<=1.000000001 &&
            Math.Abs(p.Yaw)<=15 && Math.Abs(p.Pitch)<=15 && Math.Abs(p.Roll)<=10;
    }
    public static bool Valid(FeatureAdvisorRequest? request)
    {
        var s = request?.Summary;
        if (request is null || request.RequestId == Guid.Empty || !request.Consent || s is null || s.SchemaVersion != 3 || !ValidDetection(s.Detection) ||
            s.AnalysisMethod is not ("face_detection" or "face_mesh") || s.Quality is null || !s.Quality.Frontal || !s.Quality.SingleFace ||
            s.Features is null || s.Features.Count is < 1 or > 27 || s.Missing is null || s.Missing.Length > 34 ||
            s.FeatureSources is null || s.FeatureSources.Count != s.Features.Count ||
            !s.FeatureSources.All(p => s.Features.ContainsKey(p.Key) && p.Value is "face_mesh" or "face_detection" && (s.AnalysisMethod == "face_mesh" || p.Value == "face_detection")) ||
            s.Limitations is null || s.Limitations.Length is < 1 or > 8 || !s.Limitations.All(Limitations.Contains) ||
            !s.Missing.All(MissingNames.Contains)) return false;
        return s.Features.All(pair => FeatureNames.Contains(pair.Key) && double.IsFinite(pair.Value) &&
            (!(pair.Key == "eyeSpacing" || System.Text.RegularExpressions.Regex.IsMatch(pair.Key,@"(Width|Length|Thickness|Aspect|Height)$")) || pair.Value > 0) &&
            (pair.Key == "outlineAspect" ? pair.Value is >= .5 and <= 2.5 : pair.Value >= 0 && pair.Value <= (pair.Key.EndsWith("Tilt", StringComparison.Ordinal) ? .25 : 1))) &&
            s.Missing.Distinct().Count() == s.Missing.Length && !s.Missing.Any(s.Features.ContainsKey) &&
            (!s.Quality.ExpressionLimited || !s.Features.Keys.Any(key => key is "mouthWidth" or "mouthAspect" or "upperLipThickness" or "lowerLipThickness" or "mouthPosition" or "lowerFaceHeight"));
    }
    public static bool ValidOutput(FeatureAdvisorResponse? response, FaceFeatureSummaryV3 summary)
    {
        if (response?.Recommendations is null || response.Recommendations.Length != 3) return false;
        if (response.Recommendations.Select(r => r?.StyleKey).Distinct().Count() != response.Recommendations.Length) return false;
        return response.Recommendations.All(r => r is not null && Styles.Contains(r.StyleKey) && Text(r.Title, 100) &&
            Text(r.ShortDescription) && Text(r.WhyItFits) && List(r.MakeupFocus,5,500) && r.MakeupFocus.Length>=4 && List(r.ColorSuggestions,5,80) &&
            List(r.OccasionFit,4,100) && List(r.ServiceQueryKeywords,5,60) &&
            !UnsupportedClaims.Contains(string.Join(" ", new[] {r.Title,r.ShortDescription,r.WhyItFits}.Concat(r.MakeupFocus).Concat(r.ColorSuggestions).Concat(r.OccasionFit).Concat(r.ServiceQueryKeywords))));
    }
    public const string SystemInstruction = """
        You are BBook's reference makeup style advisor. Respond in Vietnamese. Inputs are experimental roll-aligned 2D ratios,
        not anatomy or verified classifiers. Do not infer gender, ethnicity, attractiveness, health, skin, undertone, eyelid type,
        physical depth or definitive face shape. Missing measurements must remain unknown. Expression-sensitive lip data may be absent.
        Analyze the supplied measured values yourself, using the accompanying definitions, sources, ranges and quality gates.
        The input contains no local recommendations to rewrite. Explain how observed ratios suggest practical makeup emphasis,
        without making a definitive appearance classification. Do not mention absent measurements as measured.
        Never invent a confidence percentage. Do not infer anatomy from experimental unsigned contour tilt measurements.
        Return only JSON matching the schema, no markdown or text outside it. Recommend EXACTLY 3 distinct styles
        from korean (Korean), portrait (Kỷ yếu), event (Sự kiện), bridal (Cô dâu).
        Do not assume a wedding from a face; bridal is only an exploration option. Include practical eyes, brows, cheeks and lips tips,
        optional contour/highlight in makeupFocus (4-5 short practical strings: one each for eyes, brows, cheeks, lips), shortDescription, whyItFits,
        colorSuggestions (1-5 optional palettes to try, NEVER inferred from skin), occasionFit (1-4 possible occasions,
        not a user preference inferred from a face), serviceQueryKeywords (1-5 short Vietnamese catalog search phrases).
        Include the catalog style name as the first serviceQueryKeyword. Never invent MUA, service IDs,
        prices, ratings or availability. Do not repeat numerical facial measurements in user-facing text.
        Feature dictionary: outlineAspect=outline height/width; eyeSpacing=eye centroid distance/face width;
        EyeWidth=eye X span/face width; EyeAspect=eye Y span/eye X span; BrowLength=brow X span/face width;
        BrowArch=max deviation from brow endpoint chord/brow X span; BrowEyeGap=eye-brow centroid Y gap/face height;
        noseBridgeLength=bridge Y span/face height; noseBaseWidth=nose bottom X span/face width;
        mouthWidth=outer lip X span/face width; mouthAspect=outer lip Y span/outer lip X span;
        upperLipThickness/lowerLipThickness=inner-outer lip centroid Y difference/face height;
        lowerFaceHeight=face bottom-mouth centroid Y/face height; outlineUpperWidth/MiddleWidth/LowerWidth/chinWidth=
        horizontal outline width in bands at 25/50/75/90 percent of outline height divided by whole outline width.
        These measurements suggest ways to explore emphasis, never the best or uniquely suitable style.
        """;
    public static object OutputSchema()
    {
        object Str(int max=500) => new { type = "string", minLength=1, maxLength=max };
        object List(int count,int max,int min=1) => new { type = "array", minItems=min,maxItems=count,items=Str(max) };
        var rec = new { type = "object", additionalProperties=false, properties = new { styleKey = new { type = "string", @enum = Styles }, title = Str(100), shortDescription = Str(), whyItFits = Str(), makeupFocus = List(5,500,4), colorSuggestions = List(5,80), occasionFit = List(4,100), serviceQueryKeywords = List(5,60) }, required = new[] { "styleKey", "title", "shortDescription", "whyItFits", "makeupFocus", "colorSuggestions", "occasionFit", "serviceQueryKeywords" } };
        return new { type = "object",additionalProperties=false,properties = new { recommendations = new { type = "array", minItems = 3, maxItems = 3, items = rec } }, required = new[] { "recommendations" } };
    }
}
