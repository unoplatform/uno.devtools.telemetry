// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Holds process-wide ambient telemetry user context shared by all <see cref="ITelemetry"/> instances.
    /// </summary>
    public static class TelemetryUserContext
    {
        /// <summary>
        /// Name of the environment variable that seeds <see cref="AuthenticatedUserId"/> in a child
        /// process. Set it on the child's start info when launching tools that should attribute
        /// telemetry to the same signed-in account. Exposed as a field (not a const) so consumers
        /// reference this assembly's value rather than a compile-time copy.
        /// </summary>
        public static readonly string AuthenticatedUserIdEnvironmentVariable = "UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID";

        // Application Insights caps the ai.user.authUserId context tag at 1024 characters; longer
        // values normalize to null (absent) rather than truncated (a prefix could collide with
        // another account id — the failure direction must be "unattributed", never "wrong user").
        internal const int MaxAuthenticatedUserIdLength = 1024;

        private static volatile string? _authenticatedUserId = GetEnvironmentSeed();

        /// <summary>
        /// Gets or sets the authenticated user id attached to every telemetry item emitted by the
        /// <see cref="ITelemetry"/> implementations this package provides, process-wide — including
        /// instances created after the value is set.
        /// </summary>
        /// <remarks>
        /// <para>Set this value when a user signs in and set it to <see langword="null"/> when the user
        /// signs out. Null, empty, whitespace, or over-long (&gt; 1024 characters) values are normalized
        /// to <see langword="null"/>, in which case no authenticated user id is emitted.</para>
        /// <para>The initial value is seeded from the <c>UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID</c>
        /// environment variable, read once when this type is first used — allowing a parent process to
        /// flow the value to child processes it launches. An explicit assignment always takes precedence
        /// over the seeded value.</para>
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
                    // Length only — the raw value must never reach logs.
                    LogDiagnostic($"Authenticated user id assignment normalized to null (input length {value.Length}).");
                }

                _authenticatedUserId = normalized;
            }
        }

        internal static string? GetEnvironmentSeed()
        {
            try
            {
                var seed = Normalize(Environment.GetEnvironmentVariable(AuthenticatedUserIdEnvironmentVariable));
                if (seed is not null)
                {
                    LogDiagnostic($"Authenticated user id seeded from environment (length {seed.Length}).");
                }

                return seed;
            }
            catch (Exception e)
            {
                // Deliberately generic: this runs in a static field initializer, and ANY escaping
                // exception (e.g. SecurityException from the env read) would poison the type forever —
                // TypeInitializationException on every subsequent access, from inside consumer
                // sign-in code. Degrade to unseeded. (LogDiagnostic itself never throws.)
                LogDiagnostic($"Authenticated user id environment seed failed: {e.Message}");
                return null;
            }
        }

        private static string? Normalize(string? value)
            => value is { Length: > 0 and <= MaxAuthenticatedUserIdLength } && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        private static void LogDiagnostic(string message)
        {
            // Never throws: a misbehaving Trace listener must not escape into the property setter
            // or the static field initializer (where it would poison the type).
            try
            {
                Debug.WriteLine(message);
                Trace.WriteLine(message);
            }
            catch
            {
                // Diagnostics are best-effort only.
            }
        }
    }
}
