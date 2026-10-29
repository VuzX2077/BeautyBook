using Microsoft.Extensions.Options;
namespace BeautyBookBackend.Services.Monetization;

public sealed class MuaCommerceWorker(IServiceScopeFactory scopes, IOptions<MuaMonetizationOptions> options, ILogger<MuaCommerceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReconcile = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Value.WorkersEnabled)
            {
                try
                {
                    using var scope = scopes.CreateScope(); var commerce = scope.ServiceProvider.GetRequiredService<MuaEmailCommerce>();
                    await commerce.RunOnce(stoppingToken);
                    if (DateTimeOffset.UtcNow >= nextReconcile) { await commerce.ReconcilePending(stoppingToken); nextReconcile = DateTimeOffset.UtcNow.AddMinutes(2); }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogWarning("Commerce worker deferred a cycle: {ErrorType}", ex.GetType().Name); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
