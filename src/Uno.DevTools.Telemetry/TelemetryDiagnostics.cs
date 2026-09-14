// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Best-effort diagnostic output for the package's own failures and normalizations.
    /// </summary>
    /// <remarks>
    /// Writes to <see cref="Debug"/> (debug builds only) and <see cref="Trace"/>. Nothing is emitted
    /// unless the host registers a <see cref="TraceListener"/>; see the "Operational suppression"
    /// section of docs/usage.md. Never throws: a misbehaving listener must not escape into a
    /// property setter, a fire-and-forget task, or a static initializer.
    /// </remarks>
    internal static class TelemetryDiagnostics
    {
        internal static void Write(string message)
        {
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
