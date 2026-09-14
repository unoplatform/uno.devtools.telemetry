using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Uno.DevTools.Telemetry.Tests
{
    /// <summary>
    /// Allocates unique temp file paths for a test and deletes whatever was created on cleanup.
    /// Shared by every test class that drives <see cref="FileTelemetry"/> through a real file.
    /// </summary>
    internal sealed class TempFiles
    {
        private readonly List<string> _paths = new List<string>();

        /// <summary>
        /// Returns a fresh path under the temp directory. The file is not created; it is deleted on
        /// <see cref="Cleanup"/> if the test wrote it.
        /// </summary>
        public string Create(string extension = ".log")
        {
            var path = Path.Join(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}{extension}");
            _paths.Add(path);
            return path;
        }

        public void Cleanup()
        {
            foreach (var path in _paths.Where(File.Exists))
            {
                File.Delete(path);
            }

            _paths.Clear();
        }
    }
}
