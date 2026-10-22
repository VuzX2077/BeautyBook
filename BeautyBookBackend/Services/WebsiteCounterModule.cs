using System.Threading.RateLimiting;

namespace BeautyBookBackend.Services;

public static class WebsiteCounterModule
{
    // Keep counter registration self-contained so it can be deployed separately
    // from unrelated catalog/authentication changes in the working tree.
    public static IServiceCollection AddWebsiteApkCounter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebsiteCounterOptions>(configuration.GetSection("WebsiteMetrics"));
        services.AddScoped<WebsiteCounter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("website-clicks", _ => RateLimitPartition.GetFixedWindowLimiter("website-apk", _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300, Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0, AutoReplenishment = true
                }));
        });
        return services;
    }
}
