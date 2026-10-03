namespace AutoMotivePriceTracker.Web.BackgroundServices;

public class ScraperBackgroundService : BackgroundService
{
    private readonly ScrapeRunCoordinator _coordinator;
    private readonly ILogger<ScraperBackgroundService> _logger;

    public ScraperBackgroundService(ScrapeRunCoordinator coordinator, ILogger<ScraperBackgroundService> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // wait for app to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("Scheduled scrape starting");
            if (!await _coordinator.TryRunAsync(ct))
                _logger.LogInformation("Scheduled scrape skipped: a scrape is already running");

            _logger.LogInformation("Next scrape in 24 hours");
            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
