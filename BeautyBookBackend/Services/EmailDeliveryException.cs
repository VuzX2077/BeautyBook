namespace BeautyBookBackend.Services;

public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class OtpCooldownException()
    : Exception("Vui lòng đợi 60 giây trước khi yêu cầu mã mới.");
