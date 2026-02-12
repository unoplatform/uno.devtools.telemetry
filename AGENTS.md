# Agent Testing Guide

This document explains how to run tests for the Uno.DevTools.Telemetry project, specifically designed for AI agents and developers working on this codebase.

## Overview

The project has three test suites:
1. **Unit Tests** - Standard .NET unit tests for core functionality
2. **WASM Runtime Tests** - Browser-based tests that validate telemetry on actual WebAssembly
3. **Integration Tests** - End-to-end validation

## Prerequisites

### Required Tools
- .NET SDK 10.0 or later
- Node.js and npm (for Playwright browser automation)
- Uno.UI.RuntimeTests.Engine.Wasm.Runner CLI tool (prerelease)

### Install WASM Runtime Test Runner
```bash
dotnet tool install -g Uno.UI.RuntimeTests.Engine.Wasm.Runner --prerelease
```

### Install Chromium (for WASM tests)
```bash
npx playwright install chromium
```

## Running Unit Tests

### Run all unit tests
```bash
cd src
dotnet test
```

### Run specific test project
```bash
cd src/Uno.DevTools.Telemetry.Tests
dotnet test --configuration Release
```

### Generate test report (TRX format)
```bash
cd src
dotnet run --project Uno.DevTools.Telemetry.Tests/Uno.DevTools.Telemetry.Tests.csproj --configuration Release -- --report-trx --results-directory ../artifacts/test-results
```

## Running WASM Runtime Tests

WASM runtime tests run in an actual browser environment using Uno.UI.RuntimeTests.Engine. These tests validate that the telemetry package works correctly on WebAssembly, including:
- Platform detection
- HTTP-based telemetry sending
- Threading behavior
- Exception handling
- Environment variable configuration

### Method 1: Local Development Server (Interactive)

**Purpose:** Quick testing during development with live debugging in browser DevTools.

```bash
cd src/Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests

# Run the app in development mode
dotnet run -f net10.0-browserwasm

# Open browser to http://localhost:5000/
# The RuntimeTests UI will show all tests and allow manual execution
```

**Use this when:**
- You want to see test output in real-time
- You need to debug test failures in browser DevTools
- You're iterating on test code

### Method 2: CLI Runner (Automated)

**Purpose:** Automated test execution matching CI behavior, produces JUnit XML results.

#### Step 1: Publish the WASM app
```bash
cd src/Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests

# Publish with PublishTrimmed=false (required for reflection-based test discovery)
dotnet publish -c Release -f net10.0-browserwasm -p:PublishTrimmed=false -o ./publish
```

#### Step 2: Run tests using CLI tool
```bash
# From the same directory
uno-runtimetests-wasm \
  --app-path ./publish/wwwroot \
  --output ./wasm-test-results.xml \
  --timeout 300
```

**Parameters:**
- `--app-path`: Path to the published wwwroot folder
- `--output`: Where to write JUnit XML results
- `--timeout`: Maximum time in seconds (default: 300)
- `--headless`: Run browser in headless mode (default: true)
- `--filter`: Optional test name filter (regex)

**Output:**
- Test results written to `wasm-test-results.xml` (JUnit format)
- Console shows real-time progress
- Exit code 0 = all tests passed, non-zero = failures

**Use this when:**
- You want to run tests exactly as CI does
- You need JUnit XML output for test reporters
- You're validating CI configuration locally

### Viewing Test Results

After running the CLI tool, examine the results:

```bash
# View XML directly
cat wasm-test-results.xml

# Or use an XML viewer
xmllint --format wasm-test-results.xml
```

Expected output structure:
```xml
<test-run result="Passed" total="8" passed="8" failed="0">
  <test-suite name="Uno.DevTools.Telemetry.WasmTests">
    <test-case name="Telemetry_InitializesOnWasm_WithoutErrors()" result="Passed" />
    <test-case name="Telemetry_TrackEvent_OnWasm_DoesNotThrow()" result="Passed" />
    <!-- ... 6 more tests ... -->
  </test-suite>
</test-run>
```

## CI Pipeline

The CI pipeline automatically runs all tests on every push and pull request.

### Build Job
- Builds all projects (netstandard2.0, net8.0, net9.0)
- Runs unit tests
- Generates test reports

### WASM Tests Job
Runs on Ubuntu, follows these steps:

```yaml
# Install CLI tool
dotnet tool install -g Uno.UI.RuntimeTests.Engine.Wasm.Runner --prerelease

# Install browser
npx playwright install chromium

# Publish app
cd src/Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests
dotnet publish -c Release -f net10.0-browserwasm -p:PublishTrimmed=false -o ./publish

# Run tests
uno-runtimetests-wasm --app-path ./publish/wwwroot --output ./wasm-test-results.xml --timeout 300

# Upload results (using dorny/test-reporter@v2 with java-junit reporter)
```

