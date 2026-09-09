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
        /// True when <c>UNO_PLATFORM_TELEMETRY_OPTOUT</c> parses as <see langword="true"/>.
        /// </summary>
        internal static bool IsOptedOut()
            => bool.TryParse(Environment.GetEnvironmentVariable(OptOutVariable), out var optedOut) && optedOut;

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
