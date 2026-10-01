using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public Retailer Retailer { get; set; }
    public DateTime LastChecked { get; set; }

    public ICollection<PriceRecord> PriceHistory { get; set; } = new List<PriceRecord>();

    public PriceRecord? LatestPrice => PriceHistory
        .OrderByDescending(p => p.Date)
        .FirstOrDefault();

    /// <summary>Stamps a freshly scraped product (not yet persisted) with the scrape time.</summary>
    public void MarkAsNew(DateTime now)
    {
        LastChecked = now;
        foreach (var record in PriceHistory)
            record.Date = now;
    }

    /// <summary>
    /// Applies a re-scrape of this product. A new <see cref="PriceRecord"/> is only added
    /// when the price or was-price differs from the latest one (price deduplication).
    /// </summary>
    public void ApplyScrape(Product incoming, DateTime now)
    {
        Name = incoming.Name;
        ImageUrl = incoming.ImageUrl ?? ImageUrl;
        SourceUrl = incoming.SourceUrl;
        Category = incoming.Category ?? Category;
        LastChecked = now;

        var newPrice = incoming.PriceHistory.FirstOrDefault();
        if (newPrice == null) return;

        var latest = LatestPrice;
        if (latest == null || latest.Price != newPrice.Price || latest.WasPrice != newPrice.WasPrice)
        {
            PriceHistory.Add(new PriceRecord
            {
                Price = newPrice.Price,
                WasPrice = newPrice.WasPrice,
                Date = now
            });
        }
    }
}
