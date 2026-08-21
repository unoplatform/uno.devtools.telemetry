# Feature Specification: Runtime-Mutable Authenticated User Id

**Feature Branch:** dev/mara/global-userid
**Created:** 2026-08-20
**Status:** Implemented
**Spec Version:** 1.0
**Implementation Date:** 2026-08-20

## Overview

Telemetry emitted by this package is attributed to an anonymized machine id only. Downstream
consumers with signed-in users need to attribute telemetry to the authenticated account so that
client-side events can be correlated with server-side telemetry keyed on the same account id.
Consumers have had to wrap `ITelemetry` in decorators that merge a user id into every call's
properties — duplicating the package's scope-merge mechanics, leaving the id spoofable/strippable
through the per-event properties dictionary, and producing per-consumer divergence.

This feature adds a **runtime-mutable authenticated user id** owned by the package, with a single
entry point — the static `TelemetryUserContext.AuthenticatedUserId` property:

- Set on sign-in, cleared on sign-out — unlike the constructor-fixed common properties.
- **Process-wide ambient state**: once set, every `ITelemetry` instance in the process stamps it,
  including instances the consumer never holds directly (open-generic `ITelemetry<T>` DI,
  `TelemetryFactory.Create<T>()`).
- Emitted as the Application Insights-native `ai.user.authUserId` context tag (the
  `user_AuthenticatedId` column), immune to per-event property manipulation by construction.
- Identical behavior on desktop (Application Insights SDK) and WebAssembly (REST envelope).
- **Purely additive public API** — no change to `ITelemetry` or any other existing public type, so
  external implementers and callers are unaffected. (This deviates from the originating issue's
  sketch, which suggested a member on `ITelemetry`; see Key Design Decisions.)

## User Scenarios & Testing

### P1: Sign-In / Sign-Out Attribution

A consumer application sets `TelemetryUserContext.AuthenticatedUserId = accountId` when its user
signs in. Every subsequent event and exception — from any `ITelemetry` instance in the process —
carries the id. On sign-out the consumer sets the property to `null`; subsequent items carry no id.
No `ITelemetry` instance is needed to set or clear the value, so sign-in code has no dependency on
telemetry resolution or lifetime.

### P2: Process-Wide Coverage

A host registers telemetry via the open-generic `AddTelemetry()` DI registration. Library code
resolves `ITelemetry<TheirContext>` instances the host never sees. Setting
`TelemetryUserContext.AuthenticatedUserId` once covers all of them, including instances created
after the value was set.

### P3+: Parent→Child Process Seeding

A tool that spawns child processes sets the `UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID`
environment variable on the child's start info. The child's telemetry is attributed to the same
account from its first event, with no IPC.

### Edge Cases

- **EC-1**: The value changes while events are in flight — the desktop `Telemetry` implementation
  stamps each item when its queued track task **drains** (the queue is gated on background
  initialization), so items enqueued before an identity change carry the value current at drain
  time; `FileTelemetry` stamps at call time. By design: the accepted failure directions are
  "unattributed" (sign-out before drain) and, on a same-process identity switch, attribution to
  the identity active at drain — never a value that was not legitimately set.
- **EC-2**: Null, empty, whitespace, or over-long (> 1024 characters, the Application Insights tag
  limit) values normalize to `null` — the tag/field is then absent, never empty and never a
  truncated prefix (a prefix could collide with another account id).
- **EC-3**: A value set before the background initialization of a `Telemetry` instance completes
  still applies to all items emitted after initialization (the value is read per item, not captured
  at construction).
- **EC-4**: Multiple products hosting telemetry instances in one process share the single ambient
  value; last writer wins. Intended — the id identifies the user, not the product.
- **EC-5**: The environment seed is read once per process — later environment changes, and a parent
  process's sign-out, do not propagate to already-running processes. Explicit assignment always
  wins over the seed.

## Requirements

### Functional Requirements

- **FR-001**: The package MUST hold the authenticated user id as process-wide ambient state,
  mutable at any time (`TelemetryUserContext.AuthenticatedUserId`).
