using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiJsonSerializer = Microsoft.ApplicationInsights.Extensibility.Implementation.JsonSerializer;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// Desktop items must not carry the name of the computer they come from. The Application Insights
    /// SDK puts it in the cloud role instance and the internal node name unless both are set, so the
    /// items are read back from the queue folder, as they would be sent.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Mutates the process-wide opt-out variable.
    public class TelemetryMachineNameTests
    {
        private const string DummyInstrumentationKey = "00000000-0000-0000-0000-000000000000";

        private readonly List<string> _directoriesToCleanup = new List<string>();
        private string? _optOutOriginal;

        [TestInitialize]
        public void Initialize()
        {
            // A machine-wide opt-out would disable the instance and fail these tests for the wrong
            // reason; pin it off for the duration.
            _optOutOriginal = Environment.GetEnvironmentVariable(TelemetryEnvironment.OptOutVariable);
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, null);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, _optOutOriginal);

            foreach (var dir in _directoriesToCleanup.Where(Directory.Exists))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            _directoriesToCleanup.Clear();
        }

        [TestMethod]
        public async Task Given_DesktopTelemetry_When_EventAndExceptionAreQueued_Then_NeitherCarriesTheMachineName()
        {
            // Arrange: a channel without senders keeps the items in the queue folder, so nothing is sent.
            var storageDir = GetTempStorageDirectory();
            using var channel = new PersistenceChannel.PersistenceChannel(storageDir, sendersCount: 0);
            var telemetry = new Telemetry(
                DummyInstrumentationKey,
                "test",
                typeof(TelemetryMachineNameTests).Assembly,
                sessionId: null,
                blockThreadInitialization: false,
                enabledProvider: null,
                currentDirectoryProvider: null,
                productName: null,
                channel,
                wasmSender: null);

            // Act
            try
            {
                telemetry.TrackEvent("Evt", new Dictionary<string, string> { ["foo"] = "bar" }, null);
                telemetry.TrackException(new InvalidOperationException("boom"));
                await telemetry.FlushAsync(CancellationToken.None);
            }
            finally
            {
                telemetry.Dispose();
            }

            // Assert
            var items = await ReadQueuedItemsAsync(storageDir);
            items.Should().HaveCount(2, "the event and the exception are each queued in a file of their own");

            var names = new[] { Environment.MachineName, Dns.GetHostName() }
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // On Linux the device OS is the distribution's id and the runtime id starts with it ("ubuntu",
            // "ubuntu.22.04-x64"), so a machine named after its distribution would match them. Those values
            // describe the OS, not the machine.
            var osDescriptors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystem,
                Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.GetRuntimeIdentifier(),
            };

            foreach (var item in items)
            {
                var tags = item.RootElement.GetProperty("tags");
                tags.GetProperty("ai.cloud.roleInstance").GetString().Should().Be(Telemetry.AnonymousRoleInstance, "the SDK fills an empty role instance with the machine name");
                tags.GetProperty("ai.internal.nodeName").GetString().Should().Be(Telemetry.AnonymousRoleInstance, "the SDK fills an empty node name with the machine name");

                foreach (var value in GetStrings(item.RootElement).Where(value => !osDescriptors.Contains(value)))
                {
                    foreach (var name in names)
                    {
                        CarriesName(value, name).Should().BeFalse($"\"{value}\" carries the machine name \"{name}\"");
                    }
                }
            }
        }

        private string GetTempStorageDirectory()
        {
            var dir = Path.Join(Path.GetTempPath(), $"telemetry_machine_name_test_{Guid.NewGuid():N}");
            _directoriesToCleanup.Add(dir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Decodes each queued file: its content is compressed, so the raw file never shows a name.
        /// </summary>
        private static async Task<List<JsonDocument>> ReadQueuedItemsAsync(string storageDir)
        {
            var items = new List<JsonDocument>();
            foreach (var file in Directory.GetFiles(storageDir, "*.trn"))
            {
                using var stream = File.OpenRead(file);
                var transmission = await StorageTransmission.CreateFromStreamAsync(stream, file, new Uri(PersistenceChannel.PersistenceChannel.TelemetryServiceEndpoint));
                var payload = AiJsonSerializer.Deserialize(transmission.Content);
                items.AddRange(payload
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(line => JsonDocument.Parse(line)));
            }

            return items;
        }

        /// <summary>
        /// Every property name and string value in the item, tags and custom properties included.
        /// </summary>
        private static IEnumerable<string> GetStrings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        yield return property.Name;
                        foreach (var value in GetStrings(property.Value))
                        {
                            yield return value;
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                    {
                        foreach (var value in GetStrings(child))
                        {
                            yield return value;
                        }
                    }
                    break;
                case JsonValueKind.String:
                    yield return element.GetString()!;
                    break;
            }
        }

        // The SDK writes the name alone, or followed by the machine's domain. A plain substring test would
        // also match a short name inside an unrelated value, such as "Mac" in the "Mac OS X" device OS.
        private static bool CarriesName(string value, string name)
            => value.Equals(name, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase);
    }
}
