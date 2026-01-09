// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
//


namespace Uno.DevTools.Telemetry
{
    public interface ITelemetry
    {
        bool Enabled { get; }

        void Dispose();
        void Flush();
        Task FlushAsync(CancellationToken ct);
        Task<string?> GetMachineIdAsync(CancellationToken ct);
        void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements);
        void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements);
        void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements);

        /// <summary>
        /// Creates a new telemetry scope with specified properties and measurements.
        /// Scopes are nested and merged, with child properties overriding parent properties on key conflicts.
        /// Scope properties/measurements are applied to all subsequent TrackEvent and TrackException calls within the scope.
        /// </summary>
        /// <param name="properties">Optional properties to add to the scope. Will override parent scope values on key conflicts.</param>
        /// <param name="measurements">Optional measurements to add to the scope. Will override parent scope values on key conflicts.</param>
        /// <returns>A new telemetry instance with the scoped context applied.</returns>
        ITelemetry CreateScope(
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null);

        /// <summary>
        /// Tracks an exception with optional properties, measurements, and severity.
        /// Properties and measurements from the current scope (if any) are automatically included.
        /// </summary>
        /// <param name="exception">The exception to track.</param>
        /// <param name="properties">Optional additional properties specific to this exception.</param>
        /// <param name="measurements">Optional additional measurements specific to this exception.</param>
        /// <param name="severity">The severity level of the exception. Defaults to Error.</param>
        void TrackException(
            Exception exception,
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null,
            TelemetrySeverity severity = TelemetrySeverity.Error);
    }
}