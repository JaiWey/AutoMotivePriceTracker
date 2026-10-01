namespace AutoMotivePriceTracker.Domain.Entities;

public class PriceRecord
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal Price { get; set; }
    public decimal? WasPrice { get; set; }
    public DateTime Date { get; set; }

    public Product Product { get; set; } = null!;

    public bool IsOnSale => WasPrice.HasValue && WasPrice > Price;
    public decimal? DiscountPercent => IsOnSale
        ? Math.Round((1 - Price / WasPrice!.Value) * 100, 1)
        : null;
}
