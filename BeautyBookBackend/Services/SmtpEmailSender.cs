using System.Net;
using System.Net.Mail;

namespace BeautyBookBackend.Services;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendOtpAsync(string email, string otp, string purpose, CancellationToken cancellationToken = default)
    {
        var host = configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        var port = configuration.GetValue("Email:SmtpPort", 587);
        var username = configuration["Email:Username"]
            ?? throw new InvalidOperationException("Email:Username is not configured.");
        var password = configuration["Email:AppPassword"]
            ?? throw new InvalidOperationException("Email:AppPassword is not configured.");
        var fromName = configuration["Email:FromName"] ?? "BBook";
        var action = purpose == "REGISTER" ? "đăng ký tài khoản" : "đặt lại mật khẩu";

        using var message = new MailMessage
        {
            From = new MailAddress(username, fromName),
            Subject = $"Mã OTP BBook - {action}",
            Body = $"Mã OTP để {action} là: {otp}\n\nMã có hiệu lực trong 5 phút. Không chia sẻ mã này với bất kỳ ai.",
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
