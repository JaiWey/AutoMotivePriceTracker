using AutoMotivePriceTracker.Application.Services;
using AutoMotivePriceTracker.Web.BackgroundServices;

namespace AutoMotivePriceTracker.Web.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/home", async (IProductCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetHomeSummaryAsync(ct)));

        api.MapGet("/products", async (IProductCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetAllProductsAsync(ct)));

        api.MapGet("/products/search", async (string? q, IProductCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.SearchProductIdsAsync(q ?? string.Empty, ct)));

        api.MapGet("/products/{id:int}", async (int id, IProductCatalogService catalog, CancellationToken ct) =>
            await catalog.GetProductAsync(id, ct) is { } product ? Results.Ok(product) : Results.NotFound());

        api.MapPost("/admin/scrape", (ScrapeRunCoordinator coordinator) =>
            coordinator.TryStartInBackground()
                ? Results.Accepted("/api/admin/scrape/status", coordinator.Status)
                : Results.Conflict(coordinator.Status));

        api.MapPost("/admin/reindex", async (ISearchIndexService index, CancellationToken ct) =>
            Results.Ok(new { indexed = await index.ReindexAllAsync(ct) }));

        api.MapGet("/admin/scrape/status", (ScrapeRunCoordinator coordinator) =>
            Results.Ok(coordinator.Status));

        return app;
    }
}
