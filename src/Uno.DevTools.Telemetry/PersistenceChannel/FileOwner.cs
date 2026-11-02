// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Uno.DevTools.Telemetry.PersistenceChannel
{
	/// <summary>
	///     Tells whether a file belongs to the user this process runs as.
	/// </summary>
	internal static class FileOwner
	{
		// statx(2) arguments, and the layout of the struct statx it fills, which is the same on every architecture
		private const int AtFdCwd = -100;
		private const int AtSymlinkNoFollow = 0x100;
		private const uint StatxUid = 0x8;
		private const int StatxSize = 0x100;
		private const int StatxMaskOffset = 0;
		private const int StatxUidOffset = 20;

		private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

		private static volatile bool _cannotTell;

		/// <summary>
		///     Whether <paramref name="path" /> belongs to the current user. Only Linux is asked; elsewhere the queue is in a temp
		///     folder of the user's own, and every file counts as theirs. So does a file whose owner can't be read, e.g. one
		///     deleted meanwhile, or when the C library has no statx (glibc before 2.28, musl before 1.2.5).
		/// </summary>
		internal static bool IsCurrentUser(string path)
		{
			if (!IsLinux || _cannotTell)
			{
				return true;
			}

			try
			{
				// The path as a NUL-terminated UTF-8 string: netstandard2.0 has no marshaling for one
				var pathname = Encoding.UTF8.GetBytes(path + "\0");
				var buffer = new byte[StatxSize];
				if (statx(AtFdCwd, pathname, AtSymlinkNoFollow, StatxUid, buffer) != 0
					|| (BitConverter.ToUInt32(buffer, StatxMaskOffset) & StatxUid) == 0)
				{
					return true;
				}

				return BitConverter.ToUInt32(buffer, StatxUidOffset) == geteuid();
			}
			catch (Exception e)
			{
				// Deliberately generic: a check that can't run can't tell, and must not keep every file from being sent. Its
				// expected failures, no C library or no statx in it, would fail every call the same way, so it isn't retried.
				_cannotTell = true;
				PersistenceChannelDebugLog.WriteException(e, "Can't tell who owns queued files");
				return true;
			}
		}

		[DllImport("libc")]
		private static extern int statx(int dirfd, byte[] pathname, int flags, uint mask, [Out] byte[] statxbuf);

		[DllImport("libc")]
		private static extern uint geteuid();
	}
}
