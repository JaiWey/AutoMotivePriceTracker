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

# Install Playwright browsers (required before first scrape). Run from the Web
# output: the Infrastructure bin lacks Microsoft.Playwright.dll, so the script
# fails there. Build the solution first.
pwsh src/AutoMotivePriceTracker.Web/bin/Debug/net10.0/playwright.ps1 install chromium
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
- **Scraper selectors and URLs must come from the real site** — don't guess them. Fetch the page (curl with a browser User-Agent) and inspect the markup first. Supercheap Auto uses paths like `/oils-fluids-filters/engine-oil` (the old `/c/...` URLs return 410), product cards are `.product-tile[data-itemid]`, and pagination is `a.page-next`. Repco category URLs need a numeric id (`/<path>/c/<id>`, e.g. `/parts-service/braking/brake-pads/c/1862918117`), product cards are `.product-tile[data-sku]` with `data-sku`/`data-price`/`data-url` attributes, and the next link is `.pagination-next a[rel='next']` (a second `.pagination-next` is "last"). Repco's bot protection returns HTTP 403 intermittently — retry the fetch before concluding a selector or URL is wrong. Only the engine-oil page was inspected in detail; the other Repco categories assume the same template.
- **Optional elements need null-safe lookups** — `EvalOnSelectorAsync` throws when nothing matches (e.g. a was-price that only exists on discounted tiles), which silently drops the product. Use `BaseScraper.TextOrNullAsync` / `AttrOrNullAsync` for anything that may be absent.
- **Pagination loops stop on revisits** — scrapers track visited URLs (`while (visited.Add(currentUrl))`), because "next"/"last" links can still exist on the final page.
- **Fail-fast navigation** — `BaseScraper.NavigateWithRetryAsync` throws `HttpRequestException` on 4xx (except 429) without retrying, so a dead URL (404/410) or a bot block (403) is logged clearly rather than surfacing as a selector timeout.
- **Price deduplication** — lives in the domain (`Product.ApplyScrape`): a new `PriceRecord` is only added when price or `WasPrice` changed. `ProductRepository.UpsertScrapedAsync` matches by SKU + retailer. It is batched: it dedupes the incoming list per (retailer, SKU), loads existing products in chunked queries (not one `SELECT` per product), then saves new and updated rows in a single `SaveChangesAsync`.
- **SVG chart** — `PriceChart.razor` builds an inline SVG via `MarkupString` rather than using a chart library. Blazor's Razor compiler conflicts with SVG `<text>` elements, so string building is used.
- **SQLite DB path** — `{ContentRootPath}/data/prices.db`, created automatically on startup. To reset the database, delete that file and restart the app (migrations recreate the schema).
- **Migrations** — live in `Infrastructure/Persistence/Migrations` (baseline: `InitialCreate`). `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` in the startup project, so Web references it with `PrivateAssets=all`.
- **DTOs at the boundary** — the repository returns domain entities; Application services map them to DTOs (`ProductSummaryDto` etc.), which are what Razor components bind to. Product list filtering stays in the UI over `ProductSummaryDto`.
- **Dev gotcha** — a running `dotnet run` locks `Web/bin/Debug`; stop it before rebuilding, or build with `--configuration Release`.

### Adding a new retailer

1. Create `src/AutoMotivePriceTracker.Infrastructure/Scraping/Scrapers/YourScraper.cs` extending `BaseScraper`
2. Add `Retailer.YourRetailer` to the enum in `Domain/Enums/Retailer.cs`
3. Register in `Infrastructure/DependencyInjection.cs`: `services.AddScoped<IProductScraper, YourScraper>()`
