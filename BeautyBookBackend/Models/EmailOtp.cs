namespace BeautyBookBackend.Models;

public class EmailOtp
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? UsedAt { get; set; }
}
