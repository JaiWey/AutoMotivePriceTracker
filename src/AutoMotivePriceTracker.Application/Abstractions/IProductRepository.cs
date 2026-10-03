using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>Products that have at least one price record currently marked down, newest first.</summary>
    Task<IReadOnlyList<Product>> GetOnSaleAsync(int take, CancellationToken ct = default);

    /// <summary>Most recently checked products, each with only its latest price record loaded.</summary>
    Task<IReadOnlyList<Product>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>All products, each with only its latest price record loaded.</summary>
    Task<IReadOnlyList<Product>> GetAllWithLatestPriceAsync(CancellationToken ct = default);

    /// <summary>A product with its full price history, or null if not found.</summary>
    Task<Product?> GetByIdWithHistoryAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Retailer, int>> CountByRetailerAsync(CancellationToken ct = default);

    /// <summary>Up to <paramref name="take"/> of a retailer's products whose page has never been read for features.</summary>
    Task<IReadOnlyList<Product>> GetMissingFeaturesAsync(Retailer retailer, int take, CancellationToken ct = default);

    /// <summary>Stores scraped features (keyed by SKU) on a retailer's products.</summary>
    Task SaveFeaturesAsync(Retailer retailer, IReadOnlyDictionary<string, IReadOnlyList<string>> featuresBySku,
        DateTime now, CancellationToken ct = default);

    /// <summary>Inserts new products and applies <see cref="Product.ApplyScrape"/> to existing ones (matched by SKU + retailer).</summary>
    Task UpsertScrapedAsync(IEnumerable<Product> scraped, DateTime now, CancellationToken ct = default);
}
