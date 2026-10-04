using System.Net.Http.Json;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace BeautyBookBackend.Services;

public sealed class BrevoEmailSender(
    HttpClient client, IConfiguration configuration, ILogger<BrevoEmailSender> logger,
    BeautyBookBackend.Data.ApplicationDbContext? db = null, IHttpContextAccessor? httpContextAccessor = null) : IEmailSender
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

        if (db == null) throw new PlayReviewOperationException();
        var policy = new PlayReviewPolicy(db);
        await policy.EnsureExternalActorAsync(httpContextAccessor);
        await policy.EnsureEmailDeliveryAsync(email, purpose);

        var (subject, action, warning) = purpose switch
        {
            "REGISTER" => ("Mã OTP đăng ký tài khoản", "đăng ký tài khoản", "Nếu bạn không yêu cầu mã, hãy bỏ qua email này."),
            "RESET_PASSWORD" => ("Mã OTP đặt lại mật khẩu", "đặt lại mật khẩu", "Nếu bạn không yêu cầu mã, hãy bỏ qua email này."),
            "BANK_ACCOUNT_ADD" => ("Mã OTP xác minh thêm tài khoản nhận tiền", "xác minh thêm tài khoản nhận tiền", "Đây là thao tác thay đổi nơi nhận tiền. Nếu bạn không thực hiện thao tác này, hãy bỏ qua email và đổi mật khẩu nếu nghi ngờ tài khoản bị truy cập."),
            "BANK_ACCOUNT_UPDATE" => ("Mã OTP xác minh thay đổi tài khoản nhận tiền", "xác minh thay đổi tài khoản nhận tiền", "Đây là thao tác thay đổi nơi nhận tiền. Nếu bạn không thực hiện thao tác này, hãy bỏ qua email và đổi mật khẩu nếu nghi ngờ tài khoản bị truy cập."),
            "BANK_ACCOUNT_SET_DEFAULT" => ("Mã OTP xác nhận đổi tài khoản nhận tiền mặc định", "xác nhận thay đổi tài khoản nhận tiền mặc định trên BBook", "Đây là thao tác thay đổi nơi nhận tiền, không phải xác minh quyền sở hữu tài khoản ngân hàng. Nếu bạn không thực hiện thao tác này, hãy bỏ qua email và đổi mật khẩu nếu nghi ngờ tài khoản bị truy cập."),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), "Unsupported OTP purpose.")
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            sender = new { name = configuration["Email:FromName"] ?? "BBook", email = fromEmail.Trim() },
            to = new[] { new { email } },
            subject,
            textContent = $"Mã OTP để {action} là: {otp}\n\nMã có hiệu lực trong 5 phút. Không chia sẻ OTP với bất kỳ ai.\n\n{warning}",
            htmlContent = BuildOtpHtml(otp, action, warning)
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

    private static string BuildOtpHtml(string otp, string action, string warning)
    {
        var safeOtp = WebUtility.HtmlEncode(otp);
        var safeAction = WebUtility.HtmlEncode(action);
        var safeWarning = WebUtility.HtmlEncode(warning);
        // Table layout and inline styles work across mobile email clients.
        // Keep the digits contiguous so the code is easy to select and copy.
        return $$"""
            <!doctype html>
            <html lang="vi">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
            <body style="margin:0;padding:0;background-color:#fff6f8;font-family:Arial,Helvetica,sans-serif;color:#301726;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#fff6f8;">
                <tr><td align="center" style="padding:24px 12px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background-color:#ffffff;border:1px solid #f0d6df;border-radius:16px;">
                    <tr><td align="center" style="padding:28px 20px 12px;font-size:28px;line-height:36px;font-weight:bold;color:#e8436a;">BBook</td></tr>
                    <tr><td align="center" style="padding:0 20px 8px;font-size:20px;line-height:28px;font-weight:bold;color:#301726;">Mã xác thực của bạn</td></tr>
                    <tr><td align="center" style="padding:0 20px 24px;font-size:15px;line-height:24px;color:#5c5461;">Nhập mã bên dưới để {{safeAction}}.</td></tr>
                    <tr><td align="center" style="padding:0 20px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:360px;">
                        <tr><td align="center" bgcolor="#fff0f4" style="padding:24px 8px;background-color:#fff0f4;border:1px solid #f0c4cd;border-radius:12px;font-family:'Courier New',Courier,monospace;font-size:40px;line-height:52px;font-weight:bold;letter-spacing:5px;color:#8b1a2e;">{{safeOtp}}</td></tr>
                      </table>
                    </td></tr>
                    <tr><td align="center" style="padding:20px 20px 8px;font-size:14px;line-height:22px;color:#5c5461;">Mã có hiệu lực trong <strong>5 phút</strong>.</td></tr>
                    <tr><td align="center" style="padding:0 20px 28px;font-size:13px;line-height:21px;color:#5c5461;">Không chia sẻ OTP với bất kỳ ai.<br>{{safeWarning}}</td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
