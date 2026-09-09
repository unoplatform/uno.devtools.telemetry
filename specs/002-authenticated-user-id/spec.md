# Feature Specification: Runtime-Mutable Authenticated User Id

**Feature Branch:** dev/mara/global-userid
**Created:** 2026-08-20
**Status:** Implemented
**Spec Version:** 1.1
**Implementation Date:** 2026-08-20

## Overview

Telemetry emitted by this package is attributed to an anonymized machine id only. Downstream
consumers with signed-in users need to attribute telemetry to the authenticated account so that
client-side events can be correlated with server-side telemetry keyed on the same account id.
Consumers have had to wrap `ITelemetry` in decorators that merge a user id into every call's
properties, duplicating the package's scope-merge mechanics, leaving the id spoofable/strippable
through the per-event properties dictionary, and producing per-consumer divergence.

This feature adds a **runtime-mutable authenticated user id** owned by the package, with a single
entry point, the static `TelemetryUserContext.AuthenticatedUserId` property:

- Set on sign-in, cleared on sign-out, unlike the constructor-fixed common properties.
- **Process-wide ambient state**: once set, every `ITelemetry` instance in the process stamps it,
  including instances the consumer never holds directly (open-generic `ITelemetry<T>` DI,
  `TelemetryFactory.Create<T>()`).
- **Captured at the tracking call**: each item carries the value current when `TrackEvent` /
  `TrackException` was called, not the value current when the asynchronous pipeline sends it.
- Emitted as the Application Insights-native `ai.user.authUserId` context tag (the
  `user_AuthenticatedId` column), immune to per-event property manipulation by construction.
- Identical behavior on desktop (Application Insights SDK) and WebAssembly (REST envelope).
- **Purely additive public API**: no change to `ITelemetry` or any other existing public type, so
  external implementers and callers are unaffected. (This deviates from the originating issue's
  sketch, which suggested a member on `ITelemetry`; see Key Design Decisions.)

## User Scenarios & Testing

### P1: Sign-In / Sign-Out Attribution

A consumer application sets `TelemetryUserContext.AuthenticatedUserId = accountId` when its user
signs in. Every subsequent event and exception, from any `ITelemetry` instance in the process,
carries the id. On sign-out the consumer sets the property to `null`; subsequent items carry no id.
No `ITelemetry` instance is needed to set or clear the value, so sign-in code has no dependency on
telemetry resolution or lifetime.

### P2: Process-Wide Coverage

A host registers telemetry via the open-generic `AddTelemetry()` DI registration. Library code
resolves `ITelemetry<TheirContext>` instances the host never sees. Setting
`TelemetryUserContext.AuthenticatedUserId` once covers all of them, including instances created
after the value was set.

### Edge Cases

- **EC-1**: The value changes while items are in flight. Every sink captures the ambient value when
  the tracking call is made: the desktop `Telemetry` implementation reads it in `TrackEvent` /
  `TrackException` before queuing the track task (the queue is gated on background initialization,
  so an item can drain long after the call), threads it into the queued task and stamps it onto
  the item's own context; the WebAssembly sender receives the same captured value and writes it
  into the envelope tags; `ThreadBlockingTrackEvent` and `FileTelemetry` read it synchronously at
  the call. Consequently an item tracked before sign-in is never attributed to a user who signed
  in while it was queued, and an item tracked while signed in keeps that attribution if the user
  signs out before it drains. The failure direction is "unattributed", never "wrong user".
- **EC-2**: Null, empty, whitespace, or over-long values normalize to `null`; the tag/field is then
  absent, never empty and never a truncated prefix (a prefix could collide with another account
  id). The cap is `TelemetryUserContext.MaxAuthenticatedUserIdLength`, which carries the rationale
  and is pinned to the Application Insights tag limit by a unit test.
- **EC-3**: A value set before the background initialization of a `Telemetry` instance completes
  still applies to all items tracked after it was set (the value is read per tracking call, not
  captured at construction).
- **EC-4**: Multiple products hosting telemetry instances in one process share the single ambient
  value; last writer wins. Intended: the id identifies the user, not the product.
- **EC-5**: The value is per process and starts as `null`. The package reads no environment
  variable for it. A parent that wants a child process attributed passes the id through its own
  start-info contract and the child assigns it at startup.

