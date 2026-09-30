namespace BeautyBookBackend.Services;

public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class OtpCooldownException(int seconds = 60)
    : Exception($"Vui lòng đợi {seconds} giây trước khi yêu cầu mã mới.");
