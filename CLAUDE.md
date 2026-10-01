# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build entire solution
dotnet build

# Run the web app (starts on http://localhost:5xxx)
dotnet run --project src/AutoMotivePriceTracker.Web

# Add a new EF Core migration (run from solution root)
dotnet ef migrations add <MigrationName> \
  --project src/AutoMotivePriceTracker.Infrastructure \
  --startup-project src/AutoMotivePriceTracker.Web \
  --output-dir Persistence/Migrations

# Apply migrations manually (they are also applied automatically on app startup
# via InitializeDatabaseAsync in Program.cs). Add --configuration Release if the
# running app locks the Debug build.
dotnet ef database update \
  --project src/AutoMotivePriceTracker.Infrastructure \
  --startup-project src/AutoMotivePriceTracker.Web

# Install Playwright browsers (required before first scrape)
pwsh src/AutoMotivePriceTracker.Infrastructure/bin/Debug/net10.0/playwright.ps1 install chromium
```

## Architecture

Clean Architecture, four projects under `src/`. Dependencies point inward only: `Web → Infrastructure → Application → Domain` (Web also references Application).

- **Domain** — entities (`Product`, `PriceRecord`) and the `Retailer` enum. No dependencies. Holds business rules, e.g. `Product.ApplyScrape` (price deduplication).
- **Application** — use cases and the ports they need. `IProductScraper` and `IProductRepository` (abstractions implemented by Infrastructure), DTOs (`ProductSummaryDto`, `ProductDetailDto`, `PricePointDto`, `HomeSummaryDto`), and services `IProductCatalogService` (queries for the UI) and `IPriceScrapingService` (runs all scrapers and persists results). Registered via `AddApplication()`.
- **Infrastructure** — EF Core/SQLite (`Persistence/AppDbContext`, `ProductRepository`, `Persistence/Migrations`) and Playwright scrapers (`Scraping/BaseScraper`, `Scraping/Scrapers/*`). Registered via `AddInfrastructure(dbPath)`; `InitializeDatabaseAsync()` applies migrations.
- **Web** — Blazor Server UI, `ScraperBackgroundService` (full scrape 10s after startup, then every 24h) and the composition root in `Program.cs`. Components depend only on Application services/DTOs, never on EF or `AppDbContext`.

### Key design decisions

- **`IDbContextFactory` instead of `AddDbContext`** — Blazor Server components outlive a single request scope. `ProductRepository` creates a short-lived context per call with `await using`.
- **Scrapers and services are `AddScoped`** — created per scope (manual scrape / background service). `BaseScraper` implements `IAsyncDisposable` to clean up the Playwright browser (one Chromium instance per `ScrapeAsync` call).
- **Price deduplication** — lives in the domain (`Product.ApplyScrape`): a new `PriceRecord` is only added when price or `WasPrice` changed. `ProductRepository.UpsertScrapedAsync` matches by SKU + retailer.
- **SVG chart** — `PriceChart.razor` builds an inline SVG via `MarkupString` rather than using a chart library. Blazor's Razor compiler conflicts with SVG `<text>` elements, so string building is used.
- **SQLite DB path** — `{ContentRootPath}/data/prices.db`, created automatically on startup. To reset the database, delete that file and restart the app (migrations recreate the schema).
- **Migrations** — live in `Infrastructure/Persistence/Migrations` (baseline: `InitialCreate`). `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` in the startup project, so Web references it with `PrivateAssets=all`.
- **DTOs at the boundary** — the repository returns domain entities; Application services map them to DTOs (`ProductSummaryDto` etc.), which are what Razor components bind to. Product list filtering stays in the UI over `ProductSummaryDto`.
- **Dev gotcha** — a running `dotnet run` locks `Web/bin/Debug`; stop it before rebuilding, or build with `--configuration Release`.

### Adding a new retailer

1. Create `src/AutoMotivePriceTracker.Infrastructure/Scraping/Scrapers/YourScraper.cs` extending `BaseScraper`
2. Add `Retailer.YourRetailer` to the enum in `Domain/Enums/Retailer.cs`
3. Register in `Infrastructure/DependencyInjection.cs`: `services.AddScoped<IProductScraper, YourScraper>()`
