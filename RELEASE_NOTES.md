# Readest Stats v2.3.0

Version 2.3.0 focuses on trustworthy history, recoverable changes, live localization, and responsive navigation.

## History and comparison

- Archived physical books keep their reading sessions and completion history in statistics, with a direct restore action.
- Comparison summaries, tables, units and charts use the same selected baseline. Invalid custom ranges are rejected; unequal periods remain available as exact tables instead of misleading overlays.
- Chart tooltips and keyboard details identify the date and value of both periods; a single baseline point remains visible.

## Recovery and library maintenance

- Backups snapshot SQLite including pending WAL data and verify SHA-256 checksums before restoration.
- Restore stages and validates files first, keeps recovery copies, and rolls back interrupted replacements on the next launch.
- Backup retention follows calendar age, not the number of files. Older archives remain identifiable as legacy backups.
- Duplicate-edition merges preview the retained book and moved sessions, preserve app references, and offer durable transactional undo.
- Data Health lists affected records with direct review actions; manual books no longer trigger missing Readest-file warnings.

## Interface and performance

- English/Vietnamese resources cover page controls, filters, common feedback and chart annotations; changing language leaves book titles and personal notes untouched.
- Pages are created on first visit and reused. Analytics avoid repeated full-history scans, and large book lists recycle visible rows.
- Adds regression coverage for archive/restore, merge/undo references, baseline selection, verified backups, interrupted recovery, localization and large-list rendering.

---

# Readest Stats v2.2.0

Version 2.2.0 makes comparison, recovery, rediscovery, and library maintenance first-class workflows.

## Focus and comparison

- Replaces duplicate Today queues with one Next up area and direct Details/Start actions.
- Adds Off, previous-period, same-period-last-year, and custom comparison modes.
- Draws comparison trends on a shared scale with a dashed grayscale series, exact tooltips, and accessible data tables.

## Data safety and maintenance

- Turns Data Health checks into repair routes that open the relevant workspace.
- Adds a Backup & Recovery Center with selectable snapshots, immediate creation, restore, guarded deletion, folder access, and 7/14/30-day retention.
- Adds an SQLite-backed undo journal that survives restarts and restores destructive physical-library changes.
- Detects duplicate physical editions, merges them without losing sessions, and supports safe archiving.

## Notes and language

- Adds Random Note pools, rediscovery history, 1/7/30-day snooze, exclusion controls, and citation copying.
- Adds System, English, and Vietnamese language preferences with localized page context as the first app-wide language layer.
- Adds v2.2 regression tests for undo persistence, backup deletion boundaries, and new preference storage.

---

# Readest Stats v2.1.0

Version 2.1.0 makes the daily workflow more actionable and every risky library operation easier to understand or reverse.

## Daily focus and planning

- Adds a Today reading-plan queue with direct session start for physical editions.
- Adds a 14-day planned-versus-actual chart, an explicit Skip today action, and preserved skipped-day history.
- Shows every reading cycle with its date range and current/completed state instead of collapsing re-reads into one status.

## Import, rediscovery, and search

- Adds a large Import Center preview for Goodreads, StoryGraph, generic CSV, and Readest Stats JSON before anything is saved.
- Adds one-step undo for the latest import and for a deleted physical book with its sessions.
- Adds seven-day note snooze, permanent random-pick exclusion, and a 20-note random history.
- Makes Ctrl+K accent-insensitive, adds useful actions, and supports Up, Down, Home, End, and Enter entirely from the keyboard.

## Data confidence

- Shows automatic backup history with size, version, and record counts.
- Extends Data Health with backup age, duplicate ISBN, page-overflow, edition-link, database-integrity, and overlapping-session checks.
- Adds v2.1 regression tests for adherence states, Vietnamese search, persisted rediscovery preferences, and backup inspection.

---

# Readest Stats v2.0.0

Version 2.0.0 is the new local-first foundation for Readest Stats, connecting daily focus, physical reading, book planning, note rediscovery, and data safety into one workflow.

## Today, search, and session continuity

- Replaces Overview with a Today workspace containing the daily note, a rediscovery note, quick actions, continue-reading priorities, and current-period context.
- Adds app-wide Ctrl+K search across pages, books, notes, and manual sessions.
- Adds a global session dock so a physical-book timer remains visible and controllable from every page.

## Durable local data and reading lifecycle

- Introduces an app-owned transactional SQLite database with schema versioning, integrity checks, WAL checkpoints, and automatic migration from the v1.x JSON store.
- Keeps Readest strictly read-only and retains `manual-reading.json` as a portable recovery mirror.
- Adds explicit reading cycles and re-read actions so completing the same book again is preserved as a new lifecycle event.
- Upgrades per-book plans with start dates, pause/resume, and selectable reading weekdays.

