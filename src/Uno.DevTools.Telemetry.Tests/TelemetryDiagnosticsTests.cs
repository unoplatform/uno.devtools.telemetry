using System.Diagnostics;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide Trace.Listeners collection.
    public class TelemetryDiagnosticsTests
    {
        private readonly ThrowingTraceListener _throwing = new ThrowingTraceListener();

        [TestInitialize]
        public void Initialize() => Trace.Listeners.Add(_throwing);

        [TestCleanup]
        public void Cleanup()
        {
            Trace.Listeners.Remove(_throwing);
            TelemetryUserContext.AuthenticatedUserId = null;
        }

        [TestMethod]
        public void Given_ThrowingTraceListener_When_Write_Then_DoesNotThrow()
        {
            // The same helper backs the WASM sender's catch blocks and the setter below; a listener fault
            // escaping it would turn a swallowed telemetry failure into an unobserved task exception.
            var act = () => TelemetryDiagnostics.Write("diagnostic");

            act.Should().NotThrow();
        }

        [TestMethod]
        public void Given_ThrowingTraceListener_When_AssignmentNormalizesToNull_Then_SetterDoesNotThrow()
        {
            var act = () => TelemetryUserContext.AuthenticatedUserId = "   ";

            act.Should().NotThrow();
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }
    }
}
