// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
//

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.PlatformAbstractions;
using System.Diagnostics;
using System.Reflection;

namespace Uno.DevTools.Telemetry
{
    public sealed class Telemetry : ITelemetry
    {
        private readonly string? _currentSessionId;
        private TelemetryClient? _client;
        // These collections must be treated as immutable after construction.
        // Do not mutate after initialization to avoid race conditions in concurrent scenarios.
        private Dictionary<string, string>? _commonProperties;
        private Dictionary<string, double>? _commonMeasurements;
        private TelemetryConfiguration? _telemetryConfig;
        private Task? _trackEventTask;
        private string? _storageDirectoryPath;
        private string? _settingsStorageDirectoryPath;
        private PersistenceChannel.PersistenceChannel? _persistenceChannel;
        private readonly string _instrumentationKey;
        private readonly string _eventNamePrefix;
        private readonly Assembly _versionAssembly;
        private readonly string? _productName;
        private readonly Func<string>? _currentDirectoryProvider;
        private const string TelemetryOptout = "UNO_PLATFORM_TELEMETRY_OPTOUT";
        private readonly TaskCompletionSource<string?> _machineIdTcs = new TaskCompletionSource<string?>();

        public bool Enabled { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Telemetry"/> class.
        /// </summary>
        /// <param name="instrumentationKey">The App Insights Key</param>
        /// <param name="eventNamePrefix">A prefix that will be used on all events through this telemetry instance</param>
        /// <param name="versionAssembly">The assembly to use to get the version to report in telemetry</param>
        /// <param name="sessionId">Defines the session ID for this instance</param>
        /// <param name="blockThreadInitialization">Block the execution of the constructor until the telemetry is initialized</param>
        /// <param name="enabledProvider">A delegate that can determine if the telemetry is enabled</param>
        /// <param name="currentDirectoryProvider">A delegate that can provide the value to be hashed in the "Current Path Hash" custom dimension </param>
        /// <param name="productName">The product name to use in the common properties. If null, versionAssembly.Name is used instead.</param>
        public Telemetry(
            string instrumentationKey,
            string eventNamePrefix,
            Assembly versionAssembly,
            string? sessionId = null,
            bool blockThreadInitialization = false,
            Func<bool?>? enabledProvider = null,
            Func<string>? currentDirectoryProvider = null,
            string? productName = null)
        {
            _instrumentationKey = instrumentationKey;
            _currentDirectoryProvider = currentDirectoryProvider;
            _eventNamePrefix = eventNamePrefix;
            _versionAssembly = versionAssembly;
            _productName = productName;

            if (bool.TryParse(Environment.GetEnvironmentVariable(TelemetryOptout), out var telemetryOptOut))
            {
                Enabled = !telemetryOptOut;
            }
            else
            {
                Enabled = !enabledProvider?.Invoke() ?? true;
            }

            if (!Enabled)
            {
                return;
            }

            // Store the session ID in a static field so that it can be reused
            _currentSessionId = sessionId ?? Guid.NewGuid().ToString();
            if (blockThreadInitialization)
            {
                InitializeTelemetry();
            }
            else
            {
                //initialize in task to offload to parallel thread
                _trackEventTask = Task.Run(InitializeTelemetry);
            }
        }

        public void TrackEvent(
            string eventName,
            (string key, string value)[]? properties,
            (string key, double value)[]? measurements)
            => TrackEvent(eventName, properties?.ToDictionary(p => p.key, p => p.value), measurements?.ToDictionary(p => p.key, p => p.value));

        public void TrackEvent(string eventName, IDictionary<string, string>? properties,
            IDictionary<string, double>? measurements)
        {
            if (!Enabled || _trackEventTask is null)
            {
                return;
            }

            // Lock-free chaining of telemetry events:
            // 1. Read the current task (originalTask)
            // 2. Create a continuation that will send the new event after originalTask
            // 3. Atomically swap _trackEventTask to the new continuation only if it still matches originalTask
            // 4. If another thread changed it, retry with the new value
            // This ensures all events are sent in order, even with concurrent calls.
            while (true)
            {
                var originalTask = _trackEventTask;
                var continuation = originalTask.ContinueWith(
                    x => TrackEventTask(eventName, properties, measurements)
                );
                var exchanged = Interlocked.CompareExchange(ref _trackEventTask, continuation, originalTask);
                if (exchanged == originalTask)
                {
                    break;
                }
            }
        }

        public void Flush()
        {
            if (!Enabled || _trackEventTask == null)
            {
                return;
            }

            // Wait for the current chain of telemetry events to complete.
            // This reads the value atomically and does not block other threads from chaining new events.
            var task = _trackEventTask;
            if (task.Status != TaskStatus.WaitingForActivation)
            {
                task.Wait(TimeSpan.FromSeconds(1));
            }
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            if (!Enabled || _trackEventTask == null)
            {
                return;
            }

            // Wait asynchronously for the current chain of telemetry events to complete.
            var task = _trackEventTask;
            if (!task.IsCompleted)
            {
                await Task.WhenAny(task, Task.Delay(-1, ct));
            }
        }

        public Task<string?> GetMachineIdAsync(CancellationToken ct)
        {
            return _machineIdTcs.Task;
        }

        public void Dispose()
        {
            _persistenceChannel?.Dispose();
            _telemetryConfig?.Dispose();
        }

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }
            TrackEventTask(eventName, properties, measurements);
        }

