using System.Collections.Generic;
using System.Text.Json;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// The WASM sender receives the authenticated user id that <see cref="Telemetry"/> captured at
    /// the tracking call; it never reads the ambient store itself, so these tests need no reset.
    /// </summary>
    [TestClass]
    public class WasmHttpSenderAuthenticatedUserTests
    {
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
        public void Given_AuthenticatedUserId_When_CreateEventEnvelope_Then_TagsContainAuthUserIdAndMachineId()
        {
            // Arrange
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1", "user-42");

            // Assert: the wire key is hardcoded on purpose, the test exists to pin it.
            var tags = GetTags(envelope);
            tags.Should().Contain("ai.user.authUserId", "user-42");
            tags.Should().Contain("ai.user.id", "machine-1");
        }

        [TestMethod]
        public void Given_AuthenticatedUserId_When_CreateExceptionEnvelope_Then_TagsContainAuthUserIdAndMachineId()
        {
            // Arrange
            var sender = new WasmHttpSender("test-key", "test-prefix");

            // Act
            var envelope = sender.CreateExceptionEnvelope(
                new InvalidOperationException("test"), ExceptionSeverity.Error, null, null, "machine-1", "session-1", "user-42");

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
            var envelope = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1", null);

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
                new InvalidOperationException("test"), ExceptionSeverity.Error, null, null, "machine-1", "session-1", null);

            // Assert
            GetTags(envelope).Should().NotContainKey("ai.user.authUserId");
        }

        [TestMethod]
        public void Given_AmbientValueSetAfterCapture_When_CreateEventEnvelope_Then_CapturedValueWins()
        {
            // Arrange: the sender must not consult the ambient store at envelope time.
            var sender = new WasmHttpSender("test-key", "test-prefix");
            try
            {
                TelemetryUserContext.AuthenticatedUserId = "user-late";

                // Act
                var envelope = sender.CreateEventEnvelope("test-event", null, null, "machine-1", "session-1", null);

                // Assert
                GetTags(envelope).Should().NotContainKey("ai.user.authUserId");
            }
            finally
            {
                TelemetryUserContext.AuthenticatedUserId = null;
            }
        }
    }
}
