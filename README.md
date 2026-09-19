# Readest Stats 1.9.0

[![Version](https://img.shields.io/badge/version-1.9.0-black)](https://github.com/phunguyen1006/statistics_readest/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-555555)](https://github.com/phunguyen1006/statistics_readest/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-black.svg)](LICENSE)

Readest Stats is an independent native Windows desktop companion for digital and physical reading. Version 1.9.0 turns the combined library into an active reading system: per-book plans, Continue Reading, edition linking, lifecycle filters, safer session correction, scoped random-note discovery, metadata refresh, and portable physical-library import/export all work without modifying Readest.

The Statistics reading story combines a keyboard-accessible 365-day heatmap, a 30-day streak strip, a 24-hour reading clock, weekday rhythm, line-based selected-period and 12-month journeys, cumulative reading, direct period comparison, goal progress, session distribution, ranked reading-time bars, and personal records. Every chart is computed from observed event duration and local timestamps; unsupported genre, rating, word-count, and reading-speed dimensions are not guessed.

Other features include one-click opening of a selected local book in Readest, a physical-book library with page progress, source-aware analytics, locally managed reading statuses and finished-book goals, pinned-book filtering, source-quality checks, monthly Markdown reports, update checks, seven rotating complete daily backups, persistent window placement, full-screen mode, and keyboard navigation (Ctrl+M opens Manual log).

Manual books, timer segments, page ranges, session notes, and completed sessions are saved atomically in `%LOCALAPPDATA%\\ReadestStats\\manual-reading.json`, with a recovery backup. If a manual interval overlaps an imported Readest interval, the Readest interval wins in the combined view so reading time is not counted twice. Sessions crossing local midnight are split correctly between calendar days.

The Notes tab combines highlights and annotations from Readest's per-book config.json files with notes written after physical-book sessions. It centers daily and shuffle-bag random-note rediscovery, includes history/back, source diversity, copy, lightweight search, heart-based favorites and private notes, and supports privacy-confirmed Markdown/JSON/CSV export. Personal curation is stored separately in `%LOCALAPPDATA%\\ReadestStats\\settings.json`.

## Screenshots

### Overview — dark mode

![Readest Stats overview in dark mode](docs/screenshots/overview-dark.png)

### Activity — light mode

![Readest Stats activity calendar in light mode](docs/screenshots/activity-light.png)

### Goals and Year in Reading

| Goals — dark mode | Year in Reading — light mode |
| --- | --- |
| ![Reading goals in dark mode](docs/screenshots/goals-dark.png) | ![Year in Reading in light mode](docs/screenshots/year-in-reading-light.png) |

## Download

Download `ReadestStats.exe` from the [latest GitHub release](https://github.com/phunguyen1006/statistics_readest/releases/latest). The application is self-contained for Windows x64 and does not require a separate .NET runtime.

Local builds are retained side by side:

```text
artifacts\v1.0.0\ReadestStats-v1.0.0.exe
artifacts\v1.1.0\ReadestStats-v1.1.0.exe
artifacts\v1.2.0\ReadestStats-v1.2.0.exe
artifacts\v1.3.0\ReadestStats-v1.3.0.exe
artifacts\v1.4.0\ReadestStats-v1.4.0.exe
artifacts\v1.5.0\ReadestStats-v1.5.0.exe
artifacts\v1.5.0\ReadestStats-v1.5.0-win-x64.zip
artifacts\v1.5.0\ReadestStats-v1.5.1.exe
artifacts\v1.6.0\ReadestStats-v1.6.0.exe
artifacts\v1.6.0\ReadestStats-v1.6.0-win-x64.zip
artifacts\v1.7.0\ReadestStats-v1.7.0.exe
artifacts\v1.7.0\ReadestStats-v1.7.0-win-x64.zip
artifacts\v1.8.0\ReadestStats-v1.8.0.exe
artifacts\v1.8.0\ReadestStats-v1.8.0-win-x64.zip
artifacts\v1.9.0\ReadestStats-v1.9.0.exe
artifacts\v1.9.0\ReadestStats-v1.9.0-win-x64.zip
```

## Data safety

The Readest database is opened with SQLite `ReadOnly` mode and a connection-local `query_only` guard. The application contains no generic write API for the source database. Settings, goals, reading statuses, explicit completion dates, physical books, and manual sessions are stored separately in `%LOCALAPPDATA%\ReadestStats\`.

The default data location is:

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

Readest Stats also checks Readest's `customRootDir` setting and allows selecting `statistics.db` manually.

Page numbers and total-page values can change with pagination, font size, layout, or reading-engine behavior. Therefore page-based progress and completion are not used as trustworthy primary metrics. Finished-book statistics use the explicit status selected by the user on the Books page. The experimental page-metrics option is off by default.

## Statistics methodology

The earlier statistics audit was informed by BookOrbit's summary-and-chart organization but was implemented independently for Readest's available local data. Unsupported dimensions are not guessed. See the [BookOrbit audit](docs/bookorbit-statistics-audit.md), [data map](docs/readest-statistics-data-map.md), [gap analysis](docs/statistics-gap-analysis.md), and [validation notes](docs/bookorbit-validation.md).

## Refresh and local state

Refresh can be manual, triggered by Readest database changes, or run on a 30-second, 1-minute, or 5-minute interval. Refreshing happens in the background and preserves the last good dataset if the source is briefly busy or unavailable. A complete ZIP backup contains settings, goals, physical books, sessions, notes, and cached covers; restore keeps recovery copies of the replaced local files.

## Build

Run from PowerShell:

```powershell
.\build-release.ps1
```

The script runs all automated tests, validates version consistency, publishes a self-contained single-file Windows x64 application, and creates a versioned executable, ZIP, SHA-256 sidecar, and release manifest in `artifacts\v<version>\` without deleting earlier versions. Double-click the desired executable; no browser, server, Node.js, or separately installed .NET runtime is required.

## Troubleshooting

- If the database is not found, open Settings and choose **Change database**.
- If Readest is actively writing, Readest Stats retries brief SQLite busy/locked errors automatically.
- If a custom root changed, use **Re-detect**.
- If the selected file is incompatible, the app reports the schema problem without modifying it.

## License

Released under the [MIT License](LICENSE).
