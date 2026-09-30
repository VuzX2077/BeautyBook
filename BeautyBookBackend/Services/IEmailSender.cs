namespace BeautyBookBackend.Services;

public interface IEmailSender
{
    Task SendOtpAsync(string email, string otp, string purpose, CancellationToken cancellationToken = default);
}
