# Readest Stats

Readest Stats is an independent, native Windows desktop viewer for the reading statistics stored locally by Readest. It provides overview metrics, trends, a 365-day heatmap, hourly and weekday distributions, monthly/yearly activity, reconstructed sessions, book analytics, personal records, local goals, and CSV/JSON export.

## Data safety

The Readest database is opened with SQLite `ReadOnly` mode and a connection-local `query_only` guard. The application contains no generic write API for the source database. Settings and goals are stored separately in `%LOCALAPPDATA%\ReadestStats\`.

The default data location is:

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

Readest Stats also checks Readest's `customRootDir` setting and allows selecting `statistics.db` manually.

## Build

Run from PowerShell:

```powershell
.\build-release.ps1
```

The script runs all automated tests and publishes a self-contained single-file Windows x64 application.

## Release

The executable is written to:

`artifacts\win-x64\ReadestStats.exe`

Double-click it; no browser, server, Node.js, or separately installed .NET runtime is required.

## Troubleshooting

- If the database is not found, open Settings and choose **Change database**.
- If Readest is actively writing, Readest Stats retries brief SQLite busy/locked errors automatically.
- If a custom root changed, use **Re-detect**.
- If the selected file is incompatible, the app reports the schema problem without modifying it.
