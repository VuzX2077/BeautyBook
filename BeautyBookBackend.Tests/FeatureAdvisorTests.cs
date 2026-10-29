using System.Net;
using System.Text.Json;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Options;
using Xunit;
using BeautyBookBackend.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

public sealed class FeatureAdvisorTests
{
    static FaceFeatureSummaryV3 Summary() => new(3,"face_mesh",new(true,true,false),new() { ["outlineAspect"]=1.25,["eyeSpacing"]=.4 },[],[FeatureAdvisorContract.Limitations[0]],new() { ["outlineAspect"]="face_mesh",["eyeSpacing"]="face_mesh" },new(1,new(.2,.2,.5,.6),new(0,0,0)));
    static FeatureAdvisorResponse Output() => new([Row("korean"),Row("event"),Row("portrait")]);
    static FeatureAdvisorRecommendation Row(string key) => new(key,"Style","Description","Explore",["Eyes","Brows","Cheeks","Lips"],["Pink"],["Event"],["Sự kiện"]);
    [Fact] public void ValidatesSchemaConsentAndRanges()
    {
        var request = new FeatureAdvisorRequest(Guid.NewGuid(),true,Summary());
        Assert.True(FeatureAdvisorContract.Valid(request));
        Assert.False(FeatureAdvisorContract.Valid(request with { Consent=false }));
        Assert.False(FeatureAdvisorContract.Valid(request with { RequestId=Guid.Empty }));
        Assert.False(FeatureAdvisorContract.Valid(request with { Summary=Summary() with { SchemaVersion=99 } }));
        Assert.False(FeatureAdvisorContract.Valid(request with { Summary=Summary() with { Features=new() { ["rawMesh"]=1 } } }));
        foreach(var value in new[] { -1d,3d,double.NaN,double.PositiveInfinity })
            Assert.False(FeatureAdvisorContract.Valid(request with { Summary=Summary() with { Features=new() { ["outlineAspect"]=value } } }));
    }
    [Theory] [InlineData("image")] [InlineData("mesh")] [InlineData("uri")] [InlineData("email")]
    public void RejectsUnknownAndImageFields(string field)
    {
        var request=JsonSerializer.Serialize(new FeatureAdvisorRequest(Guid.NewGuid(),true,Summary()),FeatureAdvisorContract.Json);
        var modified=request[..^1]+",\""+field+"\":\"forbidden\"}";
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<FeatureAdvisorRequest>(modified,FeatureAdvisorContract.Json));
    }
    [Fact] public void ChecksExactlyThreeStylesAndRequiredFields()
    {
        Assert.True(FeatureAdvisorContract.ValidOutput(Output(),Summary()));
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("korean"),Row("event")]),Summary()));
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("fake"),Row("event"),Row("portrait")]),Summary()));
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("event"),Row("event"),Row("portrait")]),Summary()));
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("korean") with { ServiceQueryKeywords=[] },Row("event"),Row("portrait")]),Summary()));
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("korean") with { MakeupFocus=[""] },Row("event"),Row("portrait")]),Summary()));
    }
    [Fact] public void NumericStringsAreNotAcceptedAsMeasurements()
    {
        var json=JsonSerializer.Serialize(new FeatureAdvisorRequest(Guid.NewGuid(),true,Summary()),FeatureAdvisorContract.Json);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<FeatureAdvisorRequest>(json.Replace("1.25","\"1.25\""),FeatureAdvisorContract.Json));
    }
    [Fact] public void MissingPoseAndQualityNumbersNeverBecomeInventedZeroDefaults() {
        var json=JsonSerializer.Serialize(new FeatureAdvisorRequest(Guid.NewGuid(),true,Summary()),FeatureAdvisorContract.Json);
        foreach(var field in new[]{"\"yaw\":0,","\"pitch\":0,","\"expressionLimited\":false"}) {
            var modified=json.Replace(field,"").Replace(",}","}");
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<FeatureAdvisorRequest>(modified,FeatureAdvisorContract.Json));
        }
        Assert.False(FeatureAdvisorContract.Valid(new(Guid.NewGuid(),true,Summary() with {Detection=null!})));
    }
    [Fact] public void NullListTextIsInvalidWithoutThrowing()
    {
        Assert.False(FeatureAdvisorContract.ValidOutput(new([Row("korean") with { MakeupFocus=[null!] },Row("event"),Row("portrait")]),Summary()));
    }
    sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Count; public string? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Count++; Sent=await request.Content!.ReadAsStringAsync(ct);return new(status) { Content=new StringContent(body) }; }
    }
    static FeatureAdvisorOptions Enabled() => new() { Enabled=true,CloudEnabled=true,DataProcessingApproval="approved",ProviderDataPolicy="paid_terms",RealUserDataApproved=true,ModelAvailabilityVerified=true,Model="gemini-3.5-flash-lite",ApiKey="synthetic-test-key" };
    [Fact] public async Task DefaultGateNeverSendsRealFeatures()
    {
        var handler=new Handler(HttpStatusCode.OK,"");var p=new FeatureAdvisorProvider(new(handler),Options.Create(new FeatureAdvisorOptions()));
        Assert.False(p.Available);Assert.Null(await p.Recommend(Summary(),default));Assert.Equal(0,handler.Count);
    }
    [Theory][InlineData("unpaid","approved")][InlineData("paid_terms","pending")]
    public async Task DataReviewAndProviderTermsCannotBeBypassedByOwnerConsent(string policy,string approval) {
        var h=new Handler(HttpStatusCode.OK,"");var config=Enabled();config.ProviderDataPolicy=policy;config.DataProcessingApproval=approval;
        var p=new FeatureAdvisorProvider(new(h),Options.Create(config));Assert.False(p.Available);Assert.Null(await p.Recommend(Summary(),default));Assert.Equal(0,h.Count);
    }
    [Fact] public void EnvironmentGateClosesOnlineDefaultsAndDiagnostics() {
        var config=Enabled();config.SyntheticDiagnosticsEnabled=true;config.RequireHttps=false;
        FeatureAdvisorProvider.ApplyEnvironmentGate(config,false);Assert.False(config.Enabled);Assert.False(config.SyntheticDiagnosticsEnabled);Assert.True(config.RequireHttps);
        config=Enabled();config.OnlineEnvironmentApproved=true;config.SyntheticDiagnosticsEnabled=true;
        FeatureAdvisorProvider.ApplyEnvironmentGate(config,false);Assert.True(config.Enabled);Assert.False(config.SyntheticDiagnosticsEnabled);
    }
    [Theory] [InlineData(429)] [InlineData(503)] [InlineData(404)] [InlineData(401)]
    public async Task ProviderErrorsFallBackWithOneAttempt(int code)
    {
        var h=new Handler((HttpStatusCode)code,"");var p=new FeatureAdvisorProvider(new(h),Options.Create(Enabled()));
        Assert.Null(await p.Recommend(Summary(),default));Assert.Equal(1,h.Count);Assert.DoesNotContain("uri",h.Sent!);Assert.DoesNotContain("allPoints",h.Sent!);
    }
    [Theory] [InlineData("{}")] [InlineData("invalid json")]
    public async Task MalformedProviderOutputFallsBack(string body)
    {
        var h=new Handler(HttpStatusCode.OK,body);var p=new FeatureAdvisorProvider(new(h),Options.Create(Enabled()));
        Assert.Null(await p.Recommend(Summary(),default));Assert.Equal(1,h.Count);
    }
    [Fact] public async Task StructuredProviderOutputIsValidated()
    {
        var output=JsonSerializer.Serialize(Output(),FeatureAdvisorContract.Json);
        var body=JsonSerializer.Serialize(new { candidates=new[] { new { finishReason="STOP",content=new { parts=new[] { new { text=output } } } } } });
        var h=new Handler(HttpStatusCode.OK,body);var p=new FeatureAdvisorProvider(new(h),Options.Create(Enabled()));
        Assert.NotNull(await p.Recommend(Summary(),default));Assert.Equal(1,h.Count);
    }
    [Fact] public async Task DuplicateUuidDoesNotRepeatProviderCall()
    {
        var h=new Handler(HttpStatusCode.ServiceUnavailable,"");var p=new FeatureAdvisorProvider(new(h),Options.Create(Enabled()));
        using var cache=new MemoryCache(new MemoryCacheOptions());
        await using var store = await BeautyBookBackend.Tests.PlayReviewTestStore.CreateAsync();
        var owner = Guid.NewGuid();
        store.Db.Users.Add(new() { UserId = owner, IsActive = true, Role = BeautyBookBackend.Models.Enums.UserRole.Customer, CreatedAt = DateTime.UtcNow });
        await store.Db.SaveChangesAsync();
        var access = new BeautyBookBackend.Services.Monetization.CustomerAiCommerce(store.Db, null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), TimeProvider.System);
        var controller=new MakeupAdvisorController(p,cache,access) { ControllerContext=new() { HttpContext=new DefaultHttpContext() } };
        controller.Request.Scheme="https";controller.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,owner.ToString())],"Test"));
        var body=JsonSerializer.SerializeToElement(new FeatureAdvisorRequest(Guid.NewGuid(),true,Summary()),FeatureAdvisorContract.Json);
        Assert.Equal(503,Assert.IsType<ObjectResult>(await controller.Recommend(body,default)).StatusCode);
        Assert.IsType<ConflictObjectResult>(await controller.Recommend(body,default));Assert.Equal(1,h.Count);
    }
    sealed class CancelledHandler : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Count++; throw new TaskCanceledException("synthetic timeout"); }
    }
    [Fact] public async Task TimeoutFallsBackWithoutRetry()
    {
        var h=new CancelledHandler();var p=new FeatureAdvisorProvider(new(h),Options.Create(Enabled()));
        Assert.Null(await p.Recommend(Summary(),default));Assert.Equal(1,h.Count);
    }
}
