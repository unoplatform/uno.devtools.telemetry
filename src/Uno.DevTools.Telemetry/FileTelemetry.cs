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
    public sealed class FileTelemetry : ITelemetry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false // Use single-line JSON for easier parsing in tests
        };

        private readonly string _filePath;
        private readonly string _contextPrefix;
        private readonly object _lock = new();
        private readonly IReadOnlyDictionary<string, string>? _scopeProperties;
        private readonly IReadOnlyDictionary<string, double>? _scopeMeasurements;
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
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _filePath = baseFilePath;
            _contextPrefix = context;
#if NET8_0_OR_GREATER
            _timeProvider = timeProvider ?? TimeProvider.System;
#endif
            EnsureDirectoryExists(_filePath);
        }

        /// <summary>
        /// Private constructor for creating scoped telemetry instances.
        /// </summary>
#if NET8_0_OR_GREATER
        private FileTelemetry(
            string baseFilePath,
            string context,
            IReadOnlyDictionary<string, string>? scopeProperties,
            IReadOnlyDictionary<string, double>? scopeMeasurements,
            TimeProvider timeProvider)
        {
            _filePath = baseFilePath;
            _contextPrefix = context;
            _scopeProperties = scopeProperties;
            _scopeMeasurements = scopeMeasurements;
            _timeProvider = timeProvider;
        }
#else
        private FileTelemetry(
            string baseFilePath,
            string context,
            IReadOnlyDictionary<string, string>? scopeProperties,
            IReadOnlyDictionary<string, double>? scopeMeasurements)
        {
            _filePath = baseFilePath;
            _contextPrefix = context;
            _scopeProperties = scopeProperties;
            _scopeMeasurements = scopeMeasurements;
        }
