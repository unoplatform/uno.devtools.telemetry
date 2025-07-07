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
                throw new InvalidOperationException($"Assembly {assembly.GetName().Name} is missing [Telemetry] attribute.");

            var instrumentationKey = attr.InstrumentationKey;
            var prefix = attr.EventsPrefix ?? string.Empty;

            // File-based telemetry override
            var filePath = Environment.GetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_FILE");
            if (!string.IsNullOrEmpty(filePath))
            {
#if NET8_0_OR_GREATER
                return new TelemetryAdapter<T>(new FileTelemetry(filePath, prefix));
#else
                return new TelemetryAdapter<T>(new FileTelemetry(filePath, prefix));
#endif
            }

            // Default: Application Insights
            return new TelemetryAdapter<T>(new Telemetry(instrumentationKey, prefix, assembly));
        }
    }
}
