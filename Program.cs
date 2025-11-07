using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace FolderSync;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Parse and validate command line arguments
        if (args.Length != 4)
        {
            DisplayUsage();
            return 1;
        }

    string sourcePath = args[0];
    string replicaPath = args[1];
    string logFilePath = args[3];

    
    string fullLogFilePath = Path.GetFullPath(logFilePath); // Normalize log file path to absolute to avoid working-directory issues

        
        if (!int.TryParse(args[2], out int intervalSeconds) || intervalSeconds <= 0)// Validate synchronization interval
        {
            Console.WriteLine("Error: Synchronization interval must be a positive integer (seconds).");
            Console.WriteLine();
            DisplayUsage();
            return 1;
        }

        // Validate paths
        if (!ValidatePaths(sourcePath, replicaPath, logFilePath))
        {
            return 1;
        }

        // Configure Serilog with dual output (console + file)
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                fullLogFilePath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        try
        {
            Log.Information("=== Folder Synchronization Service Starting ===");
            Log.Information("Source: {SourcePath}", sourcePath);
            Log.Information("Replica: {ReplicaPath}", replicaPath);
            Log.Information("Interval: {IntervalSeconds} seconds", intervalSeconds);
            Log.Information("Log File: {LogFilePath}", logFilePath);

            // Build and run the host
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Register configuration
                    services.AddSingleton(new SyncConfiguration
                    {
                        SourcePath = sourcePath,
                        ReplicaPath = replicaPath,
                        IntervalSeconds = intervalSeconds
                    });

                    // Register services
                    services.AddSingleton<SyncEngine>();
                    services.AddHostedService<FolderSyncService>();
                })
                .UseSerilog()
                .Build();

            await host.RunAsync();

            Log.Information("=== Folder Synchronization Service Stopped ===");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    static void DisplayUsage()
    {
        Console.WriteLine("Folder Synchronization Tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  FolderSync <source_path> <replica_path> <interval_seconds> <log_file_path>");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  source_path       - Path to the source folder");
        Console.WriteLine("  replica_path      - Path to the replica folder");
        Console.WriteLine("  interval_seconds  - Synchronization interval in seconds");
        Console.WriteLine("  log_file_path     - Path to the log file");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  FolderSync C:\\Source C:\\Replica 300 C:\\Logs\\sync.log");
    }

    static bool ValidatePaths(string sourcePath, string replicaPath, string logFilePath)
    {
        try
        {
            // Validate source path
            if (!Directory.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Source directory does not exist: {sourcePath}");
                return false;
            }

            // Validate that source and replica are different
            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullReplicaPath = Path.GetFullPath(replicaPath);

            if (fullSourcePath.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(fullReplicaPath.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Error: Source and replica paths must be different.");
                return false;
            }

            // Create replica directory if it doesn't exist
            if (!Directory.Exists(replicaPath))
            {
                Console.WriteLine($"Info: Creating replica directory: {replicaPath}");
                Directory.CreateDirectory(replicaPath);
            }

            string? logDirectory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Console.WriteLine($"Info: Creating log directory: {logDirectory}");
                Directory.CreateDirectory(logDirectory);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating paths: {ex.Message}");
            return false;
        }
    }
}

public class SyncConfiguration
{
    public string SourcePath { get; init; } = string.Empty;
    public string ReplicaPath { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; }
}
