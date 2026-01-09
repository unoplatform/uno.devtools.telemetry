// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Severity level for telemetry events and exceptions.
    /// Maps to Application Insights SeverityLevel for exception telemetry.
    /// </summary>
    public enum TelemetrySeverity
    {
        /// <summary>
        /// Critical severity - system failures or catastrophic errors.
        /// </summary>
        Critical = 0,

        /// <summary>
        /// Error severity - operation failures that prevent normal execution.
        /// </summary>
        Error = 1,

        /// <summary>
        /// Warning severity - non-critical issues that may require attention.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Informational severity - general informational messages.
        /// </summary>
        Info = 3,

        /// <summary>
        /// Debug severity - detailed diagnostic information.
        /// </summary>
        Debug = 4
    }
}
