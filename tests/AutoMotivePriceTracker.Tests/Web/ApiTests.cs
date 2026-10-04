using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoMotivePriceTracker.Application.Abstractions;
using AutoMotivePriceTracker.Application.Services;
using AutoMotivePriceTracker.Domain.Entities;
using AutoMotivePriceTracker.Domain.Enums;
using AutoMotivePriceTracker.Tests.Support;
using AutoMotivePriceTracker.Web.BackgroundServices;
using AutoMotivePriceTracker.Web.Endpoints;
using AutoMotivePriceTracker.Web.Json;
using AutoMotivePriceTracker.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static AutoMotivePriceTracker.Tests.Support.Builders;

namespace AutoMotivePriceTracker.Tests.Web;

/// <summary>A scraping service the test can hold open to simulate a long-running scrape.</summary>
public class GatedScrapingService : IPriceScrapingService
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Exception? Fail { get; set; }
    public int Runs;

    public void Release() => _gate.TrySetResult();

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref Runs);
        await _gate.Task.WaitAsync(ct);
        if (Fail != null) throw Fail;
    }
}

public class ScrapeRunCoordinatorTests
{
    private readonly GatedScrapingService _scraping = new();
    private readonly ScrapeRunCoordinator _coordinator;

    public ScrapeRunCoordinatorTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<IPriceScrapingService>(_scraping)
            .BuildServiceProvider();
        _coordinator = new ScrapeRunCoordinator(services, NullLogger<ScrapeRunCoordinator>.Instance);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Starts_idle()
    {
        Assert.Equal(ScrapeState.Idle, _coordinator.Status.State);
    }

    [Fact]
    public async Task Rejects_a_second_start_while_running_then_succeeds()
    {
        Assert.True(_coordinator.TryStartInBackground());
        Assert.Equal(ScrapeState.Running, _coordinator.Status.State);
        Assert.False(_coordinator.TryStartInBackground());
        Assert.False(await _coordinator.TryRunAsync(CancellationToken.None));

        _scraping.Release();
        await WaitFor(() => _coordinator.Status.State == ScrapeState.Succeeded);

        Assert.Equal(1, _scraping.Runs);
        Assert.NotNull(_coordinator.Status.FinishedAt);
        Assert.Null(_coordinator.Status.Error);
    }

    [Fact]
    public async Task Records_failure_and_allows_a_new_run()
    {
        _scraping.Fail = new InvalidOperationException("boom");
        _scraping.Release();

        Assert.True(await _coordinator.TryRunAsync(CancellationToken.None));

        Assert.Equal(ScrapeState.Failed, _coordinator.Status.State);
        Assert.Equal("boom", _coordinator.Status.Error);
        Assert.True(await _coordinator.TryRunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_run_is_reported_as_failed()
    {
        using var cts = new CancellationTokenSource();
        var run = _coordinator.TryRunAsync(cts.Token);
        await cts.CancelAsync();
        await run;

        Assert.Equal(ScrapeState.Failed, _coordinator.Status.State);
    }
}

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public void Writes_a_Z_suffix(DateTimeKind kind)
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 3, 4, 5, 6, 7, kind), Options);

        Assert.Equal("\"2026-03-04T05:06:07Z\"", json);
    }

    [Fact]
    public void Round_trips_as_utc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-03-04T05:06:07Z\"", Options);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), value);
    }
}

public sealed class ApiEndpointsTests : IAsyncLifetime
{
    private readonly FakeProductRepository _repo = new();
    private readonly GatedScrapingService _scraping = new();
    private readonly FakeProductSearch _search = new();
    private readonly FakeSearchIndexService _index = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiJson();
        builder.Services.AddSingleton<IProductRepository>(_repo);
        builder.Services.AddSingleton<IProductSearch>(_search);
        builder.Services.AddSingleton<ISearchIndexService>(_index);
        builder.Services.AddSingleton<IPriceScrapingService>(_scraping);
        builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();
        builder.Services.AddSingleton<ScrapeRunCoordinator>();

        _app = builder.Build();
        _app.MapApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _scraping.Release();
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Search_returns_ranked_ids_for_the_query_string()
    {
        _search.Result = [9, 4];

        var ids = await _client.GetFromJsonAsync<int[]>("/api/products/search?q=oil%20fliters");

        Assert.Equal([9, 4], ids);
        Assert.Equal("oil fliters", _search.LastQuery);
    }

