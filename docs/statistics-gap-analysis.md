# Statistics gap analysis

## Baseline before v1.2.0

The pre-1.2 inventory contains 38 distinct metrics or analytics behaviors across Overview, Activity, Sessions, Books, Goals, Insights, Year in Reading and Data Quality. The main weakness was fragmentation: users had to move between several pages and the former Insights page emphasized prose over a complete statistical overview. Version 1.2 adds five calculated outputs: selected-range, current-year, and all-time finished-book counts; a four-block time-of-day distribution; and a rolling 12-month completion timeline.

| Priority | Status | Scope |
|---|---|---|
| P0 | Implemented | total time, active days, session reconstruction, average/median/longest/shortest session, streaks, date ranges, comparisons, local-time daily grouping, book ranking, yearly summary |
| P0 | Implemented in 1.2 | one Statistics destination consolidating KPIs, trends, heatmap, hour/weekday/time-block behavior, sessions, completions, top books and records |
| P0 | Improved in 1.2 | Today, rolling 6-month and 1-year selectors; selected-range finished-book count; 12-month completion timeline |
| P1 | Implemented | goal progress/history/projection, book details, clickable activity, CSV/JSON/Markdown exports, source quality |
| P1 | UX improved in 1.2 | Insights navigation removed; compact chart hierarchy based on BookOrbit's summary→charts pattern |
| P2 | Deferred | author, language, series, file-format and storage analytics; possible from Readest/library files but secondary to reading behavior |
| P2 | Deferred | deterministic achievements; needs a stable threshold/versioning design and app-owned award history |
| P3 | Not applicable | multi-user, multi-library, metadata-provider and server-health analytics from BookOrbit |
| — | Impossible now | genre analytics, device/source distribution, publication/acquisition timeline, ratings, annotation statistics |
| — | Intentionally withheld | progress funnel, reading pace and estimated remaining time because Readest page/progress values change with layout and pagination |

No BookOrbit query was transplanted. Existing `StatisticsEngine`, `AnalyticsEngine`, `GoalEngine`, `InsightEngine` and `DataQualityEngine` remain the common calculation layer; WPF views only bind prepared values.
