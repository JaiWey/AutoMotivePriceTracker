using System.Globalization;
using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSearch.Client;

namespace AutoMotivePriceTracker.Infrastructure.Search;

/// <summary>
/// Product search over one OpenSearch index (document id = product id). The index is derived data:
/// it is created on first use and can be rebuilt at any time from the database.
/// </summary>
public sealed class OpenSearchProductSearch : IProductSearch
{
    private const int BulkSize = 1000;
    private const int MaxTerms = 8;
    private const string Analyzer = "english"; // stems "filters" -> "filter"

    private readonly IOpenSearchClient _client;
    private readonly string _index;
    private readonly ILogger<OpenSearchProductSearch> _logger;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private bool _indexReady;

    public OpenSearchProductSearch(IOpenSearchClient client, IOptions<SearchOptions> options,
        ILogger<OpenSearchProductSearch> logger)
    {
        _client = client;
        _index = options.Value.Index;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> SearchIdsAsync(string query, int take, CancellationToken ct = default)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(MaxTerms).ToList();
        if (terms.Count == 0) return Array.Empty<int>();

        await EnsureIndexAsync(ct);

        // Every word must match somewhere (AND across words); within a word, typos up to AUTO edits are
        // tolerated and name hits outweigh brand, category and feature hits.
        var must = terms
            .Select(term => (Func<QueryContainerDescriptor<ProductDocument>, QueryContainer>)(q => q
                .MultiMatch(m => m
                    .Query(term)
                    .Fields(f => f.Field("name", 3).Field("brand", 2).Field("category").Field("features"))
                    .Fuzziness(Fuzziness.Auto)
                    .PrefixLength(1))))
            .ToArray();

        var response = await _client.SearchAsync<ProductDocument>(s => s
            .Index(_index)
            .Size(take)
            .Source(false)
            .Query(q => q.Bool(b => b.Must(must))), ct);

        if (!response.IsValid)
            throw new InvalidOperationException($"OpenSearch search failed: {response.DebugInformation}");

        return response.Hits
            .Select(h => int.Parse(h.Id, CultureInfo.InvariantCulture))
            .ToList();
    }

    public async Task IndexAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default)
    {
        await EnsureIndexAsync(ct);

        foreach (var chunk in products.Select(ProductDocument.From).Chunk(BulkSize))
        {
            var response = await _client.BulkAsync(b => b
                .Index(_index)
                .IndexMany(chunk, (op, doc) => op.Id(doc.Id)), ct);

            if (!response.IsValid || response.Errors)
                throw new InvalidOperationException($"OpenSearch bulk index failed: {response.DebugInformation}");
        }

        // Make the new documents searchable now rather than after the next periodic refresh.
        await _client.Indices.RefreshAsync(_index, ct: ct);
    }

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        if (_indexReady) return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_indexReady) return;

            var exists = await _client.Indices.ExistsAsync(_index, ct: ct);
            if (!exists.Exists)
            {
                var created = await _client.Indices.CreateAsync(_index, c => c
                    .Map<ProductDocument>(m => m.Properties(p => p
                        .Text(t => t.Name(d => d.Name).Analyzer(Analyzer))
                        .Text(t => t.Name(d => d.Brand).Analyzer(Analyzer))
                        .Text(t => t.Name(d => d.Category).Analyzer(Analyzer))
                        .Text(t => t.Name(d => d.Features).Analyzer(Analyzer))
                        .Keyword(k => k.Name(d => d.Retailer)))), ct);

                // Another instance may have created it first; that is fine.
                if (!created.IsValid && created.ServerError?.Error.Type != "resource_already_exists_exception")
                    throw new InvalidOperationException($"Could not create index '{_index}': {created.DebugInformation}");

                _logger.LogInformation("Created OpenSearch index {Index}", _index);
            }

            _indexReady = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    /// <summary>The searchable shape of a product; the document id is the product id.</summary>
    public sealed class ProductDocument
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Brand { get; set; }
        public string? Category { get; set; }
        public string[] Features { get; set; } = [];
        public string Retailer { get; set; } = string.Empty;

        public static ProductDocument From(Product p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Brand = p.Brand,
            Category = p.Category,
            Features = p.Features.ToArray(),
            Retailer = p.Retailer.ToString(),
        };
    }
}
