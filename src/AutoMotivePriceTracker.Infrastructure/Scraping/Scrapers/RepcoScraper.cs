using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoMotivePriceTracker.Infrastructure.Scraping.Scrapers;

public class RepcoScraper : BaseScraper
{
    private const string BaseUrl = "https://www.repco.co.nz";

    public RepcoScraper(ILogger<RepcoScraper> logger) : base(logger) { }

    public override Retailer Retailer => Retailer.Repco;

    private const string ProductTileSelector = ".product-tile[data-sku]";

    protected override IEnumerable<string> CategoryUrls => new[]
    {
        // Repco category URLs are /<path>/c/<numeric id>; the id is required.
        $"{BaseUrl}/oils-fluids/engine-oils-fluids/engine-oil/c/1862918079",
        $"{BaseUrl}/parts-service/filtration/oil-filters/c/1862918203",
        $"{BaseUrl}/globes-batteries-electrical/batteries/c/1862917986",
  //      $"{BaseUrl}/parts-service/braking/brake-pads/c/1862918117",
  //      $"{BaseUrl}/parts-service/wiper-components/wiper-blades/c/2460718084",
  //      $"{BaseUrl}/oils-fluids/engine-oils-fluids/coolant/c/1862918078",
  //      $"{BaseUrl}/oils-fluids/degreasers-cleaners/c/1862918062",
  //      $"{BaseUrl}/car-care-panel/car-care/c/1862917930",
  //      $"{BaseUrl}/tools-equipment/garage-tools-equipment/c/1862917873",
		//$"{BaseUrl}/globes-batteries-electrical/batteries/c/1862917986"
	};

    protected override async Task<List<Product>> ScrapeCategoryPageAsync(IPage page, string categoryUrl, CancellationToken ct)
    {
        var products = new List<Product>();
        var currentUrl = categoryUrl;
        var visited = new HashSet<string>();

        while (visited.Add(currentUrl))
        {
            await NavigateWithRetryAsync(page, currentUrl);
            await page.WaitForSelectorAsync(ProductTileSelector,
                new PageWaitForSelectorOptions { Timeout = 15_000 });

            var cards = await page.QuerySelectorAllAsync(ProductTileSelector);

            foreach (var card in cards)
            {
                if (ct.IsCancellationRequested) return products;
                var product = await ExtractProductAsync(card, categoryUrl);
                if (product != null) products.Add(product);
            }

            Logger.LogInformation("Scraped {Count} products from {Url}", products.Count, currentUrl);

            // pagination
            // The first .pagination-next is "next"; a second one (double angle) is "last".
            // Both exist on the final page too, so the visited set in the loop condition ends it.
            var nextBtn = await page.QuerySelectorAsync(".pagination-next a[rel='next']");
            if (nextBtn == null) break;

            var nextHref = await nextBtn.GetAttributeAsync("href");
            if (string.IsNullOrEmpty(nextHref)) break;

            currentUrl = nextHref.StartsWith("http") ? nextHref : BaseUrl + nextHref;
            await Task.Delay(Random.Shared.Next(800, 1800), ct);
        }

        return products;
    }

    protected override Task<IReadOnlyList<string>> ExtractFeaturesAsync(IPage page) =>
        ListItemTextsAsync(page, ".feature__list li");

    private async Task<Product?> ExtractProductAsync(IElementHandle card, string categoryUrl)
    {
        try
        {
            // The tile itself carries sku / price / url as data-* attributes.
            var sku = await card.GetAttributeAsync("data-sku");
            var priceText = await card.GetAttributeAsync("data-price");
            var linkHref = await card.GetAttributeAsync("data-url");

            var name = await AttrOrNullAsync(card, ".product-name", "title")
                       ?? await TextOrNullAsync(card, ".product-name");

            // ".savings" holds the crossed-out price on discounted tiles and is empty otherwise
            var wasPriceText = await TextOrNullAsync(card, ".product-price .savings");

            var imageUrl = await AttrOrNullAsync(card, ".img-wrapper img", "src");

            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText))
                return null;

            var price = ParsePrice(priceText);
            if (price <= 0) return null;

            var wasPrice = string.IsNullOrWhiteSpace(wasPriceText) ? 0 : ParsePrice(wasPriceText);

            var productUrl = string.IsNullOrEmpty(linkHref) ? categoryUrl
                : linkHref.StartsWith("http") ? linkHref : BaseUrl + linkHref;

            if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.StartsWith("http"))
                imageUrl = BaseUrl + imageUrl;

            // URL ends in .../<slug>/c/<id>, so take the slug before "/c/"
            var category = categoryUrl.Split("/c/")[0].Split('/').Last();

            return new Product
            {
                Name = name.Trim(),
                Sku = sku,
                Category = category,
                ImageUrl = imageUrl,
                SourceUrl = productUrl,
                Retailer = Retailer.Repco,
                LastChecked = DateTime.UtcNow,
                PriceHistory = new List<PriceRecord>
                {
                    new PriceRecord
                    {
                        Price = price,
                        WasPrice = wasPrice > price ? wasPrice : null,
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
