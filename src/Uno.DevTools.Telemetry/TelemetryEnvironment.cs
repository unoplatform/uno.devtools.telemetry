// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Single reader for the environment variables that select or disable a telemetry sink.
    /// </summary>
    internal static class TelemetryEnvironment
    {
        internal const string OptOutVariable = "UNO_PLATFORM_TELEMETRY_OPTOUT";
        internal const string FileVariable = "UNO_PLATFORM_TELEMETRY_FILE";

        /// <summary>
        /// Tri-state read of <c>UNO_PLATFORM_TELEMETRY_OPTOUT</c>: <see langword="true"/> for any on value
        /// <see cref="ParseFlag"/> recognises, <see langword="false"/> for the literal word <c>false</c>
        /// only, <see langword="null"/> otherwise. The <see cref="Telemetry"/> constructor treats an
        /// explicit <see langword="false"/> as forced-enable, overriding its <c>enabledProvider</c>, so the
        /// three states must stay distinct here.
        /// </summary>
        internal static bool? GetOptOut()
        {
            var value = Environment.GetEnvironmentVariable(OptOutVariable);

            return ParseFlag(value) switch
            {
                true => true,
                // Forcing telemetry on over the product's own opt-out stays limited to the word it has
                // always been, so 0, no and off read as unset and leave the decision to the product.
                false when string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase) => false,
                _ => null,
            };
        }

        /// <summary>
        /// Parses an on/off environment value: <c>1</c>, <c>true</c>, <c>yes</c> and <c>on</c> are
        /// <see langword="true"/>; <c>0</c>, <c>false</c>, <c>no</c> and <c>off</c> are
        /// <see langword="false"/>; case and surrounding whitespace are ignored. Anything else, empty
        /// included, is <see langword="null"/>. <see cref="bool.TryParse(string, out bool)"/> is not used
        /// because it rejects <c>1</c>, which the opt-out is documented to accept.
        /// </summary>
        internal static bool? ParseFlag(string? value)
            => value?.Trim().ToUpperInvariant() switch
            {
                "1" or "TRUE" or "YES" or "ON" => true,
                "0" or "FALSE" or "NO" or "OFF" => false,
                _ => null,
            };

        private static bool IsOptedOut() => GetOptOut() == true;

        /// <summary>
        /// Returns the <c>UNO_PLATFORM_TELEMETRY_FILE</c> path when file-based telemetry should be
        /// selected, or <see langword="null"/> when it is unset or when the opt-out is set. The opt-out
        /// governs every lane: with it set, an authenticated user id must not be written to disk either.
        /// </summary>
        internal static string? GetFileTelemetryPath()
        {
            if (IsOptedOut())
            {
                return null;
            }

            var filePath = Environment.GetEnvironmentVariable(FileVariable);
            return string.IsNullOrEmpty(filePath) ? null : filePath;
        }
    }
}
