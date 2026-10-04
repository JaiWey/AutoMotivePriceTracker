using AutoMotivePriceTracker.Application.Services;
using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;

namespace AutoMotivePriceTracker.Tests.Support;

public class FakeProductRepository : IProductRepository
{
    public List<Product> OnSale { get; set; } = [];
    public List<Product> Recent { get; set; } = [];
    public List<Product> All { get; set; } = [];
    public Dictionary<int, Product> ById { get; } = [];
    public Dictionary<Retailer, int> Counts { get; set; } = new() { [Retailer.Repco] = 0, [Retailer.SupercheapAuto] = 0 };

    public int? OnSaleTake { get; private set; }
    public int? RecentTake { get; private set; }
    public List<(List<Product> Products, DateTime Now)> Upserts { get; } = [];

    public Task<IReadOnlyList<Product>> GetOnSaleAsync(int take, CancellationToken ct = default)
    {
        OnSaleTake = take;
        return Task.FromResult<IReadOnlyList<Product>>(OnSale);
    }

    public Task<IReadOnlyList<Product>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        RecentTake = take;
        return Task.FromResult<IReadOnlyList<Product>>(Recent);
    }

    public Task<IReadOnlyList<Product>> GetAllWithLatestPriceAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Product>>(All);

    public Task<Product?> GetByIdWithHistoryAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(ById.GetValueOrDefault(id));

    public Task<IReadOnlyDictionary<Retailer, int>> CountByRetailerAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Retailer, int>>(Counts);

    public List<Product> MissingFeatures { get; set; } = [];
    public List<(Retailer Retailer, int Take)> MissingFeaturesRequests { get; } = [];
    public List<(Retailer Retailer, IReadOnlyDictionary<string, IReadOnlyList<string>> Features)> SavedFeatures { get; } = [];

    public Task<IReadOnlyList<Product>> GetMissingFeaturesAsync(Retailer retailer, int take, CancellationToken ct = default)
    {
        MissingFeaturesRequests.Add((retailer, take));
        return Task.FromResult<IReadOnlyList<Product>>(MissingFeatures.Where(p => p.Retailer == retailer).Take(take).ToList());
    }

    public Task SaveFeaturesAsync(Retailer retailer, IReadOnlyDictionary<string, IReadOnlyList<string>> featuresBySku,
        DateTime now, CancellationToken ct = default)
    {
        SavedFeatures.Add((retailer, featuresBySku));
        return Task.CompletedTask;
    }

    public Task UpsertScrapedAsync(IEnumerable<Product> scraped, DateTime now, CancellationToken ct = default)
    {
        Upserts.Add((scraped.ToList(), now));
        return Task.CompletedTask;
    }
}

public class FakeScraper : IProductScraper
{
    public Retailer Retailer { get; init; } = Retailer.Repco;
    public List<Product> Products { get; init; } = [];
    public Exception? Throws { get; init; }
    public int Calls { get; private set; }
    public Dictionary<string, IReadOnlyList<string>> Features { get; init; } = [];
    public Exception? FeaturesThrow { get; init; }
    public List<Product> FeatureRequests { get; } = [];

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ScrapeFeaturesAsync(
        IReadOnlyCollection<Product> products, CancellationToken ct = default)
    {
        FeatureRequests.AddRange(products);
        if (FeaturesThrow != null) throw FeaturesThrow;
        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(Features);
    }

    public Task<IEnumerable<Product>> ScrapeAsync(CancellationToken ct = default)
    {
        Calls++;
        if (Throws != null) throw Throws;
        return Task.FromResult<IEnumerable<Product>>(Products);
    }
}

public class FakeProductSearch : IProductSearch
{
    public List<int> Result { get; set; } = [];
    public string? LastQuery { get; private set; }
    public int? LastTake { get; private set; }
    public int SearchCalls { get; private set; }
    public List<IReadOnlyCollection<Product>> Indexed { get; } = [];

    public Task<IReadOnlyList<int>> SearchIdsAsync(string query, int take, CancellationToken ct = default)
    {
        SearchCalls++;
        LastQuery = query;
        LastTake = take;
        return Task.FromResult<IReadOnlyList<int>>(Result);
    }

    public Task IndexAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default)
    {
        Indexed.Add(products);
        return Task.CompletedTask;
    }
}

public class FakeSearchIndexService : ISearchIndexService
{
    public int Reindexes { get; private set; }
    public Exception? Throws { get; set; }

    public Task<int> ReindexAllAsync(CancellationToken ct = default)
    {
        Reindexes++;
        if (Throws != null) throw Throws;
        return Task.FromResult(42);
    }
}
