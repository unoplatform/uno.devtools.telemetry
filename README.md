# Uno.DevTools.Telemetry

Uno.DevTools.Telemetry is a .NET library for collecting and sending telemetry data, designed for integration with Uno Platform development tools and other .NET applications.

## Features
- Optional but easy integration with .NET dependency injection (`IServiceCollection`)
- Supports Microsoft Application Insights
- Extensible and testable architecture
- File-based telemetry channel for local development
- High-performance, immutable code style
- Modern C# features and best practices

## Installation
Add the NuGet package to your project:

```shell
dotnet add package Uno.DevTools.Telemetry
```

## Usage

### Without DI

```csharp
const string instrumentationKey = ""; // GUID from AppInsight
const string analyticsEventPrefix = "uno/my-project"; // prefix for every analytics event

var telemetry = new Telemetry(
    instrumentationKey,
    analyticsEventPrefix,
    typeof(MyProject).Assembly
);
```

## Migration

> [!IMPORTANT]
> If you already use `Telemetry` directly, no changes are required.

To adopt the new features:
- File telemetry override: set `UNO_PLATFORM_TELEMETRY_FILE` and resolve telemetry through `AddTelemetry(...)` or `TelemetryFactory.Create<T>()` (direct `new Telemetry(...)` does not read the env var).
- Typed telemetry: add an assembly-level `[Telemetry]` attribute and register `services.AddTelemetry()` to inject `ITelemetry<T>`.

### Registering Telemetry in DI

```csharp
using Uno.DevTools.Telemetry;

var services = new ServiceCollection();
services.AddTelemetry(
    instrumentationKey: "<your-app-insights-key>",
    eventNamePrefix: "MyApp/Telemetry",
    versionAssembly: typeof(MyProject).Assembly,
    sessionId: "optional-session-id"
);
```

#### Parameters for `AddTelemetry`
- `instrumentationKey` (**required**): Application Insights instrumentation key (GUID).
- `eventNamePrefix` (optional): Prefix for all telemetry events (e.g. `uno/my-project`). Use `string.Empty` for no prefix.
- `versionAssembly` (optional): Assembly used for version info (defaults to calling assembly).
- `sessionId` (optional): Custom session id.

### Tracking Events

```csharp
var telemetry = serviceProvider.GetRequiredService<ITelemetry>();
telemetry.TrackEvent("AppStarted");
telemetry.TrackEvent("UserAction", new Dictionary<string, string> { { "Action", "Clicked" } }, null);
```

> [!WARNING]
> When using `ITelemetry` (including `FileTelemetry` or any implementation), do not modify any dictionary or list after passing it as a parameter to telemetry methods (such as `TrackEvent`).
> All collections passed to telemetry should be considered owned by the telemetry system and must not be mutated by the caller after the call. Mutating collections after passing them may cause race conditions or undefined behavior.

### File-based Telemetry
By default, telemetry is persisted locally before being sent. You can configure the storage location and behavior by customizing the `Telemetry` constructor.

## Environment Variables
| Variable | Purpose |
| --- | --- |
| `UNO_PLATFORM_TELEMETRY_OPTOUT` | Set to `true` to disable telemetry. |
| `UNO_PLATFORM_TELEMETRY_FILE` | When set, telemetry is logged to the specified file path using `FileTelemetry` instead of Application Insights. On .NET 8+, FileTelemetry uses `TimeProvider` for testable timestamps; on .NET Standard 2.0, it falls back to system time. |

> [!NOTE]
> - The use of `UNO_PLATFORM_TELEMETRY_FILE` is intended for testing, debugging, or local development scenarios. To activate file-based telemetry, resolve telemetry using `AddTelemetry(...)` or `TelemetryFactory.Create<T>()` so the environment variable is detected.
> - FileTelemetry is thread-safe and writes events as single-line JSON. When a prefix is provided, the event name is prefixed and each line is also prefixed for easy filtering.
> - Multi-framework support: On .NET 8+ and .NET 9+, `FileTelemetry` uses `TimeProvider` for testable timestamps. On netstandard2.0, it falls back to `DateTime.Now`.

## FileTelemetry & Testing

> [!NOTE]
> FileTelemetry is intended for testing, CI validation, and local debugging. It is thread-safe and writes one JSON object per line.

When `UNO_PLATFORM_TELEMETRY_FILE` is set, all telemetry events are written to the specified file. This is especially useful for automated tests, CI validation, or debugging telemetry output locally. The file will contain one JSON object per line, optionally prefixed by the event name prefix (from `AddTelemetry(...)` or `[Telemetry(EventsPrefix = ...)]`). If no prefix is provided, the line is written without a prefix.

Example output:
```
global: {"Timestamp":"2025-07-07T12:34:56.789Z","EventName":"global/AppStarted","Properties":{},"Measurements":null}
```

## Crash/Exception Reporting

> [!NOTE]
> Crash and exception reporting is planned for a future release. For now, only explicit event tracking is supported. See `todos.md` for roadmap.

## Multi-framework Support

Uno.DevTools.Telemetry targets:
- .NET Standard 2.0 (broad compatibility)
- .NET 8.0
- .NET 9.0

All features are available on .NET 8+; some features (like testable time via `TimeProvider`) are not available on netstandard2.0 and will fallback to system time.

---

*For more details, see the code and comments in the repository.*

### Example: Using FileTelemetry from the Command Line (PowerShell)

To log telemetry events to a file for testing or debugging, set the environment variable before launching your application (the application must resolve telemetry via `AddTelemetry(...)` or `TelemetryFactory.Create<T>()`):

```powershell
$env:UNO_PLATFORM_TELEMETRY_FILE = "telemetry.log"
dotnet run --project path/to/YourApp.csproj
```

Replace `path/to/YourApp.csproj` with the path to your application's project file. All telemetry events will be written to `telemetry.log` in the working directory.

> [!TIP]
> You can also specify an absolute path for the log file (e.g., `C:\temp\telemetry.log`).

## Advanced usage: typed/contextual telemetry with DI

To inject contextualized telemetry by type:

```csharp
// In your Startup or Program.cs
services.AddTelemetry();

// Somewhere in your assembly containing the service
[assembly: Telemetry("instrumentation-key", EventsPrefix = "prefix")]

// In an application class
public class MyService
{
    private readonly ITelemetry<MyService> _telemetry;
    public MyService(ITelemetry<MyService> telemetry) // Telemetry will be properly configured using the [assembly: Telemetry] attribute
    {
        _telemetry = telemetry;
    }
    public void DoSomething()
    {
        _telemetry.TrackEvent("Action", new Dictionary<string, string> { { "key", "value" } }, null);
    }
}
```

The injected instance will automatically use the assembly-level configuration of `MyService` (the `[Telemetry]` attribute).

- If the `UNO_PLATFORM_TELEMETRY_FILE` environment variable is set, the instance will be a `FileTelemetry`.
- Otherwise, the instance will be a `Telemetry` (Application Insights).

> [!NOTE]
> You can inject `ITelemetry<T>` for any type, and resolution will be automatic via the DI container. If the `[Telemetry]` attribute is missing, resolution throws an `InvalidOperationException` that explains how to fix the configuration.

