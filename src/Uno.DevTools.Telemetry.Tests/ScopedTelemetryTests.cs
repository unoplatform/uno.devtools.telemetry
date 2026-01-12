using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class ScopedTelemetryTests
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
        public void Given_FileTelemetry_When_CreateScope_WithProperties_Then_EventIncludesScopeProperties()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeProperties = new Dictionary<string, string> { { "scopeKey", "scopeValue" } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(properties: scopeProperties);
            scopedTelemetry.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("scopeKey");
            lines[0].Should().Contain("scopeValue");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_CreateScope_WithMeasurements_Then_EventIncludesScopeMeasurements()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeMeasurements = new Dictionary<string, double> { { "scopeMetric", 42.0 } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(measurements: scopeMeasurements);
            scopedTelemetry.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("scopeMetric");
            lines[0].Should().Contain("42");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_NestedScopes_Then_PropertiesMerge()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var parentProperties = new Dictionary<string, string> { { "parent", "parentValue" } };
            var childProperties = new Dictionary<string, string> { { "child", "childValue" } };

            // Act
            var parentScope = telemetry.CreateScope(properties: parentProperties);
            var childScope = parentScope.CreateScope(properties: childProperties);
            childScope.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            childScope.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("parent");
            lines[0].Should().Contain("parentValue");
            lines[0].Should().Contain("child");
            lines[0].Should().Contain("childValue");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_NestedScopes_WithConflictingKeys_Then_ChildOverridesParent()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var parentProperties = new Dictionary<string, string> { { "key", "parentValue" } };
            var childProperties = new Dictionary<string, string> { { "key", "childValue" } };

            // Act
            var parentScope = telemetry.CreateScope(properties: parentProperties);
            var childScope = parentScope.CreateScope(properties: childProperties);
            childScope.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            childScope.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            
            // Parse JSON to verify the value
            var contextIndex = lines[0].IndexOf(": {");
            var jsonPart = lines[0].Substring(contextIndex + 2);
            var jsonDoc = JsonDocument.Parse(jsonPart);
            var root = jsonDoc.RootElement;
            
            var properties = root.GetProperty("Properties");
            properties.GetProperty("key").GetString().Should().Be("childValue");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_EventProperties_ConflictWithScope_Then_EventOverridesScope()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeProperties = new Dictionary<string, string> { { "key", "scopeValue" } };
            var eventProperties = new Dictionary<string, string> { { "key", "eventValue" } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(properties: scopeProperties);
            scopedTelemetry.TrackEvent("TestEvent", eventProperties, null);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            
            // Parse JSON to verify the value
            var contextIndex = lines[0].IndexOf(": {");
            var jsonPart = lines[0].Substring(contextIndex + 2);
            var jsonDoc = JsonDocument.Parse(jsonPart);
            var root = jsonDoc.RootElement;
            
            var properties = root.GetProperty("Properties");
            properties.GetProperty("key").GetString().Should().Be("eventValue");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_ScopeWithException_Then_ExceptionIncludesScopeProperties()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeProperties = new Dictionary<string, string> { { "scopeKey", "scopeValue" } };
            var exception = new InvalidOperationException("Test exception");

            // Act
            var scopedTelemetry = telemetry.CreateScope(properties: scopeProperties);
            scopedTelemetry.TrackException(exception);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("scopeKey");
            lines[0].Should().Contain("scopeValue");
            lines[0].Should().Contain("Test exception");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_EmptyScope_Then_NoError()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");

            // Act - create scope with null properties and measurements
            var scopedTelemetry = telemetry.CreateScope(null, null);
            scopedTelemetry.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.Flush();

            // Assert - should work without error
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("TestEvent");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_MultipleEventsInScope_Then_AllIncludeScopeProperties()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeProperties = new Dictionary<string, string> { { "scopeKey", "scopeValue" } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(properties: scopeProperties);
            scopedTelemetry.TrackEvent("Event1", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.TrackEvent("Event2", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.TrackEvent("Event3", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(3);
            foreach (var line in lines)
            {
                line.Should().Contain("scopeKey");
                line.Should().Contain("scopeValue");
            }
        }

        [TestMethod]
        public void Given_FileTelemetry_When_ScopeNotUsed_Then_OriginalTelemetryUnaffected()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeProperties = new Dictionary<string, string> { { "scopeKey", "scopeValue" } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(properties: scopeProperties);
            scopedTelemetry.TrackEvent("ScopedEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            telemetry.TrackEvent("UnscopedEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            telemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(2);
            
            // First event (scoped) should have scope properties
            lines[0].Should().Contain("scopeKey");
            lines[0].Should().Contain("scopeValue");
            
            // Second event (unscoped) should NOT have scope properties
            lines[1].Should().NotContain("scopeKey");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_DeeplyNestedScopes_Then_AllPropertiesMerge()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            
            var level1Props = new Dictionary<string, string> { { "level1", "value1" } };
            var level2Props = new Dictionary<string, string> { { "level2", "value2" } };
            var level3Props = new Dictionary<string, string> { { "level3", "value3" } };

            // Act
            var scope1 = telemetry.CreateScope(properties: level1Props);
            var scope2 = scope1.CreateScope(properties: level2Props);
            var scope3 = scope2.CreateScope(properties: level3Props);
            scope3.TrackEvent("TestEvent", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
            scope3.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            lines[0].Should().Contain("level1");
            lines[0].Should().Contain("value1");
            lines[0].Should().Contain("level2");
            lines[0].Should().Contain("value2");
            lines[0].Should().Contain("level3");
            lines[0].Should().Contain("value3");
        }

        [TestMethod]
        public void Given_FileTelemetry_When_ScopeMeasurements_ConflictWithEvent_Then_EventOverridesScope()
        {
            // Arrange
            var filePath = GetTempFilePath();
            var telemetry = new FileTelemetry(filePath, "test");
            var scopeMeasurements = new Dictionary<string, double> { { "metric", 10.0 } };
            var eventMeasurements = new Dictionary<string, double> { { "metric", 20.0 } };

            // Act
            var scopedTelemetry = telemetry.CreateScope(measurements: scopeMeasurements);
            scopedTelemetry.TrackEvent("TestEvent", null, eventMeasurements);
            scopedTelemetry.Flush();

            // Assert
            var lines = File.ReadAllLines(filePath);
            lines.Should().HaveCount(1);
            
            // Parse JSON to verify the value
            var contextIndex = lines[0].IndexOf(": {");
            var jsonPart = lines[0].Substring(contextIndex + 2);
            var jsonDoc = JsonDocument.Parse(jsonPart);
            var root = jsonDoc.RootElement;
            
            var measurements = root.GetProperty("Measurements");
            measurements.GetProperty("metric").GetDouble().Should().Be(20.0);
        }
    }
}
