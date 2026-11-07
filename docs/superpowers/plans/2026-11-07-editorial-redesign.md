# Stronger Editorial Redesign Implementation Plan

> **For agentic workers:** Execute natively in this session and verify each UI change using the existing WPF test harness.

**Goal:** Make the monochrome redesign immediately distinguishable from 2.6.0 while preserving reading workflows and data.
**Architecture:** Shared semantic brushes and WPF templates shape the shell; Today and Statistics reorganize existing bindings into a reading story. Existing charts, commands and persistence remain intact.
**Tech Stack:** WPF, C#, .NET 8, xUnit.
**Spec:** docs/redesign/editorial-direction.md

## Constraints
- No web runtime, fabricated statistics, source-data writes, removed actions, or localization regression.
- Keep light/dark/system themes, high contrast, keyboard focus, narrow layouts, and list virtualization.

## Review focus
- Empty library: no invented values and primary actions remain reachable.
- Long titles and Vietnamese strings: wrap or trim without hiding actions.
- Narrow windows: sidebar, filters and panels remain reachable.
- Theme changes: inverse navigation and hero text remain legible.
- Accessibility: metric commands and chart automation still work.

## Tasks
- [x] Update shared shell navigation, theme resources, navigation groups and metrics treatment; test theme contrast and navigation.
- [x] Redesign Today, Statistics, Activity and Books using existing bindings and commands; test narrow layout, chart names and interaction.
- [x] Render and inspect light, dark and narrow views using synthetic data; fix visual or layout issues.
- [x] Run the full Release suite and document verification; leave existing published release untouched.
