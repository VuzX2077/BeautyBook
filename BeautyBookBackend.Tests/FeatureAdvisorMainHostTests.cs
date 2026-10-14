using BeautyBookBackend.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Net;
using Xunit;
using BeautyBookBackend.Services;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class FeatureAdvisorMainHostTests
{
    const string Key="synthetic-only-test-signing-key-at-least-64-characters-long-000000000";
    static readonly Guid UserId=Guid.Parse("00000000-0000-4000-8000-000000000088");
    sealed class Factory:WebApplicationFactory<Program> {
        public readonly SqliteConnection Db=new("Data Source=:memory:");
        public int HttpCalls;
        public string? ProviderBody;
        private readonly bool approved;
        sealed class NoNetwork(Factory owner):HttpMessageHandler {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
                owner.HttpCalls++;
                if(!owner.approved||r.RequestUri?.Host!="generativelanguage.googleapis.com")throw new InvalidOperationException("Network forbidden in main-host test");
                owner.ProviderBody=await r.Content!.ReadAsStringAsync(ct);
                FeatureAdvisorRecommendation Row(string key)=>new(key,"Mock provider style","Reference","Uses observed projection",["Eyes","Brows","Cheeks","Lips"],["Pink"],["Event"],["Sự kiện"]);
                return new(HttpStatusCode.OK){Content=JsonContent.Create(new {candidates=new[]{new {finishReason="STOP",content=new {parts=new[]{new {text=JsonSerializer.Serialize(new FeatureAdvisorResponse([Row("korean"),Row("event"),Row("portrait")]),FeatureAdvisorContract.Json)}}}}}})};
            }
        }
        public Factory(bool active=true,bool approved=false) {
            this.approved=approved;
            Db.Open();using var command=Db.CreateCommand();
            command.CommandText="CREATE TABLE Users (UserId TEXT PRIMARY KEY, IsActive INTEGER, DeletedAt TEXT); INSERT INTO Users VALUES ($id,$active,NULL);";
            command.Parameters.AddWithValue("$id",UserId);command.Parameters.AddWithValue("$active",active?1:0);command.ExecuteNonQuery();
        }
        Dictionary<string,string?> Settings()=>new(){["ApplyMigrations"]="false",["Jwt:Key"]=Key,["Jwt:Issuer"]="advisor-test",["Jwt:Audience"]="advisor-test",["ConnectionStrings:DefaultConnection"]="Host=127.0.0.1;Database=never_connected",
            ["FeatureAdvisor:Enabled"]=approved.ToString(),["FeatureAdvisor:CloudEnabled"]=approved.ToString(),["FeatureAdvisor:OnlineEnvironmentApproved"]=approved.ToString(),["FeatureAdvisor:RealUserDataApproved"]=approved.ToString(),["FeatureAdvisor:DataProcessingApproval"]=approved?"approved":"pending",["FeatureAdvisor:ProviderDataPolicy"]=approved?"paid_terms":"unpaid",["FeatureAdvisor:ModelAvailabilityVerified"]=approved.ToString(),["FeatureAdvisor:Model"]="gemini-3.5-flash-lite",["FeatureAdvisor:ApiKey"]="synthetic-test-key"};
        protected override IHost CreateHost(IHostBuilder builder){builder.ConfigureHostConfiguration(c=>c.AddInMemoryCollection(Settings()));return base.CreateHost(builder);}
        protected override void ConfigureWebHost(IWebHostBuilder builder){
            builder.UseEnvironment("Testing");builder.ConfigureLogging(l=>l.ClearProviders());builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(Settings()));
            builder.ConfigureServices(s=>{
                foreach(var d in s.Where(d=>d.ServiceType==typeof(IHostedService)&&d.ImplementationType?.Namespace=="BeautyBookBackend.Services").ToArray())s.Remove(d);
                s.RemoveAll<ApplicationDbContext>();
                s.AddScoped(_=>new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(Db).Options));
                s.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(o=>o.HttpMessageHandlerBuilderActions.Add(b=>b.PrimaryHandler=new NoNetwork(this)));
                s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,o=>o.TokenValidationParameters=new(){ValidateIssuer=true,ValidateAudience=true,ValidateLifetime=true,ValidateIssuerSigningKey=true,ValidIssuer="advisor-test",ValidAudience="advisor-test",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))});
            });
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)Db.Dispose();}
    }
    static string Token(bool expired=false)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("advisor-test","advisor-test",[new Claim(ClaimTypes.NameIdentifier,UserId.ToString()),new Claim(ClaimTypes.Role,"Customer")],DateTime.UtcNow.AddHours(-2),expired?DateTime.UtcNow.AddMinutes(-10):DateTime.UtcNow.AddMinutes(10),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256)));
    [Fact] public async Task MainHostUsesItsJwtAndActiveAccountWithoutIdentityHttpOrMigrations() {
        using var app=new Factory();using var c=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
        c.DefaultRequestHeaders.Authorization=new("Bearer",Token());
        var response=await c.GetAsync("/api/MakeupAdvisor/capabilities");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Contains("\"featureSummaryEnabled\":false",await response.Content.ReadAsStringAsync());
        c.DefaultRequestHeaders.Authorization=new("Bearer",Token(true));Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
        var schemes=await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes,s=>s.Name==JwtBearerDefaults.AuthenticationScheme);Assert.DoesNotContain(schemes,s=>s.Name=="BBookIdentity");Assert.Equal(0,app.HttpCalls);
    }
    [Fact] public async Task MainJwtStillRejectsInactiveAccounts() {
        using var app=new Factory(false);using var c=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});c.DefaultRequestHeaders.Authorization=new("Bearer",Token());
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/MakeupAdvisor/capabilities")).StatusCode);
    }
    [Fact] public async Task MainHostSyntheticSummaryReachesMockProviderOnlyAsMinimalProfile() {
        using var app=new Factory(approved:true);using var c=app.CreateClient(new(){BaseAddress=new("https://localhost"),AllowAutoRedirect=false});c.DefaultRequestHeaders.Authorization=new("Bearer",Token());
        var response=await c.PostAsJsonAsync("/api/MakeupAdvisor/recommend",new FeatureAdvisorRequest(Guid.NewGuid(),true,FeatureAdvisorProvider.SyntheticFixture()),FeatureAdvisorContract.Json);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=await response.Content.ReadFromJsonAsync<FeatureAdvisorResponse>(FeatureAdvisorContract.Json);Assert.Equal(3,result!.Recommendations.Length);
        Assert.Equal(1,app.HttpCalls);Assert.DoesNotContain(UserId.ToString(),app.ProviderBody!);
        using var body=JsonDocument.Parse(app.ProviderBody!);
        var prompt=body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain("boundingBox",prompt);Assert.DoesNotContain("measurements",prompt);Assert.Contains("makeupProfile",prompt);
    }
}
