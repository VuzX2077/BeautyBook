using System.Net.Http.Headers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public sealed class SupabaseImageStorage : IImageStorage
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _serviceRoleKey;
        private readonly string _bucket;
        private readonly ApplicationDbContext? _db;

        public SupabaseImageStorage(HttpClient httpClient, IConfiguration configuration, ApplicationDbContext? db = null)
        {
            _db = db;
            _httpClient = httpClient;
            _supabaseUrl = (configuration["Supabase:Url"]
                ?? Environment.GetEnvironmentVariable("SUPABASE_URL")
                ?? string.Empty).TrimEnd('/');
            _serviceRoleKey = configuration["Supabase:ServiceRoleKey"]
                ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")
                ?? string.Empty;
            _bucket = configuration["Supabase:StorageBucket"]
                ?? Environment.GetEnvironmentVariable("SUPABASE_STORAGE_BUCKET")
                ?? "images";
        }

        public async Task<string> UploadPublicImageAsync(
            Stream content,
            string contentType,
            string extension,
            CancellationToken cancellationToken = default)
        {
            return await UploadCoreAsync(content, contentType, extension, null, cancellationToken);
        }

        public async Task<string> UploadOwnedPublicImageAsync(Guid owner, Stream content, string contentType, string extension, CancellationToken cancellationToken = default)
        {
            if (_db == null) throw new InvalidOperationException("Ownership tracking is required.");
            await using var operation = new MediaOperationLock(_db);
            await operation.AcquireAsync(cancellationToken);
            if (!await _db.Users.AnyAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null, cancellationToken))
                throw new UnauthorizedAccessException("Tài khoản không còn hoạt động.");
            var item = new OwnedPublicMedia { Id = Guid.NewGuid(), OwnerId = owner, CreatedAt = DateTime.UtcNow };
            return await UploadCoreAsync(content, contentType, extension, item, cancellationToken);
        }

        private async Task<string> UploadCoreAsync(Stream content, string contentType, string extension, OwnedPublicMedia? item, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(_supabaseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(_serviceRoleKey))
            {
                throw new InvalidOperationException(
                    "Supabase Storage is not configured. Set SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY.");
            }

            var objectPath = item == null ? $"uploads/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}"
                : $"uploads/{item.OwnerId:N}/{item.Id:N}{extension}";
            var encodedBucket = Uri.EscapeDataString(_bucket);
            var encodedPath = string.Join('/', objectPath.Split('/').Select(Uri.EscapeDataString));
            var uploadUrl = $"{_supabaseUrl}/storage/v1/object/{encodedBucket}/{encodedPath}";
            var publicUrl = $"{_supabaseUrl}/storage/v1/object/public/{encodedBucket}/{encodedPath}";
            if (item != null) {
                item.ObjectKey = objectPath; item.Url = publicUrl;
                _db!.OwnedPublicMedia.Add(item);
                await _db.SaveChangesAsync(cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            request.Headers.Add("apikey", _serviceRoleKey);
            request.Headers.Add("x-upsert", "false");
            request.Content = new StreamContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Supabase Storage upload failed ({(int)response.StatusCode}).");
            }

            if (item != null) { item.ReadyAt = DateTime.UtcNow; await _db!.SaveChangesAsync(cancellationToken); }
            return publicUrl;
        }
    }
}
