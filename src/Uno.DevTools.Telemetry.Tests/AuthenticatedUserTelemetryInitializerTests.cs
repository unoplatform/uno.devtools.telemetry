using System.Linq;
using Microsoft.ApplicationInsights.DataContracts;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id.
    public class AuthenticatedUserTelemetryInitializerTests
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
        public void Given_DesktopTelemetry_When_Initialized_Then_AuthenticatedUserInitializerIsRegistered()
        {
            // Arrange & Act — dummy key, blocking init so the pipeline is built before the assert.
            // This guards the single line wiring the initializer into the desktop pipeline (FR-006).
            var telemetry = new Telemetry(
                "00000000-0000-0000-0000-000000000000",
                "test",
                typeof(AuthenticatedUserTelemetryInitializerTests).Assembly,
                blockThreadInitialization: true);
            try
            {
                // Assert
                telemetry.TelemetryConfigurationInternal.Should().NotBeNull();
                telemetry.TelemetryConfigurationInternal!.TelemetryInitializers
                    .OfType<AuthenticatedUserTelemetryInitializer>().Should().ContainSingle();
            }
            finally
            {
                telemetry.Dispose();
            }
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