## Rediscovery, health, and verification

- Prioritizes unseen and least-recently-seen notes, records the last rediscovery time, and adds Unseen and Not seen recently scopes.
- Adds the Readest Stats database to Data Health and complete backups, including a direct integrity result and record counts.
- Fixes portable-library import isolation so the chosen JSON cannot accidentally resolve a neighboring local database.
- Adds v2 migration, storage, custom-plan-day, paused-plan, reading-cycle, rediscovery-state, and import-isolation coverage.

---

# Readest Stats v1.9.0

Version 1.9.0 connects planning, physical progress, notes, and library maintenance into one local-first reading workflow.

## Reading plans and library lifecycle

- Adds per-book target dates, daily page/minute pace, weekend preference, and priority with direct On track, Behind, or Complete status.
- Adds Continue Reading to Overview and an aggregate Active Book Plans section to Goals.
- Expands Books with lifecycle filters for current, planned, finished, paused, inactive, incomplete, pinned, physical, and Readest books.
- Links a Readest edition to its physical edition so combined statistics count one book while preserving source-specific views.

## Physical journal and notes

- Rejects overlapping manual sessions, allows the last deletion to be undone, and keeps corrected progress consistent.
- Adds note scopes for Readest, physical books, favorites, and the selected book; random discovery can stay within the current book.
- Makes physical-session notes editable from Notes and saves them back to their source session.
- Refreshes missing physical-book metadata without overwriting user edits and cleans unused cached covers.

## Portability, safety, and verification

- Adds physical-library JSON import/export with book deduplication and session-ID protection.
- Adds complete automatic backups with preview counts for books, sessions, notes, and covers.
- Extends Data Quality with dangling edition-link and manual-session overlap checks.
- Release packaging always recreates one clean v1.9.0 folder containing one EXE, one ZIP, one checksum, and one manifest.

---

# Readest Stats v1.8.0

Version 1.8.0 makes physical reading dependable enough to use as a daily journal, and adds regression protection for the complete WPF interface.

## Reading journal

- Records each active timer segment separately, so pause/resume gaps are excluded from reading time.
- Automatically pauses after sleep or a long system interruption instead of counting inactive wall-clock time.
- Adds optional notes when finishing a physical-reading session; these join Daily Note, Random Note, search, favorites, and note exports.
- Adds past sessions manually and edits existing sessions, including book, date, start time, duration, page range, and note.
- Edits saved physical-book metadata without creating a duplicate.

## Book discovery and local reliability

- Ranks Google Books and Open Library results by title/author relevance, metadata completeness, and accent-insensitive Vietnamese matching.
- Caches selected covers locally so physical books keep their artwork offline.
- Adds selected-period source composition and physical-page progress charts with direct summaries and exact-value text alternatives.
- Creates and restores a complete ZIP backup containing settings, goals, physical books, sessions, notes, and cached covers; restore preserves recovery copies.
- Migrates v1.7 manual-reading data automatically to schema v2 without losing books or sessions.

## Verification

- 103 core tests and one WPF smoke suite pass.
- The WPF suite constructs, measures, and arranges every page plus the add-book and session editor dialogs to catch startup-time XAML and binding regressions.
- Release packaging produces one consistently named v1.8.0 EXE, one ZIP, one SHA-256 sidecar, and one manifest.

---

# Readest Stats v1.7.0

Version 1.7.0 brings physical books into the same private reading story as Readest.

## Manual reading

- Adds a dedicated Manual log page and Ctrl+M shortcut for physical reading.
- Searches Open Library without setup and optionally searches Google Books first when an API key is supplied.
- Lets you verify the edition, cover, author, publication details, language, ISBN, and physical page count before saving.
- Runs one recoverable timer at a time with pause, resume, 15-second checkpoints, finish review, and time-only fallback.
- Records inclusive page ranges, updates physical-book progress, and marks a book complete when its final page is reached.
- Supports deleting manual sessions and physical books with confirmation while protecting an active session.

## Unified statistics and safety

- Adds All sources, Readest, and Manual filters to analytical pages.
- Shows source and physical page range in Sessions and physical progress in Books.
- Prevents overlapping Readest and manual intervals from double-counting in the combined view; Readest remains authoritative.
- Splits reading correctly across local midnight so both dates receive their actual share of time and activity.
- Stores manual data separately in `%LOCALAPPDATA%\\ReadestStats\\manual-reading.json` using atomic replacement and a recovery backup.
- Never writes book, session, or metadata changes into Readest.

## Verification

- 99 automated tests pass, including timer recovery, page math, metadata mapping, overlap removal, midnight splitting, and backup recovery.
- Release and WPF/XAML builds pass for Windows x64.

---

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
