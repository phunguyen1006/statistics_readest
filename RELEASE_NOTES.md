# Readest Stats v1.1.0

Readest Stats v1.1.0 is a major native Windows UI and analytics upgrade. It remains an independent, local-only companion for Readest and opens the Readest statistics database strictly in read-only mode.

## Highlights

- Complete monochrome dark, light, and system themes with instant switching.
- Rebuilt Overview, Activity, Sessions, Books, Goals, Insights, Year in Reading, and Settings pages.
- Reliable automatic refresh when Readest changes its local statistics database.
- Daily reading-time goals plus weekly, monthly, and yearly book goals.
- Current-year book progress and automatic archives for previous years.
- Global date ranges, fair previous-period comparisons, calendar and 365-day heatmaps.
- Session reconstruction, title-level analysis, deterministic insights, CSV and JSON export.
- New book-themed application icon.
- Self-contained Windows x64 executable; no separate .NET installation required.

## Data interpretation

Readest does not expose a trustworthy completed-book flag in the available statistics schema. Book goals therefore count unique titles with recorded reading activity during the relevant period. The application labels this behavior explicitly and never writes to Readest's database.

## Verification

- 46 automated tests passing.
- All eight pages visually checked in both dark and light themes.
- Published executable startup verified on Windows x64.
