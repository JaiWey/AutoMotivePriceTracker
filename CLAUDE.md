# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Start PostgreSQL and OpenSearch (needed to run the app; dev connection string is in appsettings.Development.json,
# search is Search:Url, default http://localhost:9200; the first start pulls a ~1GB image)
docker compose up -d --wait

# Build entire solution
dotnet build

# Run the API (http://localhost:5052)
dotnet run --project src/AutoMotivePriceTracker.Web

# Run the Angular client (http://localhost:4200; talks to the API at environment.apiUrl)
cd src/AutoMotivePriceTracker.Client && npm install && npx ng serve
# Production build: npx ng build (environment.production.ts has an empty apiUrl = API on the app's own origin; set an absolute URL if hosted elsewhere)
# SSR: `ng build` also emits a Node server; run it with `npm run serve:ssr:AutoMotivePriceTracker.Client` (port 4000, PORT env).
# In production set NG_ALLOWED_HOSTS=<public host>[,..] (angular.json only allows localhost); behind a proxy also TRUST_PROXY_HEADERS=true (see "Same-origin API URL" below).

# Run tests. .NET: xUnit in tests/AutoMotivePriceTracker.Tests (domain, services, repository on a
# real PostgreSQL via Testcontainers with the real migrations and the search adapter on a real OpenSearch container (both need Docker), API endpoints via TestServer). Add -c Release if a
# running app locks the Debug build. Angular: Vitest via the Angular CLI.
dotnet test
cd src/AutoMotivePriceTracker.Client && npx ng test --no-watch
# End-to-end (Playwright, installed Edge): starts its own API on :5099 (database `autoprice_e2e` on the
# compose PostgreSQL, seeded by e2e/seed.ts and then indexed into the `products_e2e` index of the compose OpenSearch, Scraping:Enabled=false) and `ng serve --configuration e2e` on :4299, then drives the UI.
npm run e2e

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

Clean Architecture, four .NET projects plus an Angular client under `src/`. Dependencies point inward only: `Web → Infrastructure → Application → Domain` (Web also references Application).

- **Domain** — entities (`Product`, `PriceRecord`) and the `Retailer` enum. No dependencies. Holds business rules, e.g. `Product.ApplyScrape` (price deduplication).
- **Application** — use cases and the ports they need. `IProductScraper`, `IProductRepository` and `IProductSearch` (abstractions implemented by Infrastructure), DTOs (`ProductSummaryDto`, `ProductDetailDto`, `PricePointDto`, `HomeSummaryDto`), and services `IProductCatalogService` (queries for the UI), `IPriceScrapingService` (runs all scrapers and persists results) and `ISearchIndexService` (rebuilds the search index from the database). Registered via `AddApplication()`.
- **Infrastructure** — EF Core/PostgreSQL via Npgsql (`Persistence/AppDbContext`, `ProductRepository`, `Persistence/Migrations`), the OpenSearch search adapter (`Search/OpenSearchProductSearch`) and Playwright scrapers (`Scraping/BaseScraper`, `Scraping/Scrapers/*`). Registered via `AddInfrastructure(connectionString, searchOptions)`; `InitializeDatabaseAsync()` applies migrations.
- **Web** — JSON minimal API (`Endpoints/ApiEndpoints.cs`), `ScraperBackgroundService` (full scrape 10s after startup, then every 24h), `ScrapeRunCoordinator` and the composition root in `Program.cs`. Endpoints depend only on Application services/DTOs, never on EF or `AppDbContext`.
- **AutoMotivePriceTracker.Client** — Angular (standalone components, signals) SPA with server-side rendering (`@angular/ssr`, Express in `src/server.ts`; hydration + `withFetch()`), deployed separately from the API as a Node app. `app.routes.server.ts`: all routes render per request (`RenderMode.Server`) except `admin`, which is client-only (it polls with timers). Server-side API calls come from the Node process, so `environment.apiUrl` must be reachable from it. Pages: Home, Products (client-side filtering), Product detail (Chart.js via ng2-charts), Admin (triggers/polls scrapes). The API base URL comes from `src/environments/`.

### Key design decisions

