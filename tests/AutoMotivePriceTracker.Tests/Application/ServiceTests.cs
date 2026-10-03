using AutoMotivePriceTracker.Application.Services;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using AutoMotivePriceTracker.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Application;

public class ProductCatalogServiceTests
{
    private readonly FakeProductRepository _repo = new();
    private ProductCatalogService Service => new(_repo);

    [Fact]
    public async Task Home_summary_maps_sections_and_totals_counts()
    {
        _repo.OnSale = [Product("A", price: 8m, wasPrice: 10m)];
        _repo.Recent = [Product("B"), Product("C")];
        _repo.Counts = new() { [Retailer.Repco] = 3, [Retailer.SupercheapAuto] = 4 };

        var home = await Service.GetHomeSummaryAsync();

        Assert.Single(home.Deals);
        Assert.Equal(2, home.Recent.Count);
        Assert.Equal(7, home.TotalProducts);
        Assert.Equal(3, home.CountByRetailer[Retailer.Repco]);
        Assert.Equal(8, _repo.OnSaleTake);
        Assert.Equal(8, _repo.RecentTake);
    }

    [Fact]
    public async Task Summary_dto_exposes_latest_price_and_sale_flag()
    {
        _repo.All =
        [
            Product("A", price: 8m, wasPrice: 10m),
            Product("B", price: 5m),
            Product("C", price: null),
        ];

        var all = await Service.GetAllProductsAsync();

        Assert.Equal(3, all.Count);
        Assert.True(all[0].IsOnSale);
        Assert.Equal(20m, all[0].LatestPrice!.DiscountPercent);
        Assert.False(all[1].IsOnSale);
        Assert.Null(all[2].LatestPrice);
        Assert.False(all[2].IsOnSale);
    }

    [Fact]
    public async Task GetProduct_returns_null_when_missing()
    {
        Assert.Null(await Service.GetProductAsync(404));
    }

    [Fact]
    public async Task GetProduct_returns_detail_with_history_oldest_first()
    {
        var p = Product(price: 10m, date: T0.AddDays(2));
        p.Id = 7;
        p.PriceHistory.Add(new PriceRecord { Price = 12m, Date = T0 });
        p.PriceHistory.Add(new PriceRecord { Price = 11m, Date = T0.AddDays(1) });
        _repo.ById[7] = p;

        var detail = await Service.GetProductAsync(7);

        Assert.NotNull(detail);
        Assert.Equal(p.SourceUrl, detail.SourceUrl);
        Assert.Equal([12m, 11m, 10m], detail.PriceHistory.Select(h => h.Price));
        Assert.Equal(10m, detail.LatestPrice!.Price);
    }
}

public class PriceScrapingServiceTests
{
    private readonly FakeProductRepository _repo = new();

    private PriceScrapingService Create(params FakeScraper[] scrapers) =>
        new(scrapers, _repo, NullLogger<PriceScrapingService>.Instance);

    [Fact]
    public async Task Persists_each_scrapers_results()
    {
        var a = new FakeScraper { Retailer = Retailer.Repco, Products = [Product("A"), Product("B")] };
        var b = new FakeScraper { Retailer = Retailer.SupercheapAuto, Products = [Product("C", Retailer.SupercheapAuto)] };

        await Create(a, b).RunAllAsync();

        Assert.Equal([2, 1], _repo.Upserts.Select(u => u.Products.Count));
    }

    [Fact]
    public async Task A_failing_scraper_does_not_stop_the_others()
    {
        var failing = new FakeScraper { Throws = new HttpRequestException("403") };
        var ok = new FakeScraper { Retailer = Retailer.SupercheapAuto, Products = [Product("C", Retailer.SupercheapAuto)] };

        await Create(failing, ok).RunAllAsync();

        Assert.Equal(1, ok.Calls);
        Assert.Single(_repo.Upserts);
    }

    [Fact]
    public async Task Does_nothing_when_already_cancelled()
    {
        var scraper = new FakeScraper { Products = [Product()] };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Create(scraper).RunAllAsync(cts.Token);

        Assert.Equal(0, scraper.Calls);
        Assert.Empty(_repo.Upserts);
    }

    [Fact]
    public async Task Scrapes_and_saves_features_for_products_missing_them()
    {
        var missing = Product("A");
        _repo.MissingFeatures = [missing, Product("C", Retailer.SupercheapAuto)];
        var scraper = new FakeScraper
        {
            Retailer = Retailer.Repco,
            Products = [missing],
            Features = new() { ["A"] = ["Hyspec"] },
        };

        await Create(scraper).RunAllAsync();

        Assert.Same(missing, Assert.Single(scraper.FeatureRequests));
        var (retailer, saved) = Assert.Single(_repo.SavedFeatures);
        Assert.Equal(Retailer.Repco, retailer);
        Assert.Equal(["Hyspec"], saved["A"]);
        Assert.Equal(PriceScrapingService.FeaturePagesPerRun, Assert.Single(_repo.MissingFeaturesRequests).Take);
    }

    [Fact]
    public async Task Skips_the_feature_scrape_when_no_product_is_missing_features()
    {
        var scraper = new FakeScraper { Products = [Product()] };

        await Create(scraper).RunAllAsync();

        Assert.Empty(scraper.FeatureRequests);
        Assert.Empty(_repo.SavedFeatures);
    }

    [Fact]
    public async Task A_failing_feature_scrape_does_not_stop_the_others()
    {
        _repo.MissingFeatures = [Product("A"), Product("C", Retailer.SupercheapAuto)];
        var failing = new FakeScraper { FeaturesThrow = new HttpRequestException("403") };
        var ok = new FakeScraper
        {
            Retailer = Retailer.SupercheapAuto,
            Features = new() { ["C"] = ["Fits all"] },
        };

        await Create(failing, ok).RunAllAsync();

        var (retailer, _) = Assert.Single(_repo.SavedFeatures);
        Assert.Equal(Retailer.SupercheapAuto, retailer);
    }

    [Fact]
    public async Task Stamps_upserts_with_the_current_utc_time()
    {
        var before = DateTime.UtcNow;

        await Create(new FakeScraper { Products = [Product()] }).RunAllAsync();

        var now = Assert.Single(_repo.Upserts).Now;
        Assert.InRange(now, before, DateTime.UtcNow);
    }
}
