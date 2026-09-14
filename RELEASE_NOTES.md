# Readest Stats v1.6.0

Version 1.6.0 is a correctness, accessibility, performance, and note-rediscovery release.

## Statistical correctness

- Keeps book membership/status filters independent from the selected time range, so book totals and detail charts no longer mix all-time and period data.
- Calculates historical records against the selected range end instead of filtering them again against today.
- Archives yearly book goals from explicit Finished dates and preserves each archived target as a snapshot.
- Filters sessions by stable book ID, including libraries with duplicate titles.
- Persists every calendar range preset, custom dates, trend metric, and chart granularity.

## Charts and navigation

- Adds explicit minutes, hours, sessions, books, and notes units to primary line/bar axes.
- Monthly reading trends automatically use hours while daily and weekly trends use minutes.
- Adds keyboard point navigation and accessible exact-value help to line and bar charts.
- Adds expandable text data under the primary trend for non-visual access.
- Shows the global range control only on pages it actually affects; Activity and Year keep their own month/year controls.
- Activity now charts the selected month directly.

## Books, Notes, and privacy

- Adds editable Finished dates for trustworthy historical goal counts.
- Replaces the non-virtualized cover grid with a recycling, compact library list.
- Adds shuffle-bag random notes, Back history, cross-book variety, Ctrl+N, Copy, lightweight search, and lifetime unique-seen count.
- Debounces local note-state saves and exposes note-file parse diagnostics in Data Quality.
- Confirms that note exports contain private content and documents the local plain-JSON storage location.

## Performance and reliability

- Caches canonical daily/session analysis and lazily builds heavy page-specific collections.
- Rotates diagnostic logs and treats fatal UI failures separately from recoverable errors.
- Adds Windows CI for tests, WPF/XAML compilation, publish smoke validation, and artifact upload.
- Release packaging now produces a versioned EXE, ZIP, SHA-256 sidecar, and JSON manifest.
- 91 automated tests pass before final packaging.

---

# Readest Stats v1.5.1

Version 1.5.1 streamlines the Notes Library around the two rediscovery experiences that matter most: Daily note and Random note.

## Notes Library refinements

- Replaces the filter toolbar with equal Daily note and Random note panels.
- Moves each note-selection action into its corresponding panel and keeps a smaller Refresh action in the header.
- Replaces the text favorite button with an accessible outline/filled heart control.
- Removes tags, collections, and review controls from the Notes Library workflow.
- Keeps random selection focused on the full note library while avoiding immediate repeats.
- Fixes chart refresh so metric, date-range, and time-grouping controls redraw immediately across the app.
- Uses daily, weekly, or monthly time buckets with matching horizontal-axis labels; Auto switches annual ranges to months.
- Adds the selected-period trend to Statistics so its metric and grouping controls always affect a visible chart.

## Safety and verification

- Readest source files remain read-only.
- Existing reading statistics, goals, books, and settings workflows remain unchanged.
- 74 automated tests passing before packaging.

---

# Readest Stats v1.5.0

Version 1.5.0 adds a local-first Notes Library connected to Readest's per-book note files. The source database and note files remain read-only; favorites and private notes live in Readest Stats settings.

## Notes Library

- Indexes highlights and annotations from Readest book configurations and maps them to the local book library.
- Two focused Daily note and Random note panels, with deterministic daily selection and repeat-resistant random selection.
- A cleaner newest-first note list with an accessible outline/filled heart favorite control.
- Private notes that never modify Readest and one-click opening of the source book.
- Markdown, JSON, and CSV export of the note library.
- Note analytics for books, months, and types with accessible chart labels.
- Refreshes when Readest's statistics, library, or per-book configuration files change.

## Safety and verification

- Readest note files are opened with shared read access and retry handling while Readest is writing.
- Deleted source notes are excluded from the active library without deleting local curation.
- Existing reading statistics, goals, books, and settings workflows remain unchanged.
- 74 automated tests passing before release packaging.
- The Windows x64 package is available as ReadestStats-v1.5.0-win-x64.zip with a sidecar SHA-256 file.

---

# Readest Stats v1.4.0

Version 1.4.0 turns the existing editorial report into a broader visual atlas while preserving the native WPF architecture, navigation, monochrome identity, local data pipeline, and read-only database guarantees.

## Visual atlas

- Weekday × hour matrix reveals exactly when weekly reading clusters.
- Activity uses a real 24-hour session timeline, calendar session glyphs, and rolling momentum.
- Sessions switch deterministically between individual duration dots at 15 or fewer observations and a grouped distribution above 15; a day-level style map compares frequency with typical duration.
- Books adds a 100% attention strip and a book × period matrix with daily or weekly columns based on range length.
- Goals adds actual-versus-required yearly pace, bullet charts, and compact hit-history strips.
- Statistics adds period dumbbells, a session staircase, composition, and sparse-data-aware session marks.
- Year in Reading adds a daily fingerprint, monthly glyphs, best-day lollipops, and yearly book-attention composition.

## Data integrity and accessibility

- Every new transform uses observed reading events, reconstructed sessions, locally stored finished dates, or configured goals.
- No genres, pages read, completion, ratings, speed, or AI-generated scores are inferred.
- Dense matrices aggregate before rendering; long book ranges switch to weekly columns.
- Primary messages are directly labeled and every dense visual has an automation name and descriptive help text.

## Highlights

- New 24-hour radial reading clock with a directly labeled peak reading window.
- New 30-day streak timeline paired with the keyboard-accessible 365-day contribution heatmap.
- New line-based reading journey, 12-month journey, and cumulative reading visualization.
- New paired current/previous-period comparison with finite zero-baseline behavior.
- New compact goal ring, ranked reading-time bars, and contextual session distribution.
- A dedicated `VisualizationEngine` converts canonical events into presentation-ready chart series without querying storage from UI controls.
- Statistics now follows the questions how often, when, how reading changes, and how sessions/books receive time instead of presenting six equal KPI cards.

## Data integrity

Primary visualizations use positive-duration reading events and local timestamps. Finished-book goals continue to use explicit completion dates stored by Readest Stats. Genre, rating, words-per-minute, trustworthy historical completion, and reading pace remain intentionally unavailable because the Readest statistics database does not provide reliable inputs for them.

## Verification

- 66 automated tests, including new visualization pipeline coverage.
- Debug and release builds validate WPF/XAML compilation.
- No new runtime or chart dependency.

---

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
