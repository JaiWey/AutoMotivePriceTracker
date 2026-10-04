using AutoMotivePriceTracker.Application;
using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Application.Services;
using AutoMotivePriceTracker.Infrastructure;
using AutoMotivePriceTracker.Infrastructure.Search;
using AutoMotivePriceTracker.Web.BackgroundServices;
using AutoMotivePriceTracker.Web.Endpoints;
using AutoMotivePriceTracker.Web.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

const string CorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

// Serilog behind ILogger<T>; sinks and levels come from the "Serilog" config section
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

// Composition root: wire each layer
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddApplication();
var elasticsearch = builder.Configuration.GetSection(ElasticsearchOptions.SectionName).Get<ElasticsearchOptions>()
                    ?? new ElasticsearchOptions();
builder.Services.AddInfrastructure(connectionString, elasticsearch);

// Scraping can be switched off (e.g. for end-to-end tests): no scrapers, no scheduled run.
var scrapingEnabled = builder.Configuration.GetValue("Scraping:Enabled", true);
if (!scrapingEnabled)
    builder.Services.RemoveAll<IProductScraper>();

// Background scraping (the coordinator prevents manual and scheduled runs overlapping)
builder.Services.AddSingleton<ScrapeRunCoordinator>();
if (scrapingEnabled)
    builder.Services.AddHostedService<ScraperBackgroundService>();

// JSON API: enums as strings, DateTimes always UTC with a Z suffix
builder.Services.AddApiJson();

// The Angular client is deployed separately, so allow its origin(s)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Apply migrations on startup
await app.Services.InitializeDatabaseAsync();

// Build the search index in the background so startup never waits for (or fails on) Elasticsearch.
// Search works as soon as it finishes; a failure is logged and the next scrape or reindex retries it.
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var count = await scope.ServiceProvider.GetRequiredService<ISearchIndexService>().ReindexAllAsync();
        app.Logger.LogInformation("Indexed {Count} products for search", count);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Initial search indexing failed; search stays unavailable until the next reindex");
    }
}));

app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);
app.MapApi();

app.Run();
