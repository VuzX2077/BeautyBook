using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace BeautyBookBackend.Services;

// Only mapped by the separate Development executable. No DB, JWTs, image
// endpoints, provider key in responses, CORS or external listening interface.
public static class FeatureAdvisorLocalHost
{
    public static void MapLocalAdvisor(this WebApplication app, FeatureAdvisorProvider provider, string nonce)
    {
        if(!app.Environment.IsDevelopment())throw new InvalidOperationException("Local Advisor is Development only.");
        var gate=new SemaphoreSlim(1);
        var seen=new Dictionary<Guid,DateTimeOffset>();
        var window=DateTimeOffset.UtcNow;var calls=0;
        bool Authorized(HttpContext c)=>CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(c.Request.Headers["X-Advisor-Local"].ToString()),Encoding.UTF8.GetBytes(nonce));
        app.MapGet("/api/MakeupAdvisor/capabilities",(HttpContext c)=>{
            c.Response.Headers.CacheControl="no-store";
            return Authorized(c)?Results.Json(new {featureSummaryEnabled=provider.Available,schemaVersion=3}):Results.Unauthorized();
        });
        app.MapPost("/api/MakeupAdvisor/recommend",async (HttpContext c)=>{
            c.Response.Headers.CacheControl="no-store";
            if(!Authorized(c))return Results.Unauthorized();
            if(!c.Request.HasJsonContentType())return Results.StatusCode(415);
            if(c.Request.ContentLength>12288)return Results.StatusCode(413);
            FeatureAdvisorRequest? request;
            try {
                using var bytes=new MemoryStream();var buffer=new byte[4096];int read;
                while((read=await c.Request.Body.ReadAsync(buffer,c.RequestAborted))>0){
                    if(bytes.Length+read>12288)return Results.StatusCode(413);
                    bytes.Write(buffer,0,read);
                }
                request=JsonSerializer.Deserialize<FeatureAdvisorRequest>(bytes.ToArray(),FeatureAdvisorContract.Json);
            }catch(JsonException){return Results.BadRequest(new {code="INVALID_SUMMARY"});}
            if(!FeatureAdvisorContract.Valid(request))return Results.BadRequest(new {code="INVALID_SUMMARY"});
            if(!provider.Available)return Results.Json(new {code="LOCAL_FALLBACK"},statusCode:503);
            if(!await gate.WaitAsync(0,c.RequestAborted))return Results.StatusCode(429);
            try {
                var now=DateTimeOffset.UtcNow;
                foreach(var id in seen.Where(p=>now-p.Value>TimeSpan.FromMinutes(5)).Select(p=>p.Key).ToArray())seen.Remove(id);
                if(seen.ContainsKey(request!.RequestId))return Results.Conflict(new {code="DUPLICATE_REQUEST"});
                if(now-window>=TimeSpan.FromMinutes(1)){window=now;calls=0;}
                if(calls>=12)return Results.StatusCode(429);
                calls++;seen[request.RequestId]=now;
                var result=await provider.Recommend(request.Summary,c.RequestAborted);
                return result is null?Results.Json(new {code="LOCAL_FALLBACK"},statusCode:503):Results.Json(result,FeatureAdvisorContract.Json);
            }finally{gate.Release();}
        });
    }
}
