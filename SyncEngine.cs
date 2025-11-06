using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Threading;

namespace FolderSync;

public class SyncEngine
{
    private readonly ILogger<SyncEngine> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private const int BufferSize = 256 * 1024; // 256KB buffer for optimal performance

    public SyncEngine(ILogger<SyncEngine> logger)
    {
        _logger = logger;

        // Configure retry policy for transient errors (locked files, network issues)
        _retryPolicy = Policy
            .Handle<IOException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: retryAttempt =>
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * 100);
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)(delay.TotalMilliseconds * 0.25)));
                    return delay + jitter; // Exponential backoff with jitter
                },
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Transient error encountered. Retry {RetryCount}/5 after {Delay:F2}s. Error: {Message}",
                        retryCount,
                        timeSpan.TotalSeconds,
                        exception.Message);
                });
    }

    public async Task<SyncResult> SynchronizeAsync(
        string sourcePath,
        string replicaPath,
        CancellationToken cancellationToken)
    {
        var result = new SyncResult();
        var sourceDir = new DirectoryInfo(sourcePath);
        var replicaDir = new DirectoryInfo(replicaPath);

        // Synchronize directories and files
        await SynchronizeDirectoryAsync(sourceDir, replicaDir, result, cancellationToken);

        // Remove files and directories in replica that don't exist in source
        await CleanupReplicaAsync(sourceDir, replicaDir, result, cancellationToken);

        return result;
    }

    private async Task SynchronizeDirectoryAsync(
        DirectoryInfo sourceDir,
        DirectoryInfo replicaDir,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ensure replica directory exists
        if (!replicaDir.Exists)
        {
            replicaDir.Create();
            _logger.LogInformation("Created directory: {Path}", replicaDir.FullName);
        }

        // Get all files from source directory (cached metadata)
        FileInfo[] sourceFiles;
        try
        {
            sourceFiles = sourceDir.GetFiles();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied to directory: {Path}. Error: {Message}",
                sourceDir.FullName, ex.Message);
            result.RegisterError();
            return;
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning("Directory not found: {Path}. Error: {Message}",
                sourceDir.FullName, ex.Message);
            result.RegisterError();
            return;
        }

        // Process files with parallel processing for better performance
        await Parallel.ForEachAsync(
            sourceFiles,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (sourceFile, ct) =>
            {
                try
                {
                    await SynchronizeFileAsync(sourceFile, replicaDir, result, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error synchronizing file: {SourceFile}", sourceFile.FullName);
                    result.RegisterError();
                }
            });

        // Recursively synchronize subdirectories
        DirectoryInfo[] sourceSubdirs;
        try
        {
            sourceSubdirs = sourceDir.GetDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied to directory: {Path}. Error: {Message}",
                sourceDir.FullName, ex.Message);
            result.RegisterError();
            return;
        }

        foreach (var sourceSubdir in sourceSubdirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var replicaSubdir = new DirectoryInfo(Path.Combine(replicaDir.FullName, sourceSubdir.Name));
            await SynchronizeDirectoryAsync(sourceSubdir, replicaSubdir, result, cancellationToken);
        }
    }

    private async Task SynchronizeFileAsync(
        FileInfo sourceFile,
        DirectoryInfo replicaDir,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        var replicaFilePath = Path.Combine(replicaDir.FullName, sourceFile.Name);
        var replicaFile = new FileInfo(replicaFilePath);

        // Check if file needs to be copied or updated
        bool needsCopy = !replicaFile.Exists ||
                         replicaFile.Length != sourceFile.Length ||
                         replicaFile.LastWriteTimeUtc < sourceFile.LastWriteTimeUtc;

        if (needsCopy)
        {
            bool isUpdate = replicaFile.Exists;

            // Copy file with retry policy for transient errors
            await _retryPolicy.ExecuteAsync(async (ct) =>
            {
                await CopyFileAsync(sourceFile.FullName, replicaFilePath, ct);
            }, cancellationToken);

            // Preserve source file timestamps
            File.SetLastWriteTimeUtc(replicaFilePath, sourceFile.LastWriteTimeUtc);
            File.SetCreationTimeUtc(replicaFilePath, sourceFile.CreationTimeUtc);

            if (isUpdate)
            {
                result.RegisterUpdate(sourceFile.Length);
                _logger.LogInformation(
                    "Updated: {SourceFile} -> {ReplicaFile} ({Size} bytes)",
                    sourceFile.FullName,
                    replicaFilePath,
                    sourceFile.Length);
            }
            else
            {
                result.RegisterCopy(sourceFile.Length);
                _logger.LogInformation(
                    "Copied: {SourceFile} -> {ReplicaFile} ({Size} bytes)",
                    sourceFile.FullName,
                    replicaFilePath,
                    sourceFile.Length);
            }
        }
    }

    private async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        var sourceFileInfo = new FileInfo(sourcePath);

        // Use optimal buffer size based on file size
        int bufferSize = sourceFileInfo.Length < 1024 * 1024 ? 64 * 1024 : BufferSize;

        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = bufferSize
        };

        await using var sourceStream = new FileStream(sourcePath, options);

        var destOptions = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = bufferSize,
            PreallocationSize = sourceFileInfo.Length // Preallocate for better performance
        };

        await using var destStream = new FileStream(destPath, destOptions);

        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
    }

    private async Task CleanupReplicaAsync(
        DirectoryInfo sourceDir,
        DirectoryInfo replicaDir,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!replicaDir.Exists)
            return;

        // Get files in replica that don't exist in source
        FileInfo[] replicaFiles;
        try
        {
            replicaFiles = replicaDir.GetFiles();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied to directory: {Path}. Error: {Message}",
                replicaDir.FullName, ex.Message);
            result.RegisterError();
            return;
        }

        var sourceFileNames = new HashSet<string>(
            sourceDir.GetFiles().Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var replicaFile in replicaFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sourceFileNames.Contains(replicaFile.Name))
            {
                try
                {
                    await _retryPolicy.ExecuteAsync((ct) =>
                    {
                        replicaFile.Delete();
                        return Task.CompletedTask;
                    }, cancellationToken);

                    result.RegisterDeletion();
                    _logger.LogInformation("Deleted: {ReplicaFile}", replicaFile.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting file: {ReplicaFile}", replicaFile.FullName);
                    result.RegisterError();
                }
            }
        }

        // Clean up subdirectories
        DirectoryInfo[] replicaSubdirs;
        try
        {
            replicaSubdirs = replicaDir.GetDirectories();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied to directory: {Path}. Error: {Message}",
                replicaDir.FullName, ex.Message);
            result.RegisterError();
            return;
        }

        var sourceSubdirNames = new HashSet<string>(
            sourceDir.GetDirectories().Select(d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var replicaSubdir in replicaSubdirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceSubdirNames.Contains(replicaSubdir.Name))
            {
                // Recursively clean subdirectory
                var sourceSubdir = new DirectoryInfo(Path.Combine(sourceDir.FullName, replicaSubdir.Name));
                await CleanupReplicaAsync(sourceSubdir, replicaSubdir, result, cancellationToken);
            }
            else
            {
                // Delete entire subdirectory that doesn't exist in source
                try
                {
                    await _retryPolicy.ExecuteAsync((ct) =>
                    {
                        replicaSubdir.Delete(recursive: true);
                        return Task.CompletedTask;
                    }, cancellationToken);

                    _logger.LogInformation("Deleted directory: {ReplicaDir}", replicaSubdir.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting directory: {ReplicaDir}", replicaSubdir.FullName);
                    result.RegisterError();
                }
            }
        }
    }

    private static bool IsTransientError(IOException ex)
    {
        // Check for specific transient error codes
        const int ERROR_SHARING_VIOLATION = 32;
        const int ERROR_LOCK_VIOLATION = 33;
        const int ERROR_NETNAME_DELETED = 64;
        const int ERROR_SEM_TIMEOUT = 121;

        int hResult = ex.HResult & 0xFFFF;

        return hResult == ERROR_SHARING_VIOLATION ||
               hResult == ERROR_LOCK_VIOLATION ||
               hResult == ERROR_NETNAME_DELETED ||
               hResult == ERROR_SEM_TIMEOUT;
    }
}

public class SyncResult
{
    private int _filesCopied;
    private int _filesUpdated;
    private int _filesDeleted;
    private long _bytesTransferred;
    private int _errors;

    public int FilesCopied => _filesCopied;
    public int FilesUpdated => _filesUpdated;
    public int FilesDeleted => _filesDeleted;
    public long BytesTransferred => _bytesTransferred;
    public int Errors => _errors;

    public void RegisterCopy(long bytes)
    {
        Interlocked.Increment(ref _filesCopied);
        Interlocked.Add(ref _bytesTransferred, bytes);
    }

    public void RegisterUpdate(long bytes)
    {
        Interlocked.Increment(ref _filesUpdated);
        Interlocked.Add(ref _bytesTransferred, bytes);
    }

    public void RegisterDeletion()
    {
        Interlocked.Increment(ref _filesDeleted);
    }

    public void RegisterError()
    {
        Interlocked.Increment(ref _errors);
    }
}