## Requirements

### Functional Requirements

- **FR-001**: The package MUST hold the authenticated user id as process-wide ambient state,
  mutable at any time (`TelemetryUserContext.AuthenticatedUserId`).
- **FR-002**: Setting and clearing the value MUST NOT require an `ITelemetry` instance; the static
  entry point is the single write path.
- **FR-003**: Setting the value MUST affect every `ITelemetry` instance in the process, including
  instances created later.
- **FR-004**: The value MUST be clearable at runtime; items tracked after clearing MUST carry no
  authenticated user id.
- **FR-005**: Null, empty, or whitespace assignments MUST normalize to `null` (absent, never empty).
- **FR-006**: On desktop, each tracked event and exception MUST carry the value captured at the
  tracking call as the `ai.user.authUserId` context tag (`user_AuthenticatedId`), applied to the
  item's own context, never by mutating shared client context and never by reading the ambient
  store when the queued task drains.
- **FR-007**: On WebAssembly, the event and exception envelope `tags` MUST carry
  `ai.user.authUserId` when the captured value is set and MUST omit the key when it is not.
- **FR-008**: `FileTelemetry` MUST emit a top-level `AuthenticatedUserId` JSON field only when the
  value is set; when unset, its output MUST be byte-identical to previous versions for both the
  event and the exception shape.
- **FR-009**: The package MUST NOT seed the value from the environment or any other implicit
  source; the initial value is `null` until a consumer assigns it.
- **FR-010**: Reads and writes MUST be safe from any thread.
- **FR-011**: Decorators and adapters (`ScopedTelemetry`, `TelemetryAdapter<T>`, the DI generic
  factory) MUST require no changes; stamping happens at the emitting sink, independent of any
  wrapper.
- **FR-012**: The machine id (`ai.user.id` / `Context.User.Id`) MUST be unchanged by this feature.
- **FR-013**: The implementation MUST compile for all existing TFMs, including netstandard2.0.
- **FR-014**: The public API change MUST be purely additive: no new or changed members on
  `ITelemetry` or any other existing public type, so consumers and external implementers compile
  unchanged.
- **FR-015**: `UNO_PLATFORM_TELEMETRY_OPTOUT=true` MUST govern every sink selection, including the
  `UNO_PLATFORM_TELEMETRY_FILE` lane, so the id is never written to disk while the opt-out is set.

### Key Entities

- **`TelemetryUserContext`**: public static holder of the ambient authenticated user id (volatile
  field; blank- and length-normalizing setter; no implicit seed).
- **`Telemetry` capture**: the desktop implementation captures the value in `TrackEvent` /
  `TrackException`, threads it through the queued track task and stamps it onto each item's own
  `Context.User.AuthenticatedUserId` immediately before handing the item to the
  `TelemetryClient`.
- **`WasmHttpSender`**: receives the captured value as a parameter and conditionally adds the tag.
- **`TelemetryDiagnostics`**: internal best-effort `Debug` / `Trace` writer shared by the setter,
  the WASM sender's failure paths and the desktop initialization failure path; never throws.
- **`TelemetryEnvironment`**: internal single reader for the opt-out and file-redirect variables,
  used by both sink selection sites.
- **`ai.user.authUserId`**: the Application Insights context tag carrying the value
  (`user_AuthenticatedId` in analytics).

## Success Criteria

### Measurable Outcomes

- **SC-001**: With a signed-in user, desktop and WASM items show `user_AuthenticatedId` in
  Application Insights; the value equals the consumer-supplied account id.
- **SC-002**: After sign-out, newly tracked items carry no `user_AuthenticatedId`.
- **SC-003**: All pre-existing unit tests and WASM runtime tests pass unchanged in behavior.
- **SC-004**: `FileTelemetry` output is byte-identical to the previous release when no user is
  authenticated, for both the event and the exception shape.
- **SC-005**: New unit tests (ambient store, desktop capture-at-call through an in-process channel,
  WASM envelope, file output, DI end-to-end, opt-out across both selection sites, diagnostics) and
  the new WASM runtime test pass in CI.

## Out of Scope

- Per-instance (non-ambient) authenticated user ids.
- Cross-process propagation of the id in either direction; a parent and its children are separate
  processes with separate ambient values.
