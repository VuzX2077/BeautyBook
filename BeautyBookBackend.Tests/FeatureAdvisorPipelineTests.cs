using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Claims;
using BeautyBookBackend.Services;
using BeautyBookBackend.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class FeatureAdvisorPipelineTests
{
    static FaceFeatureSummaryV3 Fixture(string method="mesh") {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","FrontendFeatureSummary.json")));
        return json.RootElement.GetProperty(method).Deserialize<FaceFeatureSummaryV3>(FeatureAdvisorContract.Json)!;
    }
    static FeatureAdvisorResponse Output() => new([Row("korean"),Row("event"),Row("portrait")]);
    static FeatureAdvisorRecommendation Row(string key) => new(key,"Provider style","Provider description","Explore the measured eye spacing",["Provider eyes","Provider brows","Provider cheeks","Provider lips"],["Pink"],["Event"],["Sự kiện"]);
    static FeatureAdvisorOptions Enabled() => new() {Enabled=true,CloudEnabled=true,DataProcessingApproval="approved",ProviderDataPolicy="paid_terms",RealUserDataApproved=true,ModelAvailabilityVerified=true,Model="gemini-3.5-flash-lite",ApiKey="synthetic-test-key"};
    sealed class Handler(FeatureAdvisorResponse output):HttpMessageHandler {
        public int Count; public string? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct) {
            Count++;Sent=await req.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) {Content=JsonContent.Create(new {candidates=new[] {new {finishReason="STOP",content=new {parts=new[] {new {text=JsonSerializer.Serialize(output,FeatureAdvisorContract.Json)}}}}}})};
        }
    }
    sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e) {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("X-Test-Auth") ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"synthetic-user")],"Test")),"Test")) : AuthenticateResult.NoResult());
    }
    static async Task<WebApplication> App(Handler handler,FeatureAdvisorOptions config,bool local=false) {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName=local?"Development":"Testing"});
        builder.WebHost.UseTestServer();builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Auth>("Test",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(new FeatureAdvisorProvider(new HttpClient(handler),Options.Create(config)));
        builder.Services.AddControllers().AddApplicationPart(typeof(MakeupAdvisorController).Assembly);
        builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("feature-advisor",context=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("test",_=>new() {PermitLimit=12,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();if(local)app.MapLocalAdvisor(new(new HttpClient(handler),Options.Create(config)),new string('A',48));else app.MapControllers();await app.StartAsync();return app;
    }
    [Theory][InlineData("mesh")][InlineData("detection")]
    public async Task ExtractorFixtureThroughHttpCreatesCanonicalMinimalProfile(string method) {
        var summary=Fixture(method);Assert.True(FeatureAdvisorContract.Valid(new(Guid.NewGuid(),true,summary)));
        var h=new Handler(Output());await using var app=await App(h,Enabled());var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");client.DefaultRequestHeaders.Add("X-Test-Auth","yes");
        var response=await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",new FeatureAdvisorRequest(Guid.NewGuid(),true,summary),FeatureAdvisorContract.Json);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("no-store",response.Headers.CacheControl!.ToString());
        var result=await response.Content.ReadFromJsonAsync<FeatureAdvisorResponse>(FeatureAdvisorContract.Json);Assert.Equal("Provider eyes",result!.Recommendations[0].MakeupFocus[0]);
        using var envelope=JsonDocument.Parse(h.Sent!);var prompt=envelope.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        using var data=JsonDocument.Parse(prompt!);
        var profile=data.RootElement.GetProperty("makeupProfile").Deserialize<MakeupProfile>(FeatureAdvisorContract.Json)!;
        Assert.True(MakeupProfileConverter.Valid(profile));
        Assert.Equal(JsonSerializer.Serialize(MakeupProfileConverter.From(summary),FeatureAdvisorContract.Json),JsonSerializer.Serialize(profile,FeatureAdvisorContract.Json));
        Assert.False(data.RootElement.TryGetProperty("measurements",out _));
        Assert.False(data.RootElement.TryGetProperty("detection",out _));
        Assert.False(data.RootElement.TryGetProperty("points",out _));Assert.False(data.RootElement.TryGetProperty("mesh",out _));Assert.DoesNotContain("recommendations",prompt);Assert.DoesNotContain("uri",prompt);Assert.Equal(1,h.Count);
    }
    [Fact] public async Task HttpAuthConsentAndDefaultGatesPreventProviderCalls() {
        var h=new Handler(Output());await using var app=await App(h,new());var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");
        var request=new FeatureAdvisorRequest(Guid.NewGuid(),true,Fixture());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Auth","yes");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request with {Consent=false})).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request)).StatusCode);Assert.Equal(0,h.Count);
    }
    [Theory][InlineData("Bạn có khuôn mặt tròn.")][InlineData("Your face is oval.")][InlineData("Bạn là nữ.")][InlineData("Bạn có undertone ấm.")][InlineData("Phù hợp 99%.")][InlineData("Khoảng cách mắt 0.32.")]
    public async Task UnsupportedAssertionsRejectProviderOutput(string claim) {
        var output=Output();output.Recommendations[0]=output.Recommendations[0] with {WhyItFits=claim};
        var h=new Handler(output);Assert.Null(await new FeatureAdvisorProvider(new(h),Options.Create(Enabled())).Recommend(Fixture(),default));
    }
    [Fact] public void ProvenanceAndRangesCannotBeInvented() {
        var s=Fixture();var r=new FeatureAdvisorRequest(Guid.NewGuid(),true,s);
        Assert.False(FeatureAdvisorContract.Valid(r with {Summary=s with {FeatureSources=[]}}));
        Assert.False(FeatureAdvisorContract.Valid(r with {Summary=s with {FeatureSources=new() { ["uri"]="file:///image" }}}));
        Assert.False(FeatureAdvisorContract.Valid(r with {Summary=s with {Missing=["eyeSpacing"]}}));
        var zero=new Dictionary<string,double>(s.Features) { ["eyeSpacing"]=0 };
        Assert.False(FeatureAdvisorContract.Valid(r with {Summary=s with {Features=zero}}));
    }
    [Fact] public async Task FixedSyntheticDiagnosticDoesNotOpenRealDataGate() {
        var h=new Handler(Output());var config=Enabled();config.RealUserDataApproved=false;config.ModelAvailabilityVerified=false;config.Enabled=false;config.SyntheticDiagnosticsEnabled=true;
        var p=new FeatureAdvisorProvider(new(h),Options.Create(config));Assert.False(p.Available);Assert.Null(await p.Recommend(Fixture(),default));Assert.Equal(0,h.Count);
        Assert.Equal("OK",(await p.DiagnoseSynthetic(default)).Code);Assert.Equal(1,h.Count);
    }
    [Theory][InlineData("ApiKey")][InlineData("Model")]
    public async Task MissingKeyOrModelPreventsBothPaths(string field) {
        var config=Enabled();config.SyntheticDiagnosticsEnabled=true;if(field=="ApiKey")config.ApiKey="";else config.Model="";
        var h=new Handler(Output());var p=new FeatureAdvisorProvider(new(h),Options.Create(config));Assert.False(p.Available);Assert.Null(await p.Recommend(Fixture(),default));Assert.Null((await p.DiagnoseSynthetic(default)).Response);Assert.Equal(0,h.Count);
    }
    [Fact] public async Task LocalHostNonceSchemaAndDuplicateGuardWithoutDatabase() {
        var h=new Handler(Output());await using var app=await App(h,Enabled(),true);var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");
        var request=new FeatureAdvisorRequest(Guid.NewGuid(),true,Fixture());
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Advisor-Local",new string('A',48));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
        var result=await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request);
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.Equal("no-store",result.Headers.CacheControl!.ToString());
        Assert.Equal(3,(await result.Content.ReadFromJsonAsync<FeatureAdvisorResponse>())!.Recommendations.Length);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",request)).StatusCode);Assert.Equal(1,h.Count);
        var json=JsonSerializer.Serialize(request,FeatureAdvisorContract.Json);
        foreach(var body in new[]{json[..^1]+",\"image\":\"base64\"}",json.Replace("\"faceCount\":1","\"faceCount\":2"),json.Replace("\"yaw\":0","\"yaw\":90")})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/api/MakeupAdvisor/recommend",new StringContent(body,System.Text.Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await client.PostAsync("/api/MakeupAdvisor/recommend",new StringContent(new string('x',13000),System.Text.Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal(1,h.Count);
    }
    [Fact] public async Task LocalHostUnavailableReturnsFallbackWithoutProviderCall() {
        var h=new Handler(Output());await using var app=await App(h,new(),true);var client=app.GetTestClient();client.BaseAddress=new Uri("https://localhost");client.DefaultRequestHeaders.Add("X-Advisor-Local",new string('A',48));
        var response=await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",new FeatureAdvisorRequest(Guid.NewGuid(),true,Fixture()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Equal(0,h.Count);
    }
    [Fact] public void ResponseHasExactlyRequestedEightFieldsAndRejectsExtraProviderFields() {
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(Output(),FeatureAdvisorContract.Json));
        var fields=json.RootElement.GetProperty("recommendations")[0].EnumerateObject().Select(p=>p.Name).ToArray();
        Assert.Equal(new[]{"styleKey","title","shortDescription","whyItFits","makeupFocus","colorSuggestions","occasionFit","serviceQueryKeywords"},fields);
        var text=JsonSerializer.Serialize(Output(),FeatureAdvisorContract.Json).Replace("\"styleKey\":\"korean\"","\"styleKey\":\"korean\",\"image\":\"x\"");
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<FeatureAdvisorResponse>(text,FeatureAdvisorContract.Json));
    }
}
