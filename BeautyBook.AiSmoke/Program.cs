using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

// Standalone operator tool; no HTTP endpoint, host startup, DB writes or production privacy bypass.
if (args.Length == 3 && args[0] == "migration-script") {
    using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=127.0.0.1;Database=offline_script;Username=unused").Options);
    var sql = db.GetService<IMigrator>().GenerateScript(args[1], null, MigrationsSqlGenerationOptions.Idempotent);
    await File.WriteAllTextAsync(args[2], sql);
    Console.WriteLine("Generated idempotent script offline. Review actual production history before any application.");
    return;
}
var key = Environment.GetEnvironmentVariable("Gemini__ApiKey");
if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Set Gemini__ApiKey securely in this process; never paste it into chat.");
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
const string model = "gemini-3.5-flash-lite";
if (args.Length == 1 && args[0] == "metadata-confirmed") {
    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://generativelanguage.googleapis.com/v1beta/models/{model}");
    request.Headers.Add("x-goog-api-key", key);
    using var response = await client.SendAsync(request);
    Console.WriteLine($"Metadata HTTP {(int)response.StatusCode}");
    if (response.IsSuccessStatusCode) {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var name = json.RootElement.GetProperty("name").GetString();
        var supported = json.RootElement.GetProperty("supportedGenerationMethods").EnumerateArray().Any(v => v.GetString() == "generateContent");
        Console.WriteLine($"Expected model: {name == "models/" + model}");
        Console.WriteLine($"generateContent supported: {supported}");
        if (name != "models/" + model || !supported) Environment.ExitCode = 1;
    } else Environment.ExitCode = 1;
    return;
}
if (args.Length != 4 || args[0] != "synthetic-free-confirmed")
    throw new InvalidOperationException("Inference requires: synthetic-free-confirmed <synthetic-image> <reviewed-sha256> <approved-catalog.json>. Explicit operator approval required; never use a real person's image here.");
if (new FileInfo(args[1]).Length is <= 0 or > AiMakeupImage.MaxBytes) throw new InvalidOperationException("Synthetic fixture must be smaller than 5 MB.");
var source = await File.ReadAllBytesAsync(args[1]);
byte[]? image = null;
try {
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(source), Convert.FromHexString(args[2])))
        throw new InvalidOperationException("Image hash differs from reviewed synthetic fixture.");
    var mime = Path.GetExtension(args[1]).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => throw new InvalidOperationException("Use PNG or JPEG.") };
    image = AiMakeupImage.Normalize(source, mime);
    Array.Clear(source);
    var catalog = JsonSerializer.Deserialize<List<ExploreStyle>>(await File.ReadAllTextAsync(args[3]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    if (catalog.Count < 3 || catalog.Select(s => s.Id).Distinct().Count() != catalog.Count) throw new InvalidOperationException("Need at least three unique real active style IDs from approved catalog.");
    var timer = Stopwatch.StartNew();
    try {
        // Exactly one provider call, same implementation/validator as backend; no retry or fallback.
        var reply = await new GeminiMakeupProvider(client, new SmokeOptions(new() { ApiKey = key, Model = model })).AnalyzeAsync(image, catalog, null, null, default);
        var result = AiMakeupOutputValidator.Validate(reply.Json, catalog);
        decimal? estimate = (reply.InputTokens * .30m + (reply.OutputTokens + reply.ThinkingTokens) * 2.50m) / 1_000_000;
        Console.WriteLine(JsonSerializer.Serialize(new { result.ImageStatus, styleIds = result.Recommendations.Select(s => s.StyleId), validThree = result.Recommendations.Count == 3, reply.InputTokens, reply.OutputTokens, reply.ThinkingTokens, latencyMs = timer.ElapsedMilliseconds, estimatedStandardPaidUsd = estimate }));
        if (result.Recommendations.Count != 3) Environment.ExitCode = 1;
        // No photo, prompt, free text response or API key is printed or persisted.
    } catch (AiMakeupException e) { Console.WriteLine($"Safe failure: {e.Status} {e.Code}; no retry performed."); Environment.ExitCode = 1; }
} finally { Array.Clear(source); if (image != null) Array.Clear(image); }

sealed class SmokeOptions(GeminiOptions value) : IOptionsMonitor<GeminiOptions> {
    public GeminiOptions CurrentValue => value;
    public GeminiOptions Get(string? name) => value;
    public IDisposable? OnChange(Action<GeminiOptions, string?> listener) => null;
}
