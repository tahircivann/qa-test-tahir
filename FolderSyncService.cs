using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FolderSync;

public class FolderSyncService : BackgroundService
{
    private readonly ILogger<FolderSyncService> _logger;
    private readonly SyncConfiguration _config;
    private readonly SyncEngine _syncEngine;

    public FolderSyncService(
        ILogger<FolderSyncService> logger,
        SyncConfiguration config,
        SyncEngine syncEngine)
    {
        _logger = logger;
        _config = config;
        _syncEngine = syncEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FolderSyncService started");

        // Perform initial synchronization immediately
        await PerformSynchronization(stoppingToken);

        // Create periodic timer for subsequent synchronizations
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.IntervalSeconds));

        try
        {
            // Wait for next tick and perform synchronization
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PerformSynchronization(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("FolderSyncService is stopping due to cancellation request");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FolderSyncService encountered a fatal error");
            throw; // This will stop the host
        }
    }

    private async Task PerformSynchronization(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("=== Starting synchronization cycle ===");
            var startTime = DateTime.Now;

            var result = await _syncEngine.SynchronizeAsync(
                _config.SourcePath,
                _config.ReplicaPath,
                cancellationToken);

            var duration = DateTime.Now - startTime;

            _logger.LogInformation(
                "=== Synchronization completed in {Duration:F2} seconds ===",
                duration.TotalSeconds);
            _logger.LogInformation(
                "Statistics: {FilesCopied} copied, {FilesUpdated} updated, {FilesDeleted} deleted, " +
                "{BytesTransferred} bytes transferred, {Errors} errors",
                result.FilesCopied,
                result.FilesUpdated,
                result.FilesDeleted,
                result.BytesTransferred,
                result.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during synchronization cycle");
            // Don't rethrow - allow the service to continue and retry on next interval
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FolderSyncService is stopping");
        return base.StopAsync(cancellationToken);
    }
}
