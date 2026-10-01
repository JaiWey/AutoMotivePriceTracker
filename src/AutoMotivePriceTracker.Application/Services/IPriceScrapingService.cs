namespace AutoMotivePriceTracker.Application.Services;

public interface IPriceScrapingService
{
    /// <summary>Runs every registered scraper and persists the results.</summary>
    Task RunAllAsync(CancellationToken ct = default);
}
