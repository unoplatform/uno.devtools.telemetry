// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Chooses the folder the desktop channel queues unsent items in, and makes sure it is the current user's alone.
    /// </summary>
    /// <remarks>
    /// Whoever can write to the folder can leave files there for this process to send, and whoever can read it can read
    /// what this process queued, so it must belong to the current user alone. The temp folder does where
    /// <see cref="PlatformDetection.IsTempFolderPerUser"/> says so: on Windows, and on macOS when <c>TMPDIR</c> is set.
    /// Elsewhere it is usually <c>/tmp</c>, which every user shares, so the queue goes in the user's cache folder instead.
    /// The folder is then prepared once, by <see cref="TryPrepare"/>, before anything is queued in it.
    /// Nothing queued in an earlier location is moved, as when 1.4.0-dev.30 moved the queue into <c>outbox</c>: items left
    /// under the temp folder stay there for the versions that still look for them, and moving them would mean trusting
    /// files that any user could have written.
    /// </remarks>
    internal static class QueueDirectory
    {
        /// <summary>
        /// How many folders at the end of every queue path this library names: <c>uno</c> (or <c>.uno</c>),
        /// <c>telemetry</c> and <c>outbox</c>. Those are made the user's own; the folder that holds them is only checked
        /// (and created, if missing).
        /// </summary>
        private const int OwnFolderCount = 3;

        /// <summary>
        /// Chooses the queue folder for <paramref name="folders"/> and prepares it.
        /// </summary>
        /// <returns>The folder, or <see langword="null"/> when there is no folder of the user's own to queue in.</returns>
        /// <param name="folders">The machine the folder is chosen on.</param>
        /// <param name="refusal">When no folder is returned, why.</param>
        internal static string? Choose(UserFolders folders, out QueueRefusal? refusal)
        {
            var path = GetPath(folders.IsWindows, folders.IsMacOS, folders.TmpDir, folders.XdgCacheHome, folders.HomeDirectory, folders.TempPath);
            if (path is null)
            {
                refusal = new QueueRefusal(QueueRefusal.NoHome, "the user has no usable home folder to queue in (it is unset, relative or /)");
                return null;
            }

            return TryPrepare(path, folders, out refusal) ? path : null;
        }

        /// <summary>
        /// Returns the queue folder, or <see langword="null"/> when it would be in the user's home and there is no usable
        /// one: unset, relative, or <c>/</c>.
        /// </summary>
        /// <param name="isWindows">Whether this process runs on Windows.</param>
        /// <param name="isMacOS">Whether this process runs on macOS.</param>
        /// <param name="tmpDir">The value of <c>TMPDIR</c>, if any.</param>
        /// <param name="xdgCacheHome">The value of <c>XDG_CACHE_HOME</c>, if any.</param>
        /// <param name="homeDirectory">The user's home folder, if any.</param>
        /// <param name="tempPath">The temp folder.</param>
        internal static string? GetPath(bool isWindows, bool isMacOS, string? tmpDir, string? xdgCacheHome, string? homeDirectory, string tempPath)
        {
            if (PlatformDetection.IsTempFolderPerUser(isWindows, isMacOS, tmpDir))
            {
                // Older versions of this library send whatever is queued in ".uno/telemetry" without claiming it first,
                // so the queue lives in a subfolder they never look into.
                return UserFolders.Join(tempPath, ".uno", "telemetry", "outbox");
            }

            if (isMacOS)
            {
                return UserFolders.IsUsableBase(homeDirectory)
                    ? UserFolders.Join(homeDirectory, "Library", "Caches", "uno", "telemetry", "outbox")
                    : null;
            }

            // Like the XDG base directory specification, a relative path is ignored.
            if (UserFolders.IsUsableBase(xdgCacheHome))
            {
                return UserFolders.Join(xdgCacheHome, "uno", "telemetry", "outbox");
            }

            // No home, e.g. a service account without one: the only folder left is the shared temp folder, which is what
            // this replaces.
            return UserFolders.IsUsableBase(homeDirectory)
                ? UserFolders.Join(homeDirectory, ".cache", "uno", "telemetry", "outbox")
                : null;
        }

        /// <summary>
        /// Creates the queue folder at <paramref name="path"/> if needed and, outside Windows, makes sure only the current
        /// user can use it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Outside Windows: whoever can rename entries in the folder that holds the ones this library names could rename
        /// the queue folder away and put their own, or a link to it, in its place. So that folder must not be writable by
        /// users outside its owner and group, even with the sticky bit, which doesn't stop its owner; and it must be the
        /// user's own, which setting its mode to the one it has shows. Its group may write to it: that is usually the
        /// user's own group, which no one else is in, and refusing it would turn telemetry off for most Ubuntu users.
        /// </para>
        /// <para>
        /// Each folder this library names must be a real folder, not a symbolic link: changing a mode follows a link, so a
        /// link to someone else's folder would be checked in its place. They are checked again at the end, for a link
        /// swapped in meanwhile. Each is created with mode 0700 and then set to it and read back. Only a folder's owner, or
        /// root, may change its mode, so once that succeeds every file in the folder is the user's own, and none needs
        /// checking one by one. The folders are set from the outermost in: once one is private, nobody else can replace
        /// what is in it.
        /// </para>
        /// <para>
        /// Root may change any folder's mode, so for root none of this shows ownership, and a folder it created or changed
        /// in another user's home would be root's from then on. So root uses no folder outside its own home, whether or not
        /// sudo or doas started it: as in a container that mounts a user's home and sets <c>HOME</c> to it, or under
        /// <c>su -m</c>, which say nothing of the user.
        /// </para>
        /// <para>
        /// On Windows the temp folder's own permissions already keep other users out.
        /// </para>
        /// </remarks>
        /// <returns>Whether <paramref name="path"/> can be queued in.</returns>
        /// <param name="path">The queue folder, from <see cref="GetPath"/>.</param>
        /// <param name="folders">The machine the folder is prepared on.</param>
        /// <param name="refusal">When it can't, why.</param>
        internal static bool TryPrepare(string path, UserFolders folders, [NotNullWhen(false)] out QueueRefusal? refusal)
        {
            refusal = null;
            try
            {
                if (folders.IsWindows)
                {
                    // The storage doesn't create its folder: it would not know how to make it private elsewhere.
                    Directory.CreateDirectory(path);
                    return true;
                }

                if (folders.Modes is not { } modes)
                {
                    refusal = new QueueRefusal(QueueRefusal.NoUnixModes, "this runtime can't set a folder's Unix permissions (it predates .NET 7, which added File.SetUnixFileMode)");
                    return false;
                }

                var ownFolders = GetOwnFolders(path);
                var baseFolder = Path.GetDirectoryName(ownFolders[0])
                    ?? throw new ArgumentException($"{path} is too short to be a queue folder", nameof(path));

                if (folders.IsRoot() && !folders.IsInHomeOfRoot(baseFolder))
                {
                    // As under sudo -E, sudo keeping XDG_CACHE_HOME, or a container that mounts a user's home: folders root
                    // created or changed there would be root's, and the user's own runs could then no longer use them.
                    refusal = new QueueRefusal(QueueRefusal.RootOutsideHome, $"it runs as root, and {baseFolder}, which would hold its queue folder, isn't in root's home, so it creates and changes nothing there");
                    return false;
                }

                modes.CreateDirectory(baseFolder);
                if (!UnixFolder.TryGetMode(modes, baseFolder, out var baseMode, out refusal))
                {
                    return false;
                }

                if ((baseMode & UnixFolder.WritableByOthers) != 0)
                {
                    refusal = new QueueRefusal(QueueRefusal.SharedParent, $"other users can write to {baseFolder}, which holds its queue folder (mode {UnixFolder.ToOctal(baseMode)}), so they could replace the queue folder");
                    return false;
                }

                refusal = UnixFolder.TryConfirmOwner(modes, baseFolder, baseMode);
                if (refusal is not null)
                {
                    return false;
                }

                foreach (var folder in ownFolders)
                {
                    if (IsSymbolicLink(folder))
                    {
                        refusal = LinkOnTheWay(folder);
                        return false;
                    }

                    modes.CreateDirectory(folder);

                    refusal = UnixFolder.TrySetOwnerOnly(modes, folder);
                    if (refusal is not null)
                    {
                        return false;
                    }
                }

                // Creating a folder and setting its mode both follow a link, so one swapped in after the check above would
                // have been followed: whoever can write to the base folder, as its group may, could have done so.
                if (ownFolders.FirstOrDefault(IsSymbolicLink) is { } swapped)
                {
                    refusal = LinkOnTheWay(swapped);
                    return false;
                }

                return true;
            }
            catch (IOException e)
            {
                refusal = CantCreate(e);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                refusal = CantCreate(e);
                return false;
            }
            catch (NotSupportedException e)
            {
                refusal = CantCreate(e);
                return false;
            }
            catch (ArgumentException e)
            {
                refusal = CantCreate(e);
                return false;
            }
        }

        private static QueueRefusal CantCreate(Exception e)
            => new QueueRefusal(QueueRefusal.IOError, $"its queue folder can't be created ({e.GetType().Name}: {e.Message})");

        private static QueueRefusal LinkOnTheWay(string folder)
            => new QueueRefusal(QueueRefusal.SymbolicLink, $"{folder}, on the way to its queue folder, is a symbolic link");

        /// <summary>
        /// The folders this library names in <paramref name="path"/>, outermost first.
        /// </summary>
        private static string[] GetOwnFolders(string path)
        {
            var folders = new string[OwnFolderCount];
            string? folder = path;
            for (var i = OwnFolderCount - 1; i >= 0; i--)
            {
                folders[i] = folder ?? throw new ArgumentException($"{path} is too short to be a queue folder", nameof(path));
                folder = Path.GetDirectoryName(folder);
            }

            return folders;
        }

        private static bool IsSymbolicLink(string path)
        {
            try
            {
                // The attributes describe the link itself, not what it points to, and a link pointing nowhere has them too.
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }
    }
}