- Consent, privacy policy, and PII handling; the consumer decides what identifier is appropriate
  to send and owns the disclosure.
- Retroactive stamping or un-stamping of items already persisted to the offline channel.

## Dependencies

- Microsoft.ApplicationInsights 2.20 (`EventTelemetry` / `ExceptionTelemetry` construction and the
  per-item `TelemetryContext`).

## Risks & Mitigations

- **Shared identity in multi-product processes** (EC-4): documented; the id identifies the user.
- **Assembly load contexts**: the static store is per assembly load context. Hosts that load the
  package into more than one context (plugin systems, MSBuild tasks, IDE extensions) must assign
  the value in each; "process-wide" in this document assumes a single load of the package.
- **Ambient state in tests**: unit tests that set the value are `[DoNotParallelize]` and reset it in
  `[TestCleanup]`; the WASM runtime test resets it in a `finally` block. Parallel tests therefore
  never observe a non-null id.
- **Unverified attribution**: the value is client-asserted; any in-process code can set the store.
  The threat this design removes is *per-event* tampering (stripping or overriding the id through
  the properties dictionary of a single event), not in-process spoofing. Server-side consumers MUST
  NOT treat `user_AuthenticatedId` as verified identity for authorization, abuse, or billing
  decisions.
- **Deferred transmission**: the desktop offline channel persists unsent items under
  `Path.GetTempPath()/.uno/telemetry` and retransmits them in a later run. Tags are serialized into
  the persisted item, so a retransmitted item carries the identity that was valid when it was
  tracked, even if that user has since signed out or another user has signed in, and downgrading
  the package does not stop them. Consumers with data-deletion obligations for that at-rest window
  delete the pending files in that directory before restarting.
- **Diagnostics reach nobody by default**: `Debug` output is compiled out of the packaged Release
  build and `Trace` output goes only to a listener the host registers. A host that wants the
  normalize-to-null or initialization-failure lines must add a `TraceListener` before the value is
  first assigned (see docs/usage.md, "Operational suppression and diagnostics").

## Implementation Summary

### Files Created

- `src/Uno.DevTools.Telemetry/TelemetryUserContext.cs`
- `src/Uno.DevTools.Telemetry/TelemetryDiagnostics.cs`
- `src/Uno.DevTools.Telemetry/TelemetryEnvironment.cs`
- `src/Uno.DevTools.Telemetry.Tests/TelemetryUserContextTests.cs` (includes the capturing and
  throwing `TraceListener` doubles and the diagnostics tests)
- `src/Uno.DevTools.Telemetry.Tests/TelemetryAuthenticatedUserCaptureTests.cs` (includes the
  in-process capturing channel)
- `src/Uno.DevTools.Telemetry.Tests/WasmHttpSenderAuthenticatedUserTests.cs`
- `src/Uno.DevTools.Telemetry.Tests/FileTelemetryAuthenticatedUserTests.cs`
- `src/Uno.DevTools.Telemetry.Tests/TempFiles.cs` (shared temp-file fixture)

### Files Modified

- `src/Uno.DevTools.Telemetry/Telemetry.cs`: captures the value in `TrackEvent` /
  `TrackException`, threads it through `TrackEventTask` / `TrackExceptionTask`, stamps it onto the
  item's own context; internal constructor overload accepting an `ITelemetryChannel` as a test seam;
  initialization failure logs the full exception through `TelemetryDiagnostics`.
- `src/Uno.DevTools.Telemetry/FileTelemetry.cs`: one private record per shape with the
  `AuthenticatedUserId` property alone marked `WhenWritingNull`.
- `src/Uno.DevTools.Telemetry/WasmHttpSender.cs`: `SendEventAsync` / `SendExceptionAsync` and the
  envelope builders take the captured id; `CreateTags` no longer reads the ambient store;
  `LogFailure` routes through `TelemetryDiagnostics`; sender methods never throw into their
  discarded fire-and-forget tasks.
- `src/Uno.DevTools.Telemetry/TelemetryServiceCollectionExtensions.cs`,
  `src/Uno.DevTools.Telemetry/ITelemetry.T.cs`: sink selection honours the opt-out through
  `TelemetryEnvironment.GetFileTelemetryPath()`.
