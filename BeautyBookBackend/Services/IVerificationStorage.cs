namespace BeautyBookBackend.Services;

public sealed class StorageObjectMissingException : InvalidOperationException
{
    public StorageObjectMissingException() : base("Storage object is missing.") { }
}

public interface IVerificationStorage
{
    // Non-secret identity of the configured origin/bucket, persisted with uploads.
    string LocationId => "test-storage";
    Task EnsurePrivateAsync(CancellationToken ct = default);
    Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default);
    Task<string> SignAsync(string key, CancellationToken ct = default);
    Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default);
    Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default);
    bool TryParseLegacyUrl(string url, out string key);
}
