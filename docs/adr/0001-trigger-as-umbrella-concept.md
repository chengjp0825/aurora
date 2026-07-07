# ADR-0001: Trigger as the Umbrella Concept

## Status

Accepted

## Context

MyQuicker wakes its menu in response to user input. Two physically different kinds of input exist:

- **Instantaneous hardware input** — pressing a mouse button (middle, X1, X2) or a keyboard shortcut.
- **Spatio-temporal pointer motion** — drawing a shape such as a circle while holding a button.

The codebase needed a top-level term for "anything that can wake the menu." The intuitive choice for many developers is **Gesture**, because the circular motion is the most distinctive wake mechanism.

However, "gesture" in common UI/UX language implies movement over time and space. Calling a single mouse-button click a "gesture" stretches the term and makes the domain model semantically imprecise.

## Decision

We will use **Trigger** as the umbrella concept for any wake action.

`Trigger` is an abstraction evaluated against input state. It returns a `TriggerMatchResult` carrying a `WakeContext`. Two specializations exist:

- `ButtonTrigger` — instantaneous hardware input.
- `GestureTrigger` (also called `PathTrigger`) — pointer motion evaluated over time and space.

## Consequences

- **Positive:** The naming matches the physical input semantics. A click is not mislabeled as a gesture.
- **Positive:** The abstraction remains open for future input types such as keyboard chords, which fit naturally under `Trigger` but not under `Gesture`.
- **Positive:** The wake pipeline is polymorphic: `Trigger.Evaluate()` is uniform regardless of input type.
- **Negative:** Developers must remember to distinguish `ButtonTrigger` from `GestureTrigger` instead of relying on a single overloaded "gesture" term.

## Alternatives Considered

- **Gesture as umbrella.** Rejected because it conflates instantaneous inputs with spatio-temporal motion and would force awkward terminology such as "button gesture."

## Related

- `CONTEXT.md` — `Trigger`, `ButtonTrigger`, `GestureTrigger`, `TriggerMatchResult`, `WakeContext`
