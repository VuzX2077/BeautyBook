namespace BeautyBookBackend.Services;

public sealed class AiMakeupOptions
{
    public bool Enabled { get; set; }
    // Operator attestations: never enable selfie processing with unpaid Gemini.
    public bool PaidTierConfirmed { get; set; }
    public bool PrivacyReady { get; set; }
    // Comma-separated real account UUIDs; invalid entire configuration fails closed.
    public string AllowedUserIds { get; set; } = "";
    public bool IsAllowed(Guid userId) {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(AllowedUserIds)) return false;
        var entries = AllowedUserIds.Split(',');
        if (entries.Length > 100) return false;
        var found = false;
        foreach (var entry in entries) {
            if (!Guid.TryParseExact(entry.Trim(), "D", out var id) || id == Guid.Empty) return false;
            found |= id == userId;
        }
        return found;
    }
    public int MaxRequestsPerUserPerDay { get; set; } = 2;
    public int MaxGlobalRequestsPerDay { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 40;
    public decimal InputUsdPerMillionTokens { get; set; }
    public decimal OutputUsdPerMillionTokens { get; set; }
    public const string ConsentVersion = "makeup-v1";
}
