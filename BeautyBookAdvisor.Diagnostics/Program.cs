using BeautyBookBackend.Services;
using Microsoft.Extensions.Options;

// Separate executable: never calls BeautyBookBackend.Program, DbContext,
// migrations, production routes, image-upload or any billing API.
var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName="Development",Args=[] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddUserSecrets<DiagnosticMarker>(optional:true).AddEnvironmentVariables();
builder.Logging.ClearProviders();
var config=builder.Configuration.GetSection("FeatureAdvisor").Get<FeatureAdvisorOptions>() ?? new();
if(!args.Contains("--serve-advisor")&&!config.SyntheticDiagnosticsEnabled) { Console.WriteLine("DIAGNOSTICS_DISABLED: enable FeatureAdvisor:SyntheticDiagnosticsEnabled locally first."); return; }
using var http=new HttpClient { Timeout=TimeSpan.FromSeconds(30),MaxResponseContentBufferSize=1048576 };
var provider=new FeatureAdvisorProvider(http,Options.Create(config));
if(!provider.Configured) { Console.WriteLine("KEY_OR_MODEL_MISSING: use docs/Configure-FeatureAdvisorKey.ps1; never paste a key in chat."); return; }
if(args.Contains("--serve-advisor")&&config.Model!="gemini-3.5-flash-lite") {Console.WriteLine("LOCAL_MODEL_MISMATCH: set FeatureAdvisor:Model to gemini-3.5-flash-lite.");Environment.ExitCode=1;return;}
if(args.Contains("--verify")) {
    var audit=await FeatureAdvisorModelAudit.Check(http,config,default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(audit,FeatureAdvisorContract.Json));
    if(!audit.SelectedModelAvailable) { Environment.ExitCode=1; return; }
    var attempt=await provider.DiagnoseSynthetic(default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { syntheticOnly=true,model=config.Model,attempt.Code,validRecommendations=attempt.Response?.Recommendations.Length ?? 0,styleKeys=attempt.Response?.Recommendations.Select(r=>r.StyleKey) },FeatureAdvisorContract.Json));
    if(attempt.Response is null)Environment.ExitCode=1;
    return;
}
if(!args.Contains("--serve")&&!args.Contains("--serve-advisor")) { Console.WriteLine("Use --verify for synthetic verification, --serve for fixed synthetic diagnostics, or --serve-advisor for the guarded local app flow.");return; }
builder.WebHost.UseUrls("http://127.0.0.1:5088");
var app=builder.Build();
var gate=new SemaphoreSlim(1); DateTimeOffset last=DateTimeOffset.MinValue;
var nonce=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
if(args.Contains("--serve-advisor")) {
    app.MapLocalAdvisor(provider,nonce);
    Console.WriteLine("Local Advisor: http://127.0.0.1:5088/api; use adb reverse tcp:5088 tcp:5088. No database or migrations.");
    Console.WriteLine("Real-data gate available: "+provider.Available);
    Console.WriteLine("BBOOK_ADVISOR_LOCAL_NONCE="+nonce); // Ephemeral loopback token; never the Gemini key.
    await app.RunAsync();return;
}
app.MapPost("/diagnostics/recommend",async (HttpContext context)=> {
    if(context.Request.Headers["X-Advisor-Diagnostic"]!=nonce)return Results.Unauthorized();
    // No caller data is allowed, even a summary claiming to be synthetic.
    if(context.Request.ContentLength is >0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))return Results.BadRequest(new {code="BODY_NOT_ALLOWED"});
    if(!await gate.WaitAsync(0,context.RequestAborted))return Results.StatusCode(429);
    try {
        if(DateTimeOffset.UtcNow-last<TimeSpan.FromMinutes(1))return Results.StatusCode(429);
        last=DateTimeOffset.UtcNow;
        var attempt=await provider.DiagnoseSynthetic(context.RequestAborted);
        context.Response.Headers.CacheControl="no-store";
        return attempt.Response is null ? Results.Json(new {attempt.Code,syntheticOnly=true},statusCode:503) : Results.Json(new {syntheticOnly=true,attempt.Code,result=attempt.Response});
    } finally {gate.Release();}
});
Console.WriteLine("Synthetic diagnostics only: POST http://127.0.0.1:5088/diagnostics/recommend with an empty body. Ctrl+C to stop.");
Console.WriteLine("Local diagnostic header X-Advisor-Diagnostic: "+nonce); // Random local nonce, never the Gemini key.
await app.RunAsync();
public sealed class DiagnosticMarker;
