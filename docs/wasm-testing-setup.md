# WebAssembly Runtime Testing Setup

This document describes how to create and configure the WASM runtime test project for validating telemetry functionality in WebAssembly environments.

## Overview

The WASM runtime tests validate that the `Uno.DevTools.Telemetry` package works correctly when running in an actual WebAssembly/browser environment. These tests complement the unit tests by verifying behavior that can only be tested in a real browser.

## Prerequisites

- .NET 8.0 SDK or later
- Uno Platform templates installed: `dotnet new install Uno.Templates`
- WASM workload: `dotnet workload install wasm-tools`
- Playwright for browser automation: `dotnet tool install --global Microsoft.Playwright.CLI`

## Creating the WASM Test Project

### Step 1: Create Uno Platform App

```bash
cd src
dotnet new unoapp -n Uno.DevTools.Telemetry.WasmTests -preset blank
cd Uno.DevTools.Telemetry.WasmTests
```

### Step 2: Add Required NuGet Packages

Edit `Uno.DevTools.Telemetry.WasmTests.csproj` and add:

```xml
<ItemGroup>
  <PackageReference Include="Uno.UI.RuntimeTests" Version="5.1.0" />
  <PackageReference Include="Microsoft.Playwright" Version="1.40.0" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\Uno.DevTools.Telemetry\Uno.DevTools.Telemetry.csproj" />
</ItemGroup>
```

### Step 3: Configure for WASM Runtime Testing

Ensure the project file includes the `net8.0-browserwasm` target framework:

```xml
<PropertyGroup>
  <TargetFrameworks>net8.0-browserwasm</TargetFrameworks>
</PropertyGroup>
```

### Step 4: Create Test Classes

Create `Tests/TelemetryWasmTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry.WasmTests
{
    [TestClass]
    public class TelemetryWasmTests
    {
        [TestMethod]
        public async Task Telemetry_InitializesOnWasm_WithoutErrors()
        {
            // Arrange & Act
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );

            // Assert
            Assert.IsTrue(telemetry.Enabled, "Telemetry should be enabled");

            // Verify machine ID can be retrieved
            var machineId = await telemetry.GetMachineIdAsync(CancellationToken.None);
            Assert.IsNotNull(machineId, "Machine ID should be generated");
        }

        [TestMethod]
        public void Telemetry_TrackEvent_OnWasm_DoesNotThrow()
        {
            // Arrange
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );

            // Act & Assert - Should not throw
            telemetry.TrackEvent("TestEvent", new[] { ("key", "value") }, null);
            telemetry.TrackEvent("TestEvent2", null, new[] { ("metric", 1.0) });
        }

        [TestMethod]
        public void Telemetry_TrackException_OnWasm_DoesNotThrow()
        {
            // Arrange
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );
            var exception = new InvalidOperationException("Test exception");

            // Act & Assert - Should not throw
            telemetry.TrackException(exception, severity: ExceptionSeverity.Warning);
            telemetry.TrackException(
                new ArgumentException("Another test"),
                new[] { ("context", "unit-test") }.ToDictionary(x => x.Item1, x => x.Item2),
                new[] { ("count", 1.0) }.ToDictionary(x => x.Item1, x => x.Item2),
                ExceptionSeverity.Error
            );
        }

        [TestMethod]
        public async Task Telemetry_FlushAsync_OnWasm_CompletesSuccessfully()
        {
            // Arrange
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );

            // Track some events
            telemetry.TrackEvent("Event1", null, null);
            telemetry.TrackEvent("Event2", null, null);

            // Act & Assert - Should complete without throwing
            await telemetry.FlushAsync(CancellationToken.None);
        }

        [TestMethod]
        public void Telemetry_DisabledViaEnvironmentVariable_OnWasm()
        {
            // Arrange
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", "true");

            // Act
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );

            // Assert
            Assert.IsFalse(telemetry.Enabled, "Telemetry should be disabled");

            // Cleanup
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", null);
        }

        [TestMethod]
        public void Telemetry_MultipleEvents_OnWasm_DoesNotCauseThreadingIssues()
        {
            // Arrange
            var telemetry = new Telemetry(
                instrumentationKey: "test-instrumentation-key",
                eventNamePrefix: "WasmTest",
                versionAssembly: typeof(TelemetryWasmTests).Assembly
            );

            // Act - Track many events rapidly (tests the lock-free chaining)
            for (int i = 0; i < 100; i++)
            {
                telemetry.TrackEvent($"Event{i}", new[] { ("index", i.ToString()) }, null);
            }

            // Assert - Should complete without threading errors
            // In WASM, this should work even though Thread.Yield() is skipped
            Assert.IsTrue(true, "Completed without threading issues");
        }
    }
}
```