    [Fact]
    public async Task Search_without_a_query_returns_an_empty_list()
    {
        var ids = await _client.GetFromJsonAsync<int[]>("/api/products/search");

        Assert.Empty(ids!);
        Assert.Equal(0, _search.SearchCalls);
    }

    [Fact]
    public async Task Reindex_endpoint_rebuilds_the_index_and_reports_the_count()
    {
        var response = await _client.PostAsync("/api/admin/reindex", null);

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(42, doc.RootElement.GetProperty("indexed").GetInt32());
        Assert.Equal(1, _index.Reindexes);
    }

    [Fact]
    public async Task Home_returns_summary_with_string_enums_and_utc_dates()
    {
        _repo.Recent = [Product("A", Retailer.SupercheapAuto, price: 5m)];
        _repo.Counts = new() { [Retailer.Repco] = 1, [Retailer.SupercheapAuto] = 2 };

        using var doc = JsonDocument.Parse(await _client.GetStringAsync("/api/home"));
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("totalProducts").GetInt32());
        Assert.Equal(2, root.GetProperty("countByRetailer").GetProperty("SupercheapAuto").GetInt32());
        var recent = root.GetProperty("recent")[0];
        Assert.Equal("SupercheapAuto", recent.GetProperty("retailer").GetString());
        Assert.EndsWith("Z", recent.GetProperty("latestPrice").GetProperty("date").GetString());
        Assert.False(recent.GetProperty("isOnSale").GetBoolean());
    }

    [Fact]
    public async Task Products_returns_the_list()
    {
        _repo.All = [Product("A", price: 8m, wasPrice: 10m), Product("B")];

        using var doc = JsonDocument.Parse(await _client.GetStringAsync("/api/products"));

        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.True(doc.RootElement[0].GetProperty("isOnSale").GetBoolean());
    }

    [Fact]
    public async Task Product_by_id_returns_detail_or_404()
    {
        var p = Product("A", price: 10m);
        p.Id = 3;
        _repo.ById[3] = p;

        var ok = await _client.GetAsync("/api/products/3");
        var missing = await _client.GetAsync("/api/products/99");
        var badId = await _client.GetAsync("/api/products/abc");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal("Repco", doc.RootElement.GetProperty("retailer").GetString());
        Assert.Single(doc.RootElement.GetProperty("priceHistory").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, badId.StatusCode);
    }

    [Fact]
    public async Task Scrape_endpoint_accepts_then_conflicts_and_status_reflects_state()
    {
        var idle = await _client.GetFromJsonAsync<JsonElement>("/api/admin/scrape/status");
        Assert.Equal("Idle", idle.GetProperty("state").GetString());

        var first = await _client.PostAsync("/api/admin/scrape", null);
        var second = await _client.PostAsync("/api/admin/scrape", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var running = await _client.GetFromJsonAsync<JsonElement>("/api/admin/scrape/status");
        Assert.Equal("Running", running.GetProperty("state").GetString());

        _scraping.Release();
        for (var i = 0; i < 200; i++)
        {
            var status = await _client.GetFromJsonAsync<JsonElement>("/api/admin/scrape/status");
            if (status.GetProperty("state").GetString() == "Succeeded") return;
            await Task.Delay(10);
        }
        Assert.Fail("Scrape never reached Succeeded");
    }
}

public sealed class OriginVerificationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseOriginVerification("s3cret");
        _app.MapGet("/ping", () => "pong");
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Rejects_requests_without_the_secret_header()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/ping")).StatusCode);
    }

    [Fact]
    public async Task Rejects_requests_with_a_wrong_secret()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
        request.Headers.Add(OriginVerificationMiddleware.HeaderName, "nope");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Serves_requests_with_the_right_secret()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
        request.Headers.Add(OriginVerificationMiddleware.HeaderName, "s3cret");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Is_disabled_when_no_secret_is_configured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseOriginVerification(null);
        app.MapGet("/ping", () => "pong");
        await app.StartAsync();

        Assert.Equal(HttpStatusCode.OK, (await app.GetTestClient().GetAsync("/ping")).StatusCode);
    }
}
