using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AutoMotivePriceTracker.Infrastructure.Persistence;

/// <summary>
/// Uses <see cref="IDbContextFactory{TContext}"/> with a short-lived context per call, so it is
/// safe to use from Blazor Server components whose lifetime exceeds a single request scope.
/// </summary>
public class ProductRepository : IProductRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ProductRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<Product>> GetOnSaleAsync(int take, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Products
            .Include(p => p.PriceHistory)
            .Where(p => p.PriceHistory.Any(r => r.WasPrice != null && r.WasPrice > r.Price))
            .OrderByDescending(p => p.LastChecked)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Product>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Products
            .Include(p => p.PriceHistory.OrderByDescending(r => r.Date).Take(1))
            .OrderByDescending(p => p.LastChecked)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllWithLatestPriceAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Products
            .Include(p => p.PriceHistory.OrderByDescending(r => r.Date).Take(1))
            .OrderByDescending(p => p.LastChecked)
            .ToListAsync(ct);
    }

    public async Task<Product?> GetByIdWithHistoryAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Products
            .Include(p => p.PriceHistory.OrderByDescending(r => r.Date))
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyDictionary<Retailer, int>> CountByRetailerAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var counts = await db.Products
            .GroupBy(p => p.Retailer)
            .Select(g => new { Retailer = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<Retailer>().ToDictionary(
            r => r,
            r => counts.FirstOrDefault(c => c.Retailer == r)?.Count ?? 0);
    }

    public async Task<IReadOnlyList<Product>> GetMissingFeaturesAsync(Retailer retailer, int take, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Products
            .Where(p => p.Retailer == retailer && p.FeaturesScrapedAt == null)
            .OrderBy(p => p.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task SaveFeaturesAsync(Retailer retailer, IReadOnlyDictionary<string, IReadOnlyList<string>> featuresBySku,
        DateTime now, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        foreach (var skus in featuresBySku.Keys.Chunk(500))
        {
            var products = await db.Products
                .Where(p => p.Retailer == retailer && skus.Contains(p.Sku))
                .ToListAsync(ct);

            foreach (var p in products)
                p.SetFeatures(featuresBySku[p.Sku], now);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertScrapedAsync(IEnumerable<Product> scraped, DateTime now, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // The same product can appear on several pages/categories; keep one per (retailer, SKU)
        var incomingByKey = scraped
            .GroupBy(p => (p.Retailer, p.Sku))
            .ToDictionary(g => g.Key, g => g.First());

        // Load existing rows in a few queries instead of one SELECT per product.
        // Chunked to keep each query's SKU list a reasonable size.
        var existingByKey = new Dictionary<(Retailer, string), Product>();
        foreach (var retailerGroup in incomingByKey.Keys.GroupBy(k => k.Retailer))
        {
            var retailer = retailerGroup.Key;
            foreach (var skus in retailerGroup.Select(k => k.Sku).Chunk(500))
            {
                var found = await db.Products
                    .Include(p => p.PriceHistory)
                    .Where(p => p.Retailer == retailer && skus.Contains(p.Sku))
                    .ToListAsync(ct);

                foreach (var p in found)
                    existingByKey[(p.Retailer, p.Sku)] = p;
            }
        }

        var newProducts = new List<Product>();
        foreach (var (key, incoming) in incomingByKey)
        {
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.ApplyScrape(incoming, now);
            }
            else
            {
                incoming.MarkAsNew(now);
                newProducts.Add(incoming);
            }
        }

        db.Products.AddRange(newProducts);
        await db.SaveChangesAsync(ct);
    }
}
