using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class ExceptionTelemetryTests
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
        public void Given_FileTelemetry_When_TrackException_Then_WritesExceptionToFile()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var exception = new InvalidOperationException("Test exception");

            // Act
            telemetry.TrackException(exception);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("exception");
            lines[0].Should().Contain("Test exception");
            lines[0].Should().Contain("InvalidOperationException");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithProperties_Then_IncludesProperties()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var exception = new InvalidOperationException("Test exception");
            var properties = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };

            // Act
            telemetry.TrackException(exception, properties);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("key1");
            lines[0].Should().Contain("value1");
            lines[0].Should().Contain("key2");
            lines[0].Should().Contain("value2");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithMeasurements_Then_IncludesMeasurements()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var exception = new InvalidOperationException("Test exception");
            var measurements = new Dictionary<string, double> { { "metric1", 123.45 }, { "metric2", 67.89 } };

            // Act
            telemetry.TrackException(exception, measurements: measurements);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("metric1");
            lines[0].Should().Contain("123.45");
            lines[0].Should().Contain("metric2");
            lines[0].Should().Contain("67.89");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithSeverity_Then_IncludesSeverity()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var exception = new InvalidOperationException("Test exception");

            // Act
            telemetry.TrackException(exception, severity: ExceptionSeverity.Critical);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("Critical");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithDifferentSeverities_Then_WritesCorrectSeverity()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act
            telemetry.TrackException(new Exception("Critical exception"), severity: ExceptionSeverity.Critical);
            telemetry.TrackException(new Exception("Error exception"), severity: ExceptionSeverity.Error);
            telemetry.TrackException(new Exception("Warning exception"), severity: ExceptionSeverity.Warning);
            telemetry.TrackException(new Exception("Info exception"), severity: ExceptionSeverity.Info);
            telemetry.TrackException(new Exception("Debug exception"), severity: ExceptionSeverity.Debug);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(5);
            lines[0].Should().Contain("Critical");
            lines[1].Should().Contain("Error");
            lines[2].Should().Contain("Warning");
            lines[3].Should().Contain("Info");
            lines[4].Should().Contain("Debug");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_DefaultSeverity_Then_UsesError()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var exception = new InvalidOperationException("Test exception");

            // Act
            telemetry.TrackException(exception); // No severity specified
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("Error"); // Default severity
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithNullException_Then_NoThrow()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act & Assert - should not throw
            telemetry.TrackException(null!);
            telemetry.Flush();

            // File should be empty or not exist
            if (File.Exists(filePath))
            {
                var lines = File.ReadAllLines(filePath);
                lines.Should().BeEmpty();
            }
        }

        [TestMethod]
        public void Given_FileTelemetry_When_TrackException_WithStackTrace_Then_IncludesStackTrace()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            Exception? exception = null;
            try
            {
                throw new InvalidOperationException("Test exception with stack trace");
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            // Act
            telemetry.TrackException(exception!);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            var content = lines[0];
            content.Should().Contain("StackTrace");
            
            // Parse JSON to verify structure
            var contextIndex = content.IndexOf(": {");
            var jsonPart = content.Substring(contextIndex + 2);
            var jsonDoc = JsonDocument.Parse(jsonPart);
            var root = jsonDoc.RootElement;
            
            root.GetProperty("Type").GetString().Should().Be("exception");
            root.GetProperty("Exception").GetProperty("Type").GetString().Should().Contain("InvalidOperationException");
            root.GetProperty("Exception").GetProperty("Message").GetString().Should().Be("Test exception with stack trace");
        }
    }
}
