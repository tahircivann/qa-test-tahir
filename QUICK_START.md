# Quick Start

This is a tiny quick-start to run the sync program.

Prereq:
- .NET 6 SDK installed

Build:

```powershell
dotnet restore
dotnet build -c Release
```

Run:

```powershell
dotnet run -- "<source>" "<replica>" <seconds> "<logfile>"
```

Example (Windows):

```powershell
dotnet run -- "C:\MySource" "C:\MyReplica" 300 "C:\Logs\sync.log"
```

What to expect:
- First sync starts right away
- Then it runs every N seconds
- Logs go to console and the log file
- Replica will be made to match source (copy/update/delete)

Stop: press Ctrl+C. The program will finish the current run and exit.

See `README.md` for more details.
