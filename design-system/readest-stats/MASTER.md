# Readest Stats 2.0 — Design System

This file is the implementation reference for every screen. It supersedes the former blue dashboard theme.

## Product character

- Native Windows desktop analytics app: quiet, precise, private, and information-dense.
- High-contrast monochrome light, dark, and system themes. No gradients, decorative shadows, glass, blue accents, or green goal bars.
- Every analytical page reads as an editorial personal report: question-led sections, direct annotations, and generous dividers replace a wall of equal KPI cards.
- Prefer native WPF controls, keyboard operation, vector icons, and stable layouts.
- Density: 8/10. Motion: 1/10; state changes remain usable when motion is reduced.

## Color tokens

| Token | Value | Use |
|---|---:|---|
| AppBackground | `#0A0A0A` | window background |
| Sidebar | `#0D0D0D` | persistent navigation |
| Surface | `#111111` | menus, dialogs, and raised controls |
| SurfaceRaised | `#151515` | controls and nested regions |
| SurfaceHover | `#1A1A1A` | hover state |
| SurfacePressed | `#222222` | pressed/selected state |
| Border | `#2B2B2B` | separators |
| BorderStrong | `#4A4A4A` | focus and strong boundaries |
| TextPrimary | `#F5F5F5` | headings and primary values |
| TextSecondary | `#C4C4C4` | body and supporting data |
| TextMuted | `#949494` | metadata |
| TextDisabled | `#6B6B6B` | disabled state |
| Primary | `#FFFFFF` | selected markers and key chart series |

Status is never communicated by color alone. Use words and restrained arrows/check marks only in textual status values.

## Typography

- Use installed Windows fonts only: Segoe UI for interface text; Cascadia Mono or Consolas for file paths and tabular technical values.
- Scale: 11 metadata, 12 labels, 13 body, 15 navigation, 18 section values, 20 page title, 28–32 primary KPI.
- Use semibold for hierarchy; avoid excessive uppercase except short eyebrow labels.
- Use tabular figures in data tables where practical.

## Layout and spacing

- Window minimum: 1100×650. Primary QA targets: 1366×768 and 1440×900.
- Sidebar: 196px, fixed, icon plus label for every destination.
- Top bar: 72px, page context left and global period controls right.
- Today is the default landing workspace. It surfaces the next reading action, daily rediscovery, and current priorities before deeper analytics.
- Ctrl+K opens one centered command palette for pages, books, notes, and sessions; the palette traps the immediate search task and closes with Escape.
- An active physical-reading session uses a compact persistent dock below the top bar on every page; it must always expose elapsed time, pause/resume, finish, and a route back to the timer.
- Main page gutter: 26px horizontal, 22px top, 28px bottom.
- Use a 4/8px rhythm. Editorial sections use 18–24px vertical breathing room and thin full-width dividers.
- Use scroll viewers for vertical overflow. Avoid horizontal scrolling and nested scroll regions.
- Large lists use virtualizing WPF controls; charts aggregate before drawing.

## Components

- Content sections sit directly on `AppBackground`; use typography, whitespace, and 1px separators instead of enclosing cards. Reserve raised surfaces for menus, dialogs, inputs, and interactive affordances.
- Buttons: minimum 34px height, 8px radius, visible hover/pressed/focus states without moving layout.
- Icon-only buttons must have a tooltip and `AutomationProperties.Name`.
- Inputs: persistent labels, 1px border, 34px minimum height, white focus outline.
- Navigation: 16px outline vector icons with consistent 1.6 stroke; active row uses white text and a surface fill.
- App identity: a black rounded-square icon with a white open-book mark; use the same mark in the executable, title bar, taskbar, and sidebar header.
- KPI bands: short uppercase label, prominent value, single supporting line, and vertical dividers rather than boxed tiles.
- Data tables: sortable/filterable where the view exposes controls; alternating rows and restrained separators.
- Book lifecycle UI distinguishes an edition from a reading cycle. Re-reads append cycles and never overwrite earlier completion history.
- Reading-plan controls expose start, target, selected reading weekdays, and paused state together; status text must explain the computed pace.
- Charts: grayscale, subtle grid lines, exact-value tooltips, explicit empty states, readable units, and a maximum bar width so sparse data never becomes a giant block.
- Heatmaps: five quantile-derived grayscale levels plus a labeled legend; cells expose exact date/time details.
- Goals: progress uses white/gray only and includes a textual status such as Ahead, On track, Behind, Complete, or Not enough data.
- Visual vocabulary: matrices for relationships, timeline bands for actual sessions, dots for sparse distributions, segmented bands for composition, dumbbells for two-period comparison, staircases for session accumulation, bullets for target tracking, and lollipops/small glyphs for yearly storytelling.
- Sparse-selection rules are deterministic: 15 or fewer sessions use individual dots; larger samples use grouped distributions; fewer than four active days withhold scatter interpretation; ranges longer than 45 days aggregate book matrices by week.
- Repeated data must change level or visual question across pages: Overview previews rhythm, Statistics explains relationships, and Year in Reading supplies annual narrative context.
- Every visualization begins with a user question and keeps the exact value available through a visible annotation, summary, or tooltip.

