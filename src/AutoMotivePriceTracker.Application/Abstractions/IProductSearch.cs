using AutoMotivePriceTracker.Domain.Entities;

namespace AutoMotivePriceTracker.Application.Abstractions;

/// <summary>Full-text product search, implemented over a separate search index (Elasticsearch).</summary>
public interface IProductSearch
{
    /// <summary>
    /// Ids of products matching the free-text query (every word, tolerant of typos and plurals, across
    /// name, brand, category and features), best match first.
    /// </summary>
    Task<IReadOnlyList<int>> SearchIdsAsync(string query, int take, CancellationToken ct = default);

    /// <summary>Adds or replaces the given products in the index (keyed by product id).</summary>
    Task IndexAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default);
}
