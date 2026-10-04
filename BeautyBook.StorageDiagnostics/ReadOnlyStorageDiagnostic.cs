using System.Security.Cryptography;
using System.Text.Json;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBook.StorageDiagnostics;

public static class ReadOnlyStorageDiagnostic
{
    public const string Bucket = "verification-private";
    public const string ObjectKey = "verification/13ff36b7f79d4a0a8c12d7c8fe37ec25/1f2704faa6c5961590112899a6bdc5a0.jpg";
    public const int ExpectedSize = 181721;
    public const string ExpectedHash = "403EE4B6525CD974F18CB0F1FB82D33537825F4A05A65E45FFE2D788BD0A143B";

    public static HttpClientHandler CreateTransport() => new() {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false
    };

    public static async Task<int> RunAsync(IConfiguration config, HttpMessageHandler transport, TextWriter output, CancellationToken ct = default)
    {
        var originText = config["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
        var bucket = config["Supabase:VerificationBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_VERIFICATION_BUCKET") ?? Bucket;
        if (!Uri.TryCreate(originText, UriKind.Absolute, out var origin) || origin.Scheme != "https"
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0
            || origin.Fragment.Length != 0 || bucket != Bucket)
            throw new InvalidOperationException("Invalid diagnostic configuration.");
        using var guard = new ReadOnlyStorageHandler(origin, transport);
        using var client = new HttpClient(guard);
        var adapter = new SupabaseVerificationStorage(client, config);
        await output.WriteLineAsync("bucket: " + Bucket);
        try
        {
            await adapter.EnsurePrivateAsync(ct);
            await output.WriteLineAsync($"bucket HTTP status: {guard.Status}");
            await output.WriteLineAsync("bucket state: private");
        }
        catch
        {
            await output.WriteLineAsync($"bucket HTTP status: {guard.Status?.ToString() ?? "unavailable"}");
            await WriteFailure(output, guard);
            return 2;
        }
        await output.WriteLineAsync("object key: " + ObjectKey);
        try
        {
            var bytes = await adapter.DownloadAsync(ObjectKey, legacy: false, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await output.WriteLineAsync($"object HTTP status: {guard.Status}");
            await output.WriteLineAsync($"size: {bytes.Length}");
            await output.WriteLineAsync("SHA256: " + hash);
            var match = bytes.Length == ExpectedSize && hash == ExpectedHash;
            await output.WriteLineAsync("result: " + (match ? "MATCH" : "STOP_CONTENT_MISMATCH"));
            return match ? 0 : 2;
        }
        catch (StorageObjectMissingException) when (guard.Status == 404)
        {
            await output.WriteLineAsync("object HTTP status: 404");
            await output.WriteLineAsync("result: OBJECT_MISSING");
            return 0;
        }
        catch
        {
            await output.WriteLineAsync($"object HTTP status: {guard.Status?.ToString() ?? "unavailable"}");
            await WriteFailure(output, guard);
            return 2;
        }
    }

    private static async Task WriteFailure(TextWriter output, ReadOnlyStorageHandler guard)
    {
        await output.WriteLineAsync("Supabase error code: " + guard.ErrorCode);
        await output.WriteLineAsync("Supabase error message: " + guard.ErrorMessage);
        await output.WriteLineAsync("result: " + (guard.Status switch {
            400 => "STOP_BAD_REQUEST", 401 or 403 => "STOP_AUTH_PERMISSION_FAILURE",
            >= 500 and <= 599 => "STOP_PROVIDER_SERVER_FAILURE", _ => "STOP_UNEXPECTED_RESPONSE_OR_TRANSPORT_FAILURE" }));
    }
}

// This handler is diagnostic-only. It observes errors before the adapter disposes
// them; it does not change production exception semantics or implement auth.
public sealed class ReadOnlyStorageHandler(Uri origin, HttpMessageHandler transport) : DelegatingHandler(transport)
{
    private int requests;
    public int? Status { get; private set; }
    public string ErrorCode { get; private set; } = "BODY_REDACTED";
    public string ErrorMessage { get; private set; } = "BODY_REDACTED";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Status = null;
        ErrorCode = ErrorMessage = "BODY_REDACTED";
        var expectedPath = requests == 0 ? "/storage/v1/bucket/" + ReadOnlyStorageDiagnostic.Bucket
            : "/storage/v1/object/authenticated/" + ReadOnlyStorageDiagnostic.Bucket + "/" + ReadOnlyStorageDiagnostic.ObjectKey;
        var uri = request.RequestUri;
        if (requests >= 2 || request.Method != HttpMethod.Get || request.Content != null || uri == null
            || uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)
            || uri.AbsolutePath != expectedPath || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Diagnostic request denied.");
        requests++;
        var response = await base.SendAsync(request, ct);
        Status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode && Status != 200)
        {
            response.Dispose();
            throw new InvalidOperationException("Unexpected diagnostic success status.");
        }
        if (Status >= 400)
        {
            // Strict allowlisting instead of attempting to sanitize arbitrary
            // provider text, which could reflect credentials in unknown formats.
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[4097];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
                    if (read == 0) break;
                    count += read;
                }
                if (count <= 4096)
                {
                    using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
                    if (json.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in new[] { "code", "error" })
                            if (json.RootElement.TryGetProperty(field, out var code) && code.ValueKind == JsonValueKind.String
                                && code.GetString() is "NoSuchKey" or "NoSuchBucket" or "AccessDenied" or "InvalidJWT" or "InvalidRequest" or "NotFound"
                                    or "not_found" or "not found" or "Bad Request" or "Unauthorized" or "Forbidden")
                                ErrorCode = code.GetString()!;
                        if (json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                            && message.GetString() is "Object not found" or "The resource was not found" or "Bucket not found" or "Invalid JWT" or "Unauthorized" or "Forbidden")
                            ErrorMessage = message.GetString()!;
                    }
                }
            }
            catch { /* Never expose body parsing or transport exception details. */ }
        }
        return response;
    }
}
