namespace BeautyBookBackend.Services;

public sealed class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public static bool IsSupportedModel(string model) => model == "gemini-3.5-flash-lite";
}
