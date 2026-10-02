using System.Net;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class FinancialStorageTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public bool Public;
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri.AbsolutePath.Contains("/bucket/") ? "{\"public\":" + Public.ToString().ToLowerInvariant() + "}" : "{}") });
        }
    }
    private static IConfiguration Config(string bucket) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Supabase:Url"]="https://storage.example.test",["Supabase:ServiceRoleKey"]="local-test-only",["Supabase:FinancialBucket"]=bucket,["Supabase:VerificationBucket"]="verification-private",["Supabase:StorageBucket"]="images" }).Build();

    [Theory]
    [InlineData("")]
    [InlineData("images")]
    [InlineData("verification-private")]
    public async Task MissingOrSharedBucketFailsWithoutAnyStorageRequest(string bucket)
    {
        var handler=new Handler();var storage=new SupabaseFinancialStorage(new HttpClient(handler),Config(bucket));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.UploadAsync("financial/test/test.jpg",[1]));Assert.Empty(handler.Requests);
    }
    [Fact]
    public async Task PublicBucketBlocksUploadReadAndDeletionAndOnlyPrivateFinancialLocationIsUsed()
    {
        var handler=new Handler { Public=true };var storage=new SupabaseFinancialStorage(new HttpClient(handler),Config("financial-private"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.UploadAsync("financial/test/test.jpg",[1]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.DownloadAsync("financial/test/test.jpg"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.DeleteAsync("financial/test/test.jpg"));
        Assert.All(handler.Requests,x=>Assert.StartsWith("GET /storage/v1/bucket/financial-private",x));
        handler.Public=false;await storage.UploadAsync("financial/test/test.jpg",[1]);Assert.Contains("POST /storage/v1/object/financial-private/financial/test/test.jpg",handler.Requests);Assert.DoesNotContain(handler.Requests,x=>x.Contains("/object/public/")||x.Contains("/object/images/"));
        handler.Public=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.DeleteAsync("financial/test/test.jpg"));Assert.DoesNotContain(handler.Requests,x=>x.StartsWith("DELETE"));
    }
}
