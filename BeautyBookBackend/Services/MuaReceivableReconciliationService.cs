namespace BeautyBookBackend.Services
{
    public class MuaReceivableReconciliationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MuaReceivableReconciliationService> _logger;
        public MuaReceivableReconciliationService(IServiceScopeFactory scopeFactory, ILogger<MuaReceivableReconciliationService> logger)
        { _scopeFactory=scopeFactory; _logger=logger; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope=_scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IMuaReceivableService>().ReconcileStatesAsync();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "MUA receivable reconciliation failed."); }
                await timer.WaitForNextTickAsync(stoppingToken);
            }
        }
    }
}
