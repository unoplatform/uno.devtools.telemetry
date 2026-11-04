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
        /// Whether <c>UNO_PLATFORM_TELEMETRY_OPTOUT</c> holds an on value <see cref="ParseFlag"/> recognises.
        /// </summary>
        /// <remarks>
        /// The variable can only turn telemetry off. Any other value, <c>false</c> included, reads as unset and
        /// leaves the decision to the product: a <c>false</c> left over in a shell profile must not override an
        /// opt-out the user chose in the product. It used to force telemetry on, and no product set it.
        /// </remarks>
        internal static bool IsOptedOut()
            => ParseFlag(Environment.GetEnvironmentVariable(OptOutVariable)) == true;

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
