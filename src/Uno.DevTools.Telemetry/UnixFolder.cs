// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// What it takes to make a folder the current user's alone on Unix, in managed code only. Never used on Windows.
    /// </summary>
    internal static class UnixFolder
    {
        /// <summary>
        /// Mode 0700: the owner may list, create and enter; nobody else may do anything.
        /// </summary>
        internal const int OwnerOnlyMode = 0x1C0;

        /// <summary>
        /// Mode bit 0002: users outside the folder's owner and group may create, rename and delete entries.
        /// </summary>
        /// <remarks>
        /// The group's write bit, 0020, is left out on purpose. Where each user has a group of their own, as on Ubuntu,
        /// Debian and Fedora, the usual umask 002 makes <c>~/.cache</c> 0775, and that group holds only its user.
        /// </remarks>
        internal const int WritableByOthers = 0x2;

        /// <summary>
        /// Mode bit 01000, the sticky bit: in a folder that has it, only an entry's owner may rename or delete it.
        /// </summary>
        internal const int Sticky = 0x200;

        private const string UserDatabasePath = "/etc/passwd";

        /// <summary>
        /// <see cref="Environment"/>'s <c>IsPrivilegedProcess</c>, which .NET 8 added, found once for the netstandard2.0 build.
        /// </summary>
        private static readonly PropertyInfo? IsPrivilegedProcessProperty = FindIsPrivilegedProcess();

        /// <summary>
        /// The mode calls the netstandard2.0 build makes, through reflection; <see langword="null"/> when the runtime has
        /// none, as before .NET 7.
        /// </summary>
        internal static readonly FolderModes? ReflectedModes = ReflectedFolderModes.Find();

#if NET8_0_OR_GREATER
        private static readonly FolderModes? DirectModes = OperatingSystem.IsWindows() ? null : new DirectFolderModes();
#endif

        /// <summary>
        /// The mode calls this build makes, or <see langword="null"/> when the runtime has none.
        /// </summary>
        internal static FolderModes? DefaultModes
#if NET8_0_OR_GREATER
            => DirectModes;
#else
            => ReflectedModes;
#endif

        /// <summary>
        /// Whether this process runs as root, which may create and change any folder.
        /// </summary>
        internal static bool IsRoot()
#if NET8_0_OR_GREATER
            => Environment.IsPrivilegedProcess;
#else
            => IsRootByReflection();
#endif

        /// <summary>
        /// <see cref="IsRoot"/> as the netstandard2.0 build does it. Built into every target so that the tests, which run on
        /// .NET 8, exercise it too.
        /// </summary>
        /// <remarks>
        /// <c>Environment.IsPrivilegedProcess</c> tells by the effective user id. Before .NET 8 the effective user's name has
        /// to do, which misses a user id 0 account with another name, such as FreeBSD's <c>toor</c>.
        /// </remarks>
        internal static bool IsRootByReflection()
        {
            try
            {
                if (IsPrivilegedProcessProperty?.GetValue(null) is bool isPrivileged)
                {
                    return isPrivileged;
                }
            }
            catch (TargetInvocationException)
            {
                // Fall back to the name
            }

            return string.Equals(Environment.UserName, "root", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether <paramref name="home"/> is root's own home folder, as the user database lists it.
        /// </summary>
        /// <remarks>
        /// .NET can't tell who owns a folder, so root's own home stands in for "owned by root": a home other than the one
        /// <c>/etc/passwd</c> lists for user id 0 (<c>/root</c>, or <c>/var/root</c> on macOS) counts as someone else's.
        /// Every Unix system lists root there, even where other users come from a directory service. A database that
        /// can't be read confirms nothing.
        /// </remarks>
        internal static bool IsHomeOfRoot(string? home)
        {
            if (string.IsNullOrEmpty(home))
            {
                return false;
            }

            try
            {
                return IsHomeOfRoot(home!, File.ReadAllLines(UserDatabasePath));
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether <paramref name="home"/> is the home of a user id 0 entry in <paramref name="userDatabase"/>, the lines of
        /// an <c>/etc/passwd</c> file.
        /// </summary>
        internal static bool IsHomeOfRoot(string home, IEnumerable<string> userDatabase)
        {
            // name:password:uid:gid:comment:home:shell
            foreach (var fields in userDatabase.Select(line => line.Split(':')))
            {
                if (fields.Length >= 6 && fields[2] == "0" && fields[5].Length > 0 && SamePath(fields[5], home))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the mode of the folder at <paramref name="path"/> to 0700, and reads it back.
        /// </summary>
        /// <returns><see langword="null"/> when the folder now has mode 0700; otherwise why not. Never throws.</returns>
        internal static string? TrySetOwnerOnly(FolderModes modes, string path)
        {
            try
            {
                modes.SetMode(path, OwnerOnlyMode);
            }
            catch (UnauthorizedAccessException)
            {
                // Only a folder's owner, or root, may change its mode.
                return $"the system refused to set the permissions of {path} to owner-only, as it does for a folder that belongs to another user";
            }
            catch (IOException e)
            {
                return CantSet(path, e);
            }
            catch (NotSupportedException e)
            {
                return CantSet(path, e);
            }
            catch (ArgumentException e)
            {
                return CantSet(path, e);
            }

            if (!TryGetMode(modes, path, out var mode, out var failure))
            {
                return failure;
            }

            return mode == OwnerOnlyMode
                ? null
                : $"the file system of {path} doesn't keep Unix permissions: it is mode {ToOctal(mode)} after being set to {ToOctal(OwnerOnlyMode)}";
        }

        /// <summary>
        /// Reads the mode of <paramref name="path"/>, following a symbolic link.
        /// </summary>
        /// <returns>Whether it could. Never throws.</returns>
        internal static bool TryGetMode(FolderModes modes, string path, out int mode, [NotNullWhen(false)] out string? failure)
        {
            try
            {
                mode = modes.GetMode(path);
                failure = null;
                return true;
            }
            catch (IOException e)
            {
                return CantRead(path, e, out mode, out failure);
            }
            catch (UnauthorizedAccessException e)
            {
                return CantRead(path, e, out mode, out failure);
            }
            catch (NotSupportedException e)
            {
                return CantRead(path, e, out mode, out failure);
            }
            catch (ArgumentException e)
            {
                return CantRead(path, e, out mode, out failure);
            }
        }

        private static string CantSet(string path, Exception e)
            => $"the permissions of {path} couldn't be set to owner-only ({e.GetType().Name}: {e.Message})";

        private static bool CantRead(string path, Exception e, out int mode, out string failure)
        {
            mode = 0;
            failure = $"the permissions of {path} couldn't be read ({e.GetType().Name}: {e.Message})";
            return false;
        }

        internal static string ToOctal(int mode) => "0" + Convert.ToString(mode & 0xFFF, 8);

        private static bool SamePath(string left, string right)
            => string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.Ordinal);

        private static PropertyInfo? FindIsPrivilegedProcess()
        {
            try
            {
                return typeof(Environment).GetProperty("IsPrivilegedProcess", BindingFlags.Public | BindingFlags.Static) is { PropertyType: var type } property
                    && type == typeof(bool)
                    ? property
                    : null;
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }

#if NET8_0_OR_GREATER
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private sealed class DirectFolderModes : FolderModes
        {
            internal override void CreateDirectory(string path) => Directory.CreateDirectory(path, (UnixFileMode)OwnerOnlyMode);

            internal override int GetMode(string path) => (int)File.GetUnixFileMode(path);

            internal override void SetMode(string path, int mode) => File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
#endif

        /// <summary>
        /// <see cref="File"/>'s and <see cref="Directory"/>'s mode methods, which .NET 7 added, found once by reflection for
        /// the netstandard2.0 build. That build mostly runs in a modern .NET host (MSBuild, source generators), where they
        /// exist.
        /// </summary>
        private sealed class ReflectedFolderModes : FolderModes
        {
            private readonly MethodInfo _createDirectory;
            private readonly MethodInfo _getMode;
            private readonly MethodInfo _setMode;
            private readonly Type _unixFileMode;

            private ReflectedFolderModes(MethodInfo createDirectory, MethodInfo getMode, MethodInfo setMode)
            {
                _createDirectory = createDirectory;
                _getMode = getMode;
                _setMode = setMode;
                _unixFileMode = getMode.ReturnType;
            }

            internal static ReflectedFolderModes? Find()
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return null;
                }

                try
                {
                    // The getter's return type is the runtime's UnixFileMode, wherever that lives, and selects the overloads
                    // that take a path rather than a handle.
                    if (typeof(File).GetMethod("GetUnixFileMode", new[] { typeof(string) }) is not { ReturnType.IsEnum: true } getMode
                        || typeof(File).GetMethod("SetUnixFileMode", new[] { typeof(string), getMode.ReturnType }) is not { } setMode
                        || typeof(Directory).GetMethod("CreateDirectory", new[] { typeof(string), getMode.ReturnType }) is not { } createDirectory)
                    {
                        return null;
                    }

                    return new ReflectedFolderModes(createDirectory, getMode, setMode);
                }
                catch (AmbiguousMatchException)
                {
                    return null;
                }
            }

            internal override void CreateDirectory(string path) => Invoke(_createDirectory, path, ToUnixFileMode(OwnerOnlyMode));

            internal override int GetMode(string path) => Convert.ToInt32(Invoke(_getMode, path));

            internal override void SetMode(string path, int mode) => Invoke(_setMode, path, ToUnixFileMode(mode));

            private object ToUnixFileMode(int mode) => Enum.ToObject(_unixFileMode, mode);

            /// <summary>
            /// Calls <paramref name="method"/>, throwing what it throws rather than the reflection wrapper, as a direct
            /// call would.
            /// </summary>
            private static object? Invoke(MethodInfo method, params object[] arguments)
            {
                try
                {
                    return method.Invoke(null, arguments);
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    throw; // Not reached
                }
            }
        }
    }

    /// <summary>
    /// The calls that create a folder and set and read its Unix mode, the same on every build.
    /// </summary>
    internal abstract class FolderModes
    {
        /// <summary>
        /// Creates the folder with mode 0700, so that it is never open to others, even briefly. A missing folder above it
        /// is created too, but with the default mode: each folder that must be private is created on its own.
        /// </summary>
        internal abstract void CreateDirectory(string path);

        /// <summary>
        /// Reads a folder's mode, following a symbolic link.
        /// </summary>
        internal abstract int GetMode(string path);

        internal abstract void SetMode(string path, int mode);
    }
}
