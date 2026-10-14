using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public interface IAiMakeupService
{
    bool Available { get; }
    bool CanAccess(Guid userId);
    Task<AiMakeupResultDto> AnalyzeAsync(Guid userId, AiMakeupRequestDto request, CancellationToken ct);
}
