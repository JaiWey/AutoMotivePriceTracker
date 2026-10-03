using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Application.Abstractions;

public interface IProductScraper
{
    Retailer Retailer { get; }
    Task<IEnumerable<Product>> ScrapeAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads each product's page for its feature bullets, keyed by SKU. Products whose page could not be
    /// read are left out, so they are tried again on a later run; a readable page with no features maps to an empty list.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ScrapeFeaturesAsync(
        IReadOnlyCollection<Product> products, CancellationToken ct = default);
}
