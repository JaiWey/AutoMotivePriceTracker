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
        $"{BaseUrl}/c/engine-oils-fluids",
        $"{BaseUrl}/c/filters",
        $"{BaseUrl}/c/batteries",
        $"{BaseUrl}/c/brakes",
        $"{BaseUrl}/c/wipers",
        $"{BaseUrl}/c/spark-plugs",
        $"{BaseUrl}/c/coolants-additives",
        $"{BaseUrl}/c/car-care",
    };

    protected override async Task<List<Product>> ScrapeCategoryPageAsync(IPage page, string categoryUrl, CancellationToken ct)
    {
        var products = new List<Product>();
        var currentUrl = categoryUrl;

        while (true)
        {
            await NavigateWithRetryAsync(page, currentUrl);
            await page.WaitForSelectorAsync(".product-tile, .product-card, [data-component='ProductTile'], [class*='product-item']",
                new PageWaitForSelectorOptions { Timeout = 15_000 });

            var cards = await page.QuerySelectorAllAsync(".product-tile, .product-card, [data-component='ProductTile'], [class*='product-item']");

            foreach (var card in cards)
            {
                if (ct.IsCancellationRequested) return products;
                var product = await ExtractProductAsync(card, categoryUrl);
                if (product != null) products.Add(product);
            }

            Logger.LogInformation("Scraped {Count} products from {Url}", products.Count, currentUrl);

            var nextBtn = await page.QuerySelectorAsync("a[aria-label='Next'], .next-page:not(.disabled), [data-testid='pagination-next']");
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
            var name = await card.EvalOnSelectorAsync<string?>(
                ".product-title, .product-name, [data-testid='product-name'], h2, h3",
                "el => el?.textContent?.trim()");

            var priceText = await card.EvalOnSelectorAsync<string?>(
                ".price-current, .selling-price, [data-testid='price'], .price__value",
                "el => el?.textContent?.trim()");

            var wasPriceText = await card.EvalOnSelectorAsync<string?>(
                ".price-was, .was-price, [data-testid='was-price'], .price__was",
                "el => el?.textContent?.trim()");

            var linkHref = await card.EvalOnSelectorAsync<string?>(
                "a[href]", "el => el?.getAttribute('href')");

            var imageUrl = await card.EvalOnSelectorAsync<string?>(
                "img[src], img[data-src]", "el => el?.getAttribute('src') ?? el?.getAttribute('data-src')");

            var sku = await card.EvalOnSelectorAsync<string?>(
                "[data-product-id], [data-part-number], [data-sku]",
                "el => el?.getAttribute('data-product-id') ?? el?.getAttribute('data-part-number') ?? el?.getAttribute('data-sku')");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText))
                return null;

            var price = ParsePrice(priceText);
            if (price <= 0) return null;

            var productUrl = string.IsNullOrEmpty(linkHref) ? categoryUrl
                : linkHref.StartsWith("http") ? linkHref : BaseUrl + linkHref;

            if (string.IsNullOrEmpty(sku) && productUrl.Contains('/'))
                sku = productUrl.Split('/').Last().Split('?').First();

            var category = categoryUrl.Split('/').Last();

            return new Product
            {
                Name = name.Trim(),
                Sku = sku ?? Guid.NewGuid().ToString("N")[..8],
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
