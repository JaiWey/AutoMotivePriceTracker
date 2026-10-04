namespace AutoMotivePriceTracker.Infrastructure.Search;

public class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>Index holding one document per product. Use a different name per environment sharing a cluster.</summary>
    public string Index { get; set; } = "products";

    /// <summary>Optional API key for secured clusters (leave empty for the unsecured dev container).</summary>
    public string? ApiKey { get; set; }
}
