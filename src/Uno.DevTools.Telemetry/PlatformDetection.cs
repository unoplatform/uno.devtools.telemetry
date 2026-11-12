// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Provides platform detection utilities for telemetry.
    /// </summary>
    internal static class PlatformDetection
    {
        /// <summary>
        /// Cached result of WASM/Browser platform detection.
        /// </summary>
        public static readonly bool IsWasmBrowser = DetectWasmBrowser();

        public static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Whether the OS temp folder (<see cref="Path.GetTempPath"/>) belongs to the current user alone.
        /// </summary>
        /// <remarks>
        /// Windows gives each user a temp folder of their own. macOS does too, but only through <c>TMPDIR</c>, which a
        /// login session sets and sudo, cron and launchd daemons usually don't: without it, the temp folder is the shared
        /// <c>/tmp</c>. Linux, the BSDs and other Unix systems use the shared <c>/tmp</c> unless told otherwise, and a
        /// <c>TMPDIR</c> there says nothing about who else can use it.
        /// </remarks>
        /// <param name="isWindows">Whether this process runs on Windows.</param>
        /// <param name="isMacOS">Whether this process runs on macOS.</param>
        /// <param name="tmpDir">The value of <c>TMPDIR</c>, if any.</param>
        internal static bool IsTempFolderPerUser(bool isWindows, bool isMacOS, string? tmpDir)
            => isWindows || (isMacOS && !string.IsNullOrEmpty(tmpDir) && Path.IsPathRooted(tmpDir));

        /// <summary>
        /// Detects if the current platform is WebAssembly/Browser.
        /// </summary>
        private static bool DetectWasmBrowser()
        {
#if NET5_0_OR_GREATER
            return OperatingSystem.IsBrowser() || OperatingSystem.IsWasi();
#else
            // For netstandard2.0, check RuntimeInformation.OSDescription
            var osDescription = RuntimeInformation.OSDescription;
            return osDescription.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
                   osDescription.Contains("WebAssembly", StringComparison.OrdinalIgnoreCase) ||
                   osDescription.Contains("WASI", StringComparison.OrdinalIgnoreCase);
#endif
        }
    }
}
