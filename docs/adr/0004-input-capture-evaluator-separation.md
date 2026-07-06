# ADR-0004: Separate Input Capture from Trigger Evaluation

## Status

Accepted

## Context

`GlobalHookService` installed the native hook, translated messages, evaluated triggers, and decided interception in one class.

## Decision

Split into `RawInputSource` (native hook adapter) and `TriggerEvaluator` (domain trigger loop). Interception policy is provided by `IInputInterceptionPolicy`.

## Consequences

- Triggers can be unit-tested with synthetic `TriggerEvent`s.
- The native hook adapter contains no policy.

## Related

- `CONTEXT.md` — `RawInputSource`, `TriggerEvaluator`
