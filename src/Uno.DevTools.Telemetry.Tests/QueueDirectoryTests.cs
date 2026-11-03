using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// The queue folder is chosen once, when telemetry starts, and outside Windows made the user's alone before anything
    /// is queued in it.
    /// </summary>
    [TestClass]
    public class QueueDirectoryTests
    {
        private const string Home = "/home/someone";
        private const string Temp = "/tmp/";
        private const string XdgCacheHome = "/var/cache/someone";

        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private const UnixFileMode OpenToAll = OwnerOnly
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        private readonly List<string> _directoriesToCleanup = new List<string>();

        [TestCleanup]
        public void Cleanup()
        {
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
        [DataRow(true, false, null, true, DisplayName = "Windows")]
        [DataRow(true, false, "/somewhere", true, DisplayName = "Windows, TMPDIR set")]
        [DataRow(false, true, "/var/folders/xy/abc/T/", true, DisplayName = "macOS, TMPDIR set, as in a login session")]
        [DataRow(false, true, null, false, DisplayName = "macOS, TMPDIR unset, as under sudo, cron or launchd")]
        [DataRow(false, true, "", false, DisplayName = "macOS, TMPDIR empty")]
        [DataRow(false, true, "relative/tmp", false, DisplayName = "macOS, TMPDIR relative")]
        [DataRow(false, false, null, false, DisplayName = "Linux, FreeBSD and other Unix systems")]
        [DataRow(false, false, "/home/someone/tmp", false, DisplayName = "Linux, FreeBSD and other Unix systems, TMPDIR set")]
        public void Given_APlatform_When_AskedWhetherTheTempFolderIsPerUser_Then_OnlyWindowsAndMacOSWithTmpDirSayYes(
            bool isWindows, bool isMacOS, string? tmpDir, bool expected)
        {
            PlatformDetection.IsTempFolderPerUser(isWindows, isMacOS, tmpDir).Should().Be(expected);
        }

        [TestMethod]
        [DataRow(true, false, null, null, DisplayName = "Windows")]
        [DataRow(true, false, null, XdgCacheHome, DisplayName = "Windows, XDG_CACHE_HOME set")]
        [DataRow(false, true, "/var/folders/xy/abc/T/", null, DisplayName = "macOS, TMPDIR set")]
        [DataRow(false, true, "/var/folders/xy/abc/T/", XdgCacheHome, DisplayName = "macOS, TMPDIR and XDG_CACHE_HOME set")]
        public void Given_ATempFolderOfTheUsersOwn_When_GettingThePath_Then_TheQueueIsInIt(
            bool isWindows, bool isMacOS, string? tmpDir, string? xdgCacheHome)
        {
            QueueDirectory.GetPath(isWindows, isMacOS, tmpDir, xdgCacheHome, Home, Temp)
                .Should().Be(Path.Join(Temp, ".uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(null, null, DisplayName = "TMPDIR unset")]
        [DataRow("", null, DisplayName = "TMPDIR empty")]
        [DataRow("relative/tmp", null, DisplayName = "TMPDIR relative")]
        [DataRow(null, XdgCacheHome, DisplayName = "TMPDIR unset, XDG_CACHE_HOME set, which macOS doesn't use")]
        public void Given_MacOSWithoutTmpDir_When_GettingThePath_Then_TheQueueIsInTheUsersCaches(string? tmpDir, string? xdgCacheHome)
        {
            QueueDirectory.GetPath(isWindows: false, isMacOS: true, tmpDir, xdgCacheHome, Home, Temp)
                .Should().Be(Path.Join(Home, "Library", "Caches", "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(null, DisplayName = "TMPDIR unset")]
        [DataRow("/home/someone/tmp", DisplayName = "TMPDIR set, which says nothing about who shares it")]
        public void Given_LinuxOrFreeBsd_When_XdgCacheHomeIsSet_Then_TheQueueIsUnderIt(string? tmpDir)
        {
            QueueDirectory.GetPath(isWindows: false, isMacOS: false, tmpDir, XdgCacheHome, Home, Temp)
                .Should().Be(Path.Join(XdgCacheHome, "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(null, DisplayName = "XDG_CACHE_HOME unset")]
        [DataRow("", DisplayName = "XDG_CACHE_HOME empty")]
        [DataRow("relative/cache", DisplayName = "XDG_CACHE_HOME relative, which the specification says to ignore")]
        [DataRow("/", DisplayName = "XDG_CACHE_HOME the root of the file system")]
        public void Given_LinuxOrFreeBsd_When_XdgCacheHomeIsNotUsable_Then_TheQueueIsUnderTheHomeCache(string? xdgCacheHome)
        {
            QueueDirectory.GetPath(isWindows: false, isMacOS: false, tmpDir: null, xdgCacheHome, Home, Temp)
                .Should().Be(Path.Join(Home, ".cache", "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(false, null, DisplayName = "Linux or FreeBSD, home unset")]
        [DataRow(false, "", DisplayName = "Linux or FreeBSD, home empty")]
        [DataRow(false, "relative/home", DisplayName = "Linux or FreeBSD, home relative")]
        [DataRow(false, "/", DisplayName = "Linux or FreeBSD, HOME=/, as for a container user with no entry of its own")]
        [DataRow(false, "//", DisplayName = "Linux or FreeBSD, HOME=//")]
        [DataRow(true, null, DisplayName = "macOS without TMPDIR, home unset")]
        [DataRow(true, "/", DisplayName = "macOS without TMPDIR, HOME=/")]
        public void Given_NoUsableHome_When_TheQueueWouldBeInIt_Then_ThereIsNoQueueFolder(bool isMacOS, string? homeDirectory)
        {
            // The shared temp folder is never a fallback
            QueueDirectory.GetPath(isWindows: false, isMacOS, tmpDir: null, xdgCacheHome: null, homeDirectory, Temp)
                .Should().BeNull();
        }

        [TestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow("relative", false)]
        [DataRow("/", false)]
        [DataRow("//", false)]
        [DataRow("/home/someone", true)]
        [DataRow("/home/someone/", true)]
        public void Given_APath_When_AskedWhetherItCanHoldTheUsersFolders_Then_OnlyARootedPathBelowTheRootCan(string? path, bool expected)
        {
            UserFolders.IsUsableBase(path).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("/etc", DisplayName = "Rooted, which Path.Combine would keep instead of the base")]
        [DataRow("a/b", DisplayName = "Several folders")]
        [DataRow("", DisplayName = "Empty")]
        public void Given_SomethingOtherThanAFolderName_When_Joined_Then_ItIsRefused(string name)
        {
            var join = () => UserFolders.Join(Home, ".cache", name);

            join.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        [DataRow(Home)]
        [DataRow(Temp, DisplayName = "Base ending with a separator")]
        public void Given_FolderNames_When_Joined_Then_TheyFollowTheBase(string basePath)
        {
            UserFolders.Join(basePath, "uno", "telemetry", "outbox")
                .Should().Be(Path.Join(basePath, "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(false, DisplayName = "File and Directory, as on .NET 8 and later")]
        [DataRow(true, DisplayName = "Through reflection, as the netstandard2.0 build does")]
        public void Given_NewFolder_When_Prepared_Then_ItAndTheFoldersThisLibraryNamesAreTheUsersAlone(bool byReflection)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange
            var home = CreateHome();
            var path = QueuePathUnder(home);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: byReflection ? UnixFolder.ReflectedModes : UnixFolder.DefaultModes), out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            File.GetUnixFileMode(Path.Join(home, ".cache")).Should().Be(OwnerOnly, "a missing folder is created with mode 0700 too");
            foreach (var folder in OwnFolders(path))
            {
                File.GetUnixFileMode(folder).Should().Be(OwnerOnly, folder);
            }
        }

        [TestMethod]
        public void Given_ExistingFolderOthersCanOpen_When_Prepared_Then_ItIsTightened()
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - as the netstandard2.0 build of an earlier version created it
            var home = CreateHome();
            var path = QueuePathUnder(home);
            Directory.CreateDirectory(path);
            foreach (var folder in OwnFolders(path))
            {
                File.SetUnixFileMode(folder, OpenToAll);
            }

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home), out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            foreach (var folder in OwnFolders(path))
            {
                File.GetUnixFileMode(folder).Should().Be(OwnerOnly, folder);
            }
        }

        [TestMethod]
        [DataRow(0x1FF, DisplayName = "0777: everyone may write")]
        [DataRow(0x1C2, DisplayName = "0702: others may write")]
        [DataRow(0x1D2, DisplayName = "0722: the group and others may write")]
        public void Given_BaseFolderOthersCanWriteTo_When_Prepared_Then_ThereIsNoUsableFolder(int baseMode)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - e.g. HOME=/tmp, whose .cache another user created first: they could rename uno away after the
            // check, and put their own folder, or a link, in its place
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            Directory.CreateDirectory(baseFolder);
            File.SetUnixFileMode(baseFolder, (UnixFileMode)baseMode);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home), out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("other users can write to").And.Contain(baseFolder);
            Directory.EnumerateFileSystemEntries(baseFolder).Should().BeEmpty("nothing is created in it");
        }

        [TestMethod]
        [DataRow(0x1FD, DisplayName = "0775: the usual ~/.cache where each user has a group of their own")]
        [DataRow(0x1F8, DisplayName = "0770: the group alone may read and write")]
        public void Given_BaseFolderOnlyItsGroupCanWriteTo_When_Prepared_Then_TheFolderIsUsed(int baseMode)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - Ubuntu's umask 002 makes ~/.cache group-writable, and that group holds only its user
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            Directory.CreateDirectory(baseFolder);
            File.SetUnixFileMode(baseFolder, (UnixFileMode)baseMode);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home), out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            File.GetUnixFileMode(baseFolder).Should().Be((UnixFileMode)baseMode, "the base folder isn't the library's to change");
            foreach (var folder in OwnFolders(path))
            {
                File.GetUnixFileMode(folder).Should().Be(OwnerOnly, folder);
            }
        }

        [TestMethod]
        public void Given_BaseFolderEveryoneCanWriteToWithTheStickyBit_When_Prepared_Then_TheFolderIsUsed()
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - like /tmp: only an entry's owner may rename it
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            Directory.CreateDirectory(baseFolder);
            File.SetUnixFileMode(baseFolder, (UnixFileMode)0x3FF);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home), out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            File.GetUnixFileMode(path).Should().Be(OwnerOnly);
        }

        [TestMethod]
        [DataRow("outbox", false)]
        [DataRow("telemetry", false)]
        [DataRow("uno", false)]
        [DataRow("outbox", true, DisplayName = "outbox, a link pointing nowhere")]
        public void Given_SymbolicLink_When_Prepared_Then_ItIsRefusedAndWhatItPointsToIsLeftAlone(string linkedFolder, bool dangling)
        {
            // Arrange - a link someone else could have left, to a folder they can read
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var link = OwnFolders(path).Single(folder => Path.GetFileName(folder) == linkedFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var target = Path.Join(home, "elsewhere");
            if (!dangling)
            {
                Directory.CreateDirectory(target);
            }

            CreateLinkOrSkip(link, target);
            var targetMode = !dangling && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(target) : default;

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: OperatingSystem.IsWindows() ? FakeModes.Accepting() : UnixFolder.DefaultModes), out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("is a symbolic link").And.Contain(link);
            if (!dangling && !OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(target).Should().Be(targetMode, "a link must not be followed to change its target");
            }
        }

        [TestMethod]
        public void Given_FolderWhoseModeTheSystemWontChange_When_Prepared_Then_ThereIsNoUsableFolderAndTheReasonSaysSo()
        {
            // Arrange - as for a folder that belongs to another user, who alone may change its mode
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var modes = FakeModes.Accepting(setMode: (_, _) => throw new UnauthorizedAccessException("Operation not permitted"));

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: modes), out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("the system refused to set the permissions of").And.Contain("owner-only");
        }

        [TestMethod]
        public void Given_FileSystemThatKeepsNoModes_When_Prepared_Then_ThereIsNoUsableFolderAndTheReasonDoesntBlameOwnership()
        {
            // Arrange - the change is accepted, and the folder stays as open as before
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var modes = FakeModes.Accepting(getMode: _ => 0x1ED, setMode: (_, _) => { });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: modes), out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("doesn't keep Unix permissions").And.Contain("0755").And.NotContain("another user");
        }

        [TestMethod]
        public void Given_RuntimeThatCantSetModes_When_Prepared_Then_ThereIsNoUsableFolderAndNothingIsCreated()
        {
            // Arrange - the netstandard2.0 build in a runtime older than .NET 7
            var home = CreateHome();
            var path = QueuePathUnder(home);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, useDefaultModes: false), out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("runtime");
            Directory.EnumerateFileSystemEntries(home).Should().BeEmpty();
        }

        [TestMethod]
        public void Given_RootThroughSudoWithTheUsersHome_When_Prepared_Then_ThereIsNoUsableFolderAndNothingIsCreated()
        {
            // Arrange - as under sudo -E, which keeps the user's HOME
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var askedAbout = new List<string?>();
            var folders = Unix(home, isRoot: () => true, isElevatedFromAnotherUser: true, isHomeOfRoot: h =>
            {
                askedAbout.Add(h);
                return false;
            });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeFalse();
            failure.Should().Contain("root through sudo or doas");
            askedAbout.Should().Equal(home);
            Directory.EnumerateFileSystemEntries(home).Should().BeEmpty("root must not create folders in another user's home");
        }

        [TestMethod]
        [DataRow(false, false, DisplayName = "Root without sudo or doas, as in a container that sets HOME")]
        [DataRow(true, true, DisplayName = "Root through sudo, with root's own home")]
        public void Given_RootThatIsTheHomesUser_When_Prepared_Then_TheFolderIsUsed(bool isElevatedFromAnotherUser, bool isHomeOfRoot)
        {
            // Arrange
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var folders = Unix(
                home,
                isRoot: () => true,
                isElevatedFromAnotherUser: isElevatedFromAnotherUser,
                isHomeOfRoot: _ => isHomeOfRoot,
                modes: FakeModes.Accepting());

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            Directory.Exists(path).Should().BeTrue();
        }

        [TestMethod]
        [DataRow(true, false, DisplayName = "Through sudo or doas, with a home that isn't root's: nothing is created")]
        [DataRow(false, true, DisplayName = "Without sudo or doas, as in a container: the folder is made private")]
        public void Given_RunningAsRoot_When_Prepared_Then_OnlyTheSudoCaseIsRefused(bool isElevatedFromAnotherUser, bool expected)
        {
            // The real checks and calls, except for whether sudo started the process: only a process running as root can
            // take this path. The musl CI job runs as root.
            if (OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess)
            {
                TestRequirements.Skip("Needs a process running as root.", TestRequirements.LinuxRoot);
                return;
            }

            // Arrange
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var folders = new UserFolders
            {
                IsLinux = OperatingSystem.IsLinux(),
                IsMacOS = OperatingSystem.IsMacOS(),
                HomeDirectory = home,
                TempPath = Path.GetTempPath(),
                IsElevatedFromAnotherUser = isElevatedFromAnotherUser,
            };

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().Be(expected, failure ?? string.Empty);
            if (expected)
            {
                File.GetUnixFileMode(path).Should().Be(OwnerOnly);
            }
            else
            {
                failure.Should().Contain("root");
                Directory.EnumerateFileSystemEntries(home).Should().BeEmpty();
            }
        }

        [TestMethod]
        public void Given_Windows_When_Prepared_Then_TheFolderIsCreatedAndNothingIsChecked()
        {
            // Arrange - the temp folder's own permissions keep other users out
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var folders = new UserFolders
            {
                IsWindows = true,
                HomeDirectory = home,
                TempPath = home,
                Modes = null,
                IsRoot = () => throw new InvalidOperationException("must not be called"),
                IsHomeOfRoot = _ => throw new InvalidOperationException("must not be called"),
            };

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeTrue(failure ?? string.Empty);
            Directory.Exists(path).Should().BeTrue("the storage never creates its folder");
        }

        /// <summary>
        /// A Unix machine whose user is <paramref name="home"/>'s, and isn't root unless <paramref name="isRoot"/> says so:
        /// the tests also run as root, in CI, and the root check is tested on its own.
        /// </summary>
        internal static UserFolders Unix(
            string home,
            Func<bool>? isRoot = null,
            bool isElevatedFromAnotherUser = false,
            Func<string?, bool>? isHomeOfRoot = null,
            FolderModes? modes = null,
            bool useDefaultModes = true)
            => new UserFolders
            {
                IsLinux = true,
                HomeDirectory = home,
                TempPath = Path.GetTempPath(),
                ApplicationData = Path.Join(home, ".config"),
                IsElevatedFromAnotherUser = isElevatedFromAnotherUser,
                Modes = modes ?? (useDefaultModes ? UnixFolder.DefaultModes : null),
                IsRoot = isRoot ?? (() => false),
                IsHomeOfRoot = isHomeOfRoot ?? (_ => false),
            };

        internal static string QueuePathUnder(string home) => Path.Join(home, ".cache", "uno", "telemetry", "outbox");

        private string CreateHome()
        {
            var dir = Path.Join(Path.GetTempPath(), $"telemetry_queue_test_{Guid.NewGuid():N}");
            _directoriesToCleanup.Add(dir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// The folders this library names in a queue path: uno, telemetry and outbox.
        /// </summary>
        private static string[] OwnFolders(string path)
        {
            var telemetry = Path.GetDirectoryName(path)!;
            return new[] { Path.GetDirectoryName(telemetry)!, telemetry, path };
        }

        private static void CreateLinkOrSkip(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Windows only lets an administrator, or developer mode, create one
                TestRequirements.Skip($"Can't create a symbolic link here: {e.Message}", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
            }
        }
    }

    /// <summary>
    /// Folder modes that a test decides, on any platform: folders are created for real, and nothing else touches the disk.
    /// </summary>
    internal sealed class FakeModes : FolderModes
    {
        private readonly Func<string, int> _getMode;
        private readonly Action<string, int> _setMode;

        private FakeModes(Func<string, int> getMode, Action<string, int> setMode)
        {
            _getMode = getMode;
            _setMode = setMode;
        }

        /// <summary>
        /// Modes that every change is made to, unless <paramref name="getMode"/> or <paramref name="setMode"/> say otherwise.
        /// </summary>
        internal static FakeModes Accepting(Func<string, int>? getMode = null, Action<string, int>? setMode = null)
            => new FakeModes(getMode ?? (_ => UnixFolder.OwnerOnlyMode), setMode ?? ((_, _) => { }));

        internal override void CreateDirectory(string path) => Directory.CreateDirectory(path);

        internal override int GetMode(string path) => _getMode(path);

        internal override void SetMode(string path, int mode) => _setMode(path, mode);
    }
}
