// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Implementation-agnostic wrapper that adds scoped context to any ITelemetry instance.
    /// Scopes can be nested, with child properties/measurements overriding parent values on key conflicts.
    /// </summary>
    internal sealed class ScopedTelemetry : ITelemetry
    {
        private readonly ITelemetry _inner;
        private readonly IReadOnlyDictionary<string, string>? _scopeProperties;
        private readonly IReadOnlyDictionary<string, double>? _scopeMeasurements;

        public ScopedTelemetry(
            ITelemetry inner,
            IReadOnlyDictionary<string, string>? scopeProperties,
            IReadOnlyDictionary<string, double>? scopeMeasurements)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _scopeProperties = scopeProperties;
            _scopeMeasurements = scopeMeasurements;
        }

        public bool Enabled => _inner.Enabled;

        public void Dispose()
        {
            // Do not dispose the inner telemetry - only the scope itself is disposed.
            // The inner telemetry may be shared across multiple scopes or used directly.
        }

        public void Flush() => _inner.Flush();

        public Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

        public Task<string?> GetMachineIdAsync(CancellationToken ct) => _inner.GetMachineIdAsync(ct);

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            var mergedProperties = MergeProperties(properties);
            var mergedMeasurements = MergeMeasurements(measurements);
            _inner.ThreadBlockingTrackEvent(eventName, mergedProperties, mergedMeasurements);
        }

        public void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements)
        {
            var propertiesDict = properties != null ? ConvertToDict(properties) : null;
            var measurementsDict = measurements != null ? ConvertToDict(measurements) : null;
            TrackEvent(eventName, propertiesDict, measurementsDict);
        }

        public void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements)
        {
            var mergedProperties = MergeProperties(properties);
            var mergedMeasurements = MergeMeasurements(measurements);
            _inner.TrackEvent(eventName, mergedProperties, mergedMeasurements);
        }

        public void TrackException(
            Exception exception,
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null,
            ExceptionSeverity severity = ExceptionSeverity.Error)
        {
            var mergedProperties = MergeProperties(properties);
            var mergedMeasurements = MergeMeasurements(measurements);
            _inner.TrackException(exception, mergedProperties, mergedMeasurements, severity);
        }

        private Dictionary<string, string> MergeProperties(IReadOnlyDictionary<string, string>? eventProperties)
            => MergePropertiesCore(eventProperties);

        private Dictionary<string, string> MergeProperties(IDictionary<string, string>? eventProperties)
            => MergePropertiesCore(eventProperties);

        private Dictionary<string, string> MergePropertiesCore(IEnumerable<KeyValuePair<string, string>>? eventProperties)
        {
            var merged = new Dictionary<string, string>();

            // Add scope properties first
            if (_scopeProperties != null)
            {
                foreach (var kvp in _scopeProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            // Override with event-specific properties
            if (eventProperties != null)
            {
                foreach (var kvp in eventProperties)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private Dictionary<string, double> MergeMeasurements(IReadOnlyDictionary<string, double>? eventMeasurements)
            => MergeMeasurementsCore(eventMeasurements);

        private Dictionary<string, double> MergeMeasurements(IDictionary<string, double>? eventMeasurements)
            => MergeMeasurementsCore(eventMeasurements);

        private Dictionary<string, double> MergeMeasurementsCore(IEnumerable<KeyValuePair<string, double>>? eventMeasurements)
        {
            var merged = new Dictionary<string, double>();

            // Add scope measurements first
            if (_scopeMeasurements != null)
            {
                foreach (var kvp in _scopeMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            // Override with event-specific measurements
            if (eventMeasurements != null)
            {
                foreach (var kvp in eventMeasurements)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private static Dictionary<string, string> ConvertToDict((string key, string value)[] items)
        {
            var dict = new Dictionary<string, string>(items.Length);
            foreach (var (key, value) in items)
            {
                dict[key] = value;
            }
            return dict;
        }

        private static Dictionary<string, double> ConvertToDict((string key, double value)[] items)
        {
            var dict = new Dictionary<string, double>(items.Length);
            foreach (var (key, value) in items)
            {
                dict[key] = value;
            }
            return dict;
        }
    }
}
