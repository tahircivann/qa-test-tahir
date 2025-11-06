# Folder Synchronization Tool

A production-ready, high-performance folder synchronization tool written in C# for .NET 6+. This implementation follows modern best practices and leverages the latest .NET performance improvements.

## Features

- ✅ **One-way synchronization** from source to replica folder
- ✅ **Periodic execution** with configurable intervals
- ✅ **Comprehensive logging** to both console and file
- ✅ **High performance** using modern .NET 6+ APIs
- ✅ **Robust error handling** with automatic retry for transient errors
- ✅ **Parallel processing** for improved throughput
- ✅ **Graceful shutdown** with cancellation token support

## Requirements

- .NET 6.0 SDK or later
- Windows, Linux, or macOS

## Build Instructions

```bash
dotnet restore
dotnet build -c Release
```

## Usage

```bash
dotnet run -- <source_path> <replica_path> <interval_seconds> <log_file_path>
```

### Arguments

1. **source_path** - Path to the source folder to synchronize from
2. **replica_path** - Path to the replica folder to synchronize to
3. **interval_seconds** - Synchronization interval in seconds (positive integer)
4. **log_file_path** - Path where the log file will be created

### Example

```bash
# Folder Sync (simple)

This is a small tool to copy files from a source folder to a replica folder so the replica matches the source. It is written in C# and runs on .NET 6+.

## What it does

- Copies new files from source to replica
- Updates files that changed (by size or timestamp)
- Deletes files in replica that are not in source
- Runs on a schedule (every N seconds)
- Logs to console and a log file

## Quick build & run

1. Restore and build:

```powershell
dotnet restore
dotnet build -c Release
```

2. Run with 4 arguments:

```powershell
dotnet run -- <source> <replica> <interval_seconds> <log_file>
```

Example:

```powershell
dotnet run -- "C:\Source" "C:\Replica" 300 "C:\Logs\sync.log"
```

## Files of interest

- `Program.cs` — start-up, args, logging
- `FolderSyncService.cs` — runs the periodic job
- `SyncEngine.cs` — does the actual copying and deleting

## Notes for users

- Make sure the source folder exists. The replica will be created if missing.
- Use a reasonable interval (e.g. 60–300 seconds) depending on folder size.
- Press Ctrl+C to stop; the program will try to exit cleanly.


