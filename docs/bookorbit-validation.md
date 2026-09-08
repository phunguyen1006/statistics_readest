# BookOrbit validation

## Method

BookOrbit source was used as a behavioral benchmark, not a runtime dependency. Its user-statistics types, service aggregations, Statistics page, chart registry and AGPL-3.0 license were inspected at commit `3d6afb314957a6c52c9242c1ed3f8a35a35821e7`.

Automated validation in Readest Stats covers duration sums, session overlap merging, configurable gap behavior, median and percentile calculation, zero-baseline comparisons, rolling/calendar ranges, local-time day boundaries, streak boundaries, leap-year heatmaps, goal progress/history, data-quality checks and read-only repository behavior.

## Expected differences

| Area | BookOrbit | Readest Stats | Reason |
|---|---|---|---|
| Source of duration | native BookOrbit sessions from multiple readers | Readest page activity events | Different source schema |
| Session identity | stored session rows | deterministic reconstruction | Readest exposes fragmented events, not stable session IDs |
| Completion | status/history stored by BookOrbit | explicit app-owned Finished date | Readest has no reliable historical completion event |
| Timezone | per-user configured timezone | current Windows local timezone | Standalone single-user desktop design |
| Device/source | captured reader source | unavailable | Readest schema has no source field |
| Progress analytics | stored normalized progress | withheld by default | Readest pagination can change with layout/font/engine |
| Library metadata | rich provider-backed metadata | title, author, series, language and local file | Readest is the sole source of truth |

## Benchmark procedure

For a manual same-history comparison, select the same local dates and compare active reading seconds first. If totals differ, inspect event filtering, overlapping duration, session-gap reconstruction and timezone boundaries before changing a formula. Do not tune results merely to match BookOrbit. BookOrbit can be stopped completely; Readest Stats starts, loads and computes without it.
