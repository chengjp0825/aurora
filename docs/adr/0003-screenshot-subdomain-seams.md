# ADR-0003: Screenshot Sub-Domain Adapters

## Status

Accepted

## Context

`MainWindow` was interpreting `ActionResult` and spawning `ScreenshotWindow`/`PinWindow` directly, coupling the menu view to the screenshot workflow.

## Decision

Introduce `IScreenshotOverlay`, `IScreenshotPinService`, and `ScreenshotWorkflow`. The `sys:snipping` command drives the workflow; WPF-specific windows live behind adapter implementations.

## Consequences

- The screenshot sub-domain is testable and mockable.
- `MainWindow` no longer needs to know about screenshot results.

## Related

- `CONTEXT.md` — `ScreenshotWorkflow`, `IScreenshotOverlay`, `IScreenshotPinService`
