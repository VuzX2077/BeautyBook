namespace BeautyBookBackend.Services;

public sealed record FeatureDefinition(string Points, string Formula, string Normalization, double Min, double Max, string QualityGate, string MakeupUse);

public static class FeatureAdvisorDefinitions
{
    // Trusted server metadata only. Client submits values and source enums,
    // never arbitrary instruction strings, images, identifiers or raw mesh.
    public static FeatureDefinition For(string key)
    {
        const string axes = "EXIF/mirror-corrected bitmap; axes aligned to eye centroids; X right, Y down. Dimensionless.";
        const string quality = "Single face; face and outline width >=200px; yaw/pitch<=15deg, roll<=10deg; in-image points; coherent outline/eyes; no calibrated anatomy.";
        var (points, formula, use) = key switch {
            "outlineAspect" => ("outline", "span(Y)/span(X)", "cheek placement"),
            "eyeSpacing" => ("leftEye,rightEye", "distance(eye centroids)/outline width", "eyeliner emphasis"),
            "noseBridgeLength" => ("noseBridge", "bridge span(Y)/outline height", "explore highlight"),
            "noseBaseWidth" => ("FaceContour.NOSE_BOTTOM", "nose bottom span(X)/outline width", "explore contour"),
            "mouthWidth" => ("upperLipTop,lowerLipBottom", "outer lip span(X)/outline width", "lip emphasis"),
            "mouthAspect" => ("upperLipTop,lowerLipBottom", "outer lip span(Y)/outer lip span(X)", "lip edge"),
            "upperLipThickness" => ("upperLipTop,upperLipBottom", "abs(inner-outer centroid delta Y)/outline height", "lip highlight"),
            "lowerLipThickness" => ("lowerLipTop,lowerLipBottom", "abs(inner-outer centroid delta Y)/outline height", "lip highlight"),
            "lowerFaceHeight" => ("outer lips,outline", "(outline bottom-mouth centroid Y)/outline height", "chin highlight"),
            "outlineUpperWidth" => ("outline Y band .25 +/- .08", "band span(X)/outline width", "cheek placement"),
            "outlineMiddleWidth" => ("outline Y band .50 +/- .08", "band span(X)/outline width", "cheek placement"),
            "outlineLowerWidth" => ("outline Y band .75 +/- .08", "band span(X)/outline width", "contour exploration"),
            "chinWidth" => ("outline Y band .90 +/- .08", "band span(X)/outline width", "contour exploration"),
            _ when key.EndsWith("EyeWidth") => (key[..^5], "eye span(X)/outline width", "eyeshadow"),
            _ when key.EndsWith("EyeAspect") => (key[..^6], "eye span(Y)/eye span(X)", "eyeliner"),
            _ when key.EndsWith("BrowLength") => (key[..^6]+"Top", "brow span(X)/outline width", "brow length emphasis"),
            _ when key.EndsWith("BrowArch") => (key[..^4]+"Top", "max perpendicular distance to horizontal-extrema endpoint chord/brow span(X)", "brow emphasis"),
            _ when key.EndsWith("BrowEyeGap") => (key.StartsWith("left") ? "leftBrowTop,leftEye" : "rightBrowTop,rightEye", "(eye centroid Y-brow centroid Y)/outline height", "eyeshadow space"),
            _ when key.EndsWith("Tilt") => (key[..^4]+(key.Contains("Brow") ? "Top" : ""), "abs(atan2(horizontal-extrema endpoint delta Y, delta X))/pi", "unsigned contour orientation; not anatomical canthal tilt"),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
        var gate = quality + (key is "mouthWidth" or "mouthAspect" or "upperLipThickness" or "lowerLipThickness" or "lowerFaceHeight" ? " Omit when available smiling probability>.55." : "")
            + (key.Contains("EyeAspect") || key.Contains("EyeTilt") ? " Omit when either available eye-open probability<.4; unknown remains unknown." : "");
        return new(points,formula,axes,key == "outlineAspect" ? .5 : 0,key == "outlineAspect" ? 2.5 : key.EndsWith("Tilt") ? .25 : 1,gate,use);
    }
    public static object Prompt(FaceFeatureSummaryV3 summary) => new {
        makeupProfile=MakeupProfileConverter.From(summary),
        supportedStyles=FeatureAdvisorContract.Styles
    };
}

public static class UnsupportedClaims
{
    // Conservative defense for common prohibited assertions; semantic accuracy
    // still needs human evaluation. This is not a comprehensive classifier.
    public static bool Contains(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"\d\s*%|\b\d+[.,]\d+\b|\bconfidence\b\s*[:=]?\s*\d|\b(you (are|have)|your (face|skin) is)\b.{0,45}\b(male|female|oval|round|square|warm|cool|fair|dark|acne)\b|\b(bạn (là|có)|da bạn|khuôn mặt bạn|mặt của bạn)\b.{0,45}\b(nam|nữ|tròn|vuông|oval|trái xoan|undertone|trắng|ngăm|mụn)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
