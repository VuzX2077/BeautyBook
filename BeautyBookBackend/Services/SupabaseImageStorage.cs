using System.Net.Http.Headers;

namespace BeautyBookBackend.Services
{
    public sealed class SupabaseImageStorage : IImageStorage
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _serviceRoleKey;
        private readonly string _bucket;

        public SupabaseImageStorage(HttpClient httpClient, IConfiguration configuration)
        {
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
            if (!Uri.TryCreate(_supabaseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(_serviceRoleKey))
            {
                throw new InvalidOperationException(
                    "Supabase Storage is not configured. Set SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY.");
            }

            var objectPath = $"uploads/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}";
            var encodedBucket = Uri.EscapeDataString(_bucket);
            var encodedPath = string.Join('/', objectPath.Split('/').Select(Uri.EscapeDataString));
            var uploadUrl = $"{_supabaseUrl}/storage/v1/object/{encodedBucket}/{encodedPath}";

            using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceRoleKey);
            request.Headers.Add("apikey", _serviceRoleKey);
            request.Headers.Add("x-upsert", "false");
            request.Content = new StreamContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Supabase Storage upload failed ({(int)response.StatusCode}): {detail}");
            }

            return $"{_supabaseUrl}/storage/v1/object/public/{encodedBucket}/{encodedPath}";
        }
    }
}
