# FTP File Monitor

A background service that watches an FTP server and reports new files as they arrive. It recursively scans a directory tree, matches files against configurable name rules, waits until each file has finished uploading, and keeps a full history of detections and scan runs in SQLite. Written in C# / .NET 8.

## How it works

1. On every cycle the monitor connects to the FTP server and walks the directory tree from the configured root
2. Each file name is checked against a list of rules (regex or wildcard such as `report_*.csv`)
3. Matching files are compared with the files already stored in SQLite
4. A file is reported as **new** only once its size has stayed the same between two scans, so half-uploaded files are never picked up
5. The interval between scans adapts to activity: it resets to the minimum when something new appears and grows step by step while the server is quiet

### Baseline on first run

The first successful scan records everything that is already on the server as a *baseline* and reports nothing. From then on, only files that appear after the baseline are reported. If a baseline file was still being uploaded during the first scan, it is reported once its upload completes.

### File lifecycle

```
            first seen                    size unchanged
  (absent) ───────────► unconfirmed ─────────────────────► confirmed ──► reported as NEW
                          │     ▲
                          └─────┘
                       size changed
```

Once a file is confirmed it is skipped on later scans, so every file is reported exactly once. If the server does not return file sizes, files are confirmed immediately.

## Features

- **Recursive scanning** with a configurable maximum depth
- **Rule-based matching:** regular expressions or simple `*` / `?` wildcards, case-insensitive
- **Upload-complete detection:** files are reported only after their size stabilises
- **Baseline mode:** existing files are not flooded as "new" on the first run
- **Adaptive polling:** 15 s when active, backing off to 5 min when idle (configurable)
- **Persistent history in SQLite:** every detection and every scan run, including failures with the full error
- **Resilience:** a failed scan is logged, the connection is reset and the next cycle reconnects automatically
- **Graceful shutdown:** `Ctrl+C` finishes the current scan before stopping
- **Configuration** via `appsettings.json`, overridable with environment variables

## Database schema

| Table | Purpose |
|---|---|
| `known_files` | Every matching file seen so far: path, size, server timestamp, first-seen time, `confirmed` and `baseline` flags |
| `detections` | One row per reported file, with the rule that matched it |
| `scan_runs` | One row per scan: start/finish time, matched and new counts, success flag and error text |

All writes for a scan run happen in a single transaction, and all queries are parameterised.

## Project structure

```
src/FtpFileMonitor/
├── Program.cs          # Main loop: connect, scan, save, adaptive delay, shutdown
├── FtpScanner.cs       # Recursive FTP listing, path normalisation, rule matching
├── Database.cs         # SQLite schema and baseline / stable-size logic
├── Rule.cs             # Regex and wildcard rules
├── Settings.cs         # Loads appsettings.json and environment overrides
└── appsettings.json    # FTP connection, monitor settings, rules
```

## Configuration

All settings live in [`appsettings.json`](src/FtpFileMonitor/appsettings.json):

| Section | What it controls |
|---|---|
| `Ftp` | Server host, port, credentials and the root directory to watch |
| `Monitor` | Polling interval range, max scan depth, stable-size check, database file |
| `Rules` | File name patterns to watch for, as regex or `*` / `?` wildcards |

Credentials are intentionally left empty in the file and are read from environment variables (`FTPMONITOR_Ftp__User`, `FTPMONITOR_Ftp__Password`). Any other value can be overridden the same way, using the `FTPMONITOR_` prefix and `__` for nesting.

## Running locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) and Python (for a throwaway test FTP server).

1. Start a local FTP server with test credentials:

   ```bash
   pip install pyftpdlib
   mkdir -p ftproot/incoming
   python -m pyftpdlib -p 2121 -u test -P 12345 -d ./ftproot -w
   ```

2. In another terminal, pass the same credentials and run the monitor:

   ```bash
   export FTPMONITOR_Ftp__User=test
   export FTPMONITOR_Ftp__Password=12345
   dotnet run --project src/FtpFileMonitor
   ```

   On Windows (PowerShell):

   ```powershell
   $env:FTPMONITOR_Ftp__User = "test"
   $env:FTPMONITOR_Ftp__Password = "12345"
   dotnet run --project src/FtpFileMonitor
   ```

3. Drop files into `ftproot/incoming` (for example `report_q3.csv`) and watch them appear:

   ```
   First run: building baseline, nothing will be reported as new.
   Connected to FTP
   Scan done: 0 matched, 0 new. Next scan in 30s.
   Scan done: 1 matched, 0 new. Next scan in 45s.
   NEW  2026-09-03 12:41:07  [reports]  /incoming/report_q3.csv  (2048 bytes)
   Scan done: 1 matched, 1 new. Next scan in 15s.
   ```

   The history is in `monitor.db` and can be opened with any SQLite client.

## Tech

C#, .NET 8, FluentFTP, SQLite (Microsoft.Data.Sqlite), Microsoft.Extensions.Configuration
