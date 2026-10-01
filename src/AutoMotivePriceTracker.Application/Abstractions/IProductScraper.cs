using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Application.Abstractions;

public interface IProductScraper
{
    Retailer Retailer { get; }
    Task<IEnumerable<Product>> ScrapeAsync(CancellationToken ct = default);
}
