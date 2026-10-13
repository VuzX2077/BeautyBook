using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Net.Http.Headers;

namespace BeautyBookAdvisor.Api;

// BBook's authenticated profile endpoint validates signature/issuer/audience,
// expiry and active account status. No signing key is copied into this service.
// Response is read transiently; only verified userId becomes an auth claim.
public sealed class AdvisorAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,
    UrlEncoder encoder,IHttpClientFactory factory):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if(!Request.Path.StartsWithSegments("/api/MakeupAdvisor"))return AuthenticateResult.NoResult();
        if(!Request.IsHttps)return AuthenticateResult.Fail("HTTPS required");
        if(!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(),out var auth)||
            !auth.Scheme.Equals("Bearer",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(auth.Parameter)||auth.Parameter.Length>8192)return AuthenticateResult.NoResult();
        try {
            using var request=new HttpRequestMessage(HttpMethod.Get,"User/profile");request.Headers.Authorization=auth;
            using var response=await factory.CreateClient("BBookIdentity").SendAsync(request,HttpCompletionOption.ResponseContentRead,Context.RequestAborted);
            if(!response.IsSuccessStatusCode)return AuthenticateResult.Fail("Account verification unavailable or unauthorized");
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Context.RequestAborted));
            if(!json.RootElement.TryGetProperty("userId",out var id)||!Guid.TryParse(id.GetString(),out var userId)||userId==Guid.Empty||
                !json.RootElement.TryGetProperty("isActive",out var active)||active.ValueKind!=JsonValueKind.True)return AuthenticateResult.Fail("Invalid account");
            var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,userId.ToString())],Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name));
        }catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException){return AuthenticateResult.Fail("Account verification unavailable");}
    }
}
