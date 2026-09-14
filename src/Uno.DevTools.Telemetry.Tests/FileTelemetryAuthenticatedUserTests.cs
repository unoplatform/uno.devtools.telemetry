using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class FileTelemetryAuthenticatedUserTests
    {
        private static readonly DateTimeOffset SnapshotTime = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly TempFiles _tempFiles = new TempFiles();

        [TestCleanup]
        public void Cleanup()
        {
            TelemetryUserContext.AuthenticatedUserId = null;
            _tempFiles.Cleanup();
        }

        private static JsonDocument ParseLine(string line)
        {
            // Lines are "<context>: <json>" when a context prefix is set.
            var jsonStart = line.IndexOf('{');
            return JsonDocument.Parse(line.Substring(jsonStart));
        }

        private static FakeTimeProvider CreatePinnedClock()
        {
            // FakeTimeProvider does not auto-advance; UTC keeps the serialized local time stable.
            var clock = new FakeTimeProvider(SnapshotTime);
            clock.SetLocalTimeZone(TimeZoneInfo.Utc);
            return clock;
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_TrackEvent_Then_JsonLineContainsAuthenticatedUserId()
        {
            // Arrange
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test");
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            telemetry.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            using var document = ParseLine(lines[0]);
            document.RootElement.GetProperty("AuthenticatedUserId").GetString().Should().Be("user-42");
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_TrackException_Then_JsonLineContainsAuthenticatedUserId()
        {
            // Arrange
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test");
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            telemetry.TrackException(new InvalidOperationException("test"));

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            using var document = ParseLine(lines[0]);
            document.RootElement.GetProperty("AuthenticatedUserId").GetString().Should().Be("user-42");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_TrackEvent_Then_JsonLineHasNoAuthenticatedUserIdProperty()
        {
            // Arrange
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act
            telemetry.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            using var document = ParseLine(lines[0]);
            document.RootElement.TryGetProperty("AuthenticatedUserId", out _).Should().BeFalse(
                "the output must stay identical to previous versions when no user is authenticated");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_TrackException_Then_JsonLineHasNoAuthenticatedUserIdProperty()
        {
            // Arrange
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act
            telemetry.TrackException(new InvalidOperationException("test"));

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            using var document = ParseLine(lines[0]);
            document.RootElement.TryGetProperty("AuthenticatedUserId", out _).Should().BeFalse(
                "the exception shape must also stay identical to previous versions when no user is authenticated");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_TrackEvent_Then_OutputLineIsByteIdenticalToPreviousFormat()
        {
            // Arrange: pinned clock so the whole line is a stable snapshot (SC-004: the unset
            // output must stay byte-identical to releases that predate the AuthenticatedUserId field).
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test", CreatePinnedClock());

            // Act
            telemetry.TrackEvent("SnapshotEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Be(
                """test: {"Type":"event","Timestamp":"2025-01-01T12:00:00","EventName":"test/SnapshotEvent","Properties":null,"Measurements":null}""");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_TrackException_Then_OutputLineIsByteIdenticalToPreviousFormat()
        {
            // Arrange: same pinned clock; an exception that was never thrown has a null StackTrace, so
            // the whole exception shape is a stable snapshot too (FR-008 / SC-004 for the exception path).
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test", CreatePinnedClock());

            // Act
            telemetry.TrackException(new InvalidOperationException("snapshot"));

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Be(
                """test: {"Type":"exception","Timestamp":"2025-01-01T12:00:00","Severity":"Error","Exception":{"Type":"System.InvalidOperationException","Message":"snapshot","StackTrace":null},"Properties":null,"Measurements":null}""");
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_TrackEvent_Then_FieldIsAppendedAfterExistingFields()
        {
            // Arrange: the field must land last so readers keyed on the previous field order are unaffected.
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test", CreatePinnedClock());
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act
            telemetry.TrackEvent("SnapshotEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            File.ReadAllLines(filePath)[0].Should().Be(
                """test: {"Type":"event","Timestamp":"2025-01-01T12:00:00","EventName":"test/SnapshotEvent","Properties":null,"Measurements":null,"AuthenticatedUserId":"user-42"}""");
        }

        [TestMethod]
        public void Given_InstanceCreatedBeforeSet_When_AuthenticatedUserIdSetLater_Then_SubsequentEventsCarryIt()
        {
            // Arrange
            var filePath = _tempFiles.Create();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act
            telemetry.TrackEvent("BeforeSignIn", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            telemetry.TrackEvent("AfterSignIn", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(2);
            using var before = ParseLine(lines[0]);
            using var after = ParseLine(lines[1]);
            before.RootElement.TryGetProperty("AuthenticatedUserId", out _).Should().BeFalse();
            after.RootElement.GetProperty("AuthenticatedUserId").GetString().Should().Be("user-42");
        }
    }
}
