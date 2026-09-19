namespace BeautyBookBackend.Services
{
    public class PayoutReconciliationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;private readonly ILogger<PayoutReconciliationService> _logger;
        public PayoutReconciliationService(IServiceScopeFactory scopes,ILogger<PayoutReconciliationService> logger){_scopes=scopes;_logger=logger;}
        protected override async Task ExecuteAsync(CancellationToken token){using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));while(!token.IsCancellationRequested){try{using var s=_scopes.CreateScope();await s.ServiceProvider.GetRequiredService<IPayoutService>().MovePendingToManualActionRequiredAsync();}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}catch(Exception ex){_logger.LogError(ex,"Payout reconciliation failed.");}await timer.WaitForNextTickAsync(token);}}
    }
}
