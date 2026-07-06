# MyQuicker Domain Glossary

## Trigger

Any user action that can wake the MyQuicker menu. The top-level abstraction in the wake pipeline.

- A Trigger is evaluated against incoming input state.
- It returns a `TriggerMatchResult`.

## ButtonTrigger

A Trigger that matches an instantaneous hardware input action.

- Examples: mouse middle button, mouse side buttons X1/X2, a keyboard shortcut combination.
- Does not require temporal or spatial tracking of pointer motion.

## GestureTrigger

A Trigger that matches a pointer movement pattern evaluated over time and space.

- Examples: drawing a circle with the pointer.
- Requires tracking the pointer trajectory and matching it against a shape or path definition.
- Also known as *PathTrigger* while the exact term is being decided.

## TriggerMatchResult

The outcome of evaluating a Trigger against input state.

- `IsMatch`: whether the Trigger matched.
- `WakeContext`: metadata captured at the moment of matching, or null if there is no match.

## WakeContext

Metadata packaged with a successful Trigger match, decoupling input capture from menu rendering.

- `Location`: the exact screen coordinates (X, Y) where the menu should open.
- `Timestamp`: the exact time the event occurred, used for debounce and stale-event filtering.
- `TriggerSource`: an identifier for the specific Trigger that fired (for example, `MiddleButton` or `CircleGesture`), allowing the menu layer to vary initial state or animation.

## TriggerBinding

A configuration-level data object that expresses the user's intent for a wake action. The stable schema stored in settings.

- Examples: `{ type: "Button", button: "Middle" }`, `{ type: "Gesture", shape: "Circle" }`.
- Contains no runtime state or system dependencies.
- Decoupled from the runtime `Trigger` class hierarchy so that refactors and migrations stay localized.

## TriggerFactory

Builds runtime `Trigger` instances from `TriggerBinding` objects.

- Injects the low-level services each Trigger needs (for example, global mouse hooks, timers).
- Centralizes construction logic and keeps settings deserialization free of runtime dependencies.

## WakeOrchestrator

The central hub that decides whether a matched Trigger should result in the menu being shown.

- Consumes `WakeContext` from the Trigger layer.
- Maintains menu lifecycle state (for example, Hidden, Opening, Visible, Closing) and handles state transitions when new wake requests arrive during animations.
- Applies debounce and stale-event filtering using `WakeContext.Timestamp`.
- Performs multi-monitor and DPI-aware boundary checks so the menu opens on the correct display without clipping.
- Optionally filters against the current foreground window/process against a user exclusion list.
- Commands the menu presentation layer through a UI-framework-agnostic command interface.

## MenuPresenter

The component responsible for rendering the menu view and managing its presentation lifecycle.

- Receives commands from `WakeOrchestrator` (for example, `ShowAt(Location)` or `Dismiss()`).
- Owns the WPF window, animations, Z-order, and hit-testing.
- Renders the action layout and forwards user selections to the action layer.
- Has no dependency on Trigger matching, screen geometry, or foreground-window policy.

## Action

A user-configurable menu item. The visual node in the menu.

- `Id`: stable identifier.
- `DisplayName`: text shown in the menu.
- `Icon`: visual glyph or icon path.
- `CommandId`: reference to the `Command` that runs when the action is activated.

An Action does not contain execution logic. It is a view-model-level concept.

## Command

The executable payload that runs when an Action (or any other trigger source) is activated.

- Independent of the menu UI.
- Implemented as a sealed class hierarchy (for example, `LaunchApplicationCommand`, `OpenUrlCommand`, `RunScriptCommand`, `SystemCommand`).
- Stateless pure data object; receives runtime dependencies through `CommandContext` when `Execute` is called.
- Registered in a command registry for O(1) lookup by ID.
- Can be triggered from multiple entry points (menu click, global hotkey, IPC, etc.).

## CommandContext

The runtime environment passed to `Command.Execute`.

- Provides access to application services such as `ProcessLauncher`, `WindowManager`, `SettingsService`, and `IpcBus`.
- Keeps commands stateless and decoupled from service lifetimes.
- Allows unit tests to run commands with mocked services.