## Accessibility and interaction

- Logical tab order follows visual order. All primary controls are native keyboard-focusable WPF elements.
- Normal text contrast must meet 4.5:1; chart marks and large glyphs at least 3:1.
- Visible focus outline is mandatory. Hover is supplementary, never the only way to access information.
- Loading is non-blocking; refresh is accompanied by progress feedback while work is active.
- Errors appear as readable inline status with a Retry action; the last good dataset remains visible.
- Search results use a visible type label plus title and context; no result may rely on an icon alone.
- No emoji as structural icons. No layout-shifting hover animation.

## Data integrity rules

- Time is canonical and stored/computed in seconds; formatting occurs only at the presentation boundary.
- Date ranges and calendar grouping use the local timezone. Current calendar periods compare equal elapsed spans.
- Consistency excludes days before the first available reading event.
- Weekday/weekend averages divide by eligible calendar weekdays/weekend days, including inactive eligible days.
- Sessions are reconstructed using the configured gap and overlapping active time is merged.
- Page-derived progress, completion, and books-finished counts are omitted unless Readest provides a stable trustworthy signal. Experimental page metrics remain off by default.
- The Readest database is read-only. App settings and goals are stored separately.

## Screen map

Overview → Activity → Sessions → Manual log → Books → Notes → Goals → Statistics → Year in Reading → Settings.

Manual log uses a single editorial workflow: current timer first, edition search only while adding a book, then the physical library beside recent sessions. Timer state is stated in words as well as time; page progress is shown only for user-entered physical editions, never inferred from Readest pagination.

The v1.8 reading journal keeps timer truth visible: Finish closes the current active segment immediately, sleep recovery explains why the timer paused, and saved sessions can be corrected later. Historical entry and edit dialogs use persistent labels, inline validation, and an explicit primary Save action. Session notes from physical books share the same Daily/Random discovery surface as Readest notes but remain clearly labeled as Manual notes.

The v1.9 planning layer stays inside the existing editorial hierarchy rather than adding another dashboard. Overview shows at most three Continue Reading actions; Goals lists every active book plan; Books owns plan editing, lifecycle state, edition linking, and metadata maintenance. Plan-versus-actual uses a grayscale bullet chart plus exact pace text, and every Behind state includes the required daily pace. Linked digital and physical editions appear as one canonical book in the combined view while source filters preserve provenance.

Source composition uses a segmented band only when it answers a real comparison; every segment also has a direct duration summary. Physical page progress uses user-confirmed session endpoints and always provides an expandable exact-value list. Remote cover art is cached only after a book is saved so browsing does not create unbounded local files.

Every screen keeps the sidebar and top bar stable, uses the global date range where relevant, and provides a meaningful no-data state rather than an empty chart frame.

## Release checklist

- No legacy blue/green palette or gradients.
- Vector icons are consistent; icon-only controls have accessible names.
- Focus, hover, pressed, disabled, loading, error, and empty states exist.
- Charts have units, restrained bars, tooltips, and no-data messaging.
- Layout works at 1366×768 and 1440×900 without horizontal clipping.
- Reduced-motion preference causes no loss of information.
- Source database remains byte-for-byte unchanged in repository safety tests.
