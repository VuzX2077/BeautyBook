using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed class AiMakeupRequestDto
{
    [Required] public IFormFile Image { get; set; } = null!;
    public bool Consent { get; set; }
    public bool AdultConfirmed { get; set; }
    [Required, StringLength(40)] public string ConsentVersion { get; set; } = "";
    [StringLength(200)] public string? Preferences { get; set; }
    [StringLength(100)] public string? Occasion { get; set; }
}
public sealed record AiMakeupObservationDto(string Feature, string Description, string Certainty);
public sealed record AiMakeupSuggestionDto(int StyleId, string StyleName, string Reason, IReadOnlyList<string> MakeupTips);
public sealed record AiMakeupResultDto(string ImageStatus, IReadOnlyList<AiMakeupObservationDto> Observations,
    IReadOnlyList<AiMakeupSuggestionDto> Recommendations, string Notice);
