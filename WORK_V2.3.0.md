# v2.3.0 implementation

Authorized scope: correct archived-book history and comparison ranges; verified recoverable backups; library maintenance with safe merge and archive restore; actionable data health; live English/Vietnamese localization; lazy pages and large-list performance; regression checks and one canonical local package.

GitHub publishing is a separate user action after this implementation.

## Verification targets
- Archived books retain historical events and can be restored.
- Comparison mode changes summary, charts, table, dates, and units together.
- SQLite snapshots include WAL data; damaged backups are rejected before writes; interrupted restores roll back.
- Merge preserves sessions and references, with explicit preview and durable undo.
- Data health opens affected records.
- Language changes update open pages and dialogs; user content is not translated.
- Only visited pages are constructed; large books/notes lists recycle containers.
- Release tests, WPF smoke checks, packaged executable startup.

## Verification results

- Release core suite: 128 passed.
- WPF integration suite: passed, covering all pages, lazy page reuse, archived history, duplicate merge and reference undo, comparison controls, workspace backup restoration, live language changes and a 2,000-row virtualized book list.
- Rendered and inspected Manual log and Statistics with isolated sample data in Vietnamese, including light/dark Statistics screenshots in `artifacts/qa-v2.3.0`.
- Every XAML localization resource key resolves. User book titles remain unchanged in the localization regression check.
- Canonical packaging target: `artifacts/v2.3.0/ReadestStats-v2.3.0.exe`, ZIP, SHA-256 and manifest; no alternate executable suffixes.
- Final package built successfully. ZIP SHA-256 matches its manifest; the single executable inside the ZIP matches the standalone executable byte-for-byte, with file version 2.3.0.0.
- Packaged startup smoke check: the Readest Stats window appeared and the process remained responsive after 12 seconds, then the test instance was closed. Live-library synchronization was not verified by this check; functional flows were verified using isolated fixtures.
- Build warning: NuGet vulnerability-feed availability could not be checked in the restricted network environment (NU1900); compilation and tests otherwise succeeded.
