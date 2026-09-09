// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Holds process-wide ambient telemetry user context shared by all <see cref="ITelemetry"/> instances.
    /// </summary>
    public static class TelemetryUserContext
    {
        // The Application Insights ContextTagKeys schema caps the ai.user.authUserId tag at 1024
        // characters (MaxStringLength). Longer values normalize to null (absent) rather than being
        // truncated: a truncated prefix could collide with another account id, so an over-long value
        // is dropped instead of misattributed. The literal is pinned by TelemetryUserContextTests and
        // restated for consumers in the <remarks> below and in docs/usage.md.
        internal const int MaxAuthenticatedUserIdLength = 1024;

        private static volatile string? _authenticatedUserId;

        /// <summary>
        /// Gets or sets the authenticated user id attached to every telemetry item emitted by the
        /// <see cref="ITelemetry"/> implementations this package provides, process-wide, including
        /// instances created after the value is set.
        /// </summary>
        /// <remarks>
        /// <para>Set this value when a user signs in and set it to <see langword="null"/> when the user
        /// signs out. Null, empty, whitespace, or over-long (&gt; 1024 characters) values are normalized
        /// to <see langword="null"/>, in which case no authenticated user id is emitted.</para>
        /// <para>Each item captures the value current when its tracking call is made, even on sinks that
        /// send asynchronously. The value is per process and starts as <see langword="null"/>; a tool
        /// that launches child processes must pass the id to them itself and have the child assign it.</para>
        /// <para>This property is safe to read and write from any thread.</para>
        /// </remarks>
        public static string? AuthenticatedUserId
        {
            get => _authenticatedUserId;
            set
            {
                var normalized = Normalize(value);
                if (normalized is null && value is not null)
                {
                    // Length only: the raw value must never reach logs.
                    TelemetryDiagnostics.Write($"Authenticated user id assignment normalized to null (input length {value.Length}).");
                }

                _authenticatedUserId = normalized;
            }
        }

        // The property pattern is kept on purpose: on netstandard2.0, IsNullOrWhiteSpace carries no
        // [NotNullWhen(false)], so collapsing this into a plain if would need a null-forgiving operator.
        private static string? Normalize(string? value)
            => value is { Length: <= MaxAuthenticatedUserIdLength } && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }
}
