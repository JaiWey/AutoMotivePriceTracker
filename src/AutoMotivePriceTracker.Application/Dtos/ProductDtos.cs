using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Application.Dtos;

public record PricePointDto(DateTime Date, decimal Price, decimal? WasPrice, bool IsOnSale, decimal? DiscountPercent)
{
    internal static PricePointDto From(PriceRecord r) =>
        new(r.Date, r.Price, r.WasPrice, r.IsOnSale, r.DiscountPercent);
}

public record ProductSummaryDto(
    int Id,
    string Name,
    string? Brand,
    string? Category,
    string? ImageUrl,
    Retailer Retailer,
    DateTime LastChecked,
    PricePointDto? LatestPrice)
{
    public bool IsOnSale => LatestPrice?.IsOnSale ?? false;

    internal static ProductSummaryDto From(Product p) =>
        new(p.Id, p.Name, p.Brand, p.Category, p.ImageUrl, p.Retailer, p.LastChecked,
            p.LatestPrice is { } latest ? PricePointDto.From(latest) : null);
}

public record ProductDetailDto(
    int Id,
    string Name,
    string? Brand,
    string? Category,
    string? ImageUrl,
    string SourceUrl,
    Retailer Retailer,
    DateTime LastChecked,
    PricePointDto? LatestPrice,
    IReadOnlyList<PricePointDto> PriceHistory,
    IReadOnlyList<string> Features)
{
    internal static ProductDetailDto From(Product p)
    {
        var history = p.PriceHistory.OrderBy(r => r.Date).Select(PricePointDto.From).ToList();
        return new(p.Id, p.Name, p.Brand, p.Category, p.ImageUrl, p.SourceUrl, p.Retailer,
            p.LastChecked, history.LastOrDefault(), history, p.Features);
    }
}

public record HomeSummaryDto(
    IReadOnlyList<ProductSummaryDto> Deals,
    IReadOnlyList<ProductSummaryDto> Recent,
    int TotalProducts,
    IReadOnlyDictionary<Retailer, int> CountByRetailer);
