// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Chooses the folder the desktop channel queues unsent items in.
    /// </summary>
    /// <remarks>
    /// Whoever can write to the folder can leave files there for this process to send, and whoever can read it can read
    /// what this process queued, so it must belong to the current user alone. The temp folder does on Windows and macOS.
    /// On Linux it is usually <c>/tmp</c>, which every user shares, so the queue goes in the user's cache folder instead.
    /// Nothing queued in an earlier location is moved, as when 1.4.0-dev.30 moved the queue into <c>outbox</c>: items left
    /// under the temp folder stay there for the versions that still look for them, and moving them would mean trusting
    /// files that any user could have written.
    /// </remarks>
    internal static class QueueDirectory
    {
        /// <summary>
        /// Returns the queue folder, or <see langword="null"/> on Linux when the user has no home to put it in.
        /// </summary>
        /// <param name="isLinux">Whether this process runs on Linux.</param>
        /// <param name="xdgCacheHome">The value of <c>XDG_CACHE_HOME</c>, if any.</param>
        /// <param name="homeDirectory">The user's home folder, if any.</param>
        /// <param name="tempPath">The temp folder.</param>
        internal static string? GetPath(bool isLinux, string? xdgCacheHome, string? homeDirectory, string tempPath)
        {
            if (!isLinux)
            {
                // Older versions of this library send whatever is queued in ".uno/telemetry" without claiming it first,
                // so the queue lives in a subfolder they never look into.
                return Path.Combine(tempPath, ".uno", "telemetry", "outbox");
            }

            // Like the XDG base directory specification, a relative path is ignored.
            if (IsRooted(xdgCacheHome))
            {
                return Path.Combine(xdgCacheHome, "uno", "telemetry", "outbox");
            }

            if (IsRooted(homeDirectory))
            {
                return Path.Combine(homeDirectory, ".cache", "uno", "telemetry", "outbox");
            }

            // No home, e.g. a service account without one: the only folder left is the shared temp folder, which is what
            // this replaces. Nothing is queued, and so nothing is sent.
            return null;
        }

        private static bool IsRooted([NotNullWhen(true)] string? path)
            => !string.IsNullOrEmpty(path) && Path.IsPathRooted(path);
    }
}
