# Readest Stats 1.2.0

[![Version](https://img.shields.io/badge/version-1.2.0-black)](https://github.com/phunguyen1006/statistics_readest/releases/tag/v1.2.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-555555)](https://github.com/phunguyen1006/statistics_readest/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-black.svg)](LICENSE)

Readest Stats is an independent native Windows desktop viewer for the reading data stored locally by Readest. Version 1.2.0 replaces the former Insights page with one consolidated Statistics dashboard, while retaining the complete dark, light, and system themes across Overview, Activity, Sessions, the cover-first Books library, Goals, Year in Reading, and Settings.

The Statistics dashboard combines reading time, active days, sessions, active and finished books, streaks, configurable trends, a clickable 365-day heatmap, hourly/weekday/time-of-day patterns, session distribution, a 12-month completion timeline, top books, yearly-goal progress, and personal records. Global date choices include Today plus rolling 7-day, 30-day, 3-month, 6-month, and 1-year ranges.

Other features include one-click opening of a selected local book in Readest, locally managed reading statuses and finished-book goals, pinned-book filtering, source-quality checks, monthly Markdown reports, update checks, daily rotating settings backups, persistent window placement, full-screen mode, and Ctrl+1–8 navigation shortcuts.

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
artifacts\v1.0.0\ReadestStats.exe
artifacts\v1.1.0\ReadestStats.exe
artifacts\v1.2.0\ReadestStats.exe
```

`artifacts\win-x64\ReadestStats.exe` remains a convenience copy of the latest build.

## Data safety

The Readest database is opened with SQLite `ReadOnly` mode and a connection-local `query_only` guard. The application contains no generic write API for the source database. Settings, goals, reading statuses, and explicit completion dates are stored separately in `%LOCALAPPDATA%\ReadestStats\`.

The default data location is:

`%APPDATA%\com.bilingify.readest\Readest\statistics.db`

Readest Stats also checks Readest's `customRootDir` setting and allows selecting `statistics.db` manually.

Page numbers and total-page values can change with pagination, font size, layout, or reading-engine behavior. Therefore page-based progress and completion are not used as trustworthy primary metrics. Finished-book statistics use the explicit status selected by the user on the Books page. The experimental page-metrics option is off by default.

## Statistics methodology

The v1.2 design was informed by BookOrbit's summary-and-chart organization but was implemented independently for Readest's available local data. Unsupported dimensions are not guessed. See the [BookOrbit audit](docs/bookorbit-statistics-audit.md), [data map](docs/readest-statistics-data-map.md), [gap analysis](docs/statistics-gap-analysis.md), and [validation notes](docs/bookorbit-validation.md).

## Refresh and local state

Refresh can be manual, triggered by Readest database changes, or run on a 30-second, 1-minute, or 5-minute interval. Refreshing happens in the background and preserves the last good dataset if the source is briefly busy or unavailable. Goals and settings can be backed up as JSON and survive upgrades through schema migration.

## Build

Run from PowerShell:

```powershell
.\build-release.ps1
```

The script runs all automated tests, reads the version from the project, and publishes a self-contained single-file Windows x64 application into `artifacts\v<version>\` without deleting earlier versions. Double-click the desired executable; no browser, server, Node.js, or separately installed .NET runtime is required.

## Troubleshooting

- If the database is not found, open Settings and choose **Change database**.
- If Readest is actively writing, Readest Stats retries brief SQLite busy/locked errors automatically.
- If a custom root changed, use **Re-detect**.
- If the selected file is incompatible, the app reports the schema problem without modifying it.

## License

Released under the [MIT License](LICENSE).
