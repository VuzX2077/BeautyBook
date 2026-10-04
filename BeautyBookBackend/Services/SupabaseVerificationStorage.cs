using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace BeautyBookBackend.Services;

public sealed class SupabaseVerificationStorage : IVerificationStorage
{
    private readonly HttpClient _http;
    private readonly string _origin;
    private readonly string _secret;
    private readonly string _bucket;
    private readonly string _legacyBucket;
    public string LocationId {
        get {
            ValidateConfig();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(_origin).GetLeftPart(UriPartial.Authority) + "/" + _bucket + "/" + _legacyBucket)));
        }
    }
    public SupabaseVerificationStorage(HttpClient http, IConfiguration config)
        : this(http, config, config["Supabase:VerificationBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_VERIFICATION_BUCKET") ?? "verification-private") { }
    internal SupabaseVerificationStorage(HttpClient http, IConfiguration config, string privateBucket)
    {
        _http = http;
        _origin = (config["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "").TrimEnd('/');
        _secret = config["Supabase:ServiceRoleKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? "";
        _bucket = privateBucket;
        _legacyBucket = config["Supabase:StorageBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_STORAGE_BUCKET") ?? "images";
    }
    private void ValidateConfig()
    {
        if (!Uri.TryCreate(_origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || string.IsNullOrWhiteSpace(_secret)
            || string.IsNullOrWhiteSpace(_bucket) || _bucket == _legacyBucket)
            throw new InvalidOperationException("Verification storage configuration is invalid.");
    }
    private static string Path(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains('\\') || key.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidOperationException("Invalid storage object key.");
        return string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? body, CancellationToken ct)
    {
        ValidateConfig();
        using var request = new HttpRequestMessage(method, $"{_origin}/storage/v1/{path}");
        SupabaseStorageAuthentication.Apply(request, _secret);
        if (method == HttpMethod.Post && path.StartsWith("object/", StringComparison.Ordinal) && !path.StartsWith("object/sign/", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation("cache-control", "0");
        request.Content = body;
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            if (status == 404 && path.StartsWith("object/authenticated/", StringComparison.Ordinal)) throw new StorageObjectMissingException();
            // Never include provider bodies, URLs or service credentials in client-visible errors.
            throw new InvalidOperationException($"Verification storage request failed ({status}).");
        }
        return response;
    }
    public async Task EnsurePrivateAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"bucket/{Uri.EscapeDataString(_bucket)}", null, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("public", out var value) || value.ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("Verification bucket must be private.");
    }
    public async Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default)
    {
        await EnsurePrivateAsync(ct);
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var response = await SendAsync(HttpMethod.Post, $"object/{Uri.EscapeDataString(_bucket)}/{Path(key)}", body, ct);
    }
    public async Task<string> SignAsync(string key, CancellationToken ct = default)
    {
        await EnsurePrivateAsync(ct);
        using var body = JsonContent.Create(new { expiresIn = 120 });
        using var response = await SendAsync(HttpMethod.Post, $"object/sign/{Uri.EscapeDataString(_bucket)}/{Path(key)}", body, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var signed = json.RootElement.GetProperty("signedURL").GetString() ?? "";
        var full = signed.StartsWith("/object/sign/", StringComparison.Ordinal) ? $"{_origin}/storage/v1{signed}" : signed;
        if (!Uri.TryCreate(full, UriKind.Absolute, out var url) || url.Scheme != "https" || url.GetLeftPart(UriPartial.Authority) != _origin
            || !url.AbsolutePath.StartsWith($"/storage/v1/object/sign/{Uri.EscapeDataString(_bucket)}/", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid signed media URL.");
        return full;
    }
    public async Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"object/authenticated/{Uri.EscapeDataString(legacy ? _legacyBucket : _bucket)}/{Path(key)}", null, ct);
        if (response.Content.Headers.ContentLength > VerificationImage.MaxBytes) throw new InvalidOperationException("Media exceeds size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + read > VerificationImage.MaxBytes) throw new InvalidOperationException("Media exceeds size limit.");
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }
    public async Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default)
    {
        using var body = JsonContent.Create(new { prefixes = new[] { key } });
        _ = Path(key);
        using var response = await SendAsync(HttpMethod.Delete, $"object/{Uri.EscapeDataString(legacy ? _legacyBucket : _bucket)}", body, ct);
    }
    public bool TryParseLegacyUrl(string url, out string key)
    {
        key = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.GetLeftPart(UriPartial.Authority) != _origin || !string.IsNullOrEmpty(uri.Query)) return false;
        var prefix = $"/storage/v1/object/public/{Uri.EscapeDataString(_legacyBucket)}/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        key = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]);
        try { _ = Path(key); return true; } catch (InvalidOperationException) { key = ""; return false; }
    }
}
