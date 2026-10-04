using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Infrastructure.Persistence;
using AutoMotivePriceTracker.Infrastructure.Search;
using AutoMotivePriceTracker.Infrastructure.Scraping.Scrapers;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AutoMotivePriceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, ElasticsearchOptions elasticsearch)
    {
        services.AddDbContextFactory<AppDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<IProductRepository, ProductRepository>();

        // Search index (Elasticsearch): thread-safe client and adapter, shared for the app's lifetime
        services.AddSingleton(Options.Create(elasticsearch));
        services.AddSingleton(_ =>
        {
            var settings = new ElasticsearchClientSettings(new Uri(elasticsearch.Url));
            if (!string.IsNullOrEmpty(elasticsearch.ApiKey))
                settings.Authentication(new ApiKey(elasticsearch.ApiKey));
            return new ElasticsearchClient(settings);
        });
        services.AddSingleton<IProductSearch, ElasticProductSearch>();

        // Scrapers: add new retailers here
       // services.AddScoped<IProductScraper, RepcoScraper>();
        services.AddScoped<IProductScraper, SupercheapAutoScraper>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations. Call once at startup.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
