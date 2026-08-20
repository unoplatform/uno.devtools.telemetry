using Microsoft.ApplicationInsights.DataContracts;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class AuthenticatedUserTelemetryInitializerTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            TelemetryUserContext.AuthenticatedUserId = null;
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_InitializingEventTelemetry_Then_ItemContextCarriesAuthUserId()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            var initializer = new AuthenticatedUserTelemetryInitializer();
            var item = new EventTelemetry("test-event");

            // Act
            initializer.Initialize(item);

            // Assert
            item.Context.User.AuthenticatedUserId.Should().Be("user-42");
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdSet_When_InitializingExceptionTelemetry_Then_ItemContextCarriesAuthUserId()
        {
            // Arrange
            TelemetryUserContext.AuthenticatedUserId = "user-42";
            var initializer = new AuthenticatedUserTelemetryInitializer();
            var item = new ExceptionTelemetry(new InvalidOperationException("test"));

            // Act
            initializer.Initialize(item);

            // Assert
            item.Context.User.AuthenticatedUserId.Should().Be("user-42");
        }

        [TestMethod]
        public void Given_NoAuthenticatedUserId_When_InitializingEventTelemetry_Then_ItemContextAuthUserIdIsNull()
        {
            // Arrange
            var initializer = new AuthenticatedUserTelemetryInitializer();
            var item = new EventTelemetry("test-event");

            // Act
            initializer.Initialize(item);

            // Assert
            item.Context.User.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_AuthenticatedUserIdChangedBetweenItems_When_Initializing_Then_EachItemReflectsCurrentValue()
        {
            // Arrange
            var initializer = new AuthenticatedUserTelemetryInitializer();
            var first = new EventTelemetry("first");
            var second = new EventTelemetry("second");
            var third = new EventTelemetry("third");

            // Act
            TelemetryUserContext.AuthenticatedUserId = "user-a";
            initializer.Initialize(first);
            TelemetryUserContext.AuthenticatedUserId = "user-b";
            initializer.Initialize(second);
            TelemetryUserContext.AuthenticatedUserId = null;
            initializer.Initialize(third);

            // Assert
            first.Context.User.AuthenticatedUserId.Should().Be("user-a");
            second.Context.User.AuthenticatedUserId.Should().Be("user-b");
            third.Context.User.AuthenticatedUserId.Should().BeNull();
        }
    }
}