- **`IDbContextFactory` instead of `AddDbContext`** — kept from the Blazor era; `ProductRepository` creates a short-lived context per call with `await using`, which also suits the singleton-triggered background scrapes.
- **Scrapers and services are `AddScoped`** — created per scope (manual scrape / background service). `BaseScraper` implements `IAsyncDisposable` to clean up the Playwright browser (one Chromium instance per `ScrapeAsync` call).
- **Scraper selectors and URLs must come from the real site** — don't guess them. Fetch the page (curl with a browser User-Agent) and inspect the markup first. Supercheap Auto uses paths like `/oils-fluids-filters/engine-oil` (the old `/c/...` URLs return 410), product cards are `.product-tile[data-itemid]`, and pagination is `a.page-next`. Repco category URLs need a numeric id (`/<path>/c/<id>`, e.g. `/parts-service/braking/brake-pads/c/1862918117`), product cards are `.product-tile[data-sku]` with `data-sku`/`data-price`/`data-url` attributes, and the next link is `.pagination-next a[rel='next']` (a second `.pagination-next` is "last"). Repco's bot protection returns HTTP 403 intermittently — retry the fetch before concluding a selector or URL is wrong. Only the engine-oil page was inspected in detail; the other Repco categories assume the same template.
- **Optional elements need null-safe lookups** — `EvalOnSelectorAsync` throws when nothing matches (e.g. a was-price that only exists on discounted tiles), which silently drops the product. Use `BaseScraper.TextOrNullAsync` / `AttrOrNullAsync` for anything that may be absent.
- **Pagination loops stop on revisits** — scrapers track visited URLs (`while (visited.Add(currentUrl))`), because "next"/"last" links can still exist on the final page.
- **Fail-fast navigation** — `BaseScraper.NavigateWithRetryAsync` throws `HttpRequestException` on 4xx (except 429) without retrying, so a dead URL (404/410) or a bot block (403) is logged clearly rather than surfacing as a selector timeout.
- **Product features** — `Product.Features` (`text[]`) holds the bullet points from each product page: Repco `.feature__list li`, Supercheap `#product-features li` (both present in the server HTML at DOMContentLoaded). `PriceScrapingService` runs it after each retailer's price scrape: `IProductRepository.GetMissingFeaturesAsync` returns products with `FeaturesScrapedAt == null` (capped at `FeaturePagesPerRun` = 300 per retailer per run, so a first run fills in over several days), `IProductScraper.ScrapeFeaturesAsync` loads each product page, `SaveFeaturesAsync` stores the result. Features are fetched once, not refreshed; a page that fails to load is left out and retried next run. Feature failures never fail the price scrape.
- **Price deduplication** — lives in the domain (`Product.ApplyScrape`): a new `PriceRecord` is only added when price or `WasPrice` changed. `ProductRepository.UpsertScrapedAsync` matches by SKU + retailer. It is batched: it dedupes the incoming list per (retailer, SKU), loads existing products in chunked queries (not one `SELECT` per product), then saves new and updated rows in a single `SaveChangesAsync`.
- **JSON API contract** — enums serialize as strings (`JsonStringEnumConverter`) and `DateTime`s always as UTC with a `Z` suffix (`Json/UtcDateTimeConverter`, a safeguard so a DateTime of Unspecified kind is never serialized without its zone). CORS origins come from `Cors:AllowedOrigins` (default `http://localhost:4200`). Endpoints: `GET /api/home`, `GET /api/products`, `GET /api/products/search?q=` (ranked product ids), `GET /api/products/{id}`, `POST /api/admin/scrape` (202, or 409 if running), `GET /api/admin/scrape/status`, `POST /api/admin/reindex` (rebuilds the search index, returns `{indexed}`).
- **Logging** — Serilog behind `ILogger<T>` (`builder.Host.UseSerilog` in `Program.cs`, request logging via `UseSerilogRequestLogging`). Levels and sinks live in the `Serilog` section of `appsettings.json` (console + daily rolling file `logs/app-.log`, 14 files kept; `*.log` is gitignored). Application/Infrastructure keep depending only on `Microsoft.Extensions.Logging`.
- **Config switches** — `ConnectionStrings:Default` (required; dev default in `appsettings.Development.json`, set it via config/env elsewhere) and `Scraping:Enabled=false` removes all scrapers and the scheduled run (used by the e2e stack), `Search:Url` (default `http://localhost:9200`), `Search:Index` (default `products`; use a different one per environment sharing a cluster), optional `Search:Username`/`Search:Password` and `Origin:SharedSecret` (when set, only requests carrying it in `X-Origin-Verify` are served; CloudFront adds it in AWS).
- **Scrape coordination** — `ScrapeRunCoordinator` (singleton) ensures manual and scheduled scrapes never overlap and holds the last run's status. `PriceScrapingService.RunAllAsync` swallows per-scraper errors, so `Succeeded` doesn't guarantee every scraper worked.
- **Database** — PostgreSQL (`docker-compose.yml`, service `postgres`, db `autoprice`). Migrations run on startup and create the database if missing. To reset: `docker compose down -v` (drops the volume) and restart the app. Timestamps are `timestamptz` and Npgsql requires UTC `DateTime`s — always use `DateTime.UtcNow`. Prices are `numeric(18,2)`.
- **One-off SQLite import** — `dotnet run --project tools/AutoMotivePriceTracker.SqliteImport -- <prices.db> <connection-string>` copies the legacy SQLite data (ids preserved) into an empty, migrated PostgreSQL database; it refuses a non-empty target.
- **Migrations** — live in `Infrastructure/Persistence/Migrations` (baseline: `InitialCreate`). `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` in the startup project, so Web references it with `PrivateAssets=all`.
- **DTOs at the boundary** — the repository returns domain entities; Application services map them to DTOs (`ProductSummaryDto` etc.), which are what the API returns and the Angular client mirrors (`src/app/models/models.ts`). Product list filtering (category, retailer, on-sale) stays in the Angular client over the `ProductSummaryDto` list; the free-text box calls `/api/products/search` (debounced) and falls back to a substring match if that fails.
- **Product search** — OpenSearch (API-compatible with Elasticsearch; the Elastic .NET client cannot talk to it, so the app uses `OpenSearch.Client`) holds one document per product (id = product id; name, brand, category, features, retailer) in an `english`-analyzed index that is created on first use. `OpenSearchProductSearch` requires every query word to match (`multi_match`, `fuzziness: AUTO`, name boosted over brand over category/features), so word order, plurals and typos don't matter. The index is derived data, rebuildable at any time: `SearchIndexService.ReindexAllAsync` bulk-upserts every product, run in the background at startup, after every scrape run (failures are logged, never fail the scrape) and via `POST /api/admin/reindex`. A mapping change needs the index deleted first (`curl -X DELETE localhost:9200/products`), then a reindex. If the search service is down, `/api/products/search` returns 500 and the client falls back to substring filtering.
- **Same-origin API URL** — `environment.production.ts` has an empty `apiUrl`: the API is on the app's origin (no CORS). The browser uses `/api/...`; during SSR `resolveApiOrigin` (`services/api.ts`) derives the origin from the incoming request. Behind a proxy the SSR server only honours `X-Forwarded-Host`/`-Proto` when `TRUST_PROXY_HEADERS=true` (`src/server.ts`), and still validates hosts against `NG_ALLOWED_HOSTS`.
- **Dev gotcha** — a running `dotnet run` locks `Web/bin/Debug`; stop it before rebuilding, or build with `--configuration Release`.