- `src/Uno.DevTools.Telemetry.Tests/TelemetryGenericDiTests.cs`: DI end-to-end and opt-out coverage.
- `src/Uno.DevTools.Telemetry.Tests/WasmHttpSenderTests.cs`: reflective wrapper passes the new
  trailing parameter.
- `src/Uno.DevTools.Telemetry.Tests/FileTelemetryTests.cs`, `ScopedTelemetryTests.cs`,
  `ExceptionTelemetryTests.cs`: shared `TempFiles` fixture.
- `src/Uno.DevTools.Telemetry.WasmTests/.../TelemetryWasmTests.cs`: set/track/clear runtime test.
- `docs/usage.md`, `AGENTS.md`.

### Key Design Decisions

- **Static-only entry point, no `ITelemetry` member (deviation from the originating issue)**: the
  issue sketched a runtime-mutable member on `ITelemetry`, but stamping happens at the emitting
  sinks, which read the ambient store directly. A member on the interface would be a pass-through
  mirror adding no capability while (a) breaking every external implementer (netstandard2.0 rules
  out default interface members) and (b) inviting hand-written fakes to implement it per-instance
  and silently diverge from the process-global production semantics. The static keeps the public
  API purely additive (FR-014) and keeps sign-in code free of any telemetry-instance dependency.

- **Capture at the tracking call, not at drain**: the desktop track queue is rooted at the
  background initialization task and drains asynchronously, so reading the ambient store when the
  queued task runs (for example through an `ITelemetryInitializer`) can attribute an item to a
  user who signed in after the call was made. Since the purpose of the feature is server-side
  correlation on the account id, misattribution is the worse failure. The value is therefore read
  in the public tracking methods and threaded through the private task methods and the WASM sender
  as an explicit parameter; the arity of those internal methods changed and the positional tests
  were updated accordingly.

- **Per-item stamping over client-context mutation**: the lock-free track chaining does not fully
  serialize under contention (failed CAS exchanges leave orphaned continuations running
  concurrently with the winning chain), so mutating `TelemetryClient.Context` per event would race.
  The captured id is written to each item's own `Context.User.AuthenticatedUserId`.

- **No environment seed**: an earlier revision seeded the store from an environment variable at
  type initialization. It was the only variable in the package that put user data into the
  payload, it did so unconditionally and for every descendant process, the originating issue did
  not ask for it, and reading it inside a static initializer made a throwing `TraceListener` a
  type-poisoning hazard. It was removed; parent/child propagation is the consumer's contract (EC-5).

- **Static ambient store over per-instance state**: the only mechanism that reaches instances the
  consumer never holds (requirement FR-003).

- **Records with a single `WhenWritingNull` property in `FileTelemetry`**: a serializer-wide
  `DefaultIgnoreCondition` would also drop the `Properties` and `Measurements` nulls that previous
  versions emit, breaking byte-identity. Declaration order preserves the field order; both shapes
  are guarded by byte-identity snapshots with a pinned `FakeTimeProvider`.

- **Opt-out governs the file lane**: `UNO_PLATFORM_TELEMETRY_OPTOUT` was read only by the
  `Telemetry` constructor, so an organisation-wide opt-out combined with a developer's
  `UNO_PLATFORM_TELEMETRY_FILE` still wrote the id to disk in cleartext. Both selection sites now
  go through one helper that returns no file path when the opt-out is set.

### Testing Status

- Unit suite passing locally (`dotnet test src/Uno.DevTools.Telemetry.Tests/...`), including the
  ambient-store tests (with a capturing `TraceListener` proving the diagnostics carry the length and
  never the value), the desktop capture-at-call tests through an injected in-process channel, the
  WASM envelope tests with an explicit id, the `FileTelemetry` byte-identity snapshots for both
  shapes, the DI end-to-end and opt-out tests, and the throwing-listener diagnostics tests.
- WASM runtime tests: the set/track/clear test is listed in AGENTS.md under "WASM Runtime Tests",
  run in CI via `uno-runtimetests-wasm`. It asserts round-trip and does-not-throw only; envelope
  tags are covered by the unit tests, since `CreateEventEnvelope` is `internal` and the WASM test
  project has no `InternalsVisibleTo`.

## References

- `specs/001-wasm-compat/spec.md`: structure and WASM sender background.
- Application Insights context tags: `ai.user.id` (anonymous), `ai.user.authUserId` (authenticated).
