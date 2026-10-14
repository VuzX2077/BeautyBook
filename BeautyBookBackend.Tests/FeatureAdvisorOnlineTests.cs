using BeautyBookAdvisor.Api;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using Xunit;

public sealed class FeatureAdvisorOnlineTests
{
    const string UserId="00000000-0000-4000-8000-000000000099";
    sealed class IdentityHandler(HttpStatusCode status,string body):HttpMessageHandler {
        public int Calls;public string? Path;public string? Auth;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;Path=r.RequestUri!.ToString();Auth=r.Headers.Authorization?.ToString();return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});}
    }
    sealed class NoGemini(FeatureAdvisorResponse? output=null):HttpMessageHandler {public int Calls;public string? Sent;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;Sent=await r.Content!.ReadAsStringAsync(ct);return output is null?new(HttpStatusCode.ServiceUnavailable):new(HttpStatusCode.OK){Content=JsonContent.Create(new {candidates=new[]{new {finishReason="STOP",content=new {parts=new[]{new {text=System.Text.Json.JsonSerializer.Serialize(output,FeatureAdvisorContract.Json)}}}}}})};}}
    static async Task<WebApplication> App(IdentityHandler identity,NoGemini gemini, bool approved=false,bool render=false) {
        var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Staging"});b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["AdvisorHost:IdentityApiUrl"]="https://bbook.example/api",["FeatureAdvisor:Enabled"]="true",["FeatureAdvisor:OnlineEnvironmentApproved"]=approved.ToString()});
        if(render)b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["RENDER"]="true",["AdvisorHost:TrustRenderProxy"]="true"});
        AdvisorApiHost.Configure(b);
        b.Services.AddHttpClient("BBookIdentity").ConfigurePrimaryHttpMessageHandler(()=>identity);
        b.Services.AddSingleton(new FeatureAdvisorProvider(new HttpClient(gemini),Options.Create(approved?new FeatureAdvisorOptions{Enabled=true,CloudEnabled=true,RealUserDataApproved=true,DataProcessingApproval="approved",ProviderDataPolicy="paid_terms",ModelAvailabilityVerified=true,Model="gemini-3.5-flash-lite",ApiKey="synthetic-test-key"}:new FeatureAdvisorOptions())));
        var app=b.Build();AdvisorApiHost.Map(app);await app.StartAsync();return app;
    }
    static HttpClient Client(WebApplication app,bool token=true){var c=app.GetTestClient();c.BaseAddress=new("https://localhost");if(token)c.DefaultRequestHeaders.Authorization=new("Bearer","synthetic-jwt");return c;}
    [Fact] public async Task OnlineAuthDelegatesExistingBearerAndMapsOnlyAdvisorWithoutDbOrSigningKey() {
        var identity=new IdentityHandler(HttpStatusCode.OK,$"{{\"userId\":\"{UserId}\",\"isActive\":true}}");var gemini=new NoGemini();await using var app=await App(identity,gemini);
        var client=Client(app);var cap=await client.GetAsync("/api/MakeupAdvisor/capabilities");Assert.Equal(HttpStatusCode.OK,cap.StatusCode);
        Assert.Equal("https://bbook.example/api/User/profile",identity.Path);Assert.Equal("Bearer synthetic-jwt",identity.Auth);
        var response=await client.PostAsJsonAsync("/api/MakeupAdvisor/recommend",new FeatureAdvisorRequest(Guid.NewGuid(),true,FeatureAdvisorProvider.SyntheticFixture()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Equal(0,gemini.Calls);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/User/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/diagnostics/recommend")).StatusCode);
        Assert.Null(app.Services.GetService<BeautyBookBackend.Data.ApplicationDbContext>());
    }
    [Theory][InlineData(401,"{}")] [InlineData(503,"{}")] [InlineData(200,"{\"userId\":\"bad\",\"isActive\":true}")] [InlineData(200,"{\"userId\":\"00000000-0000-4000-8000-000000000099\",\"isActive\":false}")] [InlineData(200,"not json")]
    public async Task InvalidOrInactiveIdentityFailsClosedWithoutGemini(int code,string body){var i=new IdentityHandler((HttpStatusCode)code,body);var g=new NoGemini();await using var app=await App(i,g);Assert.Equal(HttpStatusCode.Unauthorized,(await Client(app).GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);Assert.Equal(0,g.Calls);}
    [Fact] public async Task MissingBearerHttpAndLocalNonceNeverAuthorizeOnline() {
        var i=new IdentityHandler(HttpStatusCode.OK,"{}");var g=new NoGemini();await using var app=await App(i,g);var c=Client(app,false);c.DefaultRequestHeaders.Add("X-Advisor-Local",new string('A',48));
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);Assert.Equal(0,i.Calls);
        var http=app.GetTestClient();http.BaseAddress=new("http://localhost");Assert.Equal((HttpStatusCode)426,(await http.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);Assert.Equal(0,i.Calls);
    }
    [Fact] public async Task OnlineRequestsAreRateLimited(){var i=new IdentityHandler(HttpStatusCode.OK,$"{{\"userId\":\"{UserId}\",\"isActive\":true}}");var g=new NoGemini();await using var app=await App(i,g);var c=Client(app);for(var n=0;n<12;n++)Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);Assert.Equal(HttpStatusCode.TooManyRequests,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);}
    [Fact] public void InsecureIdentityUrlRejectedBeforeStartup(){var b=WebApplication.CreateBuilder();b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["AdvisorHost:IdentityApiUrl"]="http://localhost/api"});Assert.Throws<InvalidOperationException>(()=>AdvisorApiHost.Configure(b));}
    [Fact] public async Task ApprovedHttpsPipelineAuthenticatesAndSendsOnlyMinimalProfileToProvider() {
        FeatureAdvisorRecommendation Row(string key)=>new(key,"Style","Reference","Explore measured eye spacing",["Eyes","Brows","Cheeks","Lips"],["Pink"],["Event"],["Sự kiện"]);
        var i=new IdentityHandler(HttpStatusCode.OK,$"{{\"userId\":\"{UserId}\",\"isActive\":true}}");var g=new NoGemini(new([Row("korean"),Row("event"),Row("portrait")]));await using var app=await App(i,g,true);
        var summary=FeatureAdvisorProvider.SyntheticFixture();var response=await Client(app).PostAsJsonAsync("/api/MakeupAdvisor/recommend",new FeatureAdvisorRequest(Guid.NewGuid(),true,summary));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(3,(await response.Content.ReadFromJsonAsync<FeatureAdvisorResponse>())!.Recommendations.Length);
        Assert.Equal(1,g.Calls);using var envelope=System.Text.Json.JsonDocument.Parse(g.Sent!);var text=envelope.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();using var prompt=System.Text.Json.JsonDocument.Parse(text!);
        var profile=prompt.RootElement.GetProperty("makeupProfile").Deserialize<MakeupProfile>(FeatureAdvisorContract.Json)!; Assert.True(MakeupProfileConverter.Valid(profile)); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(MakeupProfileConverter.From(summary),FeatureAdvisorContract.Json),System.Text.Json.JsonSerializer.Serialize(profile,FeatureAdvisorContract.Json)); Assert.False(prompt.RootElement.TryGetProperty("measurements",out _));Assert.DoesNotContain("recommendations",text);Assert.DoesNotContain("synthetic-jwt",g.Sent!);Assert.DoesNotContain(UserId,g.Sent!);
    }
    [Theory][InlineData(false,426)][InlineData(true,200)]public async Task ForwardedHttpsAcceptedOnlyWithExplicitRenderProxyTrust(bool render,int expected) {
        var i=new IdentityHandler(HttpStatusCode.OK,$"{{\"userId\":\"{UserId}\",\"isActive\":true}}");var g=new NoGemini();await using var app=await App(i,g,false,render);
        var c=Client(app);c.BaseAddress=new("http://localhost");c.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");Assert.Equal((HttpStatusCode)expected,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
    }
}
