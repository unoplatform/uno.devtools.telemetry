// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Whether this process runs in a Windows AppContainer. A sandboxed host such as Win32 app isolation runs its app, and
    /// every process the app starts, in one, and asks the user for each read outside the folders it was given.
    /// </summary>
    internal static class AppContainer
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenIsAppContainer = 29;

        /// <summary>
        /// Whether the current process runs in an AppContainer; <see langword="false"/> off Windows, or when it can't be told.
        /// </summary>
        internal static bool IsCurrentProcess()
        {
            if (!PlatformDetection.IsWindows)
            {
                return false;
            }

            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
                {
                    return false;
                }

                try
                {
                    return GetTokenInformation(token, TokenIsAppContainer, out var isAppContainer, sizeof(int), out _)
                        && isAppContainer != 0;
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                TelemetryDiagnostics.Write($"Telemetry could not tell whether it runs in an AppContainer: {e.Message}");
                return false;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
