using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Services.Monetization;

public interface ICommercePayOs
{
    bool IsLive { get; }
    string Channel { get; }
    JsonElement Verify(JsonElement envelope);
    Task<JsonElement> Create(MuaCommerceOrder order, long code, CancellationToken ct);
    Task<JsonElement?> Get(long code, CancellationToken ct);
    Task<JsonElement> Cancel(long code, CancellationToken ct);
}
public sealed record CommerceMail(Guid JobId, string Recipient, string Subject, string Html);
public interface ICommerceEmailSender
{
    bool IsLive { get; }
    Task<string> Send(CommerceMail mail, CancellationToken ct);
}

// Dedicated channel and credentials: never routes through booking's payment tables,
// PayOS client configuration, webhook or refund/payout implementation.
public sealed class CommercePayOsHttp(HttpClient client, IConfiguration config, IOptions<MuaMonetizationOptions> flags) : ICommercePayOs
{
    public bool IsLive => true;
    public string Channel => Required("Channel");
    private string Required(string key) => !string.IsNullOrWhiteSpace(config["CommercePayOS:" + key]) ? config["CommercePayOS:" + key]! : throw new CommerceException("PAYOS_CONFIG_MISSING");
    public JsonElement Verify(JsonElement envelope) => PayOsCommerceProtocol.VerifiedData(envelope, Required("ChecksumKey"));
    public Task<JsonElement> Create(MuaCommerceOrder order, long code, CancellationToken ct)
    {
        var returnUrl = Required("ReturnUrl"); var cancelUrl = Required("CancelUrl");
        // Order ID is a navigation hint, never payment evidence or authorization.
        returnUrl += (returnUrl.Contains('?') ? "&" : "?") + "order=" + order.Id;
        cancelUrl += (cancelUrl.Contains('?') ? "&" : "?") + "order=" + order.Id;
        var host = new Uri(returnUrl).Host;
        if (new Uri(cancelUrl).Host != host) throw new CommerceException("INVALID_RETURN_URL");
        return Call(HttpMethod.Post, "/v2/payment-requests", PayOsCommerceProtocol.CreateLinkRequest(order, code, returnUrl, cancelUrl, Required("ChecksumKey"), host), ct);
    }
    public async Task<JsonElement?> Get(long code, CancellationToken ct)
    {
        try { return await Call(HttpMethod.Get, $"/v2/payment-requests/{code}", null, ct); }
        catch (CommerceException ex) when (ex.Code == "PAYOS_NOT_FOUND") { return null; }
    }
    public Task<JsonElement> Cancel(long code, CancellationToken ct) => Call(HttpMethod.Post, $"/v2/payment-requests/{code}/cancel", JsonSerializer.SerializeToElement(new { cancellationReason = "BBook order expired" }), ct);
    private async Task<JsonElement> Call(HttpMethod method, string path, JsonElement? body, CancellationToken ct)
    {
        if (!flags.Value.LivePaymentsEnabled) throw new CommerceException("LIVE_PAYOS_DISABLED");
        using var request = new HttpRequestMessage(method, "https://api-merchant.payos.vn" + path);
        request.Headers.Add("x-client-id", Required("ClientId")); request.Headers.Add("x-api-key", Required("ApiKey"));
        if (body.HasValue) request.Content = JsonContent.Create(body.Value);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new CommerceException("PAYOS_NOT_FOUND");
        if (!response.IsSuccessStatusCode) throw new CommerceException("PAYOS_HTTP_" + (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Verify(json.RootElement); return json.RootElement.Clone();
    }
}

// Reuses the existing Brevo account, sender environment keys and HTTP API. OTP's
// interface and implementation are unchanged. A distinct contract supplies durable
// commerce message IDs and provider idempotency without widening OTP privileges.
public sealed class BrevoCommerceEmailSender(HttpClient client, IConfiguration config, IOptions<MuaMonetizationOptions> flags) : ICommerceEmailSender
{
    public bool IsLive => true;
    public async Task<string> Send(CommerceMail mail, CancellationToken ct)
    {
        if (!flags.Value.LiveEmailEnabled || !config.GetValue<bool>("CommerceEmail:SenderDomainVerified")) throw new CommerceException("LIVE_EMAIL_DISABLED_OR_DOMAIN_UNVERIFIED");
        var sender = config["Email:FromEmail"];
        if (!System.Net.Mail.MailAddress.TryCreate(sender, out var address) || address.Host != "bbookmakeup.com") throw new CommerceException("INVALID_SENDER_DOMAIN");
        var key = config["Email:BrevoApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new CommerceException("EMAIL_CONFIG_MISSING");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", key);
        request.Content = JsonContent.Create(new { sender = new { name = config["Email:FromName"] ?? "BBook", email = sender },
            to = new[] { new { email = mail.Recipient } }, subject = mail.Subject, htmlContent = mail.Html,
            headers = new Dictionary<string, string> { ["idempotencyKey"] = mail.JobId.ToString(), ["X-Mailin-custom"] = "commerce-job:" + mail.JobId },
            tags = new[] { "commerce-job:" + mail.JobId } });
        using var response = await client.SendAsync(request, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
        {
            if (json.RootElement.TryGetProperty("code", out var code) && code.GetString() == "duplicate_parameter") throw new CommerceException("EMAIL_ACCEPTED_UNKNOWN");
            throw new CommerceException("BREVO_HTTP_" + (int)response.StatusCode);
        }
        return PayOsCommerceProtocol.Text(json.RootElement, "messageId");
    }
}
