using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;

namespace BeautyBookBackend.Services;

public sealed class BrevoEmailSender(
    HttpClient client, IConfiguration configuration, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    public async Task SendOtpAsync(string email, string otp, string purpose, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Email:BrevoApiKey"];
        var fromEmail = configuration["Email:FromEmail"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail)
            || !MailAddress.TryCreate(fromEmail, out _))
        {
            logger.LogError("Configure Email:BrevoApiKey and a valid Email:FromEmail to send OTP emails.");
            throw new EmailDeliveryException("Email provider is not configured.");
        }

        var action = purpose == "REGISTER" ? "đăng ký tài khoản" : "đặt lại mật khẩu";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            sender = new { name = configuration["Email:FromName"] ?? "BBook", email = fromEmail.Trim() },
            to = new[] { new { email } },
            subject = $"Mã OTP BBook - {action}",
            textContent = $"Mã OTP để {action} là: {otp}\n\nMã có hiệu lực trong 5 phút. Không chia sẻ mã này với bất kỳ ai."
        });

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Do not log arbitrary provider responses: they may contain
                // recipient addresses or other sensitive request information.
                string? code = null;
                try
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (body.RootElement.ValueKind == JsonValueKind.Object
                        && body.RootElement.TryGetProperty("code", out var value)
                        && value.ValueKind == JsonValueKind.String)
                        code = value.GetString();
                }
                catch (JsonException) { }
                logger.LogError("Brevo rejected OTP email. HTTP {StatusCode}; provider code {ProviderCode}.", (int)response.StatusCode, code);
                throw new EmailDeliveryException("Email provider rejected the request.");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Brevo OTP email connection failed.");
            throw new EmailDeliveryException("Email provider connection failed.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Brevo OTP email request timed out.");
            throw new EmailDeliveryException("Email provider timed out.", ex);
        }
    }
}
