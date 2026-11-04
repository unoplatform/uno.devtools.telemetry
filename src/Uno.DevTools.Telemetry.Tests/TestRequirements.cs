using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// Says which platform-specific tests must run. A test that can't run here is Inconclusive, which a run reports as
    /// skipped and counts as passing, so a CI job that exists to run some of them sets <see cref="Variable"/> to make them
    /// fail instead.
    /// </summary>
    internal static class TestRequirements
    {
        internal const string Variable = "UNO_TELEMETRY_TESTS_REQUIRE";

        /// <summary>
        /// Linux, as a user other than root: the tests that need permissions enforced on this process.
        /// </summary>
        internal const string LinuxUser = "linux-user";

        /// <summary>
        /// Linux, as root: the tests that need root.
        /// </summary>
        internal const string LinuxRoot = "linux-root";

        internal static string? Required => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value ? value : null;

        /// <summary>
        /// Skips a test that can't run here, or fails it when this run is one of <paramref name="requiredIn"/>.
        /// </summary>
        [DoesNotReturn]
        internal static void Skip(string reason, params string[] requiredIn)
        {
            if (Required is { } required && requiredIn.Contains(required))
            {
                Assert.Fail($"{reason} This run sets {Variable}={required}, which requires this test to run.");
            }

            Assert.Inconclusive(reason);
            throw new InvalidOperationException("Not reached");
        }
    }

    [TestClass]
    public class TestRequirementsTests
    {
        [TestMethod]
        public void Given_ARunThatRequiresPlatformTests_When_ItStarts_Then_ItRunsWhereTheyCan()
        {
            // A job that asked for the root tests but runs as an ordinary user would skip them all and still pass.
            switch (TestRequirements.Required)
            {
                case null:
                    return;

                case TestRequirements.LinuxUser:
                    OperatingSystem.IsLinux().Should().BeTrue();
                    Environment.IsPrivilegedProcess.Should().BeFalse("the run must be as a user other than root");
                    return;

                case TestRequirements.LinuxRoot:
                    OperatingSystem.IsLinux().Should().BeTrue();
                    Environment.IsPrivilegedProcess.Should().BeTrue("the run must be as root");
                    return;

                case var unknown:
                    Assert.Fail($"{TestRequirements.Variable}={unknown} is not one of {TestRequirements.LinuxUser} or {TestRequirements.LinuxRoot}.");
                    return;
            }
        }
    }
}
