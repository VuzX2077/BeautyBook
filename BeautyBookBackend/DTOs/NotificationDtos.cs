using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public class RegisterPushTokenRequest
{
    [Required, MaxLength(255)]
    public string ExpoPushToken { get; set; } = string.Empty;
    [Required, MaxLength(20)]
    public string Platform { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? DeviceName { get; set; }
}

public class UnregisterPushTokenRequest
{
    [Required, MaxLength(255)]
    public string ExpoPushToken { get; set; } = string.Empty;
}
