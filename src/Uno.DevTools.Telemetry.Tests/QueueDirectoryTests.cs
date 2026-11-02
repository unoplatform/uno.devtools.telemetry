using System.IO;

namespace Uno.DevTools.Telemetry.Tests
{
    [TestClass]
    public class QueueDirectoryTests
    {
        private const string Home = "/home/someone";
        private const string Temp = "/tmp/";

        [TestMethod]
        public void Given_Linux_When_XdgCacheHomeIsSet_Then_TheQueueIsUnderIt()
        {
            QueueDirectory.GetPath(isLinux: true, xdgCacheHome: "/var/cache/someone", homeDirectory: Home, tempPath: Temp)
                .Should().Be(Path.Combine("/var/cache/someone", "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(null, DisplayName = "XDG_CACHE_HOME unset")]
        [DataRow("", DisplayName = "XDG_CACHE_HOME empty")]
        [DataRow("relative/cache", DisplayName = "XDG_CACHE_HOME relative, which the specification says to ignore")]
        public void Given_Linux_When_XdgCacheHomeIsNotUsable_Then_TheQueueIsUnderTheHomeCache(string? xdgCacheHome)
        {
            QueueDirectory.GetPath(isLinux: true, xdgCacheHome, homeDirectory: Home, tempPath: Temp)
                .Should().Be(Path.Combine(Home, ".cache", "uno", "telemetry", "outbox"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("relative/home")]
        public void Given_Linux_When_ThereIsNoHome_Then_NothingIsQueued(string? homeDirectory)
        {
            // The shared temp folder is never a fallback
            QueueDirectory.GetPath(isLinux: true, xdgCacheHome: null, homeDirectory, tempPath: Temp)
                .Should().BeNull();
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("/var/cache/someone")]
        public void Given_NotLinux_When_GettingThePath_Then_TheQueueStaysInTheTempFolder(string? xdgCacheHome)
        {
            // Windows and macOS give each user a temp folder of their own
            QueueDirectory.GetPath(isLinux: false, xdgCacheHome, homeDirectory: Home, tempPath: Temp)
                .Should().Be(Path.Combine(Temp, ".uno", "telemetry", "outbox"));
        }
    }
}
