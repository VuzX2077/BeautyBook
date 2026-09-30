namespace BeautyBookBackend.Services;

public interface IEmailOtpService
{
    Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default);
    Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default);
}
