using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// The managed calls that make a folder the user's alone, run for real where the platform has them.
    /// </summary>
    [TestClass]
    public class UnixFolderTests
    {
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        private static readonly string[] UserDatabase =
        {
            "# A comment",
            "root:x:0:0:root:/root:/bin/bash",
            "toor:*:0:0:Bourne-again Superuser:/var/toor:",
            "someone:x:1000:1000:Someone:/home/someone:/bin/sh",
            "broken",
            "+::::::",
        };

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
        [DataRow("/root", true)]
        [DataRow("/root/", true, DisplayName = "With a trailing separator")]
        [DataRow("/root/.cache", true, DisplayName = "Inside root's home")]
        [DataRow("/var/toor", true, DisplayName = "The home of another user id 0 entry")]
        [DataRow("/home/someone", false, DisplayName = "Another user's home, as under sudo -E")]
        [DataRow("/home/someone/.cache", false, DisplayName = "Another user's cache, as when sudo keeps XDG_CACHE_HOME")]
        [DataRow("/rootkit", false, DisplayName = "A name that starts like root's home")]
        [DataRow("/root/../home/someone", false, DisplayName = "Leading out of root's home")]
        [DataRow("/", false)]
        [DataRow("/ROOT", false, DisplayName = "Paths are case-sensitive")]
        public void Given_UserDatabase_When_AskedWhetherAFolderIsInRootsHome_Then_OnlyAUserId0HomeAndWhatIsInItAre(string path, bool expected)
        {
            UnixFolder.IsInHomeOfRoot(path, UserDatabase).Should().Be(expected);
        }

        [TestMethod]
        public void Given_RootListedWithTheRootOfTheFileSystemAsItsHome_When_Asked_Then_NothingIsInIt()
        {
            // Otherwise every folder would be inside it
            UnixFolder.IsInHomeOfRoot("/home/someone/.cache", new[] { "root:x:0:0:root:/:/bin/sh" }).Should().BeFalse();
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void Given_NoFolder_When_AskedWhetherItIsInRootsHome_Then_ItIsNot(string? path)
        {
            UnixFolder.IsInHomeOfRoot(path).Should().BeFalse();
        }

        [TestMethod]
        public void Given_ThisMachinesUserDatabase_When_AskedAboutRootsHome_Then_ItIsFound()
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            var rootHome = OperatingSystem.IsMacOS() ? "/var/root" : "/root";
            UnixFolder.IsInHomeOfRoot(rootHome).Should().BeTrue();
            UnixFolder.IsInHomeOfRoot(Path.Join(rootHome, ".cache")).Should().BeTrue();
            UnixFolder.IsInHomeOfRoot(Path.GetTempPath()).Should().BeFalse();
        }

        [TestMethod]
        [DataRow(false, DisplayName = "Environment.IsPrivilegedProcess, as on .NET 8 and later")]
        [DataRow(true, DisplayName = "Through reflection, as the netstandard2.0 build does")]
        public void Given_ThisProcess_When_AskedWhetherItRunsAsRoot_Then_TheEffectiveUserDecides(bool byReflection)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix only: on Windows a privileged process is an administrator.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            var isRoot = byReflection ? UnixFolder.IsRootByReflection() : UnixFolder.IsRoot();

            isRoot.Should().Be(Environment.IsPrivilegedProcess);
        }

        [TestMethod]
        [DataRow(false, DisplayName = "File.SetUnixFileMode, as on .NET 8 and later")]
        [DataRow(true, DisplayName = "Through reflection, as the netstandard2.0 build does")]
        public void Given_FolderOthersCanOpen_When_SetToOwnerOnly_Then_OnlyItsOwnerCan(bool byReflection)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            // Arrange
            var dir = CreateDirectory();
            File.SetUnixFileMode(dir, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // Act
            var failure = UnixFolder.TrySetOwnerOnly(Modes(byReflection), dir);

            // Assert
            failure.Should().BeNull();
            File.GetUnixFileMode(dir).Should().Be(OwnerOnly);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Given_MissingFolder_When_Created_Then_ItIsTheOwnersAloneFromTheStart(bool byReflection)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            var folder = Path.Join(CreateDirectory(), "folder");

            Modes(byReflection).CreateDirectory(folder);

            File.GetUnixFileMode(folder).Should().Be(OwnerOnly);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Given_MissingFolder_When_SetToOwnerOnly_Then_ItFailsWithoutThrowing(bool byReflection)
        {
            if (OperatingSystem.IsWindows())
            {
                TestRequirements.Skip("Unix file modes only.", TestRequirements.LinuxUser, TestRequirements.LinuxRoot);
                return;
            }

            var missing = Path.Join(Path.GetTempPath(), $"telemetry_missing_{Guid.NewGuid():N}");

            var failure = UnixFolder.TrySetOwnerOnly(Modes(byReflection), missing);

            failure.Should().Contain(missing);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Given_FolderAnotherUserOwns_When_SetToOwnerOnly_Then_TheSystemRefuses(bool byReflection)
        {
            // Root may change any folder's mode, so only another user shows that a successful change proves ownership.
            // The glibc CI job runs as an ordinary user.
            if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            {
                TestRequirements.Skip("Needs a Unix user other than root.", TestRequirements.LinuxUser);
                return;
            }

            // The root of the file system belongs to root. Should the change go through anyway, it is undone.
            const string foreign = "/";
            var mode = File.GetUnixFileMode(foreign);
            try
            {
                var failure = UnixFolder.TrySetOwnerOnly(Modes(byReflection), foreign);

                failure.Should().Contain("the system refused");
            }
            finally
            {
                if (File.GetUnixFileMode(foreign) != mode)
                {
                    File.SetUnixFileMode(foreign, mode);
                }
            }
        }

        private static FolderModes Modes(bool byReflection)
            => (byReflection ? UnixFolder.ReflectedModes : UnixFolder.DefaultModes)
                ?? throw new InvalidOperationException("This runtime has no Unix mode calls.");

        private string CreateDirectory()
        {
            var dir = Path.Join(Path.GetTempPath(), $"telemetry_unix_folder_test_{Guid.NewGuid():N}");
            _directoriesToCleanup.Add(dir);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
