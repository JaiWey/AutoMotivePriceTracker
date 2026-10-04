using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Infrastructure.Persistence;
using AutoMotivePriceTracker.Infrastructure.Search;
using AutoMotivePriceTracker.Infrastructure.Scraping.Scrapers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenSearch.Client;

namespace AutoMotivePriceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, SearchOptions search)
    {
        services.AddDbContextFactory<AppDbContext>(opt =>
            opt.UseNpgsql(connectionString));
        services.AddScoped<IProductRepository, ProductRepository>();

        // Search index (OpenSearch): thread-safe client and adapter, shared for the app's lifetime
        services.AddSingleton(Options.Create(search));
        services.AddSingleton<IOpenSearchClient>(_ =>
        {
            var settings = new ConnectionSettings(new Uri(search.Url));
            if (!string.IsNullOrEmpty(search.Username))
                settings.BasicAuthentication(search.Username, search.Password ?? string.Empty);
            return new OpenSearchClient(settings);
        });
        services.AddSingleton<IProductSearch, OpenSearchProductSearch>();

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
