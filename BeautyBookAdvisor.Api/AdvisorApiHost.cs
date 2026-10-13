using BeautyBookBackend.Controllers;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.HttpOverrides;
using System.Reflection;
using System.Threading.RateLimiting;

namespace BeautyBookAdvisor.Api;
public static class AdvisorApiHost
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var identity=builder.Configuration["AdvisorHost:IdentityApiUrl"];
        if(!Uri.TryCreate(identity,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath.TrimEnd('/')!="/api"||uri.IsLoopback)
            throw new InvalidOperationException("AdvisorHost:IdentityApiUrl must be an approved HTTPS BBook /api URL.");
        builder.Logging.ClearProviders(); // No body/geometry/bearer/profile logging.
        builder.WebHost.ConfigureKestrel(server=>server.Limits.MaxRequestBodySize=12288);
        builder.Services.AddMemoryCache();
        builder.Services.Configure<FeatureAdvisorOptions>(builder.Configuration.GetSection("FeatureAdvisor"));
        builder.Services.PostConfigure<FeatureAdvisorOptions>(o=>FeatureAdvisorProvider.ApplyEnvironmentGate(o,builder.Environment.IsDevelopment()));
        builder.Services.AddHttpClient<FeatureAdvisorProvider>(c=>c.Timeout=TimeSpan.FromSeconds(30)).RemoveAllLoggers();
        builder.Services.AddHttpClient("BBookIdentity",c=>{c.BaseAddress=new Uri(identity!.TrimEnd('/')+"/");c.Timeout=TimeSpan.FromSeconds(3);c.MaxResponseContentBufferSize=65536;})
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler {AllowAutoRedirect=false}).RemoveAllLoggers();
        builder.Services.AddAuthentication("BBookIdentity").AddScheme<AuthenticationSchemeOptions,AdvisorAuthentication>("BBookIdentity",_=>{});
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(MakeupAdvisorController).Assembly).ConfigureApplicationPartManager(m=>{
            m.FeatureProviders.Clear();m.FeatureProviders.Add(new AdvisorControllerProvider());
        });
        builder.Services.AddRateLimiter(o=>{
            o.RejectionStatusCode=429;
            o.AddPolicy("feature-advisor",c=>RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value??"anonymous",_=>new() {PermitLimit=12,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
        });
        // Trust forwarded scheme only on Render's managed ingress when owner
        // explicitly opts in. This does not trust headers on a generic host.
        if(builder.Configuration.GetValue<bool>("AdvisorHost:TrustRenderProxy")&&builder.Configuration["RENDER"]=="true")
            builder.Services.Configure<ForwardedHeadersOptions>(o=>{o.ForwardedHeaders=ForwardedHeaders.XForwardedProto;o.ForwardLimit=1;o.KnownNetworks.Clear();o.KnownProxies.Clear();});
    }
    public static void Map(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async(c,next)=>{if(c.Request.Path.StartsWithSegments("/api")&&!c.Request.IsHttps){c.Response.StatusCode=426;return;}await next(c);});
        app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
        app.MapGet("/health",()=>Results.Ok(new {status="healthy",schemaVersion=3}));
        app.MapControllers();
    }
    sealed class AdvisorControllerProvider:ControllerFeatureProvider
    {protected override bool IsController(TypeInfo type)=>type.AsType()==typeof(MakeupAdvisorController);}
}
