// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Uno.DevTools.Telemetry
{
    /// <summary>
    /// Why the desktop channel has no queue folder of the user's own, so that it sends from memory instead.
    /// </summary>
    internal sealed class QueueRefusal
    {
        /// <summary>The user has no usable home folder: it is unset, relative or <c>/</c>.</summary>
        internal const string NoHome = "no-home";

        /// <summary>The runtime can't set a folder's Unix mode: the netstandard2.0 build before .NET 7.</summary>
        internal const string NoUnixModes = "no-unix-modes";

        /// <summary>The process runs as root, and the queue folder would be outside root's home.</summary>
        internal const string RootOutsideHome = "root-outside-home";

        /// <summary>Users outside its owner and group can write to the folder that holds the queue folder.</summary>
        internal const string SharedParent = "shared-parent";

        /// <summary>The system refused to change a folder's mode, as it does for a folder that is another user's.</summary>
        internal const string NotOwned = "not-owned";

        /// <summary>The file system doesn't keep Unix permissions: a folder set to 0700 reads back otherwise.</summary>
        internal const string NoUnixPermissions = "no-unix-permissions";

        /// <summary>A folder the library names is a symbolic link.</summary>
        internal const string SymbolicLink = "symbolic-link";

        /// <summary>A folder couldn't be created, or its mode couldn't be read or set, for another reason.</summary>
        internal const string IOError = "io-error";

        internal QueueRefusal(string code, string reason)
        {
            Code = code;
            Reason = reason;
        }

        /// <summary>
        /// One of the constants above. It is sent with every item, so it never holds a path or anything else from the
        /// machine.
        /// </summary>
        internal string Code { get; }

        /// <summary>
        /// Why, for people, to complete "Telemetry can't queue on disk for this instance: ". It names folders, so it is
        /// only written locally, never sent.
        /// </summary>
        internal string Reason { get; }

        public override string ToString() => Reason;
    }
}
