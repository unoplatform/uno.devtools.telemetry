using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            Assert.AreEqual(1, lines.Length, "Should have exactly one telemetry event written");
            StringAssert.Contains(lines[0], "TestEvent");
            StringAssert.Contains(lines[0], "foo");
            StringAssert.Contains(lines[0], "bar");
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_FILE", null);
        }
    }
}
