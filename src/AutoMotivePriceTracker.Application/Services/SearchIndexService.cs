using AutoMotivePriceTracker.Application.Abstractions;

namespace AutoMotivePriceTracker.Application.Services;

public class SearchIndexService : ISearchIndexService
{
    private readonly IProductRepository _products;
    private readonly IProductSearch _search;

    public SearchIndexService(IProductRepository products, IProductSearch search)
    {
        _products = products;
        _search = search;
    }

    public async Task<int> ReindexAllAsync(CancellationToken ct = default)
    {
        var all = await _products.GetAllWithLatestPriceAsync(ct);
        await _search.IndexAsync(all, ct);
        return all.Count;
    }
}
