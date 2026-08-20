using System.Collections.Generic;
using System.Text.Json;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class WasmHttpSenderAuthenticatedUserTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            TelemetryUserContext.AuthenticatedUserId = null;
        }

        private static Dictionary<string, string> GetTags(object envelope)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
            var tags = new Dictionary<string, string>();
            foreach (var tag in document.RootElement.GetProperty("tags").EnumerateObject())
            {
                tags[tag.Name] = tag.Value.GetString() ?? string.Empty;
            }

            return tags;
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_CreateEventEnvelope_Then_TagsContainAuthUserIdAndMachineId()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1");

            // Assert
            var tags = GetTags(envelope);
            tags.Should().Contain("ai.user.authUserId", "user-42");
            tags.Should().Contain("ai.user.id", "machine-1");
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_CreateExceptionEnvelope_Then_TagsContainAuthUserIdAndMachineId()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateExceptionEnvelope(
                new InvalidOperationException("test"), ExceptionSeverity.Error, null, null, "machine-1", "session-1");

            // Assert
            var tags = GetTags(envelope);
            tags.Should().Contain("ai.user.authUserId", "user-42");
            tags.Should().Contain("ai.user.id", "machine-1");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_CreateEventEnvelope_Then_TagsOmitAuthUserId()
        {
            // Arrange
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1");

            // Assert
            var tags = GetTags(envelope);
            tags.Should().NotContainKey("ai.user.authUserId");
            tags.Should().Contain("ai.user.id", "machine-1");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_CreateExceptionEnvelope_Then_TagsOmitAuthUserId()
        {
            // Arrange
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateExceptionEnvelope(
                new InvalidOperationException("test"), ExceptionSeverity.Error, null, null, "machine-1", "session-1");

            // Assert
            GetTags(envelope).Should().NotContainKey("ai.user.authUserId");
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdCleared_When_CreateEventEnvelope_Then_TagIsNoLongerEmitted()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            var sender = new WasmHttpSender("test-key", "test-prefix");
            var whileSignedIn = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1");

            // Act
            TelemetryUserContext.AuthenticatedUserId = null;
            var afterSignOut = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1");

            // Assert
            GetTags(whileSignedIn).Should().Contain("ai.user.authUserId", "user-42");
            GetTags(afterSignOut).Should().NotContainKey("ai.user.authUserId");
        }
    }
}