## CommandRegistry

An in-memory lookup table that maps command IDs to `Command` instances.

- Provides O(1) resolution by ID.
- Populated at startup by multiple providers.
- Rejects attempts to overwrite built-in command IDs from user configuration.

## BuiltInCommandProvider

Registers immutable system commands at application startup.

- Examples: `sys:screenshot`, `sys:settings`, `sys:exit`.
- No disk I/O; deterministic, fail-safe registration.

## UserCommandStore

Loads user-defined commands from external settings and registers them in the `CommandRegistry`.

- Source of mutable command configuration.
- Handles disk I/O, deserialization, validation, and migration.
- Failures here do not prevent built-in commands from being registered.

## MenuGroup

A presentation-level container that groups `Action`s in the menu.

- `Id`: stable identifier.
- `DisplayName` / `Icon`: visual header.
- `Actions`: ordered collection of `Action` references.
- Provides spatial topology context to `MenuPresenter` for layout calculations.
- Invisible to the command execution layer.

## Menu

The single global menu shown by MyQuicker.

- Composed of one or more `MenuGroup`s.
- Opened by any matched `Trigger` through `WakeOrchestrator` and `MenuPresenter`.
- `WakeContext.TriggerSource` may influence animation or focus but does not change available content.

## ScreenshotCaptureService

Service that captures screen content using low-level graphics APIs.

- Returns a `Screenshot` domain object.
- Runs capture logic on a background thread when possible.
- Has no knowledge of UI overlays or pinning.

## Screenshot

A captured screen region as a domain object.

- Contains bitmap data and metadata (source bounds, timestamp, DPI).
- Owns unmanaged resource lifecycle through explicit disposal.

## ScreenshotOverlay

Full-screen presenter that lets the user select a region of a screenshot.

- Blocks the UI message loop while the user selects.
- Returns selected bounds or a cropped screenshot.

## ScreenshotPinService

Service that displays a captured screenshot region as a floating sticky window.

- Manages its own top-level window with layered/alpha properties.
- Tracks pinned window lifetimes independently of the main menu.

## Settings

The top-level persisted configuration object stored in `settings.json`.

- `TriggerBindings`: configured wake actions.
- `Commands`: user-defined command data.
- `MenuGroups`: menu structure and action metadata.
- `Preferences`: general application preferences.

A pure data object with no runtime state or service dependencies.

## SettingsManager

Owns loading, saving, and migrating `Settings`.

- Reads from and writes to `settings.json`.
- Migrates legacy formats (for example, from `appsettings.json`) to the current schema.
- Produces the latest `Settings` DTO regardless of the disk format version.

## AppBootstrapper

The composition root that wires the application together at startup.

- Initializes diagnostics and logging.
- Loads `Settings` via `SettingsManager`.
- Registers core services.
- Populates the `CommandRegistry` through `BuiltInCommandProvider` and `UserCommandStore`.
- Builds runtime `Trigger`s from `TriggerBinding`s via `TriggerFactory`.
- Constructs `WakeOrchestrator` and `MenuPresenter`.
- Installs global input hooks last, after all dependent services are ready.

## CommandDefinition

A persisted entry in the user command catalog. Contains `Id`, `DisplayName`, `Type`, and `Target`. Separated from `Action` so commands can be reused across menu groups.

## ScreenshotWorkflow

Orchestrates the screenshot sub-domain: Capture → Select Region → Pin/Clipboard. Owned by the command layer, not the menu view.

## IScreenshotOverlay

Adapter seam for region selection UI. WPF implementation wraps `ScreenshotWindow`.

## IScreenshotPinService

Adapter seam for displaying a captured region as a sticky window. WPF implementation wraps `PinWindow`.

## RawInputSource

Thin adapter around the low-level mouse hook. Translates Win32 messages into `TriggerEvent`.

## TriggerEvaluator

Owns the polymorphic trigger loop. Receives `TriggerEvent`s and evaluates them against configured `ITrigger` strategies.

## IWakeBlockPolicy

Decides whether the menu should refuse to wake (e.g. while a modal overlay is open).

