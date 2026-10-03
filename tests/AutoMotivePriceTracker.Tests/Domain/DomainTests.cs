using AutoMotivePriceTracker.Domain.Entities;
using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Domain;

public class PriceRecordTests
{
    [Fact]
    public void Is_on_sale_when_was_price_is_higher()
    {
        var r = new PriceRecord { Price = 75m, WasPrice = 100m };

        Assert.True(r.IsOnSale);
        Assert.Equal(25m, r.DiscountPercent);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    public void Not_on_sale_without_a_higher_was_price(int price, int? was)
    {
        var r = new PriceRecord { Price = price, WasPrice = was };

        Assert.False(r.IsOnSale);
        Assert.Null(r.DiscountPercent);
    }

    [Fact]
    public void Discount_is_rounded_to_one_decimal()
    {
        var r = new PriceRecord { Price = 2m, WasPrice = 3m }; // 33.333…%

        Assert.Equal(33.3m, r.DiscountPercent);
    }
}

public class ProductTests
{
    private static readonly DateTime Later = T0.AddDays(1);

    [Fact]
    public void LatestPrice_is_the_record_with_the_newest_date()
    {
        var p = Product(price: 10m, date: T0);
        p.PriceHistory.Add(new PriceRecord { Price = 12m, Date = T0.AddDays(2) });
        p.PriceHistory.Add(new PriceRecord { Price = 11m, Date = T0.AddDays(1) });

        Assert.Equal(12m, p.LatestPrice!.Price);
    }

    [Fact]
    public void LatestPrice_is_null_without_history()
    {
        Assert.Null(Product(price: null).LatestPrice);
    }

    [Fact]
    public void MarkAsNew_stamps_product_and_records()
    {
        var p = Product(price: 10m, date: DateTime.MinValue);

        p.MarkAsNew(Later);

        Assert.Equal(Later, p.LastChecked);
        Assert.All(p.PriceHistory, r => Assert.Equal(Later, r.Date));
    }

    [Fact]
    public void ApplyScrape_with_unchanged_price_adds_no_record_but_updates_last_checked()
    {
        var existing = Product(price: 10m, wasPrice: 12m);
        var incoming = Product(price: 10m, wasPrice: 12m);

        existing.ApplyScrape(incoming, Later);

        Assert.Single(existing.PriceHistory);
        Assert.Equal(Later, existing.LastChecked);
    }

    [Fact]
    public void ApplyScrape_with_changed_price_adds_a_record()
    {
        var existing = Product(price: 10m);
        var incoming = Product(price: 9m);

        existing.ApplyScrape(incoming, Later);

        Assert.Equal(2, existing.PriceHistory.Count);
        var latest = existing.LatestPrice!;
        Assert.Equal(9m, latest.Price);
        Assert.Equal(Later, latest.Date);
    }

    [Fact]
    public void ApplyScrape_with_changed_was_price_only_adds_a_record()
    {
        var existing = Product(price: 10m, wasPrice: null);
        var incoming = Product(price: 10m, wasPrice: 15m);

        existing.ApplyScrape(incoming, Later);

        Assert.Equal(2, existing.PriceHistory.Count);
        Assert.Equal(15m, existing.LatestPrice!.WasPrice);
    }

    [Fact]
    public void ApplyScrape_adds_first_record_when_product_has_no_history()
    {
        var existing = Product(price: null);

        existing.ApplyScrape(Product(price: 10m), Later);

        Assert.Single(existing.PriceHistory);
    }

    [Fact]
    public void ApplyScrape_without_incoming_price_still_updates_details()
    {
        var existing = Product(price: 10m, name: "Old");
        var incoming = Product(price: null, name: "New");

        existing.ApplyScrape(incoming, Later);

        Assert.Equal("New", existing.Name);
        Assert.Single(existing.PriceHistory);
    }

    [Fact]
    public void ApplyScrape_keeps_existing_image_and_category_when_incoming_has_none()
    {
        var existing = Product(price: 10m, imageUrl: "http://img/old.jpg", category: "engine-oil");
        var incoming = Product(price: 10m, imageUrl: null, category: null);

        existing.ApplyScrape(incoming, Later);

        Assert.Equal("http://img/old.jpg", existing.ImageUrl);
        Assert.Equal("engine-oil", existing.Category);
    }

    [Fact]
    public void ApplyScrape_overwrites_image_category_and_source_when_provided()
    {
        var existing = Product(price: 10m, imageUrl: "http://img/old.jpg", category: "a");
        var incoming = Product(price: 10m, imageUrl: "http://img/new.jpg", category: "b");

        existing.ApplyScrape(incoming, Later);

        Assert.Equal("http://img/new.jpg", existing.ImageUrl);
        Assert.Equal("b", existing.Category);
        Assert.Equal(incoming.SourceUrl, existing.SourceUrl);
    }
}
