using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id and Trace.Listeners.
    public class TelemetryUserContextTests
    {
        private readonly TempFiles _tempFiles = new TempFiles();
        private readonly CapturingTraceListener _trace = new CapturingTraceListener();

        [TestInitialize]
        public void Initialize() => Trace.Listeners.Add(_trace);

        [TestCleanup]
        public void Cleanup()
        {
            Trace.Listeners.Remove(_trace);
            TelemetryUserContext.AuthenticatedUserId = null;
            _tempFiles.Cleanup();
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_MultipleInstancesEmit_Then_EveryEventCarriesIt()
        {
            // Arrange: two independent instances, including one created AFTER the value was set.
            // The ambient store is the single entry point; no instance participates in the write.
            var tempPath = _tempFiles.Create();
            ITelemetry first = new FileTelemetry(tempPath, "first");

            // Act
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            ITelemetry second = new FileTelemetry(tempPath, "second");
            first.TrackEvent("EventA", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            second.TrackEvent("EventB", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var lines = File.ReadAllLines(tempPath);
            lines.Should().HaveCount(2);
            lines.Should().OnlyContain(line => line.Contains("\"AuthenticatedUserId\":\"user-42\""));
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_SetToNull_Then_ValueIsCleared()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            TelemetryUserContext.AuthenticatedUserId = null;

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_Overwritten_Then_LatestValueWins()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            TelemetryUserContext.AuthenticatedUserId = "user-43";

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().Be("user-43");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("\t")]
        [DataRow(null)]
        public void Given_WhitespaceOrEmptyValue_When_SettingAuthenticatedUserId_Then_NormalizedToNull(string? value)
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            TelemetryUserContext.AuthenticatedUserId = value;

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_MaxAuthenticatedUserIdLength_When_Read_Then_IsThe1024CharacterTagLimit()
        {
            // The public remarks, docs/usage.md and the spec restate this number for consumers, and the
            // boundary tests below use the literal, so lowering the constant cannot pass silently.
            TelemetryUserContext.MaxAuthenticatedUserIdLength.Should().Be(1024);
        }

        [TestMethod]
        public void Given_ValueAt1024Characters_When_SettingAuthenticatedUserId_Then_ValueIsKept()
        {
            // Act
            var value = new string('a', 1024);
            TelemetryUserContext.AuthenticatedUserId = value;

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().Be(value);
        }

        [TestMethod]
        public void Given_ValueOver1024Characters_When_SettingAuthenticatedUserId_Then_NormalizedToNull()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act: over-long ids must become absent, never a truncated prefix (see the rationale on
            // TelemetryUserContext.MaxAuthenticatedUserIdLength).
            TelemetryUserContext.AuthenticatedUserId = new string('a', 1025);

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_OverLongValue_When_SettingAuthenticatedUserId_Then_DiagnosticCarriesLengthButNeverTheValue()
        {
            // Act
            TelemetryUserContext.AuthenticatedUserId = new string('z', 1025);

            // Assert: the Trace line is the only signal a Release consumer gets, so it must fire, and it
            // must carry the length only.
            _trace.Messages.Should().Contain(m => m.Contains("normalized to null") && m.Contains("input length 1025"));
            _trace.Messages.Should().NotContain(m => m.Contains("zzzz"));
        }

        [TestMethod]
        public void Given_ValidValue_When_SettingAuthenticatedUserId_Then_NoDiagnosticIsEmitted()
        {
            // Act
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Assert
            _trace.Messages.Should().NotContain(m => m.Contains("Authenticated user id"));
        }

        [TestMethod]
        public void Given_ThrowingTraceListener_When_AssignmentNormalizesToNull_Then_SetterDoesNotThrow()
        {
            // Arrange: a host listener that faults on every write must not escape into sign-in code.
            var throwing = new ThrowingTraceListener();
            Trace.Listeners.Add(throwing);
            try
            {
                // Act
                var act = () => TelemetryUserContext.AuthenticatedUserId = "   ";

                // Assert
                act.Should().NotThrow();
                TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
            }
            finally
            {
                Trace.Listeners.Remove(throwing);
            }
        }

        [TestMethod]
        public void Given_ThrowingTraceListener_When_TelemetryDiagnosticsWrite_Then_DoesNotThrow()
        {
            // Arrange: the same helper backs the WASM sender's catch blocks and the desktop init failure
            // path; a listener fault escaping it would become an unobserved task exception there.
            var throwing = new ThrowingTraceListener();
            Trace.Listeners.Add(throwing);
            try
            {
                // Act
                var act = () => TelemetryDiagnostics.Write("diagnostic");

                // Assert
                act.Should().NotThrow();
            }
            finally
            {
                Trace.Listeners.Remove(throwing);
            }
        }

        [TestMethod]
        public void Given_ScopedTelemetry_When_AuthenticatedUserIdSet_Then_ScopedEventsCarryIt()
        {
            // Arrange: scopes need no dedicated wiring, the inner sink reads the ambient store.
            var tempPath = _tempFiles.Create();
            ITelemetry inner = new FileTelemetry(tempPath, "test");
            var scoped = inner.CreateScope(properties: new Dictionary<string, string> { { "scopeKey", "scopeValue" } });

            // Act
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            scoped.TrackEvent("ScopedEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var line = File.ReadAllLines(tempPath).Should().ContainSingle().Subject;
            line.Should().Contain("\"AuthenticatedUserId\":\"user-42\"");
            line.Should().Contain("scopeKey");
        }

        /// <summary>
        /// Records every message written to <see cref="Trace"/> while installed, so the tests can assert
        /// what the diagnostics carry and, more importantly, what they never carry.
        /// </summary>
        private sealed class CapturingTraceListener : TraceListener
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
        private sealed class ThrowingTraceListener : TraceListener
        {
            public override void Write(string? message) => throw new InvalidOperationException("listener fault");

            public override void WriteLine(string? message) => throw new InvalidOperationException("listener fault");
        }
    }
}
