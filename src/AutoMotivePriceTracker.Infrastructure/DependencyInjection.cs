using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Infrastructure.Persistence;
using AutoMotivePriceTracker.Infrastructure.Scraping.Scrapers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoMotivePriceTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string sqliteDbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(sqliteDbPath)!);

        services.AddDbContextFactory<AppDbContext>(opt =>
            opt.UseSqlite($"Data Source={sqliteDbPath}"));
        services.AddScoped<IProductRepository, ProductRepository>();

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
