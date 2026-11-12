// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// What the desktop channel's folders are chosen from: the platform, the user's folders, and the calls that make a
    /// folder the user's alone. <see cref="Current"/> reads them from this process; tests describe other machines.
    /// </summary>
    internal sealed class UserFolders
    {
        internal bool IsWindows { get; init; }

        internal bool IsMacOS { get; init; }

        internal bool IsLinux { get; init; }

        /// <summary>
        /// The value of <c>TMPDIR</c>, if any.
        /// </summary>
        internal string? TmpDir { get; init; }

        /// <summary>
        /// The value of <c>XDG_CACHE_HOME</c>, if any.
        /// </summary>
        internal string? XdgCacheHome { get; init; }

        internal string? HomeDirectory { get; init; }

        internal string TempPath { get; init; } = "";

        internal string? ApplicationData { get; init; }

        /// <summary>
        /// The calls that create a folder and set its mode, or <see langword="null"/> when the runtime has none.
        /// </summary>
        internal FolderModes? Modes { get; init; } = UnixFolder.DefaultModes;

        internal Func<bool> IsRoot { get; init; } = UnixFolder.IsRoot;

        /// <summary>
        /// Whether a folder is root's own home, or inside it.
        /// </summary>
        internal Func<string, bool> IsInHomeOfRoot { get; init; } = UnixFolder.IsInHomeOfRoot;

        internal static UserFolders Current() => new UserFolders
        {
            IsWindows = PlatformDetection.IsWindows,
            IsMacOS = PlatformDetection.IsMacOS,
            IsLinux = PlatformDetection.IsLinux,
            TmpDir = Environment.GetEnvironmentVariable("TMPDIR"),
            XdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            TempPath = Path.GetTempPath(),
            ApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };

        /// <summary>
        /// Whether <paramref name="path"/> can hold a folder of the user's own: it is rooted, and isn't the root of the file
        /// system, which is what a home of <c>/</c> leaves, e.g. in a container run as a user with no entry of its own.
        /// </summary>
        internal static bool IsUsableBase([NotNullWhen(true)] string? path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            {
                return false;
            }

            var root = Path.GetPathRoot(path) ?? "";
            return !string.Equals(TrimSeparators(path!), TrimSeparators(root), StringComparison.Ordinal);
        }

        /// <summary>
        /// Appends <paramref name="folderNames"/> to <paramref name="basePath"/>. Each must be a single folder name: unlike
        /// <see cref="Path.Combine(string[])"/>, nothing appended can replace what comes before it.
        /// </summary>
        /// <exception cref="ArgumentException">A name is empty, rooted, or holds a separator.</exception>
        internal static string Join(string basePath, params string[] folderNames)
        {
            foreach (var name in folderNames)
            {
                if (name.Length == 0 || Path.IsPathRooted(name) || name.Any(IsSeparator))
                {
                    throw new ArgumentException($"\"{name}\" is not a single folder name.", nameof(folderNames));
                }
            }

#if NET8_0_OR_GREATER
            return Path.Join(basePath, string.Join(Path.DirectorySeparatorChar.ToString(), folderNames));
#else
            // netstandard2.0 has no Path.Join. As it does, a separator is added only where there is none.
            var path = basePath;
            foreach (var name in folderNames)
            {
                path = path.Length == 0 || IsSeparator(path[path.Length - 1])
                    ? path + name
                    : path + Path.DirectorySeparatorChar + name;
            }

            return path;
#endif
        }

        private static bool IsSeparator(char c)
            => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

        private static string TrimSeparators(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
