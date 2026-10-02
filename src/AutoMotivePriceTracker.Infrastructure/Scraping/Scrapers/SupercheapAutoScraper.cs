using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoMotivePriceTracker.Infrastructure.Scraping.Scrapers;

public class SupercheapAutoScraper : BaseScraper
{
    private const string BaseUrl = "https://www.supercheapauto.co.nz";

    public SupercheapAutoScraper(ILogger<SupercheapAutoScraper> logger) : base(logger) { }

    public override Retailer Retailer => Retailer.SupercheapAuto;

    protected override IEnumerable<string> CategoryUrls => new[]
    {
        $"{BaseUrl}/oils-fluids-filters/engine-oil",
        $"{BaseUrl}/oils-fluids-filters/filters",
        $"{BaseUrl}/oils-fluids-filters/coolant",
        $"{BaseUrl}/oils-fluids-filters/fluids",
        $"{BaseUrl}/batteries-electrical/batteries",
        $"{BaseUrl}/spare-parts/wipers",
        $"{BaseUrl}/spare-parts/ignition-start-charge",
        $"{BaseUrl}/spare-parts/cooling",
    };

    private const string CardSelector = ".product-tile[data-itemid]";

    protected override async Task<List<Product>> ScrapeCategoryPageAsync(IPage page, string categoryUrl, CancellationToken ct)
    {
        var products = new List<Product>();
        var currentUrl = categoryUrl;
        var visited = new HashSet<string>();

        while (visited.Add(currentUrl))
        {
            await NavigateWithRetryAsync(page, currentUrl);
            await page.WaitForSelectorAsync(CardSelector, new PageWaitForSelectorOptions { Timeout = 15_000 });

            var cards = await page.QuerySelectorAllAsync(CardSelector);

            foreach (var card in cards)
            {
                if (ct.IsCancellationRequested) return products;
                var product = await ExtractProductAsync(card, categoryUrl);
                if (product != null) products.Add(product);
            }

            Logger.LogInformation("Scraped {Count} products from {Url}", products.Count, currentUrl);

            var nextBtn = await page.QuerySelectorAsync("a.page-next");
            if (nextBtn == null) break;

            var nextHref = await nextBtn.GetAttributeAsync("href");
            if (string.IsNullOrEmpty(nextHref)) break;

            currentUrl = nextHref.StartsWith("http") ? nextHref : BaseUrl + nextHref;
            await Task.Delay(Random.Shared.Next(800, 1800), ct);
        }

        return products;
    }

    private async Task<Product?> ExtractProductAsync(IElementHandle card, string categoryUrl)
    {
        try
        {
            var name = await TextOrNullAsync(card, ".product-name a.name-link");
            var priceText = await TextOrNullAsync(card, ".product-sales-price .the-price");
            // only present on discounted tiles
            var wasPriceText = await TextOrNullAsync(card, ".product-standard-price .the-price");
            var linkHref = await AttrOrNullAsync(card, ".product-name a.name-link", "href");
            var imageUrl = await AttrOrNullAsync(card, ".product-image img", "src");

            var sku = await card.GetAttributeAsync("data-itemid");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText))
                return null;

            var price = ParsePrice(priceText);
            if (price <= 0) return null;

            var productUrl = string.IsNullOrEmpty(linkHref) ? categoryUrl
                : (linkHref.StartsWith("http") ? linkHref : BaseUrl + linkHref).Split('?').First();

            var category = categoryUrl.Split('/').Last();

            return new Product
            {
                Name = name.Trim(),
                Sku = sku ?? productUrl.Split('/').Last().Split('.').First(),
                Category = category,
                ImageUrl = imageUrl,
                SourceUrl = productUrl,
                Retailer = Retailer.SupercheapAuto,
                LastChecked = DateTime.UtcNow,
                PriceHistory = new List<PriceRecord>
                {
                    new PriceRecord
                    {
                        Price = price,
                        WasPrice = string.IsNullOrEmpty(wasPriceText) ? null : ParsePrice(wasPriceText),
                        Date = DateTime.UtcNow
                    }
                }
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to extract product from card");
            return null;
        }
    }

    private static decimal ParsePrice(string text)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(cleaned, out var price) ? price : 0;
    }
}
