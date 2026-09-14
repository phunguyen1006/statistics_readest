# Notes Library Page Overrides

These page rules extend the Readest Stats v1.4 design system for the v1.5 Notes Library.

## Product intent

- Content-first desktop knowledge library for local Readest highlights and annotations.
- Keep the editorial report language: flat background, thin separators, restrained grayscale, no decorative cards.
- Source note content is read-only; locally owned curation is limited to favorites and “My note”.

## Layout

- Keep the fixed 196px sidebar and 72px top bar.
- Use a 26px page gutter and an editorial section rhythm of 18–24px.
- Top controls: page title, summary, and a compact Refresh action.
- Follow the header with two equal spotlight panels: deterministic Daily note on the left and Random note on the right. Each panel contains its own action.
- Main content is a dense split view: virtualizable note list on the left, scrollable detail aside on the right.
- Analytics follow the library with a small book ranking, monthly line, and type distribution; every chart keeps a text summary.

## Components

- Note rows use 1px separators and hover/selected surface states rather than enclosing cards.
- Primary action is Another random note inside the Random spotlight; secondary actions are Show daily note, Refresh, Open in Readest, and Save.
- Favorite uses one consistent vector heart: outline by default, filled with the theme foreground when active, with tooltip and accessible name.
- Export actions must have text labels or accessible names; no emoji icons.
- Use native WPF ListBox, TextBox, ComboBox, Button, and ScrollViewer controls with visible keyboard focus.
- List virtualization is required when the note library exceeds 50 items.

## Data and interaction

- Preserve the last good dataset when a Readest file is locked.
- Random note draws from the complete note library and avoids the immediately previous result.
- Daily note is deterministic for the local calendar date.
- Open in Readest must degrade gracefully to book-level opening when deep-linking to CFI/xpointer is unavailable.

## Visual and accessibility checks

- Use the existing monochrome light/dark tokens from Master; do not introduce the generated blue accent.
- Body text stays at least 13px in the desktop dense layout; normal text contrast remains at least 4.5:1.
- Keep buttons at least 34px high, visible hover/pressed/focus states, and a logical tab order.
- Empty, loading, missing-book, locked-file, hidden-note, and filtered-no-result states are explicit.
- Charts use grayscale marks, units, direct labels, and a readable text fallback.