### Adding a new retailer

1. Create `src/AutoMotivePriceTracker.Infrastructure/Scraping/Scrapers/YourScraper.cs` extending `BaseScraper`
2. Add `Retailer.YourRetailer` to the enum in `Domain/Enums/Retailer.cs`
3. Register in `Infrastructure/DependencyInjection.cs`: `services.AddScoped<IProductScraper, YourScraper>()`

## Deployment (AWS, Terraform)

Layout (`infra/terraform`, `deploy/`): one CloudFront distribution is the only public entry. `/api/*` goes through a VPC origin to the API on a private EC2 host (Docker, Playwright/Chromium scrapers, scheduled scraping, so run exactly one); static Angular files come from S3; everything else is the Angular SSR server as a Lambda container image (Lambda Web Adapter, Function URL with IAM auth + origin access control). PostgreSQL is RDS and search is an Amazon OpenSearch Service domain, both in private subnets of a VPC with one NAT gateway (about US$33/month, the main non-free-tier cost). Secrets are SSM SecureStrings; the host has no SSH, use `aws ssm start-session --target <api_instance_id>`.

```powershell
# prerequisites: aws configure, Docker, terraform; optional budget alert: -var budget_email=... in infra/terraform/terraform.tfvars
pwsh deploy/deploy.ps1            # ECR repos -> build + push images -> terraform apply -> S3 sync, Lambda update, host deploy via SSM, CloudFront invalidation
terraform -chdir=infra/terraform destroy   # tear everything down
```

- Images: `src/AutoMotivePriceTracker.Web/Dockerfile` (build from the repo root; installs Chromium for the non-root `app` user) and `src/AutoMotivePriceTracker.Client/Dockerfile` (Lambda image; also runs as a plain container).
- The first deploy applies the ECR repositories before anything else because the Lambda needs its image to exist. `terraform apply` and `deploy.ps1` ask for confirmation; nothing is applied automatically.
- Terraform state is local and holds the generated database and origin secrets; it is git-ignored. Move it to an encrypted S3 backend if more people deploy.
- No custom domain yet (CloudFront `*.cloudfront.net` certificate). Free tier: 12-month EC2/RDS/OpenSearch allowances apply to accounts that have the legacy free tier; a t3.micro (1 GB) host is tight for Chromium, raise `api_instance_type` if scrapes run out of memory. Repco may block datacenter IPs.
