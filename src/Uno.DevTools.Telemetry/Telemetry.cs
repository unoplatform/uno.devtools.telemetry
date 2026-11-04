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
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.DotNet.PlatformAbstractions;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Uno.DevTools.Telemetry
{
    public sealed class Telemetry : ITelemetry
    {
        private static readonly bool IsWasmBrowser = PlatformDetection.IsWasmBrowser;

        // Stands in for the computer's name, which the SDK would otherwise put in every desktop item.
        internal const string AnonymousRoleInstance = "anonymous";

        // Set on every item an instance sends from memory because it has no queue folder of the user's own, to one of
        // QueueRefusal's codes: those items would otherwise look like fewer users.
        internal const string QueueRefusedProperty = "Queue Refused";

        // Without a queue on disk, items wait in memory: sent this often, and on Flush and Dispose for at most as long
        // as below, so that a process that stops soon after still sends them.
        private static readonly TimeSpan MemorySendingInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MemoryFlushTimeout = TimeSpan.FromSeconds(2);

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
        private Microsoft.ApplicationInsights.Channel.ITelemetryChannel? _memoryChannel;
        private WasmHttpSender? _wasmSender;
        private readonly string _instrumentationKey;
        private readonly string _eventNamePrefix;
        private readonly Assembly _versionAssembly;
        private readonly string? _productName;
        private readonly Func<string>? _currentDirectoryProvider;
        private readonly Microsoft.ApplicationInsights.Channel.ITelemetryChannel? _channelOverride;
        private readonly WasmHttpSender? _wasmSenderOverride;
        private readonly UserFolders? _foldersOverride;
        private readonly Func<Microsoft.ApplicationInsights.Channel.ITelemetryChannel>? _memoryChannelOverride;
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
            : this(instrumentationKey, eventNamePrefix, versionAssembly, sessionId, blockThreadInitialization, enabledProvider, currentDirectoryProvider, productName, channel: null, wasmSender: null)
        {
        }

        // Test seams: an injected channel replaces the persistence channel so the suite can observe the
        // items the desktop pipeline emits in-process, without disk or network I/O; an injected sender
        // takes the WASM send path on any platform so the arguments forwarded to it can be asserted;
        // injected folders describe another machine to choose the queue and settings folders on; an injected
        // memory channel stands in for the one used when no queue folder can be.
        internal Telemetry(
            string instrumentationKey,
            string eventNamePrefix,
            Assembly versionAssembly,
            string? sessionId,
            bool blockThreadInitialization,
            Func<bool?>? enabledProvider,
            Func<string>? currentDirectoryProvider,
            string? productName,
            Microsoft.ApplicationInsights.Channel.ITelemetryChannel? channel,
            WasmHttpSender? wasmSender,
            UserFolders? folders = null,
            Func<Microsoft.ApplicationInsights.Channel.ITelemetryChannel>? memoryChannel = null)
        {
            _instrumentationKey = instrumentationKey;
            _currentDirectoryProvider = currentDirectoryProvider;
            _eventNamePrefix = eventNamePrefix;
            _versionAssembly = versionAssembly;
            _productName = productName;
            _channelOverride = channel;
            _wasmSenderOverride = wasmSender;
            _foldersOverride = folders;
            _memoryChannelOverride = memoryChannel;

            // The environment can only opt out; otherwise the provider decides, and telemetry is on without one.
            Enabled = !TelemetryEnvironment.IsOptedOut() && enabledProvider?.Invoke() != true;

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

            // Captured now, not when the queued task drains: the queue is gated on background
            // initialization, so an item stamped at drain time could carry an identity that signed in
            // after this call was made (spec 002 EC-1).
            var authenticatedUserId = TelemetryUserContext.AuthenticatedUserId;

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
                    x => TrackEventTask(eventName, properties, measurements, authenticatedUserId)
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

            FlushMemoryChannel();
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

            if (_memoryChannel is Microsoft.ApplicationInsights.Channel.InMemoryChannel inMemory)
            {
                // Its own flush waits for the network for as long as a send may take.
                await Task.WhenAny(inMemory.FlushAsync(ct), Task.Delay(MemoryFlushTimeout, ct));
            }
            else
            {
                _memoryChannel?.Flush();
            }
        }

        public Task<string?> GetMachineIdAsync(CancellationToken ct)
        {
            return _machineIdTcs.Task;
        }

        public void Dispose()
        {
            FlushMemoryChannel();
            _memoryChannel?.Dispose();
            _persistenceChannel?.Dispose();
            _telemetryConfig?.Dispose();
        }

        /// <summary>
        /// Sends what waits in memory, when there is no queue on disk, waiting at most <see cref="MemoryFlushTimeout"/>.
        /// </summary>
        private void FlushMemoryChannel()
        {
            try
            {
                if (_memoryChannel is Microsoft.ApplicationInsights.Channel.InMemoryChannel inMemory)
                {
                    inMemory.Flush(MemoryFlushTimeout);
                }
                else
                {
                    _memoryChannel?.Flush();
                }
            }
            catch (Exception e)
            {
                // we don't want to fail the tool if telemetry fails.
                Debug.Fail(e.ToString());
            }
        }

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }
            TrackEventTask(eventName, properties, measurements, TelemetryUserContext.AuthenticatedUserId);
        }

        private void InitializeTelemetry()
        {
            try
            {
                // Test seam only: outside the browser this stays null unless a sender was injected, in
                // which case the track tasks below take the WASM send path while initialization proceeds
                // on the desktop path (so the machine id is resolved the normal way).
                _wasmSender = _wasmSenderOverride;

                if (IsWasmBrowser)
                {
                    // WASM path: Don't initialize Application Insights SDK (would fail)
                    // Use direct HTTP sender instead
                    _wasmSender ??= new WasmHttpSender(_instrumentationKey, _eventNamePrefix);

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

                // Non-WASM path
                var folders = _foldersOverride ?? UserFolders.Current();

                // An injected channel needs no queue folder, and none is created for it.
                QueueRefusal? queueRefusal = null;
                if (_channelOverride is null)
                {
                    _storageDirectoryPath = QueueDirectory.Choose(folders, out queueRefusal);
                    if (queueRefusal is not null)
                    {
                        // No folder of the user's own to queue in (see QueueDirectory), so items are sent from memory,
                        // and those not sent when the process stops are lost. No host listens to Trace, so the reason
                        // also goes to the console when UNO_TELEMETRY_ENABLE_PERSISTENCE_CHANNEL_DEBUG_OUTPUT is set.
                        var fromMemory = $"Telemetry can't queue on disk for this instance, so it sends from memory: {queueRefusal.Reason}.";
                        TelemetryDiagnostics.Write(fromMemory);
                        PersistenceChannel.PersistenceChannelDebugLog.WriteLine(fromMemory);
                    }
                }

                // Folders refused for the queue aren't trusted with the machine id either: run as root, they may be
                // another user's. The id is then made for this run, unless the machine has a hardware address to hash.
                _settingsStorageDirectoryPath = queueRefusal is null ? GetSettingsDirectoryPath(folders) : null;

                Microsoft.ApplicationInsights.Channel.ITelemetryChannel channel;
                if (_channelOverride is not null)
                {
                    channel = _channelOverride;
                }
                else if (queueRefusal is not null)
                {
                    _memoryChannel = _memoryChannelOverride?.Invoke() ?? new Microsoft.ApplicationInsights.Channel.InMemoryChannel
                    {
                        EndpointAddress = PersistenceChannel.PersistenceChannel.TelemetryServiceEndpoint,
                        SendingInterval = MemorySendingInterval,
                    };
                    channel = _memoryChannel;
                }
                else
                {
                    _persistenceChannel = new PersistenceChannel.PersistenceChannel(
                        storageDirectoryPath: _storageDirectoryPath);

                    _persistenceChannel.SendingInterval = TimeSpan.FromMilliseconds(1);
                    channel = _persistenceChannel;
                }

                var commonProperties = new TelemetryCommonProperties(
                    _settingsStorageDirectoryPath,
                    _versionAssembly,
                    _productName ?? _versionAssembly.GetName().Name ?? "Unknown",
                    _currentDirectoryProvider
                    ).GetTelemetryCommonProperties();
                if (queueRefusal is not null)
                {
                    // The code only, never the reason, which names folders.
                    commonProperties[QueueRefusedProperty] = queueRefusal.Code;
                }

                _commonProperties = commonProperties;
                _commonMeasurements = new Dictionary<string, double>();

                _telemetryConfig = new TelemetryConfiguration
                {
                    InstrumentationKey = _instrumentationKey,
                    TelemetryChannel = channel
                };

                _client = new TelemetryClient(_telemetryConfig);
                _client.InstrumentationKey = _instrumentationKey;
                _client.Context.User.Id = _commonProperties[TelemetryCommonProperties.MachineId];
                _client.Context.Session.Id = _currentSessionId;
                _client.Context.Device.OperatingSystem = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.OperatingSystem;
                // The SDK fills an empty role instance with the machine's name (with its domain, if any), and
                // an empty node name too once the role instance is set. Both are copied onto every item.
                _client.Context.Cloud.RoleInstance = AnonymousRoleInstance;
                _client.Context.GetInternalContext().NodeName = AnonymousRoleInstance;

                _machineIdTcs.TrySetResult(_client.Context.User.Id);
            }
            catch (Exception e)
            {
                _client = null;
                _wasmSender = null;
                _machineIdTcs.TrySetResult(null);
                // we don't want to fail the tool if telemetry fails.
                Debug.Fail(e.ToString());
                // Debug.Fail is compiled out of Release builds; without this line a failed
                // initialization leaves telemetry silently disabled with zero operator signal. The full
                // exception (type and stack) goes out, since this is the only signal a Release consumer gets.
                TelemetryDiagnostics.Write($"Telemetry initialization failed; telemetry is disabled for this instance: {e}");
            }
        }

        /// <summary>
        /// Returns the folder the machine id is kept in, or <see langword="null"/> when there is no usable home to keep it
        /// in: it is then not kept, rather than written under the current directory or at the root of the file system.
        /// </summary>
        /// <remarks>
        /// Linux keeps it in the home folder, everything else in the application data folder (on macOS, under the home
        /// folder too). Neither follows the queue folder, so that a machine keeps the id it already has.
        /// </remarks>
        internal static string? GetSettingsDirectoryPath(UserFolders folders)
        {
            if (!folders.IsWindows && !UserFolders.IsUsableBase(folders.HomeDirectory))
            {
                return null;
            }

            if (folders.IsLinux)
            {
                return UserFolders.Join(folders.HomeDirectory!, ".uno", "telemetry");
            }

            return UserFolders.IsUsableBase(folders.ApplicationData)
                ? UserFolders.Join(folders.ApplicationData, "Uno Platform", "telemetry")
                : null;
        }

        private void TrackEventTask(
            string eventName,
            IDictionary<string, string>? properties,
            IDictionary<string, double>? measurements,
            string? authenticatedUserId)
        {
            if (_wasmSender != null)
            {
                // WASM path: Use HTTP sender (the sender only exists on the browser, or under the test seam)
                try
                {
                    var eventProperties = GetEventProperties(properties);
                    var eventMeasurements = GetEventMeasures(measurements);
                    _ = _wasmSender.SendEventAsync(
                        eventName,
                        eventProperties,
                        eventMeasurements,
                        _commonProperties![TelemetryCommonProperties.MachineId],
                        _currentSessionId,
                        authenticatedUserId);
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

                var eventTelemetry = new Microsoft.ApplicationInsights.DataContracts.EventTelemetry(PrependProducerNamespace(eventName));

                if (eventProperties != null)
                {
                    foreach (var property in eventProperties)
                    {
                        eventTelemetry.Properties[property.Key] = property.Value;
                    }
                }

                foreach (var measurement in eventMeasurements)
                {
                    eventTelemetry.Metrics[measurement.Key] = measurement.Value;
                }

                StampAuthenticatedUser(eventTelemetry, authenticatedUserId);
                _client.TrackEvent(eventTelemetry);
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

        // Stamps the id captured at the tracking call onto the item's own context, emitted as the
        // ai.user.authUserId tag (user_AuthenticatedId). The shared client context must not be mutated
        // per item: the track task chain is not fully serialized under contention.
        private static void StampAuthenticatedUser(Microsoft.ApplicationInsights.Channel.ITelemetry item, string? authenticatedUserId)
        {
            if (authenticatedUserId is not null)
            {
                item.Context.User.AuthenticatedUserId = authenticatedUserId;
            }
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

            // Captured at the call, for the same reason as TrackEvent.
            var authenticatedUserId = TelemetryUserContext.AuthenticatedUserId;

            // Use the same lock-free chaining pattern as TrackEvent
            // Note: On single-threaded WASM, there's no concurrent access, so the exchange succeeds immediately.
            while (true)
            {
                var originalTask = _trackEventTask;
                var continuation = originalTask.ContinueWith(
                    x => TrackExceptionTask(exception, properties, measurements, severity, authenticatedUserId)
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
            ExceptionSeverity severity,
            string? authenticatedUserId)
        {
            if (_wasmSender != null)
            {
                // WASM path: Use HTTP sender (the sender only exists on the browser, or under the test seam)
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
                        _currentSessionId,
                        authenticatedUserId);
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

                StampAuthenticatedUser(exceptionTelemetry, authenticatedUserId);
                _client.TrackException(exceptionTelemetry);
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
        }
    }
}
