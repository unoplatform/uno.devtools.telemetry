// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Extension methods for ITelemetry providing default scope implementation.
    /// </summary>
    public static class TelemetryExtensions
    {
        /// <summary>
        /// Creates a new telemetry scope with specified properties and measurements.
        /// This is the default implementation that works with any ITelemetry instance.
        /// Scopes are nested and merged, with child properties overriding parent properties on key conflicts.
        /// </summary>
        /// <param name="telemetry">The telemetry instance to create a scope for.</param>
        /// <param name="properties">Optional properties to add to the scope. Will override parent scope values on key conflicts.</param>
        /// <param name="measurements">Optional measurements to add to the scope. Will override parent scope values on key conflicts.</param>
        /// <returns>A new telemetry instance with the scoped context applied.</returns>
        public static ITelemetry CreateScope(
            this ITelemetry telemetry,
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            // If telemetry is disabled, return the same instance
            if (!telemetry.Enabled)
            {
                return telemetry;
            }

            // Wrap in ScopedTelemetry for implementation-agnostic scope support
            return new ScopedTelemetry(telemetry, properties, measurements);
        }
    }
}
