# ADR-0002: Separate Settings DTO from Runtime Objects

## Status

Accepted

## Context

aurora persists user configuration to `settings.json`. The configuration describes:

- Which inputs wake the menu (`TriggerBinding`).
- Which commands exist (`Command` data).
- How the menu is organized (`MenuGroup` / `Action`).
- General application preferences.

Runtime objects are more than serialized data. A `Trigger` may hold a global mouse hook or timer state. A `Command` may need to call system services or hold unmanaged resources. A `MenuGroup` is a presentation construct.

If runtime objects also served as the persistence schema, serialization would have to ignore runtime-only fields, and constructors would need to satisfy serializer constraints while also accepting service dependencies.

## Decision

We will introduce a top-level `Settings` DTO that is the only object serialized to and deserialized from disk. Runtime objects are built from `Settings` through factories and stores, never persisted directly.

The build chain is:

1. `SettingsManager` loads and migrates `Settings` from `settings.json`.
2. `BuiltInCommandProvider` registers immutable system commands.
3. `UserCommandStore` registers user-defined commands from `Settings.Commands`.
4. `TriggerFactory` builds runtime `Trigger`s from `Settings.TriggerBindings`.
5. `MenuPresenter` consumes `Settings.MenuGroups`.

## Consequences

- **Positive:** Runtime objects can hold service references, unmanaged handles, and internal state without polluting the persistence schema.
- **Positive:** Schema migrations are isolated to `SettingsManager` and the `Settings` DTO; factories always receive a normalized current-schema object.
- **Positive:** Commands and triggers remain stateless pure data objects and are easy to unit test with mocked `CommandContext`.
- **Negative:** There is an extra layer of mapping code between the persisted DTO and runtime objects.
- **Negative:** Developers must remember to update both the DTO and the factory when adding a new configurable concept.

## Alternatives Considered

- **Runtime objects serialize themselves.** Rejected because it couples persistence format to class structure, forces public setters and parameterless constructors on runtime objects, and makes migration logic leak into every command and trigger type.

## Related

- `CONTEXT.md` — `Settings`, `SettingsManager`, `TriggerBinding`, `TriggerFactory`, `UserCommandStore`, `BuiltInCommandProvider`
