using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class TelemetryUserContextTests
    {
        [TestInitialize]
        public void Initialize()
        {
            // The store can be non-null at startup via the environment seed (read at type init) —
            // explicit assignment wins over the seed, so this guarantees a deterministic baseline.
            TelemetryUserContext.AuthenticatedUserId = null;
        }

        [TestCleanup]
        public void Cleanup()
        {
            TelemetryUserContext.AuthenticatedUserId = null;
        }

        [TestMethod]
        public void Given_NoAuthenticatedUser_When_ReadingAuthenticatedUserId_Then_ReturnsNull()
        {
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_MultipleInstancesEmit_Then_EveryEventCarriesIt()
        {
            // Arrange — two independent instances, including one created AFTER the value was set:
            // the ambient store is the single entry point, no instance participates in the write.
            var tempPath = Path.Join(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.log");
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
        public void Given_ValueAtMaxLength_When_SettingAuthenticatedUserId_Then_ValueIsKept()
        {
            // Act
            var value = new string('a', TelemetryUserContext.MaxAuthenticatedUserIdLength);
            TelemetryUserContext.AuthenticatedUserId = value;

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().Be(value);
        }

        [TestMethod]
        public void Given_ValueOverMaxLength_When_SettingAuthenticatedUserId_Then_NormalizedToNull()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";

            // Act — over-long ids must become absent, never a truncated (possibly colliding) prefix.
            TelemetryUserContext.AuthenticatedUserId = new string('a', TelemetryUserContext.MaxAuthenticatedUserIdLength + 1);

            // Assert
            TelemetryUserContext.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_EnvironmentVariableSet_When_GetEnvironmentSeed_Then_ReturnsValue()
        {
            try
            {
                // Arrange
                Environment.SetEnvironmentVariable(TelemetryUserContext.AuthenticatedUserIdEnvironmentVariable, "user-seed");

                // Act
                var seed = TelemetryUserContext.GetEnvironmentSeed();

                // Assert
                seed.Should().Be("user-seed");
            }
            finally
            {
                Environment.SetEnvironmentVariable(TelemetryUserContext.AuthenticatedUserIdEnvironmentVariable, null);
            }
        }

        [TestMethod]
        public void Given_EnvironmentVariableWhitespace_When_GetEnvironmentSeed_Then_ReturnsNull()
        {
            try
            {
                // Arrange
                Environment.SetEnvironmentVariable(TelemetryUserContext.AuthenticatedUserIdEnvironmentVariable, "   ");

                // Act
                var seed = TelemetryUserContext.GetEnvironmentSeed();

                // Assert
                seed.Should().BeNull();
            }
            finally
            {
                Environment.SetEnvironmentVariable(TelemetryUserContext.AuthenticatedUserIdEnvironmentVariable, null);
            }
        }

        [TestMethod]
        public void Given_ScopedTelemetry_When_AuthenticatedUserIdSet_Then_ScopedEventsCarryIt()
        {
            // Arrange — scopes need no dedicated wiring: the inner sink reads the ambient store.
            var tempPath = Path.Join(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.log");
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
    }
}
