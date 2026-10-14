using System.Net;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using BeautyBookBackend.Models.Enums;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace BeautyBookBackend.Tests;

public sealed class AiMakeupTests
{
    private static readonly ExploreStyle[] Styles = [new(11, "Natural", 1), new(23, "Party", 1), new(42, "Soft", 1)];
    private static string Valid(int last = 42, string reason = "Tông màu nhẹ phù hợp sở thích.") => JsonSerializer.Serialize(new {
        imageStatus = "valid", observations = new[] { new { feature = "lighting", description = "Ánh sáng tương đối rõ.", certainty = "uncertain" } },
        recommendations = new[] { 11, 23, last }.Select(id => new { styleId = id, reason, makeupTips = new[] { "Tán lớp nền mỏng." } })
    });
    [Fact] public void ValidOutputUsesDatabaseNamesAndServerNotice() {
        var result = AiMakeupOutputValidator.Validate(Valid(), Styles);
        Assert.Equal(3, result.Recommendations.Count); Assert.Equal("Natural", result.Recommendations[0].StyleName);
        Assert.Equal(AiMakeupOutputValidator.Notice, result.Notice);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"imageStatus\":\"valid\",\"observations\":[],\"recommendations\":[]}")]
    [InlineData("{\"imageStatus\":\"unknown\",\"observations\":[],\"recommendations\":[]}")]
    public void InvalidAndIncompleteOutputRejected(string json) => Assert.Equal("AI_INVALID_OUTPUT", Assert.Throws<AiMakeupException>(() => AiMakeupOutputValidator.Validate(json, Styles)).Code);
    [Theory] [InlineData(999)] [InlineData(11)] public void HallucinatedAndDuplicateStylesRejected(int last) => Assert.Throws<AiMakeupException>(() => AiMakeupOutputValidator.Validate(Valid(last), Styles));
    [Theory] [InlineData("Bạn có nhan sắc 9/10.")] [InlineData("Bạn có bệnh da.")] [InlineData("https://fake-mua.test")]
    public void UnsafeTextRejected(string reason) => Assert.Throws<AiMakeupException>(() => AiMakeupOutputValidator.Validate(Valid(reason: reason), Styles));
    [Theory] [InlineData("no_face")] [InlineData("multiple_faces")] [InlineData("blurry")] [InlineData("occluded")] [InlineData("insufficient")]
    public void UnsuitableImageHasNoRecommendations(string status) {
        var json = JsonSerializer.Serialize(new { imageStatus = status, observations = Array.Empty<object>(), recommendations = Array.Empty<object>() });
        Assert.Empty(AiMakeupOutputValidator.Validate(json, Styles).Recommendations);
    }
    [Fact] public void EmptyCatalogDoesNotAcceptStyle() => Assert.Throws<AiMakeupException>(() => AiMakeupOutputValidator.Validate(Valid(), []));
    internal static byte[] Png(int width = 256, int height = 256) {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray();
    }
    [Fact] public void ImageIsDecodedAndReencodedAsJpeg() { var result = AiMakeupImage.Normalize(Png(), "image/png"); Assert.Equal(0xff, result[0]); Assert.Equal(0xd8, result[1]); }
    [Fact] public void OriginalJpegExifPayloadIsNotForwarded() {
        var jpeg = AiMakeupImage.Normalize(Png(), "image/png");
        var metadata = System.Text.Encoding.ASCII.GetBytes("Exif\0\0II*\0\b\0\0\0\0\0\0\0\0\0GPS-PRIVATE-MARKER");
        var length = metadata.Length + 2;
        byte[] tagged = [.. jpeg[..2], 0xff, 0xe1, (byte)(length >> 8), (byte)length, .. metadata, .. jpeg[2..]];
        var normalized = AiMakeupImage.Normalize(tagged, "image/jpeg");
        Assert.DoesNotContain("GPS-PRIVATE-MARKER", System.Text.Encoding.Latin1.GetString(normalized));
        Assert.DoesNotContain("Exif", System.Text.Encoding.Latin1.GetString(normalized));
    }
    [Theory] [InlineData("image/jpeg")] [InlineData("image/webp")] [InlineData("text/plain")]
    public void SpoofedOrUnsupportedMimeRejected(string mime) => Assert.Throws<AiMakeupException>(() => AiMakeupImage.Normalize(Png(), mime));
    [Fact] public void BrokenOversizedAndExcessResolutionImagesRejected() {
        Assert.Throws<AiMakeupException>(() => AiMakeupImage.Normalize([1, 2, 3], "image/jpeg"));
        Assert.Throws<AiMakeupException>(() => AiMakeupImage.Normalize(new byte[AiMakeupImage.MaxBytes + 1], "image/png"));
        Assert.Throws<AiMakeupException>(() => AiMakeupImage.Normalize(Png(2001, 2000), "image/png"));
        Assert.Throws<AiMakeupException>(() => AiMakeupImage.Normalize(Png(64, 64), "image/png"));
    }
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T> {
        public T CurrentValue { get; set; } = value; public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
    private sealed class Catalog : IAiMakeupCatalogService {
        public IReadOnlyList<ExploreStyle> Rows = Styles;
        public Task<IReadOnlyList<ExploreStyle>> StylesAsync(CancellationToken ct) => Task.FromResult(Rows);
    }
    private sealed class Quota : IAiMakeupQuotaRepository {
        public int Calls; public Exception? Failure;
        public Task ReserveAsync(Guid user, int a, int b, CancellationToken ct) { Calls++; if (Failure != null) throw Failure; return Task.CompletedTask; }
    }
    private sealed class Provider : IGeminiMakeupProvider {
        public int Calls; public Func<CancellationToken, Task<GeminiMakeupReply>> Reply = _ => Task.FromResult(new GeminiMakeupReply(Valid(), 100, 200, 0));
        public Task<GeminiMakeupReply> AnalyzeAsync(byte[] image, IReadOnlyList<ExploreStyle> styles, string? preferences, string? occasion, CancellationToken ct) { Calls++; return Reply(ct); }
    }
    private sealed class Fixture {
        public static readonly Guid User = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public readonly Monitor<AiMakeupOptions> Options = new(new() { Enabled = true, PaidTierConfirmed = true, PrivacyReady = true, AllowedUserIds = User.ToString() });
        public readonly Monitor<GeminiOptions> Gemini = new(new() { ApiKey = "unit-test-placeholder-never-sent" });
        public readonly Catalog Catalog = new(); public readonly Quota Quota = new(); public readonly Provider Provider = new(); public readonly AiMakeupConcurrency Concurrency = new();
        public AiMakeupService Service => new(Provider, Quota, Catalog, Concurrency, Options, Gemini, NullLogger<AiMakeupService>.Instance);
        public AiMakeupRequestDto Request() { var bytes = Png(); return new() { Image = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", "fake-extension.txt") { Headers = new HeaderDictionary(), ContentType = "image/png" }, Consent = true, AdultConfirmed = true, ConsentVersion = AiMakeupOptions.ConsentVersion }; }
    }
    [Fact] public async Task DisabledOrUnconfirmedFeatureNeverCallsProvider() {
        var f = new Fixture(); f.Options.CurrentValue.Enabled = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Status);
        f.Options.CurrentValue.Enabled = true; f.Options.CurrentValue.PaidTierConfirmed = false;
        await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default));
        Assert.Equal(0, f.Provider.Calls); Assert.Equal(0, f.Quota.Calls);
    }
    [Fact] public async Task MissingConsentAdultOrVersionCannotConsumeQuota() {
        var f = new Fixture();
        foreach (var field in new[] { "consent", "adult", "version" }) { var r = f.Request(); if (field == "consent") r.Consent = false; if (field == "adult") r.AdultConfirmed = false; if (field == "version") r.ConsentVersion = "old";
            Assert.Equal("AI_CONSENT_REQUIRED", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, r, default))).Code); }
        Assert.Equal(0, f.Quota.Calls);
    }
    [Fact] public async Task QuotaFailureDoesNotCallProviderAndGateIsReleased() {
        var f = new Fixture(); f.Quota.Failure = new AiMakeupException(429, "AI_QUOTA_EXHAUSTED", "test");
        await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default)); Assert.Equal(0, f.Provider.Calls); Assert.Equal(2, f.Concurrency.Gate.CurrentCount);
    }
    [Fact] public async Task ConcurrentRequestsAreBoundedBeforeQuotaReservation() {
        var f = new Fixture(); var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        f.Provider.Reply = async ct => { if (f.Provider.Calls == 2) started.TrySetResult(); await release.Task.WaitAsync(ct); return new(Valid(), 1, 1, 0); };
        var a = f.Service.AnalyzeAsync(Fixture.User, f.Request(), default); var b = f.Service.AnalyzeAsync(Fixture.User, f.Request(), default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("AI_BUSY", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code);
        Assert.Equal(2, f.Quota.Calls); release.SetResult(); await Task.WhenAll(a, b);
    }
    [Fact] public async Task DisabledDuringInferenceAndChangedCatalogDiscardResult() {
        var f = new Fixture(); f.Provider.Reply = _ => { f.Options.CurrentValue.Enabled = false; return Task.FromResult(new GeminiMakeupReply(Valid(), 1, 1, 0)); };
        Assert.Equal("AI_DISABLED", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code);
        f = new Fixture(); var local = f; f.Provider.Reply = _ => { local.Catalog.Rows = Styles[..2]; return Task.FromResult(new GeminiMakeupReply(Valid(), 1, 1, 0)); };
        Assert.Equal("AI_CATALOG_CHANGED", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code);
    }
    [Fact] public async Task CancellationIsPropagatedWithoutRetry() {
        var f = new Fixture(); f.Provider.Reply = async ct => { await Task.Delay(Timeout.Infinite, ct); return null!; };
        using var abort = new CancellationTokenSource(); var task = f.Service.AnalyzeAsync(Fixture.User, f.Request(), abort.Token); abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.Equal(1, f.Provider.Calls); Assert.Equal(2, f.Concurrency.Gate.CurrentCount);
    }
    [Fact] public async Task TimeoutDoesNotRetryOrRefundQuota() {
        var f = new Fixture(); f.Provider.Reply = _ => throw new OperationCanceledException();
        Assert.Equal("AI_TIMEOUT", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code);
        Assert.Equal(1, f.Provider.Calls); Assert.Equal(1, f.Quota.Calls);
    }
    [Fact] public async Task MissingCatalogPreventsBillableCall() {
        var f = new Fixture(); f.Catalog.Rows = [];
        Assert.Equal("AI_CATALOG_UNAVAILABLE", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code); Assert.Equal(0, f.Quota.Calls);
    }
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler {
        public int Calls; public string? RequestBody; public string? Key; public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; Uri = request.RequestUri; RequestBody = await request.Content!.ReadAsStringAsync(ct); Key = request.Headers.GetValues("x-goog-api-key").Single(); return new(status) { Content = new StringContent(body) }; }
    }
    [Theory]
    [InlineData("")] [InlineData("not-a-guid")]
    [InlineData("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee,")]
    [InlineData("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee,invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MissingOrMalformedAllowlistDeniesBeforeQuotaAndProvider(string allowlist) {
        var f = new Fixture(); f.Options.CurrentValue.AllowedUserIds = allowlist;
        Assert.False(f.Service.CanAccess(Fixture.User));
        var error = await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default));
        Assert.Equal(403, error.Status); Assert.Equal("AI_ACCESS_DENIED", error.Code);
        Assert.Equal(0, f.Quota.Calls); Assert.Equal(0, f.Provider.Calls);
    }
    [Fact] public async Task OrdinaryAccountCannotOverrideAllowlistAndRevocationDiscardsResult() {
        var f = new Fixture();
        Assert.True(f.Service.CanAccess(Fixture.User)); Assert.False(f.Service.CanAccess(Guid.NewGuid()));
        await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Guid.NewGuid(), f.Request(), default));
        Assert.Equal(0, f.Provider.Calls);
        f.Provider.Reply = _ => { f.Options.CurrentValue.AllowedUserIds = ""; return Task.FromResult(new GeminiMakeupReply(Valid(), 1, 1, 0)); };
        Assert.Equal("AI_ACCESS_DENIED", (await Assert.ThrowsAsync<AiMakeupException>(() => f.Service.AnalyzeAsync(Fixture.User, f.Request(), default))).Code);
    }
    [Fact] public void CapabilitiesUsesAuthenticatedUserAndFailsClosedForMissingClaim() {
        var f = new Fixture();
        var http = new DefaultHttpContext();
        var controller = new AiMakeupController(f.Service, NullLogger<AiMakeupController>.Instance) { ControllerContext = new ControllerContext { HttpContext = http } };
        bool Enabled() => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(controller.Capabilities()).Value).GetProperty("enabled").GetBoolean();
        Assert.False(Enabled());
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Fixture.User.ToString()) }, "test"));
        Assert.True(Enabled());
        f.Options.CurrentValue.Enabled = false; Assert.False(Enabled());
    }
    [Fact] public async Task UnsupportedModelHasNoFallbackOrHttpCall() {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var config = new Monitor<GeminiOptions>(new() { ApiKey = "local-test", Model = "gemini-3.8-flash" });
        await Assert.ThrowsAsync<AiMakeupException>(() => new GeminiMakeupProvider(new HttpClient(handler), config).AnalyzeAsync([1], Styles, null, null, default));
        Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData(400, "AI_PROVIDER_BAD_REQUEST")] [InlineData(401, "AI_PROVIDER_AUTH")]
    [InlineData(402, "AI_PROVIDER_BILLING")] [InlineData(403, "AI_PROVIDER_FORBIDDEN")] [InlineData(404, "AI_PROVIDER_MODEL_NOT_FOUND")]
    public async Task ProviderHttpFailuresHaveSafeSpecificCodes(int status, string code) {
        var handler = new Handler((HttpStatusCode)status, "key secret selfie");
        var error = await Assert.ThrowsAsync<AiMakeupException>(() => new GeminiMakeupProvider(new HttpClient(handler), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" })).AnalyzeAsync([1], Styles, null, null, default));
        Assert.Equal(code, error.Code); Assert.DoesNotContain("secret", error.Message); Assert.Equal(1, handler.Calls);
    }
    [Theory] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public async Task ProviderErrorsAreSanitizedAndNeverRetried(int status) {
        var handler = new Handler((HttpStatusCode)status, "sensitive provider error");
        var provider = new GeminiMakeupProvider(new HttpClient(handler), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" }));
        var error = await Assert.ThrowsAsync<AiMakeupException>(() => provider.AnalyzeAsync([1], Styles, null, null, default));
        Assert.DoesNotContain("sensitive", error.Message); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task ProviderUsesCurrentSchemaAndIncludesUsage() {
        var body = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Valid() } } } } }, usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 200, thoughtsTokenCount = 0 } });
        var handler = new Handler(HttpStatusCode.OK, body); var provider = new GeminiMakeupProvider(new HttpClient(handler), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" }));
        var reply = await provider.AnalyzeAsync([1], Styles, "soft", null, default); Assert.Equal(100, reply.InputTokens); Assert.Equal(200, reply.OutputTokens); Assert.Equal(0, reply.ThinkingTokens);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", handler.Uri!.ToString()); Assert.Equal("local-test", handler.Key);
        using var request = JsonDocument.Parse(handler.RequestBody!); var root = request.RootElement;
        Assert.Equal("image/jpeg", root.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal("AQ==", root.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("data").GetString());
        Assert.Equal("MINIMAL", root.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        Assert.False(root.GetProperty("generationConfig").GetProperty("thinkingConfig").TryGetProperty("thinkingBudget", out _));
        Assert.Equal(1.0, root.GetProperty("generationConfig").GetProperty("temperature").GetDouble());
        Assert.False(root.TryGetProperty("cachedContent", out _));
        Assert.False(root.GetProperty("store").GetBoolean()); Assert.Equal("APPLICATION_JSON", root.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text").GetProperty("mimeType").GetString());
        Assert.Equal(3, root.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text").GetProperty("schema").GetProperty("properties").GetProperty("recommendations").GetProperty("items").GetProperty("properties").GetProperty("styleId").GetProperty("enum").GetArrayLength());
    }
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"candidates\":{}}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\"}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}")]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
    public async Task MalformedOrBlockedProviderResponseHasTypedSafeError(string body) {
        var handler = new Handler(HttpStatusCode.OK, body);
        var provider = new GeminiMakeupProvider(new HttpClient(handler), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" }));
        var error = await Assert.ThrowsAsync<AiMakeupException>(() => provider.AnalyzeAsync([1], Styles, null, null, default));
        Assert.Equal(502, error.Status); Assert.Equal("AI_INVALID_OUTPUT", error.Code); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task ThoughtPartsAreNotPublishedAndTokenCountsRemainSeparate() {
        var body = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new object[] { new { text = "private thought", thought = true }, new { text = Valid() } } } } }, usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 200, thoughtsTokenCount = 30 } });
        var provider = new GeminiMakeupProvider(new HttpClient(new Handler(HttpStatusCode.OK, body)), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" }));
        var reply = await provider.AnalyzeAsync([1], Styles, null, null, default);
        Assert.Equal(Valid(), reply.Json); Assert.Equal(100, reply.InputTokens); Assert.Equal(200, reply.OutputTokens); Assert.Equal(30, reply.ThinkingTokens);
    }
    [Fact] public async Task MissingUsageIsUnknownRatherThanMeasuredZero() {
        var body = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Valid() } } } } } });
        var provider = new GeminiMakeupProvider(new HttpClient(new Handler(HttpStatusCode.OK, body)), new Monitor<GeminiOptions>(new() { ApiKey = "local-test" }));
        var reply = await provider.AnalyzeAsync([1], Styles, null, null, default);
        Assert.Null(reply.InputTokens); Assert.Null(reply.OutputTokens); Assert.Null(reply.ThinkingTokens);
    }
    [Fact] public void EndpointRequiresAuthenticationAndBuffersSmallUploadInMemory() {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(AiMakeupController), typeof(AuthorizeAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(AiMakeupController), typeof(RequireHttpsAttribute)));
        var method = typeof(AiMakeupController).GetMethod("Analyze")!;
        var limits = (RequestFormLimitsAttribute)Attribute.GetCustomAttribute(method, typeof(RequestFormLimitsAttribute))!;
        Assert.True(limits.MemoryBufferThreshold > AiMakeupImage.MaxBytes);
    }
    [PostgreSqlFact] public async Task DistributedDailyQuotaSurvivesNewRepositoryAndConcurrentRequests() {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var user = Guid.NewGuid();
        await using (var coverage = database.CreateContext()) {
            Assert.Equal(2, await coverage.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid
                WHERE r.relname='AiMakeupDailyUsage' AND t.tgname IN ('private_media_write_guard','account_deletion_write_guard')
                AND t.tgenabled='O' AND NOT t.tgisinternal
                """).SingleAsync());
            await AccountDeletionService.VerifyWriterCoverageAsync(coverage, default);
        }
        async Task<bool> Reserve(Guid id) { await using var db = database.CreateContext(); try { await new AiMakeupQuotaRepository(db).ReserveAsync(id, 3, 5, default); return true; } catch (AiMakeupException e) when (e.Status == 429) { return false; } }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Reserve(user))); Assert.Equal(3, results.Count(x => x));
        var global = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Reserve(Guid.NewGuid()))); Assert.Equal(2, global.Count(x => x));
        Assert.False(await Reserve(Guid.NewGuid()));
    }
    [PostgreSqlFact] public async Task MatchingUsesActiveServicesAndPublicArtistsAndHttpRejectsUnauthenticatedCalls() {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var customer = Guid.NewGuid(); var listed = Guid.NewGuid(); var suspended = Guid.NewGuid(); var empty = Guid.NewGuid();
        foreach (var id in new[] { customer, listed, suspended, empty }) db.Users.Add(new() { UserId = id, Email = $"{id}@example.test", PasswordHash = "test", IsActive = true, Role = UserRole.Customer });
        db.MakeupArtistProfiles.AddRange(new() { MUAId = listed, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved },
            new() { MUAId = suspended, Status = MuaStatus.Suspended, VerificationStatus = MuaVerificationStatus.Approved },
            new() { MUAId = empty, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        var styleId = (await db.MakeupStyles.MaxAsync(s => (int?)s.StyleId) ?? 0) + 1;
        db.MakeupStyles.Add(new() { StyleId = styleId, Name = "AI local matching style", IsActive = true });
        foreach (var id in new[] { listed, suspended, empty }) db.MUAStyles.Add(new() { MUAId = id, StyleId = styleId });
        var serviceId = Guid.NewGuid();
        db.Services.AddRange(new() { ServiceId = serviceId, MUAId = listed, ServiceName = "Real", IsActive = true, Price = 500000 },
            new() { ServiceId = Guid.NewGuid(), MUAId = listed, ServiceName = "Inactive", IsActive = false, Price = 100 },
            new() { ServiceId = Guid.NewGuid(), MUAId = suspended, ServiceName = "Hidden", IsActive = true, Price = 100 });
        await db.SaveChangesAsync();
        var explore = new ExploreService(db, new EphemeralDataProtectionProvider());
        var page = Assert.IsType<ExplorePage<ExploreServiceItem>>(await explore.SearchAsync(new() { Kind = "services", StyleId = styleId }, default));
        Assert.Equal(serviceId, Assert.Single(page.Items).Id);
        Assert.Empty(Assert.IsType<ExplorePage<ExploreServiceItem>>(await explore.SearchAsync(new() { Kind = "services", StyleId = 99999 }, default)).Items);
        Assert.Empty(Assert.IsType<ExplorePage<ExploreServiceItem>>(await explore.SearchAsync(new() { Kind = "services", StyleId = styleId, MaxPrice = 1000 }, default)).Items);
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        const string path = "/api/ai/makeup-recommendations";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "/capabilities")).StatusCode);
        using var anonymousForm = new MultipartFormDataContent(); anonymousForm.Add(new ByteArrayContent(Png()), "image", "selfie.png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, anonymousForm)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(customer, UserRole.Customer));
        using var form = new MultipartFormDataContent(); var bytes = new ByteArrayContent(Png()); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(bytes, "image", "selfie.png"); form.Add(new StringContent(AiMakeupOptions.ConsentVersion), "consentVersion");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync(path, form)).StatusCode);
    }
}
