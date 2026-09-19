namespace BeautyBookBackend.DTOs
{
    public sealed class MuaEligibilityRequirementDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsMet { get; set; }
        public int? Current { get; set; }
        public int? Required { get; set; }
    }

    public sealed class MuaEligibilityDto
    {
        public int CompletionPercentage { get; set; }
        public string ProfileStatus { get; set; } = "Draft";
        public bool CanPublishProfile { get; set; }
        public bool CanReceiveBookings { get; set; }
        public bool CanWithdraw { get; set; }
        public string VerificationStatus { get; set; } = "NOT_SUBMITTED";
        public List<MuaEligibilityRequirementDto> Requirements { get; set; } = new();
        public List<MuaEligibilityRequirementDto> MissingRequirements { get; set; } = new();
    }

    public sealed class SetMuaSuspensionRequest
    {
        public bool Suspended { get; set; }
    }

    public sealed class SetAccountActiveRequest
    {
        public bool IsActive { get; set; }
    }

    public sealed class SetPortfolioVisibilityRequest
    {
        public bool IsHidden { get; set; }
    }
}
