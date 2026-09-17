namespace BeautyBookBackend.Services
{
    public class BookingAutoCompletionService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingAutoCompletionService> _logger;

        public BookingAutoCompletionService(
            IServiceScopeFactory scopeFactory,
            ILogger<BookingAutoCompletionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                    await bookingService.AutoCompleteOverdueAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to auto-complete overdue bookings.");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
