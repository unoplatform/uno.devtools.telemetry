// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Holds process-wide ambient telemetry user context shared by all <see cref="ITelemetry"/> instances.
    /// </summary>
    public static class TelemetryUserContext
    {
        internal const string AuthenticatedUserIdEnvironmentVariable = "UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID";

        private static volatile string? _authenticatedUserId = GetEnvironmentSeed();

        /// <summary>
        /// Gets or sets the authenticated user id attached to every telemetry item emitted by any
        /// <see cref="ITelemetry"/> instance in the current process, including instances created
        /// after the value is set.
        /// </summary>
        /// <remarks>
        /// <para>Set this value when a user signs in and set it to <see langword="null"/> when the user
        /// signs out. Null, empty or whitespace values are normalized to <see langword="null"/>, in which
        /// case no authenticated user id is emitted.</para>
        /// <para>The initial value is seeded once from the <c>UNO_PLATFORM_TELEMETRY_AUTHENTICATED_USER_ID</c>
        /// environment variable, allowing a parent process to flow the value to child processes. An explicit
        /// assignment always takes precedence over the seeded value.</para>
        /// <para>This property is safe to read and write from any thread.</para>
        /// </remarks>
        public static string? AuthenticatedUserId
        {
            get => _authenticatedUserId;
            set => _authenticatedUserId = Normalize(value);
        }

        internal static string? GetEnvironmentSeed()
            => Normalize(Environment.GetEnvironmentVariable(AuthenticatedUserIdEnvironmentVariable));

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
