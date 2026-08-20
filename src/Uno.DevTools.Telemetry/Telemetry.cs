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
using System.Runtime.InteropServices;

namespace Uno.DevTools.Telemetry
{
    public sealed class Telemetry : ITelemetry
    {
        private static readonly bool IsWasmBrowser = PlatformDetection.IsWasmBrowser;

        private readonly string? _currentSessionId;
        private TelemetryClient? _client;
        // These collections must be treated as immutable after construction.
        // Do not mutate after initialization to avoid race conditions in concurrent scenarios.
        private IReadOnlyDictionary<string, string>? _commonProperties;
        private IReadOnlyDictionary<string, double>? _commonMeasurements;
        private TelemetryConfiguration? _telemetryConfig;
        private Task? _trackEventTask;
        private string? _storageDirectoryPath;
        private string? _settingsStorageDirectoryPath;
        private PersistenceChannel.PersistenceChannel? _persistenceChannel;
        private WasmHttpSender? _wasmSender;
        private readonly string _instrumentationKey;
        private readonly string _eventNamePrefix;
        private readonly Assembly _versionAssembly;
        private readonly string? _productName;
        private readonly Func<string>? _currentDirectoryProvider;
        private const string TelemetryOptout = "UNO_PLATFORM_TELEMETRY_OPTOUT";
        private readonly TaskCompletionSource<string?> _machineIdTcs = new TaskCompletionSource<string?>();

        public bool Enabled { get; }

        /// <inheritdoc />
        public string? AuthenticatedUserId
        {
            get => TelemetryUserContext.AuthenticatedUserId;
            set => TelemetryUserContext.AuthenticatedUserId = value;
        }

