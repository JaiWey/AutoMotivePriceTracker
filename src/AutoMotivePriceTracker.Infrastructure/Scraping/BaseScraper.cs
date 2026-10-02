using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AutoMotivePriceTracker.Infrastructure.Scraping;

public abstract class BaseScraper : IProductScraper, IAsyncDisposable
{
    protected readonly ILogger Logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    protected BaseScraper(ILogger logger)
    {
        Logger = logger;
    }

    public abstract Retailer Retailer { get; }

    protected abstract IEnumerable<string> CategoryUrls { get; }

    protected abstract Task<List<Product>> ScrapeCategoryPageAsync(IPage page, string categoryUrl, CancellationToken ct);

    public async Task<IEnumerable<Product>> ScrapeAsync(CancellationToken ct = default)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });

        var allProducts = new List<Product>();

        foreach (var url in CategoryUrls)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                Logger.LogInformation("Scraping {Retailer} category: {Url}", Retailer, url);
                var page = await context.NewPageAsync();
                var products = await ScrapeCategoryPageAsync(page, url, ct);
                allProducts.AddRange(products);
                await page.CloseAsync();

                // polite delay between categories
                await Task.Delay(Random.Shared.Next(1000, 2500), ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to scrape {Retailer} category: {Url}", Retailer, url);
            }
        }

        await context.CloseAsync();
        return allProducts;
    }

    protected static async Task<bool> NavigateWithRetryAsync(IPage page, string url, int retries = 3)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });

                // Fail fast with a clear message instead of timing out on a missing selector
                // (403 = bot protection / security check, 404/410 = dead category URL).
                if (response is { Status: >= 400 and not 429 and < 500 })
                    throw new HttpRequestException(
                        $"{url} returned HTTP {response.Status}" +
                        (response.Status == 403 ? " (likely blocked by bot protection)" : ""),
                        null, (System.Net.HttpStatusCode)response.Status);

                return true;
            }
            catch (HttpRequestException)
            {
                throw; // non-retryable client error
            }
            catch
            {
                if (i == retries - 1) throw;
                await Task.Delay(2000 * (i + 1));
            }
        }
        return false;
    }

    // EvalOnSelectorAsync throws when nothing matches; these return null instead,
    // for optional elements such as a was-price that only exists on discounted tiles.
    protected static async Task<string?> TextOrNullAsync(IElementHandle parent, string selector)
    {
        var el = await parent.QuerySelectorAsync(selector);
        return el == null ? null : (await el.TextContentAsync())?.Trim();
    }

    protected static async Task<string?> AttrOrNullAsync(IElementHandle parent, string selector, string attribute)
    {
        var el = await parent.QuerySelectorAsync(selector);
        return el == null ? null : await el.GetAttributeAsync(attribute);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
