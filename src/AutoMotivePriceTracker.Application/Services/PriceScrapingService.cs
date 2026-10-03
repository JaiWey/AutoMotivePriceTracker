using AutoMotivePriceTracker.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AutoMotivePriceTracker.Application.Services;

public class PriceScrapingService : IPriceScrapingService
{
    private readonly IEnumerable<IProductScraper> _scrapers;
    private readonly IProductRepository _products;
    private readonly ILogger<PriceScrapingService> _logger;

    // Each product needs its own page load, so a first run is spread over several runs.
    public const int FeaturePagesPerRun = 300;

    public PriceScrapingService(
        IEnumerable<IProductScraper> scrapers,
        IProductRepository products,
        ILogger<PriceScrapingService> logger)
    {
        _scrapers = scrapers;
        _products = products;
        _logger = logger;
    }

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        foreach (var scraper in _scrapers)
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation("Starting scrape for {Retailer}", scraper.Retailer);
            try
            {
                var products = (await scraper.ScrapeAsync(ct)).ToList();
                await _products.UpsertScrapedAsync(products, DateTime.UtcNow, ct);
                _logger.LogInformation("Finished scrape for {Retailer}: {Count} products", scraper.Retailer, products.Count);
                await ScrapeFeaturesAsync(scraper, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scrape failed for {Retailer}", scraper.Retailer);
            }
        }
    }

    private async Task ScrapeFeaturesAsync(IProductScraper scraper, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        try
        {
            var missing = await _products.GetMissingFeaturesAsync(scraper.Retailer, FeaturePagesPerRun, ct);
            if (missing.Count == 0) return;

            var features = await scraper.ScrapeFeaturesAsync(missing, ct);
            await _products.SaveFeaturesAsync(scraper.Retailer, features, DateTime.UtcNow, ct);
            _logger.LogInformation("Scraped features for {Count}/{Requested} {Retailer} products",
                features.Count, missing.Count, scraper.Retailer);
        }
        catch (Exception ex)
        {
            // Features are secondary to prices; a failure here must not fail the price scrape.
            _logger.LogError(ex, "Feature scrape failed for {Retailer}", scraper.Retailer);
        }
    }
}
