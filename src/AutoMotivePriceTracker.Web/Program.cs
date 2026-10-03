using AutoMotivePriceTracker.Application;
using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Infrastructure;
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
builder.Services.AddInfrastructure(connectionString);

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

app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);
app.MapApi();

app.Run();
