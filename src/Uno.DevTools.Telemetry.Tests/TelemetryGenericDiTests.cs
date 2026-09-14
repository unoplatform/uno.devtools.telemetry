[assembly: Uno.DevTools.Telemetry.Telemetry("test-key", EventsPrefix = "test-prefix")] // For test context

namespace Uno.DevTools.Telemetry.Tests;

[TestClass]
[DoNotParallelize] // Mutates process-wide environment variables and the ambient authenticated user id.
public class TelemetryGenericDiTests
{
    public class MyContext {}

    private readonly TempFiles _tempFiles = new TempFiles();
    private string? _optOutOriginal;

    [TestInitialize]
    public void Initialize()
    {
        // The opt-out governs the file lane too, so a machine-wide opt-out would route these tests to
        // the disabled Application Insights sink. Pin it off.
        _optOutOriginal = Environment.GetEnvironmentVariable(TelemetryEnvironment.OptOutVariable);
        Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TelemetryUserContext.AuthenticatedUserId = null;
        Environment.SetEnvironmentVariable(TelemetryEnvironment.FileVariable, null);
        Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, _optOutOriginal);
        _tempFiles.Cleanup();
    }

    [TestMethod]
    public void Given_TelemetryGenericDiTests_When_ITelemetryT_FileTelemetry_Writes_To_File_Then_Content_Is_Valid()
    {
        // Arrange
        var tempFile = _tempFiles.Create();
        Environment.SetEnvironmentVariable(TelemetryEnvironment.FileVariable, tempFile);
        IServiceCollection services = new ServiceCollection();
        services.AddTelemetry();
        using var provider = services.BuildServiceProvider();
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

    [TestMethod]
    public void Given_TelemetryGenericDiTests_When_AuthenticatedUserIdSet_Then_FileOutputContainsAuthenticatedUserId()
    {
        // Arrange
        var tempFile = _tempFiles.Create();
        Environment.SetEnvironmentVariable(TelemetryEnvironment.FileVariable, tempFile);
        IServiceCollection services = new ServiceCollection();
        services.AddTelemetry();
        using var provider = services.BuildServiceProvider();
        var telemetry = provider.GetRequiredService<ITelemetry<MyContext>>();

        // Act: the ambient store covers DI-resolved instances the consumer never constructed directly.
        TelemetryUserContext.AuthenticatedUserId = "user-42";
        telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);
        telemetry.Flush();

        // Assert
        var lines = File.ReadAllLines(tempFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        lines.Should().HaveCount(1, "Should have exactly one telemetry event written");
        lines[0].Should().Contain("AuthenticatedUserId");
        lines[0].Should().Contain("user-42");
    }

    [TestMethod]
    public void Given_OptOutSet_When_FileTelemetryRequestedThroughGenericDi_Then_NothingIsWrittenToDisk()
    {
        // Arrange: an organisation-wide opt-out plus a developer's file redirect. The opt-out must win,
        // otherwise the authenticated user id lands on disk in cleartext despite the kill switch.
        var tempFile = _tempFiles.Create();
        Environment.SetEnvironmentVariable(TelemetryEnvironment.FileVariable, tempFile);
        Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, "true");
        TelemetryUserContext.AuthenticatedUserId = "user-42";
        IServiceCollection services = new ServiceCollection();
        services.AddTelemetry();
        using var provider = services.BuildServiceProvider();
        var telemetry = provider.GetRequiredService<ITelemetry<MyContext>>();

        // Act
        telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);
        telemetry.Flush();

        // Assert
        telemetry.Enabled.Should().BeFalse();
        File.Exists(tempFile).Should().BeFalse("FileTelemetry would have created the file on the first write");
    }

    [TestMethod]
    public void Given_OptOutSet_When_FileTelemetryRequestedThroughAddTelemetryOverload_Then_NothingIsWrittenToDisk()
    {
        // Arrange: same guarantee for the non-generic registration, which selects the sink separately.
        var tempFile = _tempFiles.Create();
        Environment.SetEnvironmentVariable(TelemetryEnvironment.FileVariable, tempFile);
        Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, "true");
        TelemetryUserContext.AuthenticatedUserId = "user-42";
        IServiceCollection services = new ServiceCollection();
        services.AddTelemetry("test-key", "test-prefix");
        using var provider = services.BuildServiceProvider();
        var telemetry = provider.GetRequiredService<ITelemetry>();

        // Act
        telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);
        telemetry.Flush();

        // Assert
        telemetry.Enabled.Should().BeFalse();
        File.Exists(tempFile).Should().BeFalse("FileTelemetry would have created the file on the first write");
    }
}