        // Test seam: lets the suite assert pipeline wiring (e.g. initializer registration) without network I/O.
        internal TelemetryConfiguration? TelemetryConfigurationInternal => _telemetryConfig;

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
            // Note: On single-threaded WASM, there's no concurrent access, so the exchange succeeds immediately.
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
                // Yield to allow other threads to progress. Skip on WASM where it's not needed.
                if (!IsWasmBrowser)
                {
                    Thread.Yield();
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
                if (IsWasmBrowser)
                {
                    // WASM path: Don't initialize Application Insights SDK (would fail)
                    // Use direct HTTP sender instead
                    _wasmSender = new WasmHttpSender(_instrumentationKey, _eventNamePrefix);

                    // Get WASM-compatible common properties (no file I/O)
                    _commonProperties = new TelemetryCommonProperties(
                        storageDirectoryPath: "wasm-no-storage",
                        _versionAssembly,
                        _productName ?? _versionAssembly.GetName().Name ?? "Unknown",
                        _currentDirectoryProvider
                        ).GetTelemetryCommonProperties();
                    _commonMeasurements = new Dictionary<string, double>();

                    // Use session-specific machine ID on WASM (no persistent storage)
                    _machineIdTcs.TrySetResult(_commonProperties[TelemetryCommonProperties.MachineId]);
                    return;
                }

                // Non-WASM path: Existing implementation unchanged
                _storageDirectoryPath = Path.Combine(Path.GetTempPath(), ".uno", "telemetry");

                // Store the settings on in the user profile for linux
                if (Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystemPlatform == Platform.Linux)
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

                // Stamps the ambient authenticated user id per item when the queued track task drains
                // (not at the TrackEvent call): items enqueued before an identity change carry the value
                // current at drain time (spec 002 EC-1). The client-level context must not be mutated
                // per event (the track task chain is not fully serialized).
                _telemetryConfig.TelemetryInitializers.Add(new AuthenticatedUserTelemetryInitializer());

                _client = new TelemetryClient(_telemetryConfig);
                _client.InstrumentationKey = _instrumentationKey;
                _client.Context.User.Id = _commonProperties[TelemetryCommonProperties.MachineId];
                _client.Context.Session.Id = _currentSessionId;
                _client.Context.Device.OperatingSystem = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystem;

                _machineIdTcs.TrySetResult(_client.Context.User.Id);
            }
            catch (Exception e)
            {
                _client = null;
                _wasmSender = null;
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
            if (IsWasmBrowser && _wasmSender != null)
            {
                // WASM path: Use HTTP sender
                try
                {
                    var eventProperties = GetEventProperties(properties);
                    var eventMeasurements = GetEventMeasures(measurements);
                    _ = _wasmSender.SendEventAsync(
                        eventName,
                        eventProperties,
                        eventMeasurements,
                        _commonProperties![TelemetryCommonProperties.MachineId],
                        _currentSessionId);
                }
                catch (Exception e)
                {
                    Debug.Fail(e.ToString());
                }
                return;
            }

            // Non-WASM path: Existing implementation
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

        private IDictionary<string, double> GetEventMeasures(IDictionary<string, double>? measurements)
            => GetEventMeasuresCore(measurements);

        private IDictionary<string, double> GetEventMeasures(IReadOnlyDictionary<string, double>? measurements)
            => GetEventMeasuresCore(measurements);

        private IDictionary<string, double> GetEventMeasuresCore(IEnumerable<KeyValuePair<string, double>>? measurements)
        {
            var eventMeasurements = new Dictionary<string, double>(_commonMeasurements?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, double>(0));
            if (measurements != null)
            {
                foreach (var measurement in measurements)
                {
                    eventMeasurements[measurement.Key] = measurement.Value;
                }
            }
            return eventMeasurements;
        }

        private IDictionary<string, string>? GetEventProperties(IDictionary<string, string>? properties)
            => GetEventPropertiesCore(properties);

        private IDictionary<string, string>? GetEventProperties(IReadOnlyDictionary<string, string>? properties)
            => GetEventPropertiesCore(properties);

        private IDictionary<string, string>? GetEventPropertiesCore(IEnumerable<KeyValuePair<string, string>>? properties)
        {
            if (properties == null)
            {
                return _commonProperties is IDictionary<string, string> commonProperties
                    ? commonProperties
                    : new Dictionary<string, string>(_commonProperties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                        ?? new Dictionary<string, string>(0));
            }

            var eventProperties = new Dictionary<string, string>(_commonProperties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, string>(0));
            foreach (var property in properties)
            {
                eventProperties[property.Key] = property.Value;
            }
            return eventProperties;
        }

        public void TrackException(
            Exception exception,
            IReadOnlyDictionary<string, string>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null,
            ExceptionSeverity severity = ExceptionSeverity.Error)
        {
            if (!Enabled || _trackEventTask is null || exception == null)
            {
                return;
            }

            // Use the same lock-free chaining pattern as TrackEvent
            // Note: On single-threaded WASM, there's no concurrent access, so the exchange succeeds immediately.
            while (true)
            {
                var originalTask = _trackEventTask;
                var continuation = originalTask.ContinueWith(
                    x => TrackExceptionTask(exception, properties, measurements, severity)
                );
                var exchanged = Interlocked.CompareExchange(ref _trackEventTask, continuation, originalTask);
                if (exchanged == originalTask)
                {
                    break;
                }
                // Yield to allow other threads to progress. Skip on WASM where it's not needed.
                if (!IsWasmBrowser)
                {
                    Thread.Yield();
                }
            }
        }

        private void TrackExceptionTask(
            Exception exception,
            IReadOnlyDictionary<string, string>? properties,
            IReadOnlyDictionary<string, double>? measurements,
            ExceptionSeverity severity)
        {
            if (IsWasmBrowser && _wasmSender != null)
            {
                // WASM path: Use HTTP sender
                try
                {
                    var eventProperties = GetEventProperties(properties);
                    var eventMeasurements = GetEventMeasures(measurements);
                    _ = _wasmSender.SendExceptionAsync(
                        exception,
                        severity,
                        eventProperties,
                        eventMeasurements,
                        _commonProperties![TelemetryCommonProperties.MachineId],
                        _currentSessionId);
                }
                catch (Exception e)
                {
                    Debug.Fail(e.ToString());
                }
                return;
            }

            // Non-WASM path: Existing implementation
            if (_client == null)
            {
                return;
            }

            try
            {
                var eventProperties = GetEventProperties(properties);
                var eventMeasurements = GetEventMeasures(measurements);

                var exceptionTelemetry = new Microsoft.ApplicationInsights.DataContracts.ExceptionTelemetry(exception);

                // Map ExceptionSeverity to Application Insights SeverityLevel
                exceptionTelemetry.SeverityLevel = severity switch
                {
                    ExceptionSeverity.Critical => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Critical,
                    ExceptionSeverity.Error => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Error,
                    ExceptionSeverity.Warning => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Warning,
                    ExceptionSeverity.Info => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Information,
                    ExceptionSeverity.Debug => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Verbose,
                    _ => Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Error
                };

                if (eventProperties != null)
                {
                    foreach (var property in eventProperties)
                    {
                        exceptionTelemetry.Properties[property.Key] = property.Value;
                    }
                }

                if (eventMeasurements != null)
                {
                    foreach (var measurement in eventMeasurements)
                    {
                        exceptionTelemetry.Metrics[measurement.Key] = measurement.Value;
                    }
                }

                _client.TrackException(exceptionTelemetry);
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
        }
    }
}