- **FR-002**: Setting and clearing the value MUST NOT require an `ITelemetry` instance — the static
  entry point is the single write path.
- **FR-003**: Setting the value MUST affect every `ITelemetry` instance in the process, including
  instances created later.
- **FR-004**: The value MUST be clearable at runtime; items emitted after clearing MUST carry no
  authenticated user id.
- **FR-005**: Null, empty, or whitespace assignments MUST normalize to `null` (absent, never empty).
- **FR-006**: On desktop, each tracked event and exception MUST carry the value as the
  `ai.user.authUserId` context tag (`user_AuthenticatedId`), applied per item via an
  `ITelemetryInitializer` — never by mutating shared client context.
- **FR-007**: On WebAssembly, the event and exception envelope `tags` MUST carry
  `ai.user.authUserId` when the value is set and MUST omit the key when it is not.
- **FR-008**: `FileTelemetry` MUST emit a top-level `AuthenticatedUserId` JSON field only when the
  value is set; when unset, its output MUST be byte-identical to previous versions.
- **FR-009**: The initial value MUST be seeded once from the
  `UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID` environment variable; explicit assignment wins.
- **FR-010**: Reads and writes MUST be safe from any thread.
- **FR-011**: Decorators and adapters (`ScopedTelemetry`, `TelemetryAdapter<T>`, the DI generic
  factory) MUST require no changes — stamping happens at the emitting sink, independent of any
  wrapper.
- **FR-012**: The machine id (`ai.user.id` / `Context.User.Id`) MUST be unchanged by this feature.
- **FR-013**: The implementation MUST compile for all existing TFMs, including netstandard2.0.
- **FR-014**: The public API change MUST be purely additive — no new or changed members on
  `ITelemetry` or any other existing public type, so consumers and external implementers compile
  unchanged.

### Key Entities

- **`TelemetryUserContext`** — public static holder of the ambient authenticated user id
  (volatile field; blank-normalizing setter; one-time environment seed).
- **`AuthenticatedUserTelemetryInitializer`** — internal `ITelemetryInitializer` stamping the
  ambient value onto each item's own context at track time.
- **`ai.user.authUserId`** — the Application Insights context tag carrying the value
  (`user_AuthenticatedId` in analytics).

## Success Criteria

### Measurable Outcomes

- **SC-001**: With a signed-in user, desktop and WASM items show `user_AuthenticatedId` in
  Application Insights; the value equals the consumer-supplied account id.
- **SC-002**: After sign-out, newly emitted items carry no `user_AuthenticatedId`.
- **SC-003**: All pre-existing unit tests and WASM runtime tests pass unchanged.
- **SC-004**: `FileTelemetry` output is byte-identical to the previous release when no user is
  authenticated.
- **SC-005**: New unit tests (ambient store, initializer, WASM envelope, file output, DI
  end-to-end) and the new WASM runtime test pass in CI.

## Out of Scope

- Per-instance (non-ambient) authenticated user ids.
- Cross-process propagation of sign-out (the environment seed is read once; children clear via
  their own assignment).
- Consent, privacy policy, and PII handling — the consumer decides what identifier is appropriate
  to send and owns the disclosure.
- Retroactive stamping of items already persisted to the offline channel.

## Dependencies

- Microsoft.ApplicationInsights 2.20 (`ITelemetryInitializer`, per-item context initialization at
  `Track()` time).

## Risks & Mitigations

- **Shared identity in multi-product processes** (EC-4): documented; the id identifies the user.
- **Initializer ordering**: registered before the `TelemetryClient` is created, so no item can be
  tracked ahead of it within an enabled instance.
- **Ambient state in tests**: all tests that set the value are `[DoNotParallelize]` and reset it in
  both `[TestInitialize]` and `[TestCleanup]` (the environment seed can make the startup value
  non-null), so parallel tests never observe a non-null id.
- **Unverified attribution**: the value is client-asserted — any in-process code can set the store,
  and any parent process controls the environment seed. The threat this design removes is
  *per-event* tampering (stripping or overriding the id through the properties dictionary of a
  single event), not in-process spoofing. Server-side consumers MUST NOT treat
  `user_AuthenticatedId` as verified identity for authorization, abuse, or billing decisions.