        private void InitializeTelemetry()
        {
            try
            {
                _storageDirectoryPath = Path.Combine(Path.GetTempPath(), ".uno", "telemetry");

                // Store the settings on in the user profile for linux
                if (RuntimeEnvironment.OperatingSystemPlatform == Platform.Linux)
                {
                    _settingsStorageDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".uno", "telemetry");
                }
                else
                {
                    _settingsStorageDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uno Platform", "telemetry");
                }

                _persistenceChannel = new PersistenceChannel.PersistenceChannel(
                    storageDirectoryPath: _storageDirectoryPath);

                _persistenceChannel.SendingInterval = TimeSpan.FromMilliseconds(1);

                _commonProperties = new TelemetryCommonProperties(
                    _settingsStorageDirectoryPath,
                    _versionAssembly,
                    _productName ?? _versionAssembly.GetName().Name ?? "Unknown",
                    _currentDirectoryProvider
                    ).GetTelemetryCommonProperties();
                _commonMeasurements = new Dictionary<string, double>();

                _telemetryConfig = new TelemetryConfiguration
                {
                    InstrumentationKey = _instrumentationKey,
                    TelemetryChannel = _persistenceChannel
                };

                _client = new TelemetryClient(_telemetryConfig);
                _client.InstrumentationKey = _instrumentationKey;
                _client.Context.User.Id = _commonProperties[TelemetryCommonProperties.MachineId];
                _client.Context.Session.Id = _currentSessionId;
                _client.Context.Device.OperatingSystem = RuntimeEnvironment.OperatingSystem;

                _machineIdTcs.TrySetResult(_client.Context.User.Id);
            }
            catch (Exception e)
            {
                _client = null;
                _machineIdTcs.TrySetResult(null);
                // we don't want to fail the tool if telemetry fails.
                Debug.Fail(e.ToString());
            }
        }

        private void TrackEventTask(
            string eventName,
            IDictionary<string, string>? properties,
            IDictionary<string, double>? measurements)
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                var eventProperties = GetEventProperties(properties);
                var eventMeasurements = GetEventMeasures(measurements);

                _client.TrackEvent(PrependProducerNamespace(eventName), eventProperties, eventMeasurements);
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
        }

        private string PrependProducerNamespace(string eventName)
        {
            return _eventNamePrefix + "/" + eventName;
        }

        private Dictionary<string, double> GetEventMeasures(IDictionary<string, double>? measurements)
        {
            var eventMeasurements = new Dictionary<string, double>(_commonMeasurements ?? []);
            if (measurements != null)
            {
                foreach (var measurement in measurements)
                {
                    eventMeasurements[measurement.Key] = measurement.Value;
                }
            }
            return eventMeasurements;
        }

        private Dictionary<string, string>? GetEventProperties(IDictionary<string, string>? properties)
        {
            if (properties == null)
            {
                return _commonProperties;
            }

            var eventProperties = new Dictionary<string, string>(_commonProperties ?? []);
            foreach (var property in properties)
            {
                eventProperties[property.Key] = property.Value;
            }
            return eventProperties;
        }
    }
}
