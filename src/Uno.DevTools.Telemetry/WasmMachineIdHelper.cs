// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#if NET5_0_OR_GREATER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace Uno.DevTools.Telemetry
{
#if NET5_0_OR_GREATER
    internal static partial class WasmMachineIdHelper
    {
        private static string? _cachedMachineId;

        /// <summary>
        /// Gets a persistent machine ID from browser localStorage.
        /// Falls back to session-specific GUID if localStorage is unavailable.
        /// </summary>
        public static string GetOrCreateMachineId()
        {
            if (_cachedMachineId != null)
            {
                return _cachedMachineId;
            }

            try
            {
                _cachedMachineId = GetMachineIdFromLocalStorage();

                if (string.IsNullOrWhiteSpace(_cachedMachineId))
                {
                    throw new InvalidOperationException("JavaScript returned empty machine ID");
                }
            }
            catch (Exception)
            {
                // If JavaScript interop fails, fall back to session-specific GUID
                _cachedMachineId = Guid.NewGuid().ToString();
            }

            return _cachedMachineId;
        }

        [JSImport("globalThis.unoDevToolsGetOrCreateMachineId")]
        private static partial string GetMachineIdFromLocalStorage();
    }
#else
    internal static class WasmMachineIdHelper
    {
        /// <summary>
        /// For netstandard2.0, returns session-specific GUID.
        /// WASM runs on net9.0+, so this code path won't be used on browser.
        /// </summary>
        public static string GetOrCreateMachineId()
        {
            return Guid.NewGuid().ToString();
        }
    }
#endif
}
