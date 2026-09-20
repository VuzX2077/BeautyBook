using System.Net;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class SupabaseImageStorageTests
{
    [Fact]
    public async Task UploadPublicImageAsync_UsesPrivateServerKeyAndReturnsHttpsPublicUrl()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://project.supabase.co",
                ["Supabase:ServiceRoleKey"] = "server-secret",
                ["Supabase:StorageBucket"] = "public-images"
            })
            .Build();
        var storage = new SupabaseImageStorage(client, configuration);

        var result = await storage.UploadPublicImageAsync(
            new MemoryStream([1, 2, 3]),
            "image/jpeg",
            ".jpg");

        Assert.StartsWith(
            "https://project.supabase.co/storage/v1/object/public/public-images/uploads/",
            result);
        Assert.EndsWith(".jpg", result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.StartsWith(
            "https://project.supabase.co/storage/v1/object/public-images/uploads/",
            handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("server-secret", handler.AuthorizationValue);
        Assert.Equal("server-secret", handler.ApiKey);
        Assert.Equal("image/jpeg", handler.ContentType);
    }

    [Fact]
    public async Task UploadPublicImageAsync_RejectsMissingConfigurationBeforeNetworkCall()
    {
        var handler = new RecordingHandler();
        var storage = new SupabaseImageStorage(
            new HttpClient(handler),
            new ConfigurationBuilder().Build());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.UploadPublicImageAsync(new MemoryStream([1]), "image/png", ".png"));

        Assert.Contains("SUPABASE_URL", exception.Message);
        Assert.Null(handler.Method);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationValue { get; private set; }
        public string? ApiKey { get; private set; }
        public string? ContentType { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationValue = request.Headers.Authorization?.Parameter;
            ApiKey = request.Headers.GetValues("apikey").Single();
            ContentType = request.Content?.Headers.ContentType?.MediaType;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
