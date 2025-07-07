using System.Collections.Generic;
using System.IO;
using System.Threading;

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

        [TestMethod]
        public void TrackEvent_MultiThreaded_StressTest()
        {
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "stress");
            var threadCount = 16;
            var eventsPerThread = 800;
            var totalEvents = threadCount * eventsPerThread;
            var threads = new List<Thread>();
            var exceptions = new List<Exception>();
            var startEvent = new ManualResetEventSlim(false);

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

            threads.ForEach(t => t.Start());
            startEvent.Set();
            threads.ForEach(t => t.Join());
            telemetry.Flush();

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
