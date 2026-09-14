using System;
using System.Reflection;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Generic interface for contextual/typed telemetry.
    /// </summary>
    public interface ITelemetry<T> : ITelemetry { }


    /// <summary>
    /// Factory for ITelemetry<T> that resolves assembly-level configuration and instantiates the correct telemetry implementation.
    /// </summary>
    public static class TelemetryFactory
    {
        /// <summary>
        /// Creates a contextual ITelemetry<T> instance, using the TelemetryAttribute on the assembly of T.
        /// </summary>
        public static ITelemetry<T> Create<T>()
        {
            var assembly = typeof(T).Assembly;
            var attr = assembly.GetCustomAttribute<TelemetryAttribute>();
            if (attr == null)
                throw new InvalidOperationException(
                    $"Assembly '{assembly.GetName().Name}' is missing [Telemetry] attribute. " +
                    "Add [assembly: Telemetry(\"<instrumentation-key>\", EventsPrefix = \"<prefix>\")] " +
                    "or register ITelemetry with AddTelemetry(instrumentationKey, eventNamePrefix, ...).");

            var instrumentationKey = attr.InstrumentationKey;
            var prefix = attr.EventsPrefix ?? string.Empty;

            // File-based telemetry override (null when unset or when the opt-out is set)
            var filePath = TelemetryEnvironment.GetFileTelemetryPath();
            if (filePath is not null)
            {
                return new TelemetryAdapter<T>(new FileTelemetry(filePath, prefix));
            }

            // Default: Application Insights
            return new TelemetryAdapter<T>(new Telemetry(instrumentationKey, prefix, assembly));
        }
    }
}
