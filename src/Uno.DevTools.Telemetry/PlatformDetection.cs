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
