# Design decisions

- Retain the native WPF interface, system fonts, grayscale themes and existing semantic hierarchy; use whitespace, dividers and text rather than adding decorative dashboard chrome.
- Lead Today with period metrics and weekly reading, and lead Statistics with the period summary and selected-period trend. The monthly review keeps its independent month scope.
- Keep the original analytics and bindings. Solid and dashed lines distinguish comparison series, heatmaps use a labeled grayscale scale, and summary text or exact-value lists complement visual marks.
- Use AdaptiveColumnsPanel for related sections that must stack at narrow widths. KeyboardChartElement supplies a shared visible readout for focused charts; normal WPF controls and virtualization remain in place.
- Keep tables horizontally scrollable when columns cannot fit, rather than silently removing fields. The narrow layout prioritizes access over fitting every row and column in the first viewport.
- Preserve the existing timer, book, notes, goals, export, backup, localization and theme flows. No new web runtime or analytics source was introduced.
- A keyboard readout is intentionally bounded to avoid covering an entire chart; its full text is retained in the automation description. Direct testing with a screen reader and actual Windows DPI changes remains outstanding.