#endif

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

        public Task<string?> GetMachineIdAsync(CancellationToken ct)
            => Task.FromResult<string?>(null);

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            TrackEvent(eventName, properties, measurements);
        }

        public void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements)
        {
            Dictionary<string, string>? propertiesDict = null;
            if (properties != null)
            {
                propertiesDict = new Dictionary<string, string>(properties.Length);
                foreach (var (key, value) in properties)
                {
                    propertiesDict[key] = value;
                }
            }

            Dictionary<string, double>? measurementsDict = null;
            if (measurements != null)
            {
                measurementsDict = new Dictionary<string, double>(measurements.Length);
                foreach (var (key, value) in measurements)
                {
                    measurementsDict[key] = value;
                }
            }

            TrackEvent(eventName, propertiesDict, measurementsDict);
        }

        public void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements)
        {
            var prefixedEventName = string.IsNullOrEmpty(_contextPrefix)
                ? eventName
                : _contextPrefix + "/" + eventName;

            // Merge scope properties and measurements with event-specific ones
            var mergedProperties = MergeProperties(properties);
            var mergedMeasurements = MergeMeasurements(measurements);

            var telemetryEvent = new
            {
                Type = "event",
#if NET8_0_OR_GREATER
                Timestamp = _timeProvider.GetLocalNow().DateTime, // Use TimeProvider for testability
#else
                Timestamp = DateTime.Now, // Fallback for netstandard2.0
#endif
                EventName = prefixedEventName,
                Properties = mergedProperties,
                Measurements = mergedMeasurements
            };

            WriteToFile(telemetryEvent);
        }

        public ITelemetry CreateScope(
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            // Merge parent scope properties with new scope properties
            Dictionary<string, string>? mergedScopeProperties = null;
            if (_scopeProperties != null || properties != null)
            {
                mergedScopeProperties = new Dictionary<string, string>();
                if (_scopeProperties != null)
                {
                    foreach (var kvp in _scopeProperties)
                    {
                        mergedScopeProperties[kvp.Key] = kvp.Value;
                    }
                }
                if (properties != null)
                {
                    foreach (var kvp in properties)
                    {
                        mergedScopeProperties[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Merge parent scope measurements with new scope measurements
            Dictionary<string, double>? mergedScopeMeasurements = null;
            if (_scopeMeasurements != null || measurements != null)
            {
                mergedScopeMeasurements = new Dictionary<string, double>();
                if (_scopeMeasurements != null)
                {
                    foreach (var kvp in _scopeMeasurements)
                    {
                        mergedScopeMeasurements[kvp.Key] = kvp.Value;
                    }
                }
                if (measurements != null)
                {
                    foreach (var kvp in measurements)
                    {
                        mergedScopeMeasurements[kvp.Key] = kvp.Value;
                    }
                }
            }

#if NET8_0_OR_GREATER
            return new FileTelemetry(_filePath, _contextPrefix, mergedScopeProperties, mergedScopeMeasurements, _timeProvider);
#else
            return new FileTelemetry(_filePath, _contextPrefix, mergedScopeProperties, mergedScopeMeasurements);
#endif
        }

        public void TrackException(
            Exception exception,
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null,
            TelemetrySeverity severity = TelemetrySeverity.Error)
        {
            if (exception == null)
            {
                return;
            }

            // Merge scope properties and measurements with exception-specific ones
            var mergedProperties = MergeProperties(properties);
            var mergedMeasurements = MergeMeasurements(measurements);

            var exceptionEvent = new
            {
                Type = "exception",
#if NET8_0_OR_GREATER
                Timestamp = _timeProvider.GetLocalNow().DateTime,
#else
                Timestamp = DateTime.Now,
#endif
                Severity = severity.ToString(),
                Exception = new
                {
                    Type = exception.GetType().FullName,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace
                },
                Properties = mergedProperties,
                Measurements = mergedMeasurements
            };

            WriteToFile(exceptionEvent);
        }

        private Dictionary<string, string>? MergeProperties(IReadOnlyDictionary<string, string>? eventProperties)
        {
            if (_scopeProperties == null && eventProperties == null)
            {
                return null;
            }

            var merged = new Dictionary<string, string>();
            if (_scopeProperties != null)
            {
                foreach (var kvp in _scopeProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            if (eventProperties != null)
            {
                foreach (var kvp in eventProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged.Count > 0 ? merged : null;
        }

        private Dictionary<string, string>? MergeProperties(IDictionary<string, string>? eventProperties)
        {
            if (_scopeProperties == null && eventProperties == null)
            {
                return null;
            }

            var merged = new Dictionary<string, string>();
            if (_scopeProperties != null)
            {
                foreach (var kvp in _scopeProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            if (eventProperties != null)
            {
                foreach (var kvp in eventProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged.Count > 0 ? merged : null;
        }

        private Dictionary<string, double>? MergeMeasurements(IReadOnlyDictionary<string, double>? eventMeasurements)
        {
            if (_scopeMeasurements == null && eventMeasurements == null)
            {
                return null;
            }

            var merged = new Dictionary<string, double>();
            if (_scopeMeasurements != null)
            {
                foreach (var kvp in _scopeMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            if (eventMeasurements != null)
            {
                foreach (var kvp in eventMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged.Count > 0 ? merged : null;
        }

        private Dictionary<string, double>? MergeMeasurements(IDictionary<string, double>? eventMeasurements)
        {
            if (_scopeMeasurements == null && eventMeasurements == null)
            {
                return null;
            }

            var merged = new Dictionary<string, double>();
            if (_scopeMeasurements != null)
            {
                foreach (var kvp in _scopeMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            if (eventMeasurements != null)
            {
                foreach (var kvp in eventMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }
            return merged.Count > 0 ? merged : null;
        }

        private void WriteToFile(object telemetryData)
        {
            var json = JsonSerializer.Serialize(telemetryData, JsonOptions);

            lock (_lock)
            {
                try
                {
                    var line = string.IsNullOrEmpty(_contextPrefix)
                        ? json + Environment.NewLine
                        : _contextPrefix + ": " + json + Environment.NewLine;
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
