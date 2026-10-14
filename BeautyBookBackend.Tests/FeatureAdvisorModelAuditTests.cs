using System.Net;
using System.Net.Http.Json;
using BeautyBookBackend.Services;
using Xunit;

public sealed class FeatureAdvisorModelAuditTests
{
    sealed class Handler : HttpMessageHandler {
        public int Count;public HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Count++;Assert.DoesNotContain("key=",request.RequestUri!.Query);Assert.True(request.Headers.Contains("x-goog-api-key"));
            return Task.FromResult(new HttpResponseMessage(Status) {Content=JsonContent.Create(new {models=new[] {new {name="models/gemini-3.5-flash-lite",supportedGenerationMethods=new[]{"generateContent"},inputTokenLimit=1048576,outputTokenLimit=65536}}})});
        }
    }
    [Fact]public async Task MissingKeyNeverMakesNetworkRequest() {
        var h=new Handler();Assert.Equal("KEY_MISSING",(await FeatureAdvisorModelAudit.Check(new(h),new(),default)).Code);Assert.Equal(0,h.Count);
    }
    [Theory][InlineData(401)][InlineData(403)][InlineData(429)]public async Task InvalidKeyOrQuotaReportsOnlyStatus(int code) {
        var h=new Handler {Status=(HttpStatusCode)code};var result=await FeatureAdvisorModelAudit.Check(new(h),new() {ApiKey="synthetic-test",Model="gemini-3.5-flash-lite"},default);
        Assert.Equal($"HTTP_{code}",result.Code);Assert.Empty(result.AccessibleModels);Assert.False(result.SelectedModelAvailable);Assert.Equal(1,h.Count);
    }
    [Theory][InlineData("gemini-3.5-flash-lite",true)][InlineData("gemini-missing",false)]public async Task VerifiesProjectModelInsteadOfAssumingPublicCatalog(string model,bool accessible) {
        var h=new Handler();var result=await FeatureAdvisorModelAudit.Check(new(h),new() {ApiKey="synthetic-test",Model=model},default);
        Assert.Equal("OK",result.Code);Assert.Equal(accessible,result.SelectedModelAvailable);Assert.Contains("gemini-3.5-flash-lite",result.AccessibleModels);
        if(accessible)Assert.Equal(1048576,result.InputTokenLimit);else Assert.Null(result.InputTokenLimit);
    }
}
