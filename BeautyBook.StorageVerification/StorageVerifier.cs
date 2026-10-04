using System.Security.Cryptography;
using System.Text.Json;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBook.StorageVerification;

internal sealed record ExpectedObject(string Purpose, string Key, int Size, string Hash);

public static class StorageVerifier
{
    public const string Bucket = "verification-private";
    internal const string Prefix = "verification/13ff36b7f79d4a0a8c12d7c8fe37ec25/";
    internal static IReadOnlyList<ExpectedObject> Manifest { get; } = Array.AsReadOnly(new[] {
        new ExpectedObject("identity-front", Prefix + "1f2704faa6c5961590112899a6bdc5a0.jpg", 181721, "403EE4B6525CD974F18CB0F1FB82D33537825F4A05A65E45FFE2D788BD0A143B"),
        new ExpectedObject("identity-back", Prefix + "41d38076a8dff76fb141b9cc51e7ae8e.jpg", 192615, "4AC78D9EAA316BBE3D851FE0E9D1F90FB69C939C1F0BD240318AF9318D5A5707"),
        new ExpectedObject("portrait", Prefix + "d6b7c8eb04977faef2db54fa58cd1746.jpg", 62945, "E2EB3E4FA08CF514438182C1CCAF2D586FC1DF634EC098EE96F7F9C9D2089D1F") });

    public static HttpClientHandler CreateTransport() => new() {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false
    };

    public static Task<int> RunAsync(IConfiguration config, HttpMessageHandler transport, TextWriter output, CancellationToken ct = default)
        => RunCoreAsync(config, transport, output, Manifest, ct);

    // Internal seam permits synthetic byte fixtures; the executable cannot override its manifest.
    internal static async Task<int> RunCoreAsync(IConfiguration config, HttpMessageHandler transport, TextWriter output,
        IReadOnlyList<ExpectedObject> expected, CancellationToken ct = default)
    {
        var originText = config["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
        var bucket = config["Supabase:VerificationBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_VERIFICATION_BUCKET") ?? Bucket;
        if (!Uri.TryCreate(originText, UriKind.Absolute, out var origin) || origin.Scheme != "https"
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0
            || origin.Fragment.Length != 0 || bucket != Bucket) throw new InvalidOperationException("Invalid verifier configuration.");
        using var guard = new VerificationRequestGuard(origin, transport, expected);
        using var client = new HttpClient(guard);
        var adapter = new SupabaseVerificationStorage(client, config);
        await output.WriteLineAsync("bucket: " + Bucket);
        try
        {
            await adapter.EnsurePrivateAsync(ct);
            await output.WriteLineAsync("bucket HTTP status: 200");
            await output.WriteLineAsync("bucket state: private");
        }
        catch { return await Failure(output, guard); }
        foreach (var item in expected)
        {
            await output.WriteLineAsync("object key: " + item.Key);
            try
            {
                var bytes = await adapter.DownloadAsync(item.Key, legacy: false, ct);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                await output.WriteLineAsync("object HTTP status: 200");
                await output.WriteLineAsync("size: " + bytes.Length);
                await output.WriteLineAsync("SHA256: " + hash);
                if (bytes.Length != item.Size || hash != item.Hash)
                {
                    await output.WriteLineAsync("result: FAIL_CONTENT_MISMATCH");
                    return 2;
                }
                await output.WriteLineAsync("object result: MATCH");
            }
            catch { return await Failure(output, guard); }
        }
        await output.WriteLineAsync("result: PASS_ALL_3_OBJECTS");
        return 0;
    }

    private static async Task<int> Failure(TextWriter output, VerificationRequestGuard guard)
    {
        await output.WriteLineAsync("HTTP status: " + (guard.Status?.ToString() ?? "unavailable"));
        await output.WriteLineAsync("error code: " + guard.ErrorCode);
        await output.WriteLineAsync("result: FAIL_STORAGE_VERIFICATION");
        return 2;
    }
}

// Tool-only policy: no auth implementation and no mutation or repair path.
internal sealed class VerificationRequestGuard(Uri origin, HttpMessageHandler transport, IReadOnlyList<ExpectedObject> expected)
    : DelegatingHandler(transport)
{
    private int requests;
    public int? Status { get; private set; }
    public string ErrorCode { get; private set; } = "BODY_REDACTED";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Status = null;
        ErrorCode = "BODY_REDACTED";
        var index = requests;
        var path = index == 0 ? "/storage/v1/bucket/" + StorageVerifier.Bucket
            : index <= expected.Count ? "/storage/v1/object/authenticated/" + StorageVerifier.Bucket + "/" + expected[index - 1].Key : "";
        var uri = request.RequestUri;
        if (index > expected.Count || request.Method != HttpMethod.Get || request.Content != null || uri == null
            || uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)
            || uri.AbsolutePath != path || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Verifier request denied.");
        requests++;
        var response = await base.SendAsync(request, ct);
        Status = (int)response.StatusCode;
        try
        {
            if (response.RequestMessage?.RequestUri != uri) throw new InvalidOperationException("Unexpected response origin.");
            var limit = Status == 200 && index > 0 ? expected[index - 1].Size : 4096;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Response exceeds verifier limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[limit + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > limit) throw new InvalidOperationException("Response exceeds verifier limit.");
            if (Status != 200)
            {
                ReadSafeError(buffer.AsMemory(0, count));
                throw new InvalidOperationException("Unexpected verifier HTTP status.");
            }
            if (index == 0)
            {
                using var json = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
                var root = json.RootElement;
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => !names.Add(p.Name))
                    || !root.TryGetProperty("public", out var value) || value.ValueKind != JsonValueKind.False)
                    throw new InvalidOperationException("Bucket privacy not established.");
            }
            var replacement = new ByteArrayContent(buffer.AsSpan(0, count).ToArray());
            response.Content.Dispose();
            response.Content = replacement;
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    private void ReadSafeError(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => !names.Add(p.Name))) return;
            if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                && code.GetString() is "not_found" or "NoSuchKey" or "AccessDenied" or "InvalidJWT" or "InvalidRequest")
                ErrorCode = code.GetString()!;
        }
        catch { /* Raw bodies and exception details are never output. */ }
    }
}
