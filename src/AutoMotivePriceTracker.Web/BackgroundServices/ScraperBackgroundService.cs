using AutoMotivePriceTracker.Application.Services;

namespace AutoMotivePriceTracker.Web.BackgroundServices;

public class ScraperBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScraperBackgroundService> _logger;

    public ScraperBackgroundService(IServiceProvider services, ILogger<ScraperBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // wait for app to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Scheduled scrape starting");
            try
            {
                using var scope = _services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPriceScrapingService>();
                await service.RunAllAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduled scrape encountered an error");
            }

            _logger.LogInformation("Next scrape in 24 hours");
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
