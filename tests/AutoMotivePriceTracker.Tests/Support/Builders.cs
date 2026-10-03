using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Tests.Support;

public static class Builders
{
    public static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A scraped (or stored) product with at most one price record.</summary>
    public static Product Product(
        string sku = "SKU1",
        Retailer retailer = Retailer.Repco,
        decimal? price = 10m,
        decimal? wasPrice = null,
        string name = "Oil",
        string? category = "engine-oil",
        string? imageUrl = null,
        DateTime? date = null)
    {
        var p = new Product
        {
            Sku = sku,
            Retailer = retailer,
            Name = name,
            Category = category,
            ImageUrl = imageUrl,
            SourceUrl = $"https://example.test/{sku}",
        };
        if (price.HasValue)
            p.PriceHistory.Add(new PriceRecord { Price = price.Value, WasPrice = wasPrice, Date = date ?? T0 });
        return p;
    }
}
