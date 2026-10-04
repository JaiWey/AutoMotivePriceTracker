using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Infrastructure.Search;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Infrastructure;

/// <summary>One single-node Elasticsearch (security off) for all search tests (needs Docker).</summary>
public sealed class ElasticsearchFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("docker.elastic.co/elasticsearch/elasticsearch:9.2.0")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    public ElasticsearchClient Client { get; private set; } = null!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync()
    {
        await _container.StartAsync();
        Client = new ElasticsearchClient(new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(9200)}"));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class ElasticProductSearchTests : IClassFixture<ElasticsearchFixture>
{
    private readonly ElasticsearchFixture _es;

    public ElasticProductSearchTests(ElasticsearchFixture es) => _es = es;

    /// <summary>A search over a fresh index (one per test) holding the given products with ids 1..n.</summary>
    private async Task<ElasticProductSearch> SearchWith(params Product[] products)
    {
        for (var i = 0; i < products.Length; i++) products[i].Id = i + 1;

        var options = Options.Create(new ElasticsearchOptions { Index = $"products-{Guid.NewGuid():N}" });
        var search = new ElasticProductSearch(_es.Client, options, NullLogger<ElasticProductSearch>.Instance);
        await search.IndexAsync(products);
        return search;
    }

    private static Product Item(string sku, string name, string? brand = null, string? category = null,
        params string[] features)
    {
        var p = Product(sku, name: name, category: category);
        p.Brand = brand;
        p.Features = features.ToList();
        return p;
    }

    [Theory]
    [InlineData("engine oil filter")]
    [InlineData("engine oil fliters")] // typo + plural
    [InlineData("FILTER OIL ENGINE")]  // order and case
    public async Task Finds_products_containing_every_word_tolerating_typos_and_plurals(string query)
    {
        var search = await SearchWith(
            Item("FILTER", "Ryco Oil Filter Z145A", "Ryco", "oil-filters", "Suits most engine types"),
            Item("OIL", "Penrite 5W-30 Engine Oil 5L", "Penrite", "engine-oil"),
            Item("WIPER", "Bosch Wiper Blade", "Bosch", "wipers"));

        var ids = await search.SearchIdsAsync(query, 50);

        Assert.Equal([1], ids);
    }

    [Fact]
    public async Task Searches_the_feature_bullet_points()
    {
        var search = await SearchWith(
            Item("A", "Generic Cleaner", features: "Fully synthetic formula"),
            Item("B", "Brake Pads"));

        Assert.Equal([1], await search.SearchIdsAsync("synthetic", 50));
    }

    [Fact]
    public async Task Ranks_name_matches_above_feature_matches()
    {
        var search = await SearchWith(
            Item("FEAT", "Generic Cleaner", features: "Fully synthetic formula"),
            Item("NAME", "Synthetic Oil 5L"));

        Assert.Equal([2, 1], await search.SearchIdsAsync("synthetic", 50));
    }

    [Fact]
    public async Task Returns_nothing_when_one_word_matches_no_product()
    {
        var search = await SearchWith(Item("A", "Oil Filter"));

        Assert.Empty(await search.SearchIdsAsync("oil zebra", 50));
    }

    [Fact]
    public async Task Reindexing_replaces_a_product_instead_of_duplicating_it()
    {
        var search = await SearchWith(Item("A", "Oil Filter"));

        var renamed = Item("A", "Air Filter");
        renamed.Id = 1;
        await search.IndexAsync([renamed]);

        Assert.Empty(await search.SearchIdsAsync("oil", 50));
        Assert.Equal([1], await search.SearchIdsAsync("air", 50));
    }

    [Fact]
    public async Task Limits_the_number_of_results()
    {
        var search = await SearchWith(Item("A", "Oil One"), Item("B", "Oil Two"), Item("C", "Oil Three"));

        Assert.Equal(2, (await search.SearchIdsAsync("oil", 2)).Count);
    }
}
