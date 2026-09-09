using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// Desktop (Application Insights SDK) path: each item carries the authenticated user id that was
    /// current when the tracking call was made, not when the queued track task drains (spec 002 EC-1).
    /// The queue is gated on background initialization, so every call below is queued, not sent, at
    /// the moment the ambient value changes.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide ambient authenticated user id and UNO_PLATFORM_TELEMETRY_OPTOUT.
    public class TelemetryAuthenticatedUserCaptureTests
    {
        private const string DummyInstrumentationKey = "00000000-0000-0000-0000-000000000000";

        private readonly List<Telemetry> _instances = new List<Telemetry>();
        private string? _optOutOriginal;

        [TestInitialize]
        public void Initialize()
        {
            // A machine-wide UNO_PLATFORM_TELEMETRY_OPTOUT=true would disable the instance and fail these
            // tests for the wrong reason; pin it off for the duration.
            _optOutOriginal = Environment.GetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT");
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", null);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // ITelemetry exposes Dispose() without implementing IDisposable, so no using declaration.
            foreach (var instance in _instances)
            {
                instance.Dispose();
            }

            _instances.Clear();
            TelemetryUserContext.AuthenticatedUserId = null;
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", _optOutOriginal);
        }

        private Telemetry CreateTelemetry(CapturingTelemetryChannel channel, bool blockThreadInitialization = false)
        {
            var telemetry = new Telemetry(
                DummyInstrumentationKey,
                "test",
                typeof(TelemetryAuthenticatedUserCaptureTests).Assembly,
                sessionId: null,
                blockThreadInitialization,
                enabledProvider: null,
                currentDirectoryProvider: null,
                productName: null,
                channel);
            _instances.Add(telemetry);
            return telemetry;
        }

        [TestMethod]
        public async Task Given_UserSignedInAtTrackEvent_When_AnotherUserSignsInBeforeQueueDrains_Then_ItemCarriesUserAtCallTime()
        {
            // Arrange
            var channel = new CapturingTelemetryChannel();
            TelemetryUserContext.AuthenticatedUserId = "user-a";
            var telemetry = CreateTelemetry(channel);

            // Act: sign-out then sign-in while the call is still queued behind initialization.
            telemetry.TrackEvent("SignOutClicked", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            TelemetryUserContext.AuthenticatedUserId = null;
            TelemetryUserContext.AuthenticatedUserId = "user-b";
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            var item = channel.Items.Should().ContainSingle().Subject.Should().BeOfType<EventTelemetry>().Subject;
            item.Name.Should().Be("test/SignOutClicked");
            item.Context.User.AuthenticatedUserId.Should().Be("user-a");
        }

        [TestMethod]
        public async Task Given_UserSignedInAtTrackException_When_UserSignsOutBeforeQueueDrains_Then_ItemCarriesUserAtCallTime()
        {
            // Arrange
            var channel = new CapturingTelemetryChannel();
            TelemetryUserContext.AuthenticatedUserId = "user-a";
            var telemetry = CreateTelemetry(channel);

            // Act
            telemetry.TrackException(new InvalidOperationException("boom"));
            TelemetryUserContext.AuthenticatedUserId = null;
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            var item = channel.Items.Should().ContainSingle().Subject.Should().BeOfType<ExceptionTelemetry>().Subject;
            item.Context.User.AuthenticatedUserId.Should().Be("user-a");
        }

        [TestMethod]
        public async Task Given_NoUserAtTrackEvent_When_UserSignsInBeforeQueueDrains_Then_ItemCarriesNoUser()
        {
            // Arrange: the mirror image. A startup event emitted before sign-in must not be attributed to
            // the user who signs in while it is still queued.
            var channel = new CapturingTelemetryChannel();
            var telemetry = CreateTelemetry(channel);

            // Act
            telemetry.TrackEvent("Startup", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            TelemetryUserContext.AuthenticatedUserId = "user-a";
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            channel.Items.Should().ContainSingle().Subject.Context.User.AuthenticatedUserId.Should().BeNull();
        }

        [TestMethod]
        public void Given_UserSignedIn_When_ThreadBlockingTrackEvent_Then_ItemCarriesUserAtCallTime()
        {
            // Arrange: the synchronous path bypasses the queue and stamps at call time as well.
            var channel = new CapturingTelemetryChannel();
            var telemetry = CreateTelemetry(channel, blockThreadInitialization: true);
            TelemetryUserContext.AuthenticatedUserId = "user-a";

            // Act
            telemetry.ThreadBlockingTrackEvent("Blocking", new Dictionary<string, string>(), new Dictionary<string, double>());
            TelemetryUserContext.AuthenticatedUserId = null;

            // Assert
            channel.Items.Should().ContainSingle().Subject.Context.User.AuthenticatedUserId.Should().Be("user-a");
        }

        [TestMethod]
        public async Task Given_UserSignedIn_When_TrackEvent_Then_MachineIdAndCustomPropertiesAreUnchanged()
        {
            // Arrange: FR-012, the anonymous machine id (ai.user.id) is independent of the authenticated
            // user id, and the event payload is unaffected by the stamping.
            var channel = new CapturingTelemetryChannel();
            TelemetryUserContext.AuthenticatedUserId = "user-a";
            var telemetry = CreateTelemetry(channel);

            // Act
            telemetry.TrackEvent("Evt", new Dictionary<string, string> { ["foo"] = "bar" }, new Dictionary<string, double> { ["n"] = 1.5 });
            await telemetry.FlushAsync(CancellationToken.None);
            var machineId = await telemetry.GetMachineIdAsync(CancellationToken.None);

            // Assert
            var item = channel.Items.Should().ContainSingle().Subject.Should().BeOfType<EventTelemetry>().Subject;
            item.Context.User.Id.Should().Be(machineId);
            item.Context.User.AuthenticatedUserId.Should().Be("user-a");
            item.Properties.Should().Contain("foo", "bar");
            item.Metrics.Should().Contain("n", 1.5);
        }

        /// <summary>
        /// In-process channel that records every item the Application Insights pipeline hands it, so
        /// the desktop path can be asserted without disk or network I/O.
        /// </summary>
        private sealed class CapturingTelemetryChannel : ITelemetryChannel
        {
            private readonly ConcurrentQueue<Microsoft.ApplicationInsights.Channel.ITelemetry> _items = new();

            public IReadOnlyList<Microsoft.ApplicationInsights.Channel.ITelemetry> Items => _items.ToList();

            public bool? DeveloperMode { get; set; }

            public string EndpointAddress { get; set; } = string.Empty;

            public void Send(Microsoft.ApplicationInsights.Channel.ITelemetry item) => _items.Enqueue(item);

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
