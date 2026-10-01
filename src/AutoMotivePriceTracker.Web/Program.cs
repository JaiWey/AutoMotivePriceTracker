using AutoMotivePriceTracker.Application;
using AutoMotivePriceTracker.Infrastructure;
using AutoMotivePriceTracker.Web.BackgroundServices;
using AutoMotivePriceTracker.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Composition root: wire each layer
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "prices.db");
builder.Services.AddApplication();
builder.Services.AddInfrastructure(dbPath);

// Background scraping
builder.Services.AddHostedService<ScraperBackgroundService>();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply migrations on startup
await app.Services.InitializeDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
