using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// A telemetry implementation that writes events to a file for testing purposes.
    /// Can use either individual files per context or a single file with prefixes.
    /// </summary>
    public class FileTelemetry : ITelemetry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false // Use single-line JSON for easier parsing in tests
        };

        private readonly string _filePath;
        private readonly string _contextPrefix;
        private readonly object _lock = new();
#if NET8_0_OR_GREATER
        private readonly TimeProvider _timeProvider;
#endif

        /// <summary>
        /// Creates a FileTelemetry instance with a contextual file name or prefix.
        /// </summary>
        /// <param name="baseFilePath">The base file path (with or without extension)</param>
        /// <param name="context">The telemetry context (e.g., "global") - used when multiple instances are required, to differentiate them in the output</param>
#if NET8_0_OR_GREATER
        /// <param name="timeProvider">Optional time provider for testability (defaults to TimeProvider.System)</param>
        public FileTelemetry(string baseFilePath, string context, TimeProvider? timeProvider = null)
#else
        public FileTelemetry(string baseFilePath, string context)
#endif
        {
            if (string.IsNullOrEmpty(baseFilePath))
                throw new ArgumentNullException(nameof(baseFilePath));
            if (string.IsNullOrEmpty(context))
                throw new ArgumentNullException(nameof(context));

            _filePath = baseFilePath;
            _contextPrefix = context;
#if NET8_0_OR_GREATER
            _timeProvider = timeProvider ?? TimeProvider.System;
#endif
            EnsureDirectoryExists(_filePath);
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    // Log the exception for diagnostics (but do not throw)
                    System.Diagnostics.Debug.WriteLine($"[FileTelemetry] Failed to create directory '{directory}': {ex}");
                }
            }
        }

        public bool Enabled => true;

        public void Dispose()
        {
            // Don't dispose to allow post-shutdown logging
        }

        public void Flush()
        {
            // File-based telemetry doesn't need explicit flushing as we write immediately
        }

        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            TrackEvent(eventName, properties, measurements);
        }

        public void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements)
        {
            var propertiesDict = properties != null ? new Dictionary<string, string>() : null;
            if (properties != null)
            {
                foreach (var (key, value) in properties)
                {
                    propertiesDict![key] = value;
                }
            }

            var measurementsDict = measurements != null ? new Dictionary<string, double>() : null;
            if (measurements != null)
            {
                foreach (var (key, value) in measurements)
                {
                    measurementsDict![key] = value;
                }
            }

            TrackEvent(eventName, propertiesDict, measurementsDict);
        }

        public void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements)
        {
            // Add the context prefix to properties if specified
            var finalProperties = properties;
            if (!string.IsNullOrEmpty(_contextPrefix))
            {
                // Clone properties and add context
                finalProperties = properties != null ? new Dictionary<string, string>(properties) : new Dictionary<string, string>();
            }

            var telemetryEvent = new
            {
#if NET8_0_OR_GREATER
                Timestamp = _timeProvider.GetLocalNow().DateTime, // Use TimeProvider for testability
#else
                Timestamp = DateTime.Now, // Fallback for netstandard2.0
#endif
                EventName = eventName,
                Properties = finalProperties,
                Measurements = measurements
            };

            var json = JsonSerializer.Serialize(telemetryEvent, JsonOptions);

            lock (_lock)
            {
                try
                {
                    var line = _contextPrefix + ": " + json + Environment.NewLine;
                    File.AppendAllText(_filePath, line);
                }
                catch (Exception ex)
                {
                    // Log the exception for diagnostics (but do not throw)
                    System.Diagnostics.Debug.WriteLine($"[FileTelemetry] Failed to write telemetry to '{_filePath}': {ex}");
                }
            }
        }
    }
}
