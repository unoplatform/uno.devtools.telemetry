using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// <c>UNO_PLATFORM_TELEMETRY_OPTOUT</c> accepts the usual spellings of on, not only the one that
    /// <see cref="bool.TryParse(string, out bool)"/> knows: a user who sets it to <c>1</c>, as the
    /// public instructions say, is opted out. Only the literal <c>false</c> forces telemetry on; every
    /// other value, <c>0</c>, <c>no</c> and <c>off</c> included, reads as unset, so the instance's
    /// <c>enabledProvider</c> decides.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide opt-out variable.
    public class TelemetryEnvironmentTests
    {
        private const string DummyInstrumentationKey = "00000000-0000-0000-0000-000000000000";

        private readonly List<Telemetry> _instances = new List<Telemetry>();
        private string? _optOutOriginal;

        [TestInitialize]
        public void Initialize()
        {
            _optOutOriginal = Environment.GetEnvironmentVariable(TelemetryEnvironment.OptOutVariable);
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, null);
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
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, _optOutOriginal);
        }

        private Telemetry CreateTelemetry(CapturingTelemetryChannel channel, Func<bool?>? enabledProvider = null)
        {
            var telemetry = new Telemetry(
                DummyInstrumentationKey,
                "test",
                typeof(TelemetryEnvironmentTests).Assembly,
                sessionId: null,
                blockThreadInitialization: false,
                enabledProvider,
                currentDirectoryProvider: null,
                productName: null,
                channel,
                wasmSender: null);
            _instances.Add(telemetry);
            return telemetry;
        }

        [TestMethod]
        [DataRow("1")]
        [DataRow("true")]
        [DataRow("yes")]
        [DataRow("on")]
        [DataRow("TRUE", DisplayName = "Upper case")]
        [DataRow("Yes", DisplayName = "Mixed case")]
        [DataRow("oN", DisplayName = "Mixed case, two letters")]
        [DataRow(" 1 ", DisplayName = "Surrounding spaces")]
        [DataRow("\tyes\n", DisplayName = "Surrounding tab and newline")]
        public void Given_OptOutSetToAnOnValue_When_Read_Then_IsOptedOut(string value)
        {
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            TelemetryEnvironment.GetOptOut().Should().BeTrue();
        }

        [TestMethod]
        [DataRow("false")]
        [DataRow("FALSE", DisplayName = "Upper case")]
        [DataRow("False", DisplayName = "Mixed case")]
        [DataRow(" false ", DisplayName = "Surrounding spaces")]
        [DataRow("\tfalse\n", DisplayName = "Surrounding tab and newline")]
        public void Given_OptOutSetToFalse_When_Read_Then_IsExplicitlyNotOptedOut(string value)
        {
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            TelemetryEnvironment.GetOptOut().Should().BeFalse();
        }

        [TestMethod]
        [DataRow("0")]
        [DataRow("no")]
        [DataRow("off")]
        [DataRow("No", DisplayName = "Mixed case")]
        [DataRow("oFf", DisplayName = "Mixed case, three letters")]
        [DataRow(" 0 ", DisplayName = "Surrounding spaces")]
        public void Given_OptOutSetToAnotherOffValue_When_Read_Then_IsUnset(string value)
        {
            // Only the literal false may force telemetry on over the product's own opt-out.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            TelemetryEnvironment.GetOptOut().Should().BeNull();
        }

        [TestMethod]
        [DataRow("0")]
        [DataRow("false")]
        [DataRow("no")]
        [DataRow("OFF", DisplayName = "Upper case")]
        public void Given_AnOffValue_When_Parsed_Then_IsFalse(string value)
        {
            // The parser itself reads every off spelling as false; only the opt-out narrows it.
            TelemetryEnvironment.ParseFlag(value).Should().BeFalse();
        }

        [TestMethod]
        [DataRow("   ", DisplayName = "Whitespace only")]
        [DataRow("2")]
        [DataRow("-1")]
        [DataRow("1.0")]
        [DataRow("y")]
        [DataRow("enabled")]
        [DataRow("truee")]
        [DataRow("t rue", DisplayName = "Inner whitespace")]
        public void Given_OptOutSetToAnUnrecognisedValue_When_Read_Then_IsUnset(string value)
        {
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            TelemetryEnvironment.GetOptOut().Should().BeNull();
        }

        [TestMethod]
        public void Given_OptOutUnset_When_Read_Then_IsUnset()
        {
            TelemetryEnvironment.GetOptOut().Should().BeNull();
        }

        [TestMethod]
        public void Given_EmptyOrMissingValue_When_Parsed_Then_IsUnset()
        {
            // Parsed directly: setting a variable to an empty string removes it on some platforms, so
            // the environment cannot be relied on to hold an empty value.
            TelemetryEnvironment.ParseFlag(string.Empty).Should().BeNull();
            TelemetryEnvironment.ParseFlag(null).Should().BeNull();
        }

        [TestMethod]
        [DataRow("1")]
        [DataRow("true")]
        [DataRow(" YES ")]
        [DataRow("On")]
        public async Task Given_OptOutSetToAnOnValue_When_TelemetryCreatedWithoutProvider_Then_ItIsDisabledAndSendsNothing(string value)
        {
            // Arrange: no provider, so only the environment can turn telemetry off.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);
            var channel = new CapturingTelemetryChannel();
            var telemetry = CreateTelemetry(channel);

            // Act
            telemetry.TrackEvent("Evt", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            telemetry.TrackException(new InvalidOperationException("boom"));
            telemetry.ThreadBlockingTrackEvent("Blocking", new Dictionary<string, string>(), new Dictionary<string, double>());
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            telemetry.Enabled.Should().BeFalse();
            channel.Items.Should().BeEmpty();
        }

        [TestMethod]
        public async Task Given_OptOutUnrecognised_When_TelemetryCreatedWithoutProvider_Then_ItStaysEnabled()
        {
            // Arrange: an unrecognised value is not an opt-out; with no provider the default applies.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, "garbage");
            var channel = new CapturingTelemetryChannel();
            var telemetry = CreateTelemetry(channel);

            // Act
            telemetry.TrackEvent("Evt", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            telemetry.Enabled.Should().BeTrue();
            channel.Items.Should().ContainSingle();
        }

        [TestMethod]
        public void Given_OptOutUnrecognised_When_ProviderOptsOut_Then_ProviderDecides()
        {
            // The provider's answer is negated by the constructor: true means opted out.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, "garbage");

            CreateTelemetry(new CapturingTelemetryChannel(), enabledProvider: () => true).Enabled.Should().BeFalse();
        }

        [TestMethod]
        [DataRow("false")]
        [DataRow("FALSE", DisplayName = "Upper case")]
        public void Given_OptOutSetToFalse_When_ProviderOptsOut_Then_EnvironmentWins(string value)
        {
            // The literal false is distinct from unset: it overrides the provider, as it always has.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            CreateTelemetry(new CapturingTelemetryChannel(), enabledProvider: () => true).Enabled.Should().BeTrue();
        }

        [TestMethod]
        [DataRow("0")]
        [DataRow("no")]
        [DataRow("Off")]
        public void Given_OptOutSetToAnotherOffValue_When_ProviderOptsOut_Then_ProviderDecides(string value)
        {
            // 0, no and off don't override a product that opted out on its own.
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, value);

            CreateTelemetry(new CapturingTelemetryChannel(), enabledProvider: () => true).Enabled.Should().BeFalse();
        }

        [TestMethod]
        public void Given_OptOutSetToAnOnValue_When_ProviderSaysNotOptedOut_Then_EnvironmentWins()
        {
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, "1");

            CreateTelemetry(new CapturingTelemetryChannel(), enabledProvider: () => false).Enabled.Should().BeFalse();
        }
    }
}
