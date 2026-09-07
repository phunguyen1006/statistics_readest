# Readest Stats 1.1 — Design System

This file is the implementation reference for every screen. It supersedes the former blue dashboard theme.

## Product character

- Native Windows desktop analytics app: quiet, precise, private, and information-dense.
- Fixed high-contrast dark monochrome appearance. No gradients, decorative shadows, glass, blue accents, or green goal bars.
- Prefer native WPF controls, keyboard operation, vector icons, and stable layouts.
- Density: 8/10. Motion: 1/10; state changes remain usable when motion is reduced.

## Color tokens

| Token | Value | Use |
|---|---:|---|
| AppBackground | `#0A0A0A` | window background |
| Sidebar | `#0D0D0D` | persistent navigation |
| Surface | `#111111` | cards |
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
- Main page gutter: 26px horizontal, 22px top, 28px bottom.
- Use a 4/8px rhythm. Standard card padding 16px; section gap 16–18px.
- Use scroll viewers for vertical overflow. Avoid horizontal scrolling and nested scroll regions.
- Large lists use virtualizing WPF controls; charts aggregate before drawing.

## Components

- Cards: 1px border, 10px corner radius, no shadow. Nested sections use surface contrast and spacing.
- Buttons: minimum 34px height, 8px radius, visible hover/pressed/focus states without moving layout.
- Icon-only buttons must have a tooltip and `AutomationProperties.Name`.
- Inputs: persistent labels, 1px border, 34px minimum height, white focus outline.
- Navigation: 16px outline vector icons with consistent 1.6 stroke; active row uses white text and a surface fill.
- App identity: a black rounded-square icon with a white open-book mark; use the same mark in the executable, title bar, taskbar, and sidebar header.
- KPI cards: short uppercase label, prominent value, single supporting line.
- Data tables: sortable/filterable where the view exposes controls; alternating rows and restrained separators.
- Charts: grayscale, subtle grid lines, exact-value tooltips, explicit empty states, readable units, and a maximum bar width so sparse data never becomes a giant block.
- Heatmaps: five quantile-derived grayscale levels plus a labeled legend; cells expose exact date/time details.
- Goals: progress uses white/gray only and includes a textual status such as Ahead, On track, Behind, Complete, or Not enough data.

## Accessibility and interaction

- Logical tab order follows visual order. All primary controls are native keyboard-focusable WPF elements.
- Normal text contrast must meet 4.5:1; chart marks and large glyphs at least 3:1.
- Visible focus outline is mandatory. Hover is supplementary, never the only way to access information.
- Loading is non-blocking; refresh is accompanied by progress feedback while work is active.
- Errors appear as readable inline status with a Retry action; the last good dataset remains visible.
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

Overview → Activity → Sessions → Books → Goals → Insights → Year in Reading → Settings.

Every screen keeps the sidebar and top bar stable, uses the global date range where relevant, and provides a meaningful no-data state rather than an empty chart frame.

## Release checklist

- No legacy blue/green palette or gradients.
- Vector icons are consistent; icon-only controls have accessible names.
- Focus, hover, pressed, disabled, loading, error, and empty states exist.
- Charts have units, restrained bars, tooltips, and no-data messaging.
- Layout works at 1366×768 and 1440×900 without horizontal clipping.
- Reduced-motion preference causes no loss of information.
- Source database remains byte-for-byte unchanged in repository safety tests.
