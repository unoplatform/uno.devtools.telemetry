# Uno.DevTools.Telemetry

Uno.DevTools.Telemetry is a .NET library for collecting and sending telemetry data, designed for integration with Uno Platform development tools and other .NET applications.

## Features
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

### Tracking Events

```csharp
var telemetry = serviceProvider.GetRequiredService<ITelemetry>();
telemetry.TrackEvent("AppStarted");
telemetry.TrackEvent("UserAction", new Dictionary<string, string> { { "Action", "Clicked" } }, null);
```

### File-based Telemetry
By default, telemetry is persisted locally before being sent. You can configure the storage location and behavior by customizing the `Telemetry` constructor.

## Environment Variables
- `UNO_PLATFORM_TELEMETRY_OPTOUT`: Set to `true` to disable telemetry.

## Development
- All code and comments are in English.
- Follows strict code style and performance guidelines (see `agent-instructions.md`).
- Tasks and project status are tracked in `todos.md` (local only).
- Do not disable analyzers.
- See also: `CODE_OF_CONDUCT.md`, `SECURITY.md`, `LICENSE.md`.

## License
Apache 2.0 License. See `LICENSE.md` for details.

---

*For more details, see the code and comments in the repository.*

