namespace BeautyBookBackend.DTOs;

// Read model only: no DTO accepts activation, prices, expiry or payment proof from a client.
public sealed record MuaMonetizationDto
{
    public bool Enabled { get; init; }
    public string ReviewStatus { get; init; } = "Draft";
    public bool EnforcementEnabled { get; init; }
    public bool ActivationCheckoutAvailable { get; init; }
    public bool VisibilityCheckoutAvailable { get; init; }
    public int ActivationPriceVnd { get; init; } = 0;
    public int VisibilityPriceVnd { get; init; } = 75_000;
    public int VisibilityDurationDays { get; init; } = 30;
    public string Cohort { get; init; } = "LegacyWaiver";
    public string ActivationStatus { get; init; } = "Waived";
    public bool HasPermanentActivation { get; init; }
    public bool WouldRequireActivation { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public string VisibilityStatus { get; init; } = "None";
    public DateTime? VisibilityEndsAt { get; init; }
    public DateTime EvaluatedAt { get; init; }
    public IReadOnlyList<MuaVisibilityIntervalDto> VisibilityIntervals { get; init; } = [];
}

public sealed record MuaVisibilityIntervalDto(string Kind, DateTime StartsAt, DateTime EndsAt, bool Revoked);
