# Monochrome redesign: execution outline

The user-provided master prompt is the specification. The work stays within native WPF and preserves the existing data model, read-only Readest access, local state and analytics.

- Captured a synthetic baseline and assessed the available design skills.
- Refined the shared shell and all ten primary pages, preserving their commands and data bindings.
- Reflowed compact layouts, promoted the selected-period trend, clarified calendar selection and improved keyboard chart exploration.
- Rendered synthetic pages in both themes and across several logical window sizes; exercised localization, workflows, data safety and list virtualization in the test suite.
- Built a self-contained win-x64 preview in a separate QA directory without replacing historical release artifacts.

See ui-review.md, design-decisions.md, skills-report.md and visual-qa.md for evidence and remaining limitations.
