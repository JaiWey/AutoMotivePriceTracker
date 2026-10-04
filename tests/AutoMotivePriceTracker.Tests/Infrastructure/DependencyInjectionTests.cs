using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Infrastructure;
using AutoMotivePriceTracker.Infrastructure.Search;
using Microsoft.Extensions.DependencyInjection;

namespace AutoMotivePriceTracker.Tests.Infrastructure;

/// <summary>The composition root must be resolvable as wired in Program.cs (no connections are opened).</summary>
public class DependencyInjectionTests
{
    [Fact]
    public void Infrastructure_registrations_resolve_the_search_and_repository_ports()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure("Host=localhost;Database=unused", new SearchOptions { Url = "http://localhost:9200" });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<OpenSearchProductSearch>(scope.ServiceProvider.GetRequiredService<IProductSearch>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductRepository>());
    }
}
