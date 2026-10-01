namespace BeautyBookBackend.Services;

public static class NotificationChannelResolver
{
    public static string Resolve(string notificationType) => notificationType switch
    {
        "ADMIN_ANNOUNCEMENT" => "default",
        "CHAT_MESSAGE" => "chat",
        var type when type.StartsWith("REFUND_", StringComparison.Ordinal) => "payments",
        _ => "booking-reminders"
    };
}
