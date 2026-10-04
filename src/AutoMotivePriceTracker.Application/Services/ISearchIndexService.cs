namespace AutoMotivePriceTracker.Application.Services;

public interface ISearchIndexService
{
    /// <summary>Rebuilds the search index from the database (idempotent). Returns the number of products indexed.</summary>
    Task<int> ReindexAllAsync(CancellationToken ct = default);
}
