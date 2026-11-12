using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// In-process channel that records every item the Application Insights pipeline hands it, so
    /// the desktop path can be asserted without disk or network I/O.
    /// Shared by every test class that injects a channel into <see cref="Telemetry"/>.
    /// </summary>
    internal sealed class CapturingTelemetryChannel : ITelemetryChannel
    {
        private readonly ConcurrentQueue<Microsoft.ApplicationInsights.Channel.ITelemetry> _items = new();

        public IReadOnlyList<Microsoft.ApplicationInsights.Channel.ITelemetry> Items => _items.ToList();

        public bool? DeveloperMode { get; set; }

        public string EndpointAddress { get; set; } = string.Empty;

        public void Send(Microsoft.ApplicationInsights.Channel.ITelemetry item) => _items.Enqueue(item);

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
