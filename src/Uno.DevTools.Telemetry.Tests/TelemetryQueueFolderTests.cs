using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.DataContracts;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// A desktop instance settles its queue folder once, when it starts. Without a folder of the user's own it sends from
    /// memory, marks every item with the reason's code, and says why once; and it never keeps the machine id where it
    /// doesn't belong.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Mutates Trace.Listeners, the current directory and process-wide environment variables.
    public class TelemetryQueueFolderTests
    {
        private const string DummyInstrumentationKey = "00000000-0000-0000-0000-000000000000";
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

#if DEBUG
        // A debug build of the library writes each diagnostic to Debug as well, and Debug's listeners are Trace's.
        private const int CopiesPerDiagnostic = 2;
#else
        private const int CopiesPerDiagnostic = 1;
#endif

        private readonly CapturingTraceListener _trace = new CapturingTraceListener();
        private readonly List<string> _directoriesToCleanup = new List<string>();
        private readonly List<Telemetry> _instances = new List<Telemetry>();
        private string? _optOutOriginal;

        [TestInitialize]
        public void Initialize()
        {
            // A machine-wide opt-out would disable the instance before it chooses a folder.
            _optOutOriginal = Environment.GetEnvironmentVariable(TelemetryEnvironment.OptOutVariable);
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, null);
            Trace.Listeners.Add(_trace);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Trace.Listeners.Remove(_trace);

            // ITelemetry exposes Dispose() without implementing IDisposable, so no using declaration.
            foreach (var instance in _instances)
            {
                instance.Dispose();
            }

            _instances.Clear();
            Environment.SetEnvironmentVariable(TelemetryEnvironment.OptOutVariable, _optOutOriginal);

            foreach (var dir in _directoriesToCleanup.Where(Directory.Exists))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (IOException)
                {
                    // Best effort cleanup: something still holds the folder
                }
                catch (UnauthorizedAccessException)
                {
                    // Best effort cleanup: a test left a folder without write permission
                }
            }

            _directoriesToCleanup.Clear();
        }

        [TestMethod]
        [DataRow("no home", "no usable home folder", QueueRefusal.NoHome)]
        [DataRow("home is /", "no usable home folder", QueueRefusal.NoHome)]
        [DataRow("runtime can't set modes", "this runtime can't set a folder's Unix permissions", QueueRefusal.NoUnixModes)]
        [DataRow("system refuses the mode", "the system refused to set the permissions of", QueueRefusal.NotOwned)]
        [DataRow("file system keeps no modes", "doesn't keep Unix permissions", QueueRefusal.NoUnixPermissions)]
        [DataRow("others can write to the base folder", "other users can write to", QueueRefusal.SharedParent)]
        [DataRow("root with a home that isn't root's", "runs as root", QueueRefusal.RootOutsideHome)]
        public async Task Given_NoUsableQueueFolder_When_TelemetryStarts_Then_ItSendsFromMemoryWithTheCodeAndSaysWhyOnce(string situation, string reason, string code)
        {
            // Arrange
            var home = CreateDirectory();
            var folders = situation switch
            {
                "no home" => Linux(homeDirectory: null),
                "home is /" => Linux(homeDirectory: "/"),
                "runtime can't set modes" => QueueDirectoryTests.Unix(home, useDefaultModes: false),
                "system refuses the mode" => QueueDirectoryTests.Unix(home, modes: FakeModes.Accepting(setMode: (_, _) => throw new UnauthorizedAccessException())),
                "file system keeps no modes" => QueueDirectoryTests.Unix(home, modes: FakeModes.Accepting(getMode: _ => 0x1ED, setMode: (_, _) => { })),
                "others can write to the base folder" => QueueDirectoryTests.Unix(home, modes: FakeModes.Accepting(getMode: folder => Path.GetFileName(folder) == ".cache" ? 0x1FF : UnixFolder.OwnerOnlyMode)),
                "root with a home that isn't root's" => QueueDirectoryTests.Unix(home, isRoot: () => true, isInHomeOfRoot: _ => false, modes: FakeModes.Accepting()),
                _ => throw new ArgumentOutOfRangeException(nameof(situation)),
            };
            var memory = new CapturingTelemetryChannel();

            // Act
            var telemetry = CreateTelemetry(folders, channel: null, memoryChannel: memory);
            telemetry.TrackEvent("Evt", new Dictionary<string, string> { ["foo"] = "bar" }, null);
            telemetry.TrackException(new InvalidOperationException("boom"));
            await telemetry.FlushAsync(CancellationToken.None);

            // Assert
            telemetry.Enabled.Should().BeTrue();
            (await telemetry.GetMachineIdAsync(CancellationToken.None)).Should().NotBeNullOrEmpty("the instance still sends, with an id");
            memory.Items.Should().HaveCount(2, "the event and the exception are sent from memory")
                .And.AllSatisfy(item => ((ISupportProperties)item).Properties.Should().Contain(Telemetry.QueueRefusedProperty, code, "the code, and never a path, says why"));
            var messages = _trace.Messages;
            messages.Should().HaveCount(CopiesPerDiagnostic, "the reason is given once")
                .And.AllSatisfy(message => message.Should().Be(messages[0]))
                .And.AllSatisfy(message => message.Should().StartWith("Telemetry can't queue on disk for this instance, so it sends from memory: ").And.Contain(reason));
            Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories).Should().BeEmpty("nothing is queued, and the machine id isn't stored");
        }

        [TestMethod]
        public async Task Given_UsableQueueFolder_When_TelemetryStarts_Then_ItIsMadePrivateBeforeAnythingIsQueued()
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - an existing folder others can open. Nothing is tracked, so nothing is sent.
            var home = CreateDirectory();
            var queue = QueueDirectoryTests.QueuePathUnder(home);
            Directory.CreateDirectory(queue);
            File.SetUnixFileMode(queue, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // Act
            var telemetry = CreateTelemetry(QueueDirectoryTests.Unix(home), channel: null);

            // Assert
            (await telemetry.GetMachineIdAsync(CancellationToken.None)).Should().NotBeNull();
            _trace.Messages.Should().BeEmpty();
            File.GetUnixFileMode(queue).Should().Be(OwnerOnly);
            File.Exists(Path.Join(home, ".uno", "telemetry", ".machinehash")).Should().BeTrue("Linux keeps the machine id in the home folder");
        }

        [TestMethod]
        [DataRow(true, false, null, null, DisplayName = "Linux, no home")]
        [DataRow(true, false, "", null, DisplayName = "Linux, home empty")]
        [DataRow(true, false, "relative/home", null, DisplayName = "Linux, home relative")]
        [DataRow(false, true, null, "", DisplayName = "macOS, no home and no application data folder")]
        [DataRow(false, false, null, "relative/config", DisplayName = "FreeBSD, no home and a relative application data folder")]
        public async Task Given_InjectedChannelAndNoUsableHome_When_TelemetryStarts_Then_TheMachineIdIsNotWrittenUnderTheCurrentDirectory(
            bool isLinux, bool isMacOS, string? homeDirectory, string? applicationData)
        {
            // Arrange
            var workingDirectory = CreateDirectory();
            var channel = new CapturingTelemetryChannel();
            var folders = new UserFolders
            {
                IsLinux = isLinux,
                IsMacOS = isMacOS,
                HomeDirectory = homeDirectory,
                ApplicationData = applicationData,
                TempPath = Path.GetTempPath(),
                IsRoot = () => throw new InvalidOperationException("an injected channel needs no queue folder"),
                Modes = FakeModes.Accepting(
                    getMode: _ => throw new InvalidOperationException("an injected channel needs no queue folder"),
                    setMode: (_, _) => throw new InvalidOperationException("an injected channel needs no queue folder")),
            };
            var originalDirectory = Directory.GetCurrentDirectory();

            // Act
            Directory.SetCurrentDirectory(workingDirectory);
            string? machineId;
            try
            {
                var telemetry = CreateTelemetry(folders, channel);
                telemetry.TrackEvent("Evt", new Dictionary<string, string>(), null);
                await telemetry.FlushAsync(CancellationToken.None);
                machineId = await telemetry.GetMachineIdAsync(CancellationToken.None);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }

            // Assert
            channel.Items.Should().ContainSingle("the instance still works, with an id for this run")
                .Which.As<ISupportProperties>().Properties.Should().NotContainKey(Telemetry.QueueRefusedProperty, "no queue folder was refused");
            machineId.Should().NotBeNullOrEmpty();
            Directory.EnumerateFileSystemEntries(workingDirectory).Should().BeEmpty("nothing is kept relative to the current directory");
        }

        [TestMethod]
        [DataRow(false, DisplayName = "This build, through File and Directory")]
        [DataRow(true, DisplayName = "The netstandard2.0 build, through reflection")]
        public async Task Given_PublicConstructor_When_TheCacheFolderIsAPrivateOne_Then_TheQueueIsMadePrivateInIt(bool netStandardBuild)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix only: on Windows the queue is in the temp folder, unchanged.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - the machine is described by the environment, as for any host. Nothing is tracked, so nothing is sent.
            // Root uses no folder outside its own home, and the musl CI job runs as root.
            var root = CreateDirectory(Environment.IsPrivilegedProcess ? QueueDirectoryTests.HomeOfRoot() : null);
            var home = Path.Join(root, "home");
            var cache = Path.Join(root, "cache");
            Directory.CreateDirectory(home);
            using var environment = new EnvironmentScope(
                ("HOME", home),
                ("XDG_CACHE_HOME", cache),
                ("XDG_CONFIG_HOME", Path.Join(root, "config")),
                ("TMPDIR", null));
            var queue = OperatingSystem.IsMacOS()
                ? Path.Join(home, "Library", "Caches", "uno", "telemetry", "outbox")
                : Path.Join(cache, "uno", "telemetry", "outbox");

            // Act
            var machineId = netStandardBuild ? await StartNetStandardBuildAsync() : await StartThisBuildAsync();

            // Assert
            machineId.Should().NotBeNullOrEmpty("the instance started");
            _trace.Messages.Should().BeEmpty();
            File.GetUnixFileMode(queue).Should().Be(OwnerOnly);
        }

        [TestMethod]
        [DataRow(true, false, false, "/home/someone", "/home/someone/.config", "/home/someone/.uno/telemetry", DisplayName = "Linux keeps it in the home folder")]
        [DataRow(false, true, false, "/Users/someone", "/Users/someone/.config", "/Users/someone/.config/Uno Platform/telemetry", DisplayName = "macOS keeps it in the application data folder")]
        [DataRow(false, false, false, "/home/someone", "/home/someone/.config", "/home/someone/.config/Uno Platform/telemetry", DisplayName = "FreeBSD keeps it in the application data folder")]
        [DataRow(true, false, false, "/", "/.config", null, DisplayName = "Linux, HOME=/")]
        [DataRow(false, true, false, "/", "/.config", null, DisplayName = "macOS, HOME=/")]
        [DataRow(false, false, true, null, "/Users/someone/AppData/Roaming", "/Users/someone/AppData/Roaming/Uno Platform/telemetry", DisplayName = "Windows needs no home")]
        public void Given_AMachine_When_ChoosingWhereTheMachineIdIsKept_Then_ItIsAFolderOfTheUsersOrNone(
            bool isLinux, bool isMacOS, bool isWindows, string? homeDirectory, string? applicationData, string? expected)
        {
            var folders = new UserFolders
            {
                IsLinux = isLinux,
                IsMacOS = isMacOS,
                IsWindows = isWindows,
                HomeDirectory = homeDirectory,
                ApplicationData = applicationData,
            };

            var path = Telemetry.GetSettingsDirectoryPath(folders);

            (path is null ? null : path.Replace(Path.DirectorySeparatorChar, '/')).Should().Be(expected);
        }

        private static UserFolders Linux(string? homeDirectory)
            => new UserFolders
            {
                IsLinux = true,
                HomeDirectory = homeDirectory,
                TempPath = Path.GetTempPath(),
                IsRoot = () => false,
                Modes = FakeModes.Accepting(
                    getMode: _ => throw new InvalidOperationException("there is no folder to prepare"),
                    setMode: (_, _) => throw new InvalidOperationException("there is no folder to prepare")),
            };

        private async Task<string?> StartThisBuildAsync()
        {
            var telemetry = new Telemetry(DummyInstrumentationKey, "test", typeof(TelemetryQueueFolderTests).Assembly);
            _instances.Add(telemetry);
            return await telemetry.GetMachineIdAsync(CancellationToken.None);
        }

        /// <summary>
        /// Starts the netstandard2.0 build of the library, the one MSBuild tasks and source generators load, in a context
        /// of its own: this process already has the .NET 8 build. The test project copies it next to the tests.
        /// </summary>
        private static async Task<string?> StartNetStandardBuildAsync()
        {
            var path = Path.Join(AppContext.BaseDirectory, "netstandard2.0", "Uno.DevTools.Telemetry.dll");
            File.Exists(path).Should().BeTrue("the test project copies the netstandard2.0 build to {0}", path);

            var assembly = new AssemblyLoadContext("Uno.DevTools.Telemetry for netstandard2.0").LoadFromAssemblyPath(path);
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName.Should().Be(".NETStandard,Version=v2.0");

            var type = assembly.GetType("Uno.DevTools.Telemetry.Telemetry", throwOnError: true)!;
            var constructor = type.GetConstructor(new[]
            {
                typeof(string), typeof(string), typeof(Assembly), typeof(string), typeof(bool), typeof(Func<bool?>), typeof(Func<string>), typeof(string),
            })!;
            var telemetry = constructor.Invoke(new object?[] { DummyInstrumentationKey, "test", typeof(TelemetryQueueFolderTests).Assembly, null, false, null, null, null });
            try
            {
                return await (Task<string?>)type.GetMethod("GetMachineIdAsync")!.Invoke(telemetry, new object[] { CancellationToken.None })!;
            }
            finally
            {
                type.GetMethod("Dispose")!.Invoke(telemetry, null);
            }
        }

        private Telemetry CreateTelemetry(UserFolders folders, CapturingTelemetryChannel? channel, CapturingTelemetryChannel? memoryChannel = null)
        {
            var telemetry = new Telemetry(
                DummyInstrumentationKey,
                "test",
                typeof(TelemetryQueueFolderTests).Assembly,
                sessionId: null,
                // TrackEvent only queues behind an initialization that runs in the background
                blockThreadInitialization: false,
                enabledProvider: null,
                currentDirectoryProvider: null,
                productName: null,
                channel,
                wasmSender: null,
                folders,
                // Never the real one in tests: it would send to the service.
                memoryChannel: () => memoryChannel ?? new CapturingTelemetryChannel());
            _instances.Add(telemetry);
            return telemetry;
        }

        private string CreateDirectory(string? parent = null)
        {
            var dir = Path.Join(parent ?? Path.GetTempPath(), $"telemetry_queue_folder_test_{Guid.NewGuid():N}");
            _directoriesToCleanup.Add(dir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Sets environment variables until disposed, then puts back what they were.
        /// </summary>
        private sealed class EnvironmentScope : IDisposable
        {
            private readonly (string Name, string? Value)[] _original;

            public EnvironmentScope(params (string Name, string? Value)[] variables)
            {
                _original = variables.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name))).ToArray();
                foreach (var (name, value) in variables)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }

            public void Dispose()
            {
                foreach (var (name, value) in _original)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        /// <summary>
        /// Records every message written to <see cref="Trace"/> while installed.
        /// </summary>
        private sealed class CapturingTraceListener : TraceListener
        {
            private readonly ConcurrentQueue<string> _messages = new();

            public IReadOnlyList<string> Messages => _messages.ToList();

            public override void Write(string? message)
            {
                if (message is not null)
                {
                    _messages.Enqueue(message);
                }
            }

            public override void WriteLine(string? message)
            {
                if (message is not null)
                {
                    _messages.Enqueue(message);
                }
            }
        }
    }
}
