using AutoMotivePriceTracker.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AutoMotivePriceTracker.Application.Services;

public class PriceScrapingService : IPriceScrapingService
{
    private readonly IEnumerable<IProductScraper> _scrapers;
    private readonly IProductRepository _products;
    private readonly ILogger<PriceScrapingService> _logger;

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scrape failed for {Retailer}", scraper.Retailer);
            }
        }
    }
}
