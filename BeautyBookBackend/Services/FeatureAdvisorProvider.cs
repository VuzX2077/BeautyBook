using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Services;

public sealed class FeatureAdvisorOptions
{
    // Default OFF. Owner enablement is not billing/provider-terms verification;
    // the app also requires separate per-tester opt-in before sending a summary.
    public bool RealUserDataApproved { get; set; }
    public bool Enabled { get; set; }
    public bool CloudEnabled { get; set; }
    public string DataProcessingApproval { get; set; } = "pending";
    public string ProviderDataPolicy { get; set; } = "unpaid";
    public bool OnlineEnvironmentApproved { get; set; }
    public bool RequireHttps { get; set; } = true;
    public bool SyntheticDiagnosticsEnabled { get; set; }
    public bool ModelAvailabilityVerified { get; set; }
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 18;
    public int MaxConcurrentRequests { get; set; } = 2;
}
public sealed record FeatureAdvisorAttempt(FeatureAdvisorResponse? Response, string Code);
public sealed class FeatureAdvisorProvider(HttpClient client, IOptions<FeatureAdvisorOptions> options, FeatureAdvisorConcurrency? concurrency = null)
{
    private readonly FeatureAdvisorConcurrency admission = concurrency ?? new(options);
    public static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length<=200 && key.All(c=>c>='!'&&c<='~');
    public bool Configured =>
        ValidKey(options.Value.ApiKey) && System.Text.RegularExpressions.Regex.IsMatch(options.Value.Model, @"^gemini-[a-zA-Z0-9.\-]{1,80}$");
    public bool RequiresHttps => options.Value.RequireHttps;
    public bool Available => options.Value.Enabled && options.Value.CloudEnabled && options.Value.RealUserDataApproved &&
        options.Value.DataProcessingApproval=="approved" && options.Value.ProviderDataPolicy=="paid_terms" &&
        options.Value.ModelAvailabilityVerified && options.Value.Model=="gemini-3.5-flash-lite" && Configured;
    public static void ApplyEnvironmentGate(FeatureAdvisorOptions config,bool development) {
        if(!development&&!config.OnlineEnvironmentApproved)config.Enabled=false;
        if(!development){config.RequireHttps=true;config.SyntheticDiagnosticsEnabled=false;}
    }
    public async Task<FeatureAdvisorResponse?> Recommend(FaceFeatureSummaryV3 summary, CancellationToken ct)
    {
        if (!Available) return null;
        if (!FeatureAdvisorContract.Valid(new(Guid.NewGuid(),true,summary))) return null;
        if (!MakeupProfileConverter.Valid(MakeupProfileConverter.From(summary))) return null;
        return (await Send(summary, ct)).Response;
    }
    // Never accepts caller measurements: only this audited synthetic fixture.
    // Kept out of the production MVC controller/routes.
    public Task<FeatureAdvisorAttempt> DiagnoseSynthetic(CancellationToken ct) =>
        options.Value.SyntheticDiagnosticsEnabled && Configured
        ? Send(SyntheticFixture(), ct) : Task.FromResult(new FeatureAdvisorAttempt(null,"DIAGNOSTIC_DISABLED_OR_KEY_MISSING"));
    public static FaceFeatureSummaryV3 SyntheticFixture() => new(3,"face_mesh",new(true,true,false),
        new() { ["outlineAspect"]=1.25,["eyeSpacing"]=.32,["leftEyeAspect"]=.3,["rightEyeAspect"]=.31,["leftBrowArch"]=.07,["rightBrowArch"]=.08,["mouthWidth"]=.35 },
        [],[FeatureAdvisorContract.Limitations[0]],
        new() { ["outlineAspect"]="face_mesh",["eyeSpacing"]="face_mesh",["leftEyeAspect"]="face_mesh",["rightEyeAspect"]="face_mesh",["leftBrowArch"]="face_mesh",["rightBrowArch"]="face_mesh",["mouthWidth"]="face_mesh" },new(1,new(.2,.2,.5,.6),new(0,0,0)));
    private async Task<FeatureAdvisorAttempt> Send(FaceFeatureSummaryV3 summary, CancellationToken ct)
    {
        using var slot = admission.TryEnter(ct);
        if(slot is null)return new(null,"PROVIDER_BUSY");
        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent");
        request.Headers.Add("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(new {
            systemInstruction = new { parts = new[] { new { text = FeatureAdvisorContract.SystemInstruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = JsonSerializer.Serialize(FeatureAdvisorDefinitions.Prompt(summary), FeatureAdvisorContract.Json) } } } },
            generationConfig = new { temperature = .4, maxOutputTokens = 3840, responseMimeType = "application/json", responseJsonSchema = FeatureAdvisorContract.OutputSchema() }
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds,1,25)));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return new(null,$"HTTP_{(int)response.StatusCode}"); // One attempt, including 429/503/model missing.
            // Bound untrusted provider output before deserialization; never log it.
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0) { if (bytes.Length + read > 32768) return new(null,"OUTPUT_TOO_LARGE"); bytes.Write(buffer, 0, read); }
            using var json = JsonDocument.Parse(bytes.ToArray());
            var candidate = json.RootElement.GetProperty("candidates")[0];
            if (candidate.GetProperty("finishReason").GetString() != "STOP") return new(null,"INCOMPLETE_OR_BLOCKED_OUTPUT");
            var text = candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
                .Where(p => !p.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)
                .Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString());
            var result = JsonSerializer.Deserialize<FeatureAdvisorResponse>(string.Concat(text), FeatureAdvisorContract.Json);
            return FeatureAdvisorContract.ValidOutput(result, summary) ? new(result,"OK") : new(null,"INVALID_RECOMMENDATIONS");
        }
        catch (OperationCanceledException) { return new(null,"CANCELLED_OR_TIMEOUT"); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException) { return new(null,"NETWORK_OR_INVALID_OUTPUT"); }
    }
}
