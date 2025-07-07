using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class FileTelemetryTests
    {
        private string GetTempFilePath() => Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.log");

        [TestMethod]
        public void TrackEvent_WritesToFile()
        {
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");

            telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);

            telemetry.Flush();

            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("TestEvent");
            lines[0].Should().Contain("foo");
            lines[0].Should().Contain("bar");
        }

        [TestMethod]
        public void TrackEvent_MultipleEvents_AreAllWritten()
        {
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "multi");

            for (var i = 0; i < 5; i++)
            {
                telemetry.TrackEvent($"Event{i}", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            }
            telemetry.Flush();

            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(5);
            for (var i = 0; i < 5; i++)
            {
                lines[i].Should().Contain($"Event{i}");
            }
        }
    }
}
