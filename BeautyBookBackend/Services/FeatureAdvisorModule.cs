using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace BeautyBookBackend.Services;

// Hosted by the existing BBook API. No second authentication scheme, HTTP
// identity lookup, database context, migration or background warm-up.
public static class FeatureAdvisorModule
{
    public static IServiceCollection AddFeatureAdvisor(this IServiceCollection services, IConfiguration configuration, bool development)
    {
        services.AddMemoryCache();
        services.Configure<FeatureAdvisorOptions>(configuration.GetSection("FeatureAdvisor"));
        services.PostConfigure<FeatureAdvisorOptions>(o => FeatureAdvisorProvider.ApplyEnvironmentGate(o, development));
        services.TryAddSingleton<FeatureAdvisorConcurrency>();
        services.AddHttpClient<FeatureAdvisorProvider>(c => c.Timeout = TimeSpan.FromSeconds(30)).RemoveAllLoggers();
        services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.AddPolicy("feature-advisor", c => RateLimitPartition.GetFixedWindowLimiter(
                c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new() { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        return services;
    }
}

public sealed class FeatureAdvisorConcurrency : IDisposable
{
    private readonly SemaphoreSlim slots;
    public FeatureAdvisorConcurrency(IOptions<FeatureAdvisorOptions> options) =>
        slots = new(Math.Clamp(options.Value.MaxConcurrentRequests, 1, 8));
    public IDisposable? TryEnter(CancellationToken ct) => slots.Wait(0, ct) ? new Lease(slots) : null;
    public void Dispose() => slots.Dispose();
    private sealed class Lease(SemaphoreSlim slots) : IDisposable {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) slots.Release(); }
    }
}
