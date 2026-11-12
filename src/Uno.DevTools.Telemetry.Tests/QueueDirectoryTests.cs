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
            prepared.Should().BeTrue(failure?.Reason ?? string.Empty);
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
            prepared.Should().BeTrue(failure?.Reason ?? string.Empty);
            foreach (var folder in OwnFolders(path))
            {
                File.GetUnixFileMode(folder).Should().Be(OwnerOnly, folder);
            }
        }

        [TestMethod]
        [DataRow(0x1FF, DisplayName = "0777: everyone may write")]
        [DataRow(0x1C2, DisplayName = "0702: others may write")]
        [DataRow(0x1D2, DisplayName = "0722: the group and others may write")]
        [DataRow(0x3FF, DisplayName = "1777: everyone may write, with the sticky bit, as /tmp")]
        public void Given_BaseFolderOthersCanWriteTo_When_Prepared_Then_ThereIsNoUsableFolder(int baseMode)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange - e.g. HOME=/tmp, whose .cache another user created first: they could rename uno away after the
            // check, and put their own folder, or a link, in its place. The sticky bit doesn't stop the folder's owner.
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            Directory.CreateDirectory(baseFolder);
            File.SetUnixFileMode(baseFolder, (UnixFileMode)baseMode);

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home), out var failure);

            // Assert
            prepared.Should().BeFalse();
            (failure?.Reason).Should().Contain("other users can write to").And.Contain(baseFolder);
            (failure?.Code).Should().Be(QueueRefusal.SharedParent);
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
            prepared.Should().BeTrue(failure?.Reason ?? string.Empty);
            File.GetUnixFileMode(baseFolder).Should().Be((UnixFileMode)baseMode, "the base folder isn't the library's to change");
            foreach (var folder in OwnFolders(path))
            {
                File.GetUnixFileMode(folder).Should().Be(OwnerOnly, folder);
            }
        }

        [TestMethod]
        public void Given_BaseFolderAnotherUserOwns_When_Prepared_Then_ThereIsNoUsableFolderAndNothingIsCreated()
        {
            // Arrange - its owner could rename uno away, whatever its mode: the system refuses to change it for anyone else
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            var modes = FakeModes.Accepting(
                getMode: _ => 0x1ED,
                setMode: (folder, _) =>
                {
                    if (folder == baseFolder)
                    {
                        throw new UnauthorizedAccessException("Operation not permitted");
                    }
                });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: modes), out var failure);

            // Assert
            prepared.Should().BeFalse();
            (failure?.Reason).Should().Contain("the system refused to set the permissions of").And.Contain(baseFolder).And.Contain("another user");
            (failure?.Code).Should().Be(QueueRefusal.NotOwned);
            Directory.EnumerateFileSystemEntries(baseFolder).Should().BeEmpty("nothing is created in it");
        }

        [TestMethod]
        public void Given_BaseFolderRootOwns_When_PreparedByAnotherUser_Then_TheSystemRefusesAndThereIsNoUsableFolder()
        {
            // The real calls: the root of the file system belongs to root, and holds a queue path such as /uno/telemetry/outbox.
            // Root may change any folder's mode, so the glibc CI job, which runs as an ordinary user, is the one that shows it.
            if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            {
                TestRequirements.Skip("Needs a Unix user other than root.", TestRequirements.LinuxUser);
                return;
            }

            // Arrange
            const string baseFolder = "/";
            var path = Path.Join(baseFolder, $"uno_{Guid.NewGuid():N}", "telemetry", "outbox");
            var mode = File.GetUnixFileMode(baseFolder);
            try
            {
                // Act
                var prepared = QueueDirectory.TryPrepare(path, Unix(CreateHome()), out var failure);

                // Assert
                prepared.Should().BeFalse();
                (failure?.Reason).Should().Contain("the system refused to set the permissions of /,");
            }
            finally
            {
                if (File.GetUnixFileMode(baseFolder) != mode)
                {
                    File.SetUnixFileMode(baseFolder, mode);
                }
            }
        }

        [TestMethod]
        public void Given_LinkSwappedInWhileTheFoldersAreSet_When_Prepared_Then_ItIsFoundAndThereIsNoUsableFolder()
        {
            // Arrange - someone who can write to the base folder, as its group may, puts a link where uno was just checked:
            // creating a folder and setting its mode follow it
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var uno = OwnFolders(path)[0];
            var elsewhere = Path.Join(home, "elsewhere");
            Directory.CreateDirectory(elsewhere);
            var swapped = false;
            var modes = FakeModes.Accepting(setMode: (folder, _) =>
            {
                if (folder == uno && !swapped)
                {
                    swapped = true;
                    Directory.Delete(uno);
                    CreateLinkOrSkip(uno, elsewhere);
                }
            });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: modes), out var failure);

            // Assert
            swapped.Should().BeTrue();
            prepared.Should().BeFalse();
            (failure?.Reason).Should().Contain("is a symbolic link").And.Contain(uno);
            (failure?.Code).Should().Be(QueueRefusal.SymbolicLink);
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
            (failure?.Reason).Should().Contain("is a symbolic link").And.Contain(link);
            (failure?.Code).Should().Be(QueueRefusal.SymbolicLink);
            if (!dangling && !OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(target).Should().Be(targetMode, "a link must not be followed to change its target");
            }
        }

        [TestMethod]
        public void Given_FolderWhoseModeTheSystemWontChange_When_Prepared_Then_ThereIsNoUsableFolderAndTheReasonSaysSo()
        {
            // Arrange - as for a folder that belongs to another user, who alone may change its mode; the base folder is the
            // user's own
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var baseFolder = Path.Join(home, ".cache");
            var modes = FakeModes.Accepting(setMode: (folder, _) =>
            {
                if (folder != baseFolder)
                {
                    throw new UnauthorizedAccessException("Operation not permitted");
                }
            });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, Unix(home, modes: modes), out var failure);

            // Assert
            prepared.Should().BeFalse();
            (failure?.Reason).Should().Contain("the system refused to set the permissions of").And.Contain("owner-only");
            (failure?.Code).Should().Be(QueueRefusal.NotOwned);
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
            (failure?.Reason).Should().Contain("doesn't keep Unix permissions").And.Contain("0755").And.NotContain("another user");
            (failure?.Code).Should().Be(QueueRefusal.NoUnixPermissions);
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
            (failure?.Reason).Should().Contain("runtime");
            (failure?.Code).Should().Be(QueueRefusal.NoUnixModes);
            Directory.EnumerateFileSystemEntries(home).Should().BeEmpty();
        }

        [TestMethod]
        public void Given_RootWithAHomeThatIsntRoots_When_Prepared_Then_ThereIsNoUsableFolderAndNothingIsCreated()
        {
            // Arrange - as under sudo -E, or in a container that mounts the user's home and sets HOME to it: whether sudo
            // or doas started it makes no difference. Fake modes, so the check is reached on Windows too, where the
            // runtime has none.
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var askedAbout = new List<string>();
            var folders = Unix(home, isRoot: () => true, modes: FakeModes.Accepting(), isInHomeOfRoot: folder =>
            {
                askedAbout.Add(folder);
                return false;
            });

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeFalse();
            (failure?.Code).Should().Be(QueueRefusal.RootOutsideHome);
            (failure?.Reason).Should().Contain("runs as root").And.Contain(Path.Join(home, ".cache"));
            askedAbout.Should().Equal(Path.Join(home, ".cache"));
            Directory.EnumerateFileSystemEntries(home).Should().BeEmpty("root must not create folders in another user's home");
        }

        [TestMethod]
        public void Given_RootKeepingTheUsersCacheFolder_When_Chosen_Then_ThereIsNoUsableFolderAndNothingIsCreated()
        {
            // Arrange - HOME is root's, but XDG_CACHE_HOME is the user's, as with sudo -E where always_set_home is on
            var rootHome = CreateHome();
            var userCache = CreateHome();
            var folders = new UserFolders
            {
                IsLinux = true,
                HomeDirectory = rootHome,
                XdgCacheHome = userCache,
                TempPath = Path.GetTempPath(),
                Modes = FakeModes.Accepting(),
                IsRoot = () => true,
                IsInHomeOfRoot = folder => folder.StartsWith(rootHome, StringComparison.Ordinal),
            };

            // Act
            var path = QueueDirectory.Choose(folders, out var failure);

            // Assert
            path.Should().BeNull();
            (failure?.Code).Should().Be(QueueRefusal.RootOutsideHome);
            (failure?.Reason).Should().Contain(userCache);
            Directory.EnumerateFileSystemEntries(userCache).Should().BeEmpty("root must not create folders in another user's cache");
        }

        [TestMethod]
        public void Given_RootWithItsOwnHome_When_Prepared_Then_TheFolderIsUsed()
        {
            // Arrange
            var home = CreateHome();
            var path = QueuePathUnder(home);
            var folders = Unix(home, isRoot: () => true, isInHomeOfRoot: _ => true, modes: FakeModes.Accepting());

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeTrue(failure?.Reason ?? string.Empty);
            Directory.Exists(path).Should().BeTrue();
        }

        [TestMethod]
        [DataRow(true, true, DisplayName = "In root's home: the folder is made private")]
        [DataRow(false, false, DisplayName = "Outside root's home: nothing is created")]
        public void Given_RunningAsRoot_When_Prepared_Then_OnlyRootsOwnHomeIsUsed(bool inHomeOfRoot, bool expected)
        {
            // The real checks and calls, /etc/passwd included: only a process running as root can take this path. The musl
            // CI job runs as root.
            if (OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess)
            {
                TestRequirements.Skip("Needs a process running as root.", TestRequirements.LinuxRoot);
                return;
            }

            // Arrange
            var home = inHomeOfRoot ? CreateHome(HomeOfRoot()) : CreateHome();
            var path = QueuePathUnder(home);
            var folders = new UserFolders
            {
                IsLinux = OperatingSystem.IsLinux(),
                IsMacOS = OperatingSystem.IsMacOS(),
                HomeDirectory = home,
                TempPath = Path.GetTempPath(),
            };

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().Be(expected, failure?.Reason ?? string.Empty);
            if (expected)
            {
                File.GetUnixFileMode(path).Should().Be(OwnerOnly);
            }
            else
            {
                (failure?.Code).Should().Be(QueueRefusal.RootOutsideHome);
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
                IsInHomeOfRoot = _ => throw new InvalidOperationException("must not be called"),
            };

            // Act
            var prepared = QueueDirectory.TryPrepare(path, folders, out var failure);

            // Assert
            prepared.Should().BeTrue(failure?.Reason ?? string.Empty);
            Directory.Exists(path).Should().BeTrue("the storage never creates its folder");
        }

        /// <summary>
        /// A Unix machine whose user is <paramref name="home"/>'s, and isn't root unless <paramref name="isRoot"/> says so:
        /// the tests also run as root, in CI, and the root check is tested on its own.
        /// </summary>
        internal static UserFolders Unix(
            string home,
            Func<bool>? isRoot = null,
            Func<string, bool>? isInHomeOfRoot = null,
            FolderModes? modes = null,
            bool useDefaultModes = true)
            => new UserFolders
            {
                IsLinux = true,
                HomeDirectory = home,
                TempPath = Path.GetTempPath(),
                ApplicationData = Path.Join(home, ".config"),
                Modes = modes ?? (useDefaultModes ? UnixFolder.DefaultModes : null),
                IsRoot = isRoot ?? (() => false),
                IsInHomeOfRoot = isInHomeOfRoot ?? (_ => false),
            };

        internal static string QueuePathUnder(string home) => Path.Join(home, ".cache", "uno", "telemetry", "outbox");

        /// <summary>
        /// Root's home, as <c>/etc/passwd</c> lists it.
        /// </summary>
        internal static string HomeOfRoot()
            => File.ReadAllLines("/etc/passwd")
                .Select(line => line.Split(':'))
                .First(fields => fields.Length >= 6 && fields[2] == "0")[5];

        private string CreateHome(string? parent = null)
        {
            var dir = Path.Join(parent ?? Path.GetTempPath(), $"telemetry_queue_test_{Guid.NewGuid():N}");
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
