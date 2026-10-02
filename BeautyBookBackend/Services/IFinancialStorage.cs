namespace BeautyBookBackend.Services;

public interface IFinancialStorage : IVerificationStorage { }

// Reuse the private storage transport; never fall back to identity/public buckets.
public sealed class SupabaseFinancialStorage : IFinancialStorage
{
    private readonly SupabaseVerificationStorage inner;
    private readonly string bucket;
    private readonly string identity;
    public SupabaseFinancialStorage(HttpClient http, IConfiguration config)
    {
        bucket = config["Supabase:FinancialBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_FINANCIAL_BUCKET") ?? "";
        identity = config["Supabase:VerificationBucket"] ?? Environment.GetEnvironmentVariable("SUPABASE_VERIFICATION_BUCKET") ?? "verification-private";
        inner = new(http, config, bucket);
    }
    private void Validate() { if (string.IsNullOrWhiteSpace(bucket) || bucket == identity) throw new InvalidOperationException("Financial storage requires a separate configured private bucket."); }
    public string LocationId { get { Validate(); return inner.LocationId; } }
    public Task EnsurePrivateAsync(CancellationToken ct = default) { Validate(); return inner.EnsurePrivateAsync(ct); }
    public Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) { Validate(); return inner.UploadAsync(key, bytes, ct); }
    public Task<string> SignAsync(string key, CancellationToken ct = default) { Validate(); return inner.SignAsync(key, ct); }
    public async Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default)
    {
        if (legacy) throw new InvalidOperationException("Financial legacy copy requires a separately authorized migration.");
        await EnsurePrivateAsync(ct); return await inner.DownloadAsync(key, ct: ct);
    }
    public async Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default)
    {
        if (legacy) throw new InvalidOperationException("Financial legacy delete is not supported by this endpoint.");
        await EnsurePrivateAsync(ct); await inner.DeleteAsync(key, ct: ct);
    }
    public bool TryParseLegacyUrl(string url, out string key) { key = ""; return false; }
}
