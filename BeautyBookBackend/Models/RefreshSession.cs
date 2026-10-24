namespace BeautyBookBackend.Models;
// Only a SHA-256 digest of the random bearer credential is stored.
public class RefreshSession {
 public Guid Id { get; set; }
 public Guid FamilyId { get; set; }
 public Guid UserId { get; set; }
 public string TokenHash { get; set; } = null!;
 public string PasswordContext { get; set; } = null!;
 public DateTime ExpiresAt { get; set; }
 public DateTime? RevokedAt { get; set; }
}
