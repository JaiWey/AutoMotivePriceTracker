using AutoMotivePriceTracker.Application.Services;

namespace AutoMotivePriceTracker.Web.BackgroundServices;

public enum ScrapeState { Idle, Running, Succeeded, Failed }

public record ScrapeStatus(ScrapeState State, DateTime? StartedAt, DateTime? FinishedAt, string? Error);

/// <summary>
/// Serialises scrape runs so a manual trigger and the scheduled run never overlap,
/// and exposes the last run's status to the API.
/// </summary>
public class ScrapeRunCoordinator
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScrapeRunCoordinator> _logger;
    private readonly object _lock = new();
    private ScrapeStatus _status = new(ScrapeState.Idle, null, null, null);

    public ScrapeRunCoordinator(IServiceProvider services, ILogger<ScrapeRunCoordinator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public ScrapeStatus Status
    {
        get { lock (_lock) return _status; }
    }

    /// <summary>Starts a run in the background. Returns false if one is already running.</summary>
    public bool TryStartInBackground()
    {
        if (!TryBegin()) return false;
        _ = Task.Run(() => ExecuteAsync(CancellationToken.None));
        return true;
    }

    /// <summary>Runs and awaits a scrape. Returns false (without running) if one is already running.</summary>
    public async Task<bool> TryRunAsync(CancellationToken ct)
    {
        if (!TryBegin()) return false;
        await ExecuteAsync(ct);
        return true;
    }

    private bool TryBegin()
    {
        lock (_lock)
        {
            if (_status.State == ScrapeState.Running) return false;
            _status = new ScrapeStatus(ScrapeState.Running, DateTime.UtcNow, null, null);
            return true;
        }
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
        var started = Status.StartedAt;
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IPriceScrapingService>();
            await service.RunAllAsync(ct);
            Finish(new ScrapeStatus(ScrapeState.Succeeded, started, DateTime.UtcNow, null));
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                _logger.LogError(ex, "Scrape run failed");
            Finish(new ScrapeStatus(ScrapeState.Failed, started, DateTime.UtcNow, ex.Message));
        }
    }

    private void Finish(ScrapeStatus status)
    {
        lock (_lock) _status = status;
    }
}