### Step 5: Configure App Entry Point

Ensure `App.xaml.cs` or `MainPage.xaml.cs` initializes the runtime tests properly. Follow the Uno.UI.RuntimeTests documentation for the specific setup required.

### Step 6: Add to Solution

```bash
cd ..
dotnet sln add Uno.DevTools.Telemetry.WasmTests/Uno.DevTools.Telemetry.WasmTests.csproj
```

## Running Tests Locally

### Option 1: Using dotnet test

```bash
cd src/Uno.DevTools.Telemetry.WasmTests
dotnet test -c Release -f net8.0-browserwasm
```

This will:
1. Build the WASM application
2. Launch a local web server
3. Open a browser (via Playwright)
4. Run the tests in the browser
5. Report results back to the console

### Option 2: Manual Browser Testing

```bash
cd src/Uno.DevTools.Telemetry.WasmTests
dotnet build -c Release -f net8.0-browserwasm
dotnet serve bin/Release/net8.0-browserwasm
```

Then open a browser to the displayed URL and use the runtime test UI to run tests manually.

## CI Integration

The WASM tests are integrated into the CI pipeline in `.github/workflows/ci.yml`. To enable them:

1. Complete the steps above to create the test project
2. Verify tests pass locally
3. In `.github/workflows/ci.yml`, change the `wasm-tests` job's `if` condition from `false` to `true`:

```yaml
wasm-tests:
  name: WASM Runtime Tests
  runs-on: ubuntu-latest
  if: true  # Changed from false to enable WASM tests
```

4. Commit and push - the CI will now run WASM tests on every PR and push

## Troubleshooting

### Tests fail with "Thread.Yield() not supported"

This should not happen with the current implementation as we skip `Thread.Yield()` on WASM. If it does occur, verify that the `IsWasmBrowser` detection is working correctly.

### Tests fail with "MemoryMappedFile not available"

This indicates the Application Insights SDK is being initialized on WASM. Verify that:
1. The WASM detection is working (`IsWasmBrowser` should be `true`)
2. The `InitializeTelemetry()` method is properly branching to use `WasmHttpSender`

### Network requests fail with CORS errors

The Application Insights endpoint (`https://dc.services.visualstudio.com/v2/track`) has CORS enabled by default. If you see CORS errors:
1. Check browser console for specific error details
2. Verify the HTTP request includes proper headers
3. Consider using a test Application Insights instance if needed

### Tests timeout

WASM tests can take longer than regular unit tests due to browser startup. Increase the timeout in your test configuration if needed.

## Expected Test Behavior

When running correctly, the WASM runtime tests should:

✅ Initialize telemetry without throwing exceptions
✅ Generate a valid machine ID (GUID format)
✅ Track events without threading errors
✅ Track exceptions with proper severity mapping
✅ Complete flush operations successfully
✅ Respect the `UNO_PLATFORM_TELEMETRY_OPTOUT` environment variable
✅ Handle rapid event tracking without errors

In the browser DevTools:
- **Network Tab**: Should show POST requests to `https://dc.services.visualstudio.com/v2/track`
- **Console**: Should have no errors related to threading, file I/O, or Application Insights SDK
- **Application Insights**: Events should appear in the portal (if using a real instrumentation key)

## Performance Considerations

WASM tests run in the browser and are inherently slower than regular unit tests. Expected timings:
- Browser startup: 3-10 seconds
- Test execution: 1-5 seconds per test
- Total CI time: 1-3 minutes for the WASM test job

## References

- [Uno.UI.RuntimeTests Documentation](https://github.com/unoplatform/uno/blob/master/doc/articles/features/runtime-tests.md)
- [Uno Platform WASM Documentation](https://platform.uno/docs/articles/get-started-wasm.html)
- [Playwright for .NET](https://playwright.dev/dotnet/)
- [Application Insights REST API](https://docs.microsoft.com/en-us/azure/azure-monitor/app/api-custom-events-metrics)