- **Deferred transmission**: the desktop offline channel persists unsent items to disk and
  retransmits them in a later run. Tags are serialized into the persisted item, so a retransmitted
  item carries the identity that was valid when it was emitted — even if that user has since signed
  out or another user has signed in. Data-deletion obligations for that at-rest window are owned by
  the consumer.

## Implementation Summary

### Files Created

- `src/Uno.DevTools.Telemetry/TelemetryUserContext.cs`
- `src/Uno.DevTools.Telemetry/AuthenticatedUserTelemetryInitializer.cs`
- `src/Uno.DevTools.Telemetry.Tests/TelemetryUserContextTests.cs`
- `src/Uno.DevTools.Telemetry.Tests/AuthenticatedUserTelemetryInitializerTests.cs`
- `src/Uno.DevTools.Telemetry.Tests/WasmHttpSenderAuthenticatedUserTests.cs`
- `src/Uno.DevTools.Telemetry.Tests/FileTelemetryAuthenticatedUserTests.cs`

### Files Modified

- `src/Uno.DevTools.Telemetry/Telemetry.cs` — initializer registration in `InitializeTelemetry`;
  internal test seam for the pipeline configuration.
- `src/Uno.DevTools.Telemetry/FileTelemetry.cs` — conditional top-level field.
- `src/Uno.DevTools.Telemetry/WasmHttpSender.cs` — shared `CreateTags` helper with conditional
  `ai.user.authUserId`; envelope builders made internal for tests.
- `src/Uno.DevTools.Telemetry.Tests/TelemetryGenericDiTests.cs` — DI end-to-end coverage.
- `src/Uno.DevTools.Telemetry.WasmTests/.../TelemetryWasmTests.cs` — set/track/clear runtime test.
- `docs/usage.md`, `AGENTS.md`.

### Key Design Decisions

- **Static-only entry point, no `ITelemetry` member (deviation from the originating issue)**: the
  issue sketched a runtime-mutable member on `ITelemetry`, but stamping happens at the emitting
  sinks, which read the ambient store directly — a member on the interface would be a pass-through
  mirror adding no capability while (a) breaking every external implementer (netstandard2.0 rules
  out default interface members) and (b) inviting hand-written fakes to implement it per-instance
  and silently diverge from the process-global production semantics. The static keeps the public
  API purely additive (FR-014) and keeps sign-in code free of any telemetry-instance dependency.

- **`ITelemetryInitializer` over client-context mutation**: the lock-free track chaining does not
  fully serialize under contention (failed CAS exchanges leave orphaned continuations running
  concurrently with the winning chain), so mutating `TelemetryClient.Context` per event would race.
  Initializers run synchronously on the tracking thread against each item's own context.
- **Static ambient store over per-instance state**: the only mechanism that reaches instances the
  consumer never holds (requirement FR-003).
- **Two anonymous shapes in `FileTelemetry`** instead of a nullable field: keeps the unset output
  byte-identical without touching the serializer options.
- **No method arity changes in `WasmHttpSender`**: existing tests invoke `SendEventAsync` /
  `SendExceptionAsync` reflectively with positional arguments.

### Testing Status

- Unit suite passing locally (`dotnet run --project src/Uno.DevTools.Telemetry.Tests/...`),
  including the new ambient-store, initializer (incl. a registration guard on the pipeline wiring),
  WASM-envelope, FileTelemetry (incl. a byte-identity snapshot with a pinned clock), and DI
  end-to-end tests.
- WASM runtime tests: 9 total (1 new), run in CI via `uno-runtimetests-wasm`.
- Known coverage gap (accepted): the static-field seeding wire-up (`= GetEnvironmentSeed()`) is
  only covered through direct `GetEnvironmentSeed()` tests — the field initializer itself runs once
  per process at type init, which an in-process test cannot re-arm. Full coverage would need a
  child-process integration test.

## References

- `specs/001-wasm-compat/spec.md` — structure and WASM sender background.
- Application Insights context tags: `ai.user.id` (anonymous), `ai.user.authUserId` (authenticated).
