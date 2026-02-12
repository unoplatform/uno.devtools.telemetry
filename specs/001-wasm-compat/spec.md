# Feature Specification: WebAssembly Compatibility for Uno.DevTools.Telemetry

**Feature Branch:** wasm-compatibility
**Created:** 2026-02-12
**Status:** Implemented
**Spec Version:** 1.0
**Implementation Date:** 2026-02-12

## Overview

Enable the Uno.DevTools.Telemetry package to function correctly when running on WebAssembly/Browser platforms. Currently, the package fails during initialization on WASM due to incompatibilities between the Microsoft Application Insights SDK and the browser execution environment.

**Problem Statement:**
When Uno Platform tools run in a WebAssembly environment (e.g., browser-based applications), the telemetry package throws runtime errors during static initialization of the Application Insights SDK. This occurs because the SDK attempts to use:
- `System.Threading.ThreadLocal<T>` (not supported in WASM's single-threaded environment)
- `System.IO.MemoryMappedFiles` (not available in browser sandbox)
- `Thread.Yield()` and `AutoResetEvent` (threading primitives unavailable in WASM)

**Business Impact:**
Without WASM support, telemetry data from browser-based Uno Platform applications is lost, creating a significant blind spot in understanding tool usage, performance issues, and user behavior on the web platform.

---

## User Scenarios & Testing

### P1: Developer Using Telemetry Package on WASM

**User Story:**
As an Uno Platform developer building a WASM application, I want to use the Uno.DevTools.Telemetry package so that I can collect usage analytics and error telemetry from my browser-based application without runtime failures.

**User Journey:**
1. Developer creates an Uno Platform application targeting WebAssembly
2. Developer adds `Uno.DevTools.Telemetry` package reference
3. Developer initializes telemetry: `services.AddTelemetry(instrumentationKey, "MyApp")`
4. Application runs in browser without errors
5. Telemetry events are successfully sent to Application Insights
6. Developer can view telemetry data in Application Insights portal

**Priority Justification:**
Critical functionality - the package must work on all supported Uno Platform targets including WASM. Current failure blocks all telemetry collection from browser deployments.

**Testing Approach:**
- Create minimal Uno Platform WASM app
- Initialize telemetry with test instrumentation key
- Track events and exceptions
- Verify no console errors in browser DevTools
- Verify HTTP POST requests to Application Insights endpoint
- Verify telemetry appears in Application Insights portal

**Acceptance Scenarios:**

**Given** an Uno Platform WebAssembly application
**When** I initialize the telemetry package with `new Telemetry(key, prefix, assembly)`
**Then** no runtime exceptions occur during initialization
**And** the telemetry instance is in enabled state

**Given** telemetry is initialized on WASM
**When** I call `TrackEvent("TestEvent", properties, measurements)`
**Then** the event is queued for transmission
**And** an HTTP POST request is made to the Application Insights endpoint
**And** no threading errors occur

**Given** telemetry is initialized on WASM
**When** I call `TrackException(exception, properties, measurements, severity)`
**Then** the exception is formatted and sent to Application Insights
**And** severity level is correctly mapped
**And** no runtime errors occur

---

### P2: CI Pipeline Validation

**User Story:**
As a package maintainer, I want automated CI tests that validate WASM compatibility so that regressions are caught before release.

**User Journey:**
1. Developer makes changes to the telemetry package
2. Developer creates a pull request
3. CI pipeline automatically builds the package
4. CI pipeline runs WASM runtime tests using Uno.UI.RuntimeTests
5. Tests execute in actual browser environment via Playwright/Puppeteer
6. Test results are reported in the PR
7. If tests fail, PR cannot be merged

**Priority Justification:**
High priority - prevents regressions and ensures ongoing WASM support. Without automated testing, WASM compatibility could break silently.

**Testing Approach:**
- Create Uno Platform test project with Uno.UI.RuntimeTests
- Write runtime tests that exercise telemetry on WASM
- Integrate into CI pipeline (.github/workflows or Azure Pipelines)
- Run on every PR and merge to main

**Acceptance Scenarios:**

**Given** a pull request with package changes
**When** CI pipeline executes
**Then** WASM runtime tests are executed in browser environment
**And** test results are reported to the PR
**And** failing tests block merge

**Given** WASM runtime tests are running
**When** tests initialize telemetry on WASM
**Then** no threading exceptions occur
**And** no SDK initialization failures occur

---

### P3+: Multi-Platform Consistency

**User Story:**
As a developer using telemetry across multiple platforms, I want consistent API behavior whether running on desktop, mobile, or web so that my code works identically everywhere.

**User Journey:**
1. Developer writes telemetry code once
2. Code targets netstandard2.0 for broad compatibility
3. Application runs on Windows, Linux, macOS, iOS, Android, and WebAssembly
4. Telemetry API behaves identically on all platforms
5. Events appear in Application Insights with platform-appropriate metadata

**Priority Justification:**
Important for developer experience and code portability, but lower priority than making WASM work at all.

**Testing Approach:**
- Run same telemetry code on all supported platforms
- Verify identical API surface
- Verify appropriate metadata for each platform (OS, device info, etc.)

**Acceptance Scenarios:**

**Given** the same telemetry initialization code
**When** running on desktop (Windows/macOS/Linux)
**Then** Application Insights SDK is used
**And** file-based persistence is available

**Given** the same telemetry initialization code
**When** running on WebAssembly
**Then** direct HTTP sender is used
**And** in-memory queuing is used
**And** the same ITelemetry interface is exposed

---

### Edge Cases

#### EC-1: Network Failure on WASM
**Scenario:** Browser has no network connectivity or Application Insights endpoint is unreachable
**Expected Behavior:** Telemetry failures should be silent and not crash the application. Events may be dropped if queue is full.

#### EC-2: CORS Issues
**Scenario:** Application deployed to domain that has CORS restrictions
**Expected Behavior:** HTTP requests should include proper headers. CORS failures should be logged but not throw exceptions.

#### EC-3: Large Event Payloads
**Scenario:** Developer tracks event with very large property values
**Expected Behavior:** Payload should be serialized successfully. If too large for HTTP request, appropriate error handling occurs.

#### EC-4: Rapid Event Generation
**Scenario:** Application tracks hundreds of events in quick succession
**Expected Behavior:** Events are queued in memory. Oldest events dropped if queue limit (50) is exceeded. Batching occurs to minimize HTTP requests.

#### EC-5: Thread.Yield Compatibility
**Scenario:** Existing lock-free event chaining code calls Thread.Yield()
**Expected Behavior:** On WASM, Thread.Yield() is skipped to avoid potential issues. Event ordering is still guaranteed due to single-threaded execution.

---

## Requirements

### Functional Requirements

**FR-001:** The telemetry package MUST successfully initialize when running on WebAssembly/Browser platforms without throwing runtime exceptions.

**FR-002:** The telemetry package MUST detect WASM execution environment at runtime using `RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))` or equivalent.

**FR-003:** On WASM platforms, the package MUST bypass Application Insights SDK initialization to avoid threading and file I/O incompatibilities.

**FR-004:** On WASM platforms, the package MUST send telemetry directly to the Application Insights REST API endpoint (`https://dc.services.visualstudio.com/v2/track`) using HTTP POST.

**FR-005:** The telemetry payload format on WASM MUST match the Application Insights SDK format to ensure compatibility with the Application Insights service.

**FR-006:** On WASM platforms, the package MUST queue events in memory (maximum 50 events) instead of using file-based persistence.

**FR-007:** On non-WASM platforms, the package MUST continue using the existing Application Insights SDK implementation without changes.

**FR-008:** The `ITelemetry` interface MUST remain unchanged, providing 100% API compatibility across all platforms.

**FR-009:** The package MUST handle `Thread.Yield()` calls appropriately on WASM by skipping them to avoid runtime issues.

**FR-010:** On WASM platforms, machine ID generation MUST not rely on file I/O or `NetworkInterface` enumeration.

**FR-011:** The package MUST generate a persistent machine ID (GUID) for WASM platforms using browser localStorage, falling back to session-specific GUID if localStorage is unavailable.

**FR-012:** Telemetry events MUST include platform-appropriate metadata (OS="Browser", OS Version="WebAssembly") on WASM.

**FR-013:** The package MUST handle network failures gracefully on WASM without crashing the host application.

**FR-014:** The package MUST support both event tracking (`TrackEvent`) and exception tracking (`TrackException`) on WASM.

**FR-015:** Exception severity levels MUST be correctly mapped to Application Insights severity format on WASM.

**FR-016:** The `Flush()` and `FlushAsync()` methods MUST work correctly on WASM by ensuring queued events are sent.

**FR-017:** The package MUST continue targeting netstandard2.0, net8.0, and net9.0 without adding new target frameworks.

**FR-018:** CI pipeline MUST include automated WASM runtime tests using Uno.UI.RuntimeTests framework.

**FR-019:** WASM runtime tests MUST execute in actual browser environment to validate real-world compatibility.

**FR-020:** WASM runtime tests MUST run on every pull request and block merge if failing.

---

### Key Entities

#### TelemetryEnvironment
**Attributes:**
- `IsWasmBrowser` (bool) - Runtime detection of WASM execution environment
- Platform detection method using `RuntimeInformation.IsOSPlatform()`

**Relationships:**
- Determines which telemetry sender to use (Application Insights SDK vs. WasmHttpSender)

#### WasmHttpSender
**Attributes:**
- `InstrumentationKey` (string) - Application Insights key
- `EventNamePrefix` (string) - Prefix for all events
- `EndpointUrl` (string) - Application Insights REST endpoint
- `EventQueue` (Queue) - In-memory event queue (max 50 items)
- `HttpClient` (HttpClient) - For sending HTTP requests

**Relationships:**
- Used by `Telemetry` class when running on WASM
- Replaces `TelemetryClient` and `PersistenceChannel` on WASM

#### ApplicationInsightsEnvelope
**Attributes:**
- `name` (string) - Event or exception type identifier
- `time` (DateTime) - Event timestamp (ISO 8601 format)
- `iKey` (string) - Instrumentation key
- `tags` (Dictionary<string, string>) - User, session, device metadata
- `data` (object) - Event or exception payload

**Relationships:**
- JSON serialization format for HTTP POST to Application Insights
- Created by WasmHttpSender for each telemetry item

#### WasmTestProject
**Attributes:**
- Uno Platform app with Uno.UI.RuntimeTests
- References Uno.DevTools.Telemetry package
- Contains runtime test classes

**Relationships:**
- Executed by CI pipeline
- Validates WASM compatibility in browser environment

---

## Success Criteria

### Measurable Outcomes

**SC-001:** Zero runtime exceptions occur when initializing telemetry on WASM platform
**Measurement:** WASM runtime tests pass 100% of test runs

**SC-002:** Telemetry events from WASM applications appear in Application Insights portal within 5 minutes
**Measurement:** Manual verification with test application

**SC-003:** No breaking changes to public API surface (ITelemetry interface)
**Measurement:** All existing unit tests pass without modification

**SC-004:** Backward compatibility maintained for all non-WASM platforms (netstandard2.0, net8.0, net9.0)
**Measurement:** Existing functionality tests pass on Windows, macOS, Linux

**SC-005:** CI pipeline includes and executes WASM runtime tests on every PR
**Measurement:** CI configuration includes WASM test step, visible in PR checks

**SC-006:** WASM runtime tests execute in less than 5 minutes in CI pipeline
**Measurement:** CI build time metrics

**SC-007:** Telemetry HTTP requests from WASM use correct Application Insights payload format
**Measurement:** Network inspection shows properly formatted JSON matching SDK schema

**SC-008:** Package documentation includes WASM-specific guidance
**Measurement:** Spec document includes comprehensive WASM implementation details and design decisions

**SC-009:** Zero `Thread.Yield()` related errors on WASM platform
**Measurement:** Browser console shows no threading errors during runtime tests

**SC-010:** Machine ID generation works on WASM without file system access and persists across sessions
**Measurement:** `GetMachineIdAsync()` returns valid GUID on WASM platform, same GUID returned across page refreshes (via localStorage)

---

## Out of Scope

The following are explicitly **not** included in this specification:

1. **Persistent event storage across page refreshes on WASM** - Events stored in memory only, cleared on page reload. Machine ID is persisted using localStorage, but event queue is not.

2. **File-based telemetry (`FileTelemetry` class) on WASM** - No file system in browser environment. Only Application Insights telemetry works on WASM.

3. **Background threading on WASM** - Single-threaded environment, all operations are synchronous or use async/await.

4. **Adding new target frameworks** - Package continues to target netstandard2.0, net8.0, net9.0 only.

5. **Conditional compilation directives** - Runtime detection used instead of `#if` directives for platform branching.

6. **Changes to Application Insights SDK** - External dependency, not modified by this work.

7. **Browser-specific optimizations** - Initial implementation focuses on compatibility, not performance optimization for browser environment.

---

## Dependencies

- **Microsoft.ApplicationInsights** (v2.20.0) - Existing dependency, used on non-WASM platforms
- **System.Text.Json** - For JSON serialization on WASM
- **Uno.UI.RuntimeTests** - For WASM test infrastructure in CI
- **RuntimeInformation** (.NET) - For platform detection

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Application Insights API format changes | High | Use well-documented v2 REST API, extensive integration testing |
| CORS restrictions from some domains | Medium | Application Insights endpoint supports CORS, document requirements |
| Memory constraints in browser | Medium | Lower queue size (50 vs 100), aggressive batching |
| CI test infrastructure complexity | Medium | Use established Uno.UI.RuntimeTests framework |
| Performance overhead of runtime checks | Low | Platform detection cached at initialization, minimal overhead |

---

## Implementation Summary

### Files Created

1. **`src/Uno.DevTools.Telemetry/WasmHttpSender.cs`**
   - Internal HTTP sender for WASM platforms
   - Implements direct communication with Application Insights REST API
   - Supports both event and exception telemetry
   - Uses System.Text.Json for payload serialization
   - Graceful error handling to prevent app crashes

2. **`src/Uno.DevTools.Telemetry.Tests/WasmHttpSenderTests.cs`**
   - Unit tests for WasmHttpSender functionality
   - Uses reflection to test internal class
   - Validates event and exception payload creation
   - Tests all severity levels and null parameter handling

3. **`src/Uno.DevTools.Telemetry/WasmScript/machineId.js`**
   - JavaScript module for persistent machine ID storage
   - Uses browser localStorage API for cross-session persistence
   - Generates and stores GUID on first run
   - Gracefully handles localStorage unavailability (privacy mode, disabled)
   - Embedded as resource in assembly

4. **`src/Uno.DevTools.Telemetry/WasmMachineIdHelper.cs`**
   - C# helper class for JavaScript interop
   - Uses `[JSImport]` for CSP-safe JavaScript calls (net8.0/net9.0)
   - Falls back to session-specific GUID for netstandard2.0
   - Caches machine ID to avoid repeated JavaScript calls
   - No reflection, no eval, CSP-compliant

5. **`src/Uno.DevTools.Telemetry/PlatformDetection.cs`**
   - Shared utility class for platform detection
   - Centralizes WASM/Browser detection logic
   - Used by both Telemetry.cs and TelemetryCommonProperties.cs
   - Eliminates code duplication and provides single source of truth

### Files Modified

1. **`src/Uno.DevTools.Telemetry/Telemetry.cs`**
   - Uses `PlatformDetection.IsWasmBrowser` for runtime detection
   - Added `_wasmSender` field for WASM HTTP sender
   - Modified `InitializeTelemetry()` to branch on WASM vs non-WASM
   - Modified `TrackEventTask()` to use WasmHttpSender on WASM
   - Modified `TrackExceptionTask()` to use WasmHttpSender on WASM
   - Fixed `Thread.Yield()` calls to skip on WASM

2. **`src/Uno.DevTools.Telemetry/TelemetryCommonProperties.cs`**
   - Uses `PlatformDetection.IsWasmBrowser` for runtime detection
   - Modified `GetMachineId()` to use WasmMachineIdHelper for persistent machine ID on WASM
   - Bypasses file I/O and NetworkInterface enumeration on WASM

3. **`src/Uno.DevTools.Telemetry/Uno.DevTools.Telemetry.csproj`**
   - Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for net8.0 and net9.0 (required by JSImport)
   - Added WasmScript/machineId.js as EmbeddedResource

### Implementation Approach

- **Runtime Detection:** Uses `RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))` to detect WASM
- **No New Target Frameworks:** Continues to target netstandard2.0, net8.0, net9.0
- **100% API Compatibility:** ITelemetry interface unchanged
- **Persistent Machine ID:** Uses browser localStorage via JSImport for cross-session persistence on WASM
- **Error Resilience:** All network failures silently logged, never crash the app

### Key Design Decisions

1. **Runtime branching instead of conditional compilation:** Simpler build, single binary, easier maintenance
2. **Skip Application Insights SDK on WASM:** Avoids all threading and file I/O incompatibilities
3. **Direct HTTP to Application Insights:** Proven REST API, CORS-enabled, well-documented
4. **Persistent machine ID via localStorage:** Uses CSP-safe JSImport to store machine ID in browser localStorage for cross-session tracking
5. **In-memory queuing only on WASM:** No persistence across page refreshes, but prevents complexity

### Testing Status

- ✅ Unit tests created for WasmHttpSender
- ✅ Existing tests continue to pass on non-WASM platforms
- ✅ CI pipeline updated with WASM test job and enabled
- ✅ WASM runtime test project created with 8 comprehensive tests
- ✅ All WASM runtime tests passing (8/8) in actual browser environment
- ✅ Machine ID persistence validated via localStorage in browser tests

### CI Pipeline Changes

Updated `.github/workflows/ci.yml` to include a new `wasm-tests` job that:
- Runs on Ubuntu (Linux) for consistency with WASM tooling
- Installs .NET 10.0 SDK
- Installs Playwright for browser automation
- Installs WASM workload via uno-check
- Builds the WASM test project (`net10.0-browserwasm` target)
- Runs tests in actual browser environment using `uno-runtimetests-wasm` tool
- Reports test results alongside unit tests
- Currently enabled (`if: true`) and running successfully

All 8 WASM runtime tests pass successfully in the CI pipeline.

### Key Implementation Details

**Machine ID Persistence:**
- JavaScript file (`WasmScript/machineId.js`) uses browser localStorage API
- Embedded as resource in the assembly, loaded automatically by Uno Platform apps
- C# interop via `[JSImport]` for CSP-safe JavaScript calls (net8.0/net9.0)
- No reflection, no eval, fully CSP-compliant
- Graceful fallback to session-specific GUID if localStorage unavailable

**Testing:**
- WASM runtime test project created with 8 comprehensive tests
- Tests run in actual browser environment via Playwright
- All tests passing successfully
- CI pipeline enabled and validating on every PR

## References

- [GitHub Spec Kit](https://github.com/github/spec-kit)
- [Spec Kit Documentation](https://speckit.org/)
- [Application Insights REST API Documentation](https://docs.microsoft.com/en-us/azure/azure-monitor/app/api-custom-events-metrics)
- [Uno Platform Documentation](https://platform.uno/)
- [Uno.UI.RuntimeTests Framework](https://github.com/unoplatform/uno)
