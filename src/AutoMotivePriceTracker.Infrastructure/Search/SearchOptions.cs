namespace AutoMotivePriceTracker.Infrastructure.Search;

public class SearchOptions
{
    public const string SectionName = "Search";

    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>Index holding one document per product. Use a different name per environment sharing a cluster.</summary>
    public string Index { get; set; } = "products";

    /// <summary>Optional basic-auth credentials for a secured cluster (empty for the local dev container and VPC-only AWS domain).</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }
}
