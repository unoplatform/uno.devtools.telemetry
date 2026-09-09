using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// Records every message written to <see cref="Trace"/> while installed, so tests can assert
    /// what the package's diagnostics carry (and, more importantly, what they never carry).
    /// </summary>
    internal sealed class CapturingTraceListener : TraceListener
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToList();

        public override void Write(string? message)
        {
            if (message is not null)
            {
                _messages.Enqueue(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                _messages.Enqueue(message);
            }
        }
    }

    /// <summary>
    /// A host listener that faults on every write. The package's diagnostics must survive it.
    /// </summary>
    internal sealed class ThrowingTraceListener : TraceListener
    {
        public override void Write(string? message) => throw new InvalidOperationException("listener fault");

        public override void WriteLine(string? message) => throw new InvalidOperationException("listener fault");
    }
}
