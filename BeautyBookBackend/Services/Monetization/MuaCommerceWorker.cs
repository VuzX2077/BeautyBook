using Microsoft.Extensions.Options;
namespace BeautyBookBackend.Services.Monetization;

public sealed class MuaCommerceWorker(IServiceScopeFactory scopes, IOptions<MuaMonetizationOptions> options, IConfiguration config, ILogger<MuaCommerceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReconcile = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var customerEnabled = config.GetValue<bool>("CustomerAi:ReconciliationEnabled");
            if (options.Value.WorkersEnabled || customerEnabled)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    if (options.Value.WorkersEnabled) await scope.ServiceProvider.GetRequiredService<MuaEmailCommerce>().RunOnce(stoppingToken);
                    if (DateTimeOffset.UtcNow >= nextReconcile)
                    {
                        if (options.Value.WorkersEnabled) await scope.ServiceProvider.GetRequiredService<MuaEmailCommerce>().ReconcilePending(stoppingToken);
                        if (customerEnabled) await scope.ServiceProvider.GetRequiredService<CustomerAiCommerce>().ReconcilePending(null, stoppingToken);
                        nextReconcile = DateTimeOffset.UtcNow.AddMinutes(2);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogWarning("Commerce worker deferred a cycle: {ErrorType}", ex.GetType().Name); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
