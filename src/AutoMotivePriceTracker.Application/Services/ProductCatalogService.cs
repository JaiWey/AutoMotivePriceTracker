using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Application.Dtos;

namespace AutoMotivePriceTracker.Application.Services;

public class ProductCatalogService : IProductCatalogService
{
    private const int HomeSectionSize = 8;
    private const int MaxSearchResults = 2000;

    private readonly IProductRepository _products;
    private readonly IProductSearch _search;

    public ProductCatalogService(IProductRepository products, IProductSearch search)
    {
        _products = products;
        _search = search;
    }

    public async Task<HomeSummaryDto> GetHomeSummaryAsync(CancellationToken ct = default)
    {
        var deals = await _products.GetOnSaleAsync(HomeSectionSize, ct);
        var recent = await _products.GetRecentAsync(HomeSectionSize, ct);
        var counts = await _products.CountByRetailerAsync(ct);

        return new HomeSummaryDto(
            deals.Select(ProductSummaryDto.From).ToList(),
            recent.Select(ProductSummaryDto.From).ToList(),
            counts.Values.Sum(),
            counts);
    }

    public async Task<IReadOnlyList<ProductSummaryDto>> GetAllProductsAsync(CancellationToken ct = default)
    {
        var all = await _products.GetAllWithLatestPriceAsync(ct);
        return all.Select(ProductSummaryDto.From).ToList();
    }

    public async Task<IReadOnlyList<int>> SearchProductIdsAsync(string query, CancellationToken ct = default)
    {
        var text = (query ?? string.Empty).Trim();
        return text.Length == 0 ? Array.Empty<int>() : await _search.SearchIdsAsync(text, MaxSearchResults, ct);
    }

    public async Task<ProductDetailDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdWithHistoryAsync(id, ct);
        return product == null ? null : ProductDetailDto.From(product);
    }
}
