[assembly: Uno.DevTools.Telemetry.Telemetry("test-key", EventsPrefix = "test-prefix")] // For test context

namespace Uno.DevTools.Telemetry.Tests;

[TestClass]
public class TelemetryGenericDiTests
{
    public class MyContext {}

    [TestMethod]
    public void Given_TelemetryGenericDiTests_When_ITelemetryT_FileTelemetry_Writes_To_File_Then_Content_Is_Valid()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_FILE", tempFile);
        try
        {
            IServiceCollection services = new ServiceCollection();
            services.AddTelemetry();
            var provider = services.BuildServiceProvider();
            var telemetry = provider.GetRequiredService<ITelemetry<MyContext>>();

            // Act
            telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(tempFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Should().HaveCount(1, "Should have exactly one telemetry event written");
            lines[0].Should().Contain("TestEvent");
            lines[0].Should().Contain("foo");
            lines[0].Should().Contain("bar");
            lines[0].Should().Contain("test-prefix");
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_FILE", null);
        }
    }
}
