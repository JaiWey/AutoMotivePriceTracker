using AutoMotivePriceTracker.Application.Dtos;

namespace AutoMotivePriceTracker.Application.Services;

public interface IProductCatalogService
{
    Task<HomeSummaryDto> GetHomeSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductSummaryDto>> GetAllProductsAsync(CancellationToken ct = default);
    Task<ProductDetailDto?> GetProductAsync(int id, CancellationToken ct = default);
}
