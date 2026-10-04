using AutoMotivePriceTracker.Application.Dtos;

namespace AutoMotivePriceTracker.Application.Services;

public interface IProductCatalogService
{
    Task<HomeSummaryDto> GetHomeSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductSummaryDto>> GetAllProductsAsync(CancellationToken ct = default);
    /// <summary>Ids of products matching the free-text query, best match first; empty for a blank query.</summary>
    Task<IReadOnlyList<int>> SearchProductIdsAsync(string query, CancellationToken ct = default);
    Task<ProductDetailDto?> GetProductAsync(int id, CancellationToken ct = default);
}