### Viewing CI Results
- Test results appear in PR checks
- Detailed report available via test-reporter action
- Artifacts include:
  - `test-results` - Unit test TRX files
  - `wasm-test-results` - WASM test XML

## Common Issues and Solutions

### Issue: "uno-runtimetests-wasm not found"
**Solution:** Install with `--prerelease` flag:
```bash
dotnet tool install -g Uno.UI.RuntimeTests.Engine.Wasm.Runner --prerelease
```

### Issue: Tests fail with reflection errors during publish
**Cause:** Trimming is enabled, which removes test metadata
**Solution:** Always use `-p:PublishTrimmed=false` when publishing for tests:
```bash
dotnet publish -c Release -f net10.0-browserwasm -p:PublishTrimmed=false -o ./publish
```

### Issue: Browser crashes or times out
**Cause:** Insufficient timeout or memory issues
**Solution:** Increase timeout or run in non-headless mode for debugging:
```bash
uno-runtimetests-wasm --app-path ./publish/wwwroot --output ./results.xml --timeout 600 --headless false
```

### Issue: "RuntimeTests.Engine" package not found
**Cause:** Outdated Uno.SDK version
**Solution:** Update to Uno.SDK 6.5.31 or later in `global.json`:
```json
{
  "msbuild-sdks": {
    "Uno.Sdk": "6.5.31"
  }
}
```
Then run:
```bash
dotnet clean && dotnet restore --force-evaluate
```

### Issue: WASM tests pass locally but fail in CI
**Possible Causes:**
1. Different browser versions - ensure CI uses same Chromium version
2. Timing issues - increase timeout in CI
3. Missing dependencies - verify all packages restored in CI

## Test Coverage

### Unit Tests (Uno.DevTools.Telemetry.Tests)
- Core telemetry API
- Event tracking
- Exception tracking
- Common properties
- Service collection extensions

### WASM Runtime Tests (Uno.DevTools.Telemetry.WasmTests)
**Current Tests (8 total):**
1. `Telemetry_InitializesOnWasm_WithoutErrors()` - Validates initialization succeeds on WASM
2. `Telemetry_TrackEvent_OnWasm_DoesNotThrow()` - Validates event tracking works
3. `Telemetry_TrackException_OnWasm_DoesNotThrow()` - Validates exception tracking works
4. `Telemetry_FlushAsync_OnWasm_CompletesSuccessfully()` - Validates async flush operations
5. `Telemetry_DisabledViaEnvironmentVariable_OnWasm()` - Validates opt-out via env var
6. `Telemetry_MultipleEvents_OnWasm_DoesNotCauseThreadingIssues()` - Validates concurrent event tracking
7. `Telemetry_AllExceptionSeverities_OnWasm()` - Validates all severity levels work
8. `Telemetry_ThreadBlockingTrackEvent_OnWasm()` - Validates synchronous blocking behavior

## Adding New Tests

### Adding WASM Runtime Tests

1. Edit `src/Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests/TelemetryWasmTests.cs`

2. Add test method with proper attributes:
```csharp
[TestMethod]
public void Telemetry_MyNewTest_OnWasm()
{
    // Arrange
    var telemetry = new Telemetry("<test-key>", "Test", typeof(App).Assembly);

    // Act
    // ... perform operation ...

    // Assert
    Assert.IsTrue(condition, "failure message");
}
```

3. Run locally to verify:
```bash
cd src/Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests
dotnet run -f net10.0-browserwasm
# Check http://localhost:5000/ for your new test
```

4. Publish and run via CLI:
```bash
dotnet publish -c Release -f net10.0-browserwasm -p:PublishTrimmed=false -o ./publish
uno-runtimetests-wasm --app-path ./publish/wwwroot --output ./results.xml --timeout 300
```

5. Verify test appears in results:
```bash
grep "MyNewTest" results.xml
```

## References

- [Uno.UI.RuntimeTests.Engine Documentation](https://github.com/unoplatform/uno.ui.runtimetests.engine)
- [Uno Platform Testing Guide](https://aka.platform.uno/testing)
- [GitHub Actions Test Reporter](https://github.com/dorny/test-reporter)
- [MSTest Framework Documentation](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)

## Quick Reference: Complete Test Run

```bash
# 1. Build everything
cd /home/jay/s/uno.devtools.telemetry
dotnet build -c Release

# 2. Run unit tests
cd src
dotnet test --configuration Release

# 3. Publish WASM tests
cd Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests
dotnet publish -c Release -f net10.0-browserwasm -p:PublishTrimmed=false -o ./publish

# 4. Run WASM tests via CLI
uno-runtimetests-wasm --app-path ./publish/wwwroot --output ./wasm-test-results.xml --timeout 300

# 5. Check results
cat wasm-test-results.xml | grep "result="
```

Expected output: `result="Passed" total="8" passed="8" failed="0"`
