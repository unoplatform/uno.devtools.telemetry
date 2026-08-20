using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class FileTelemetryAuthenticatedUserTests
    {
        private readonly List<string> _filesToCleanup = new List<string>();

        private string GetTempFilePath()
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.log");
            _filesToCleanup.Add(filePath);
            return filePath;
        }

        [TestCleanup]
        public void Cleanup()
        {
            TelemetryUserContext.AuthenticatedUserId = null;

            foreach (var filePath in _filesToCleanup.Where(File.Exists))
            {
                File.Delete(filePath);
            }

            _filesToCleanup.Clear();
        }

        private static JsonDocument ParseLine(string line)
        {
            // Lines are "<context>: <json>" when a context prefix is set.
            var jsonStart = line.IndexOf('{');
            return JsonDocument.Parse(line.Substring(jsonStart));
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_TrackEvent_Then_JsonLineContainsAuthenticatedUserId()
        {
            // Arrange
            var filePath = GetTempFilePath();
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
            var filePath = GetTempFilePath();
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
            var filePath = GetTempFilePath();
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
        public void Given_InstanceCreatedBeforeSet_When_AuthenticatedUserIdSetLater_Then_SubsequentEventsCarryIt()
        {
            // Arrange
            var filePath = GetTempFilePath();
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
