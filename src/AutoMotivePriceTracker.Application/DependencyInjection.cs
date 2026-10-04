using AutoMotivePriceTracker.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AutoMotivePriceTracker.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPriceScrapingService, PriceScrapingService>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddScoped<ISearchIndexService, SearchIndexService>();
        return services;
    }
}
