namespace BeautyBookBackend.Services;

public static class NotificationChannelResolver
{
    public static string Resolve(string notificationType) => notificationType switch
    {
        "ADMIN_ANNOUNCEMENT" => "default",
        "CHAT_MESSAGE" => "chat",
        _ => "booking-reminders"
    };
}
