using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Application.Dtos;

namespace AutoMotivePriceTracker.Application.Services;

public class ProductCatalogService : IProductCatalogService
{
    private const int HomeSectionSize = 8;

    private readonly IProductRepository _products;

    public ProductCatalogService(IProductRepository products)
    {
        _products = products;
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

    public async Task<ProductDetailDto?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdWithHistoryAsync(id, ct);
        return product == null ? null : ProductDetailDto.From(product);
    }
}
