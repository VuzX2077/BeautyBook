using System.Diagnostics;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Repositories;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Services;

public sealed class AiMakeupService(IGeminiMakeupProvider provider, IAiMakeupQuotaRepository quota,
    IAiMakeupCatalogService explore, AiMakeupConcurrency concurrency, IOptionsMonitor<AiMakeupOptions> options,
    IOptionsMonitor<GeminiOptions> gemini, ILogger<AiMakeupService> logger) : IAiMakeupService
{
    public bool Available => options.CurrentValue.Enabled && options.CurrentValue.PaidTierConfirmed && options.CurrentValue.PrivacyReady &&
        !string.IsNullOrWhiteSpace(gemini.CurrentValue.ApiKey) && GeminiOptions.IsSupportedModel(gemini.CurrentValue.Model);
    public bool CanAccess(Guid userId) => Available && options.CurrentValue.IsAllowed(userId);
    public async Task<AiMakeupResultDto> AnalyzeAsync(Guid userId, AiMakeupRequestDto request, CancellationToken ct)
    {
        void CheckEnabled() {
            if (!Available) throw new AiMakeupException(503, "AI_DISABLED", "Tư vấn AI hiện chưa được bật. Bạn vẫn có thể khám phá MUA.");
            if (!options.CurrentValue.IsAllowed(userId)) throw new AiMakeupException(403, "AI_ACCESS_DENIED", "Tài khoản chưa được cấp quyền thử nghiệm AI.");
        }
        CheckEnabled();
        if (!request.Consent || !request.AdultConfirmed || request.ConsentVersion != AiMakeupOptions.ConsentVersion)
            throw new AiMakeupException(400, "AI_CONSENT_REQUIRED", "Cần xác nhận đủ 18 tuổi và đồng ý xử lý ảnh trước khi phân tích.");
        if (request.Image == null || request.Image.Length is <= 0 or > AiMakeupImage.MaxBytes)
            throw new AiMakeupException(400, "AI_IMAGE_SIZE", "Ảnh phải nhỏ hơn 5 MB.");
        // Never queue multipart images in memory waiting for a provider slot.
        if (!await concurrency.Gate.WaitAsync(0, ct)) throw new AiMakeupException(429, "AI_BUSY", "AI đang xử lý nhiều yêu cầu. Vui lòng thử lại sau.");
        byte[]? source = null, image = null;
        var timer = Stopwatch.StartNew(); var outcome = "failed"; GeminiMakeupReply? reply = null;
        try {
            await using var stream = new MemoryStream();
            try {
                await request.Image.CopyToAsync(stream, ct);
                source = stream.ToArray(); image = AiMakeupImage.Normalize(source, request.Image.ContentType);
            }
            finally { Array.Clear(stream.GetBuffer()); }
            Array.Clear(source); source = null;
            var catalog = await explore.StylesAsync(ct);
            if (catalog.Count < 3) throw new AiMakeupException(503, "AI_CATALOG_UNAVAILABLE", "Chưa có đủ phong cách để tư vấn. Hãy khám phá các dịch vụ hiện có.");
            CheckEnabled();
            var config = options.CurrentValue;
            await quota.ReserveAsync(userId, config.MaxRequestsPerUserPerDay, config.MaxGlobalRequestsPerDay, ct);
            CheckEnabled();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 5, 60)));
            try { reply = await provider.AnalyzeAsync(image, catalog, request.Preferences, request.Occasion, deadline.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AiMakeupException(504, "AI_TIMEOUT", "Phân tích mất nhiều thời gian hơn dự kiến. Vui lòng thử lại sau."); }
            CheckEnabled();
            var result = AiMakeupOutputValidator.Validate(reply.Json, catalog);
            // A style may be disabled during inference. Revalidate against current public catalog.
            var currentIds = (await explore.StylesAsync(ct)).Select(s => s.Id).ToHashSet();
            if (result.Recommendations.Any(r => !currentIds.Contains(r.StyleId))) throw new AiMakeupException(502, "AI_CATALOG_CHANGED", "Danh mục vừa thay đổi. Vui lòng khám phá phong cách khác.");
            outcome = "success"; return result;
        }
        finally {
            if (source != null) Array.Clear(source);
            if (image != null) Array.Clear(image);
            concurrency.Gate.Release();
            var config = options.CurrentValue;
            decimal? estimated = reply == null || config.InputUsdPerMillionTokens <= 0 || config.OutputUsdPerMillionTokens <= 0 ? null :
                (reply.InputTokens * config.InputUsdPerMillionTokens + (reply.OutputTokens + reply.ThinkingTokens) * config.OutputUsdPerMillionTokens) / 1_000_000;
            logger.LogInformation("AI makeup outcome={Outcome} model={Model} latencyMs={Latency} inputTokens={Input} outputTokens={Output} thinkingTokens={Thinking} estimatedUsd={Estimated}",
                outcome, gemini.CurrentValue.Model, timer.ElapsedMilliseconds, reply?.InputTokens, reply?.OutputTokens, reply?.ThinkingTokens, estimated);
        }
    }
}
