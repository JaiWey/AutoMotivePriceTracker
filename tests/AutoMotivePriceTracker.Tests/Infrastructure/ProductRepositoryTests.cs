using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using AutoMotivePriceTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Infrastructure;

/// <summary>One PostgreSQL container for all repository tests; the schema comes from the real migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();

    public IDbContextFactory<AppDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Factory = new TestDbFactory(_container.GetConnectionString());
        await using var db = Factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = Factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "PriceRecords", "Products" RESTART IDENTITY CASCADE""");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class TestDbFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
}

/// <summary>Runs the real repository against real PostgreSQL (needs Docker).</summary>
public sealed class ProductRepositoryTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ProductRepository _repo;

    public ProductRepositoryTests(PostgresFixture db)
    {
        _db = db;
        _factory = db.Factory;
        _repo = new ProductRepository(_factory);
    }

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<Product>> StoredProducts()
    {
        await using var db = _factory.CreateDbContext();
        return await db.Products.Include(p => p.PriceHistory).OrderBy(p => p.Id).ToListAsync();
    }

    [Fact]
    public async Task Features_are_listed_as_missing_until_saved_and_then_round_trip()
    {
        await _repo.UpsertScrapedAsync([Product("A"), Product("B"), Product("C", Retailer.SupercheapAuto)], T0);

        var missing = await _repo.GetMissingFeaturesAsync(Retailer.Repco, 10);
        Assert.Equal(["A", "B"], missing.Select(p => p.Sku).Order());

        await _repo.SaveFeaturesAsync(Retailer.Repco,
            new Dictionary<string, IReadOnlyList<string>> { ["A"] = ["Hyspec", "5L"], ["B"] = [] }, T0.AddDays(1));

        Assert.Empty(await _repo.GetMissingFeaturesAsync(Retailer.Repco, 10));
        Assert.Single(await _repo.GetMissingFeaturesAsync(Retailer.SupercheapAuto, 10));

        var stored = (await StoredProducts()).ToDictionary(p => p.Sku);
        Assert.Equal(["Hyspec", "5L"], stored["A"].Features);
        Assert.Empty(stored["B"].Features);
        Assert.Equal(T0.AddDays(1), stored["B"].FeaturesScrapedAt);
    }

    [Fact]
    public async Task GetMissingFeatures_respects_take()
    {
        await _repo.UpsertScrapedAsync([Product("A"), Product("B"), Product("C")], T0);

        Assert.Single(await _repo.GetMissingFeaturesAsync(Retailer.Repco, 1));
    }

    [Fact]
    public async Task Upsert_inserts_new_products_with_first_price_stamped()
    {
        await _repo.UpsertScrapedAsync([Product("A", price: 10m)], T0);

        var stored = Assert.Single(await StoredProducts());
        Assert.Equal(T0, stored.LastChecked);
        var record = Assert.Single(stored.PriceHistory);
        Assert.Equal(10m, record.Price);
        Assert.Equal(T0, record.Date);
    }

    [Fact]
    public async Task Upsert_unchanged_price_adds_no_record_but_refreshes_last_checked()
    {
        await _repo.UpsertScrapedAsync([Product("A", price: 10m)], T0);
        await _repo.UpsertScrapedAsync([Product("A", price: 10m)], T0.AddDays(1));

        var stored = Assert.Single(await StoredProducts());
        Assert.Single(stored.PriceHistory);
        Assert.Equal(T0.AddDays(1), stored.LastChecked);
    }

    [Fact]
    public async Task Upsert_changed_price_appends_a_record()
    {
        await _repo.UpsertScrapedAsync([Product("A", price: 10m)], T0);
        await _repo.UpsertScrapedAsync([Product("A", price: 8m, wasPrice: 10m)], T0.AddDays(1));

        var stored = Assert.Single(await StoredProducts());
        Assert.Equal([10m, 8m], stored.PriceHistory.OrderBy(r => r.Date).Select(r => r.Price));
    }

    [Fact]
    public async Task Upsert_dedupes_the_same_sku_within_one_batch()
    {
        await _repo.UpsertScrapedAsync([Product("A", price: 10m), Product("A", price: 11m)], T0);

        var stored = Assert.Single(await StoredProducts());
        Assert.Equal(10m, Assert.Single(stored.PriceHistory).Price);
    }

    [Fact]
    public async Task Same_sku_at_different_retailers_are_distinct_products()
    {
        await _repo.UpsertScrapedAsync(
            [Product("A", Retailer.Repco), Product("A", Retailer.SupercheapAuto)], T0);

        Assert.Equal(2, (await StoredProducts()).Count);
    }

    [Fact]
    public async Task Upsert_handles_batches_larger_than_one_chunk()
    {
        var batch = Enumerable.Range(0, 1200).Select(i => Product($"S{i}", price: 1m + i)).ToList();
        await _repo.UpsertScrapedAsync(batch, T0);

        // second pass: every product is matched (no unique-index violation) and one price changes
        var again = Enumerable.Range(0, 1200).Select(i => Product($"S{i}", price: i == 700 ? 99m : 1m + i)).ToList();
        await _repo.UpsertScrapedAsync(again, T0.AddDays(1));

        var stored = await StoredProducts();
        Assert.Equal(1200, stored.Count);
        Assert.Equal(1201, stored.Sum(p => p.PriceHistory.Count));
    }

    [Fact]
    public async Task GetByIdWithHistory_returns_full_history_or_null()
    {
        await _repo.UpsertScrapedAsync([Product("A", price: 10m)], T0);
        await _repo.UpsertScrapedAsync([Product("A", price: 9m)], T0.AddDays(1));
        var id = (await StoredProducts()).Single().Id;

        var product = await _repo.GetByIdWithHistoryAsync(id);

        Assert.Equal(2, product!.PriceHistory.Count);
        Assert.Null(await _repo.GetByIdWithHistoryAsync(id + 1000));
    }

    [Fact]
    public async Task GetAll_and_Recent_load_only_the_latest_price_newest_checked_first()
    {
        await _repo.UpsertScrapedAsync([Product("OLD", price: 1m)], T0);
        await _repo.UpsertScrapedAsync([Product("OLD", price: 2m)], T0.AddDays(1));
        await _repo.UpsertScrapedAsync([Product("NEW", price: 5m)], T0.AddDays(2));

        var all = await _repo.GetAllWithLatestPriceAsync();
        var recent = await _repo.GetRecentAsync(1);

        Assert.Equal(["NEW", "OLD"], all.Select(p => p.Sku));
        Assert.Equal(2m, all[1].PriceHistory.Single().Price);
        Assert.Equal("NEW", Assert.Single(recent).Sku);
    }

    [Fact]
    public async Task GetOnSale_returns_only_products_with_a_markdown_up_to_take()
    {
        await _repo.UpsertScrapedAsync(
        [
            Product("SALE1", price: 8m, wasPrice: 10m),
            Product("SALE2", price: 8m, wasPrice: 10m),
            Product("FULL", price: 10m),
        ], T0);

        var deals = await _repo.GetOnSaleAsync(10);
        var limited = await _repo.GetOnSaleAsync(1);

        Assert.Equal(["SALE1", "SALE2"], deals.Select(p => p.Sku).Order());
        Assert.Single(limited);
    }

    [Fact]
    public async Task CountByRetailer_includes_retailers_with_no_products()
    {
        await _repo.UpsertScrapedAsync([Product("A"), Product("B")], T0);

        var counts = await _repo.CountByRetailerAsync();

        Assert.Equal(2, counts[Retailer.Repco]);
        Assert.Equal(0, counts[Retailer.SupercheapAuto]);
    }
}
