using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public sealed record GeminiMakeupReply(string Json, int? InputTokens, int? OutputTokens, int? ThinkingTokens);
public interface IGeminiMakeupProvider
{
    Task<GeminiMakeupReply> AnalyzeAsync(byte[] image, IReadOnlyList<ExploreStyle> styles, string? preferences, string? occasion, CancellationToken ct);
}
