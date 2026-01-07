using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class FileTelemetryTests
    {
        private readonly List<string> _filesToCleanup = new List<string>();

        private string GetTempFilePath()
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.log");
            _filesToCleanup.Add(filePath);
            return filePath;
        }

        [TestCleanup]
        public void Cleanup()
        {
            foreach (var filePath in _filesToCleanup.Where(File.Exists))
            {
                File.Delete(filePath);
            }

            _filesToCleanup.Clear();
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackEvent_Then_WritesToFile()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act
            telemetry.TrackEvent("TestEvent", new Dictionary<string, string> { { "foo", "bar" } }, null);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("TestEvent");
            lines[0].Should().Contain("foo");
            lines[0].Should().Contain("bar");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackEvent_MultipleEvents_Then_AllAreWritten()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "multi");

            // Act
            for (var i = 0; i < 5; i++)
            {
                telemetry.TrackEvent($"Event{i}", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            }
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(5);
            for (var i = 0; i < 5; i++)
            {
                lines[i].Should().Contain($"Event{i}");
            }
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackEvent_MultiThreaded_Then_AllEventsAreWrittenWithoutError()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "stress");
            var threadCount = 16;
            var eventsPerThread = 800;
            var totalEvents = threadCount * eventsPerThread;
            var threads = new List<Thread>();
            var exceptions = new List<Exception>();
            using var startEvent = new ManualResetEventSlim(false);

            for (var t = 0; t < threadCount; t++)
            {
                threads.Add(new Thread(() =>
                {
                    try
                    {
                        startEvent.Wait(); // Ensure all threads start together
                        for (var i = 0; i < eventsPerThread; i++)
                        {
                            telemetry.TrackEvent($"StressEvent", new Dictionary<string, string> { { "thread", Thread.CurrentThread.ManagedThreadId.ToString() }, { "i", i.ToString() } }, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions) { exceptions.Add(ex); }
                    }
                }));
            }

            // Act
            threads.ForEach(t => t.Start());
            startEvent.Set();
            threads.ForEach(t => t.Join());
            telemetry.Flush();

            // Assert
            exceptions.Should().BeEmpty();
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(totalEvents);
            foreach (var line in lines)
            {
                line.Should().Contain("StressEvent");
                line.Should().Contain("thread");
                line.Should().Contain("i");
            }
        }
    }
}
