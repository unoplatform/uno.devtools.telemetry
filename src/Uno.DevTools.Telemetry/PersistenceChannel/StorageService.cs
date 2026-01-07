// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2019/04/12 (Jerome Laban <jerome.laban@nventive.com>):
//	- Extracted from dotnet.exe
// 2024/12/05 (Jerome Laban <jerome@platform.uno>):
//	- Updated for nullability
// 2026/01/07 (carldebilly/copilot):
//	- Added resiliency for concurrent access (multiple processes)
//	- Added retry logic for Delete operations
//	- Added FileShare modes for concurrent file access
//	- Added File.Exists checks to prevent race conditions
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;

namespace Uno.DevTools.Telemetry.PersistenceChannel
{
	internal sealed class StorageService : BaseStorageService
	{
		private const string DefaultStorageFolderName = "TelemetryStorageService";
		
		/// <summary>
		///     TTL for .trn files: 30 days. After this period, files are deleted even if not sent.
		/// </summary>
		internal static readonly TimeSpan TransmissionFileTtl = TimeSpan.FromDays(30);
		
		/// <summary>
		///     TTL for .corrupt files: 7 days. Corrupted files are kept for diagnostics then removed.
		/// </summary>
		internal static readonly TimeSpan CorruptedFileTtl = TimeSpan.FromDays(7);
		
		/// <summary>
		///     Maximum retry duration: 2 hours. After this period, failed transmissions are dropped.
		/// </summary>
		internal static readonly TimeSpan MaxRetryDuration = TimeSpan.FromHours(2);
		
		private readonly FixedSizeQueue<string> _deletedFilesQueue = new FixedSizeQueue<string>(10);

		private readonly object _peekLockObj = new object();
		private readonly object _storageFolderLock = new object();
		private string? _storageDirectoryPath;
		private string? _storageDirectoryPathUsed;
		private long _storageCountFiles;
		private bool _storageFolderInitialized;
		private long _storageSize;
		private uint _transmissionsDropped;

		/// <summary>
		///     Gets the storage's folder name.
		/// </summary>
		internal override string? StorageDirectoryPath => _storageDirectoryPath;

		/// <summary>
		///     Gets the storage folder. If storage folder couldn't be created, null will be returned.
		/// </summary>
		private string? StorageFolder
		{
			get
			{
				if (!_storageFolderInitialized)
				{
					lock (_storageFolderLock)
					{
						if (!_storageFolderInitialized && _storageDirectoryPath is not null)
						{
							try
							{
								_storageDirectoryPathUsed = _storageDirectoryPath;

								if (!Directory.Exists(_storageDirectoryPathUsed))
								{
									Directory.CreateDirectory(_storageDirectoryPathUsed);
								}
							}
							catch (Exception e)
							{
								_storageDirectoryPathUsed = null;
								PersistenceChannelDebugLog.WriteException(e, "Failed to create storage folder");
							}

							_storageFolderInitialized = true;
						}
					}
				}

				return _storageDirectoryPathUsed;
			}
		}

		internal override void Init(string? storageDirectoryPath)
		{
			PeekedTransmissions = new SnapshottingDictionary<string, string>();

			VerifyOrSetDefaultStorageDirectoryPath(storageDirectoryPath);

			CapacityInBytes = 10 * 1024 * 1024; // 10 MB
			MaxFiles = 100;

			Task.Run(DeleteObsoleteFiles)
				.ContinueWith(
					task =>
					{
						PersistenceChannelDebugLog.WriteException(
							task.Exception,
							"Storage: Unhandled exception in DeleteObsoleteFiles");
					},
					TaskContinuationOptions.OnlyOnFaulted);
		}

		private void VerifyOrSetDefaultStorageDirectoryPath(string? desireStorageDirectoryPath)
		{
			if (string.IsNullOrEmpty(desireStorageDirectoryPath))
			{
				_storageDirectoryPath = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					DefaultStorageFolderName);
			}
			else
			{
				if (!Path.IsPathRooted(desireStorageDirectoryPath))
				{
					throw new ArgumentException($"{nameof(desireStorageDirectoryPath)} need to be rooted (full path)");
				}

				_storageDirectoryPath = desireStorageDirectoryPath;
			}
		}

		/// <summary>
		///     Reads an item from the storage. Order is Last-In-First-Out.
		///     When the Transmission is no longer needed (it was either sent or failed with a non-retryable error) it should be
		///     disposed.
		/// </summary>
		internal override StorageTransmission? Peek()
		{
			var files = GetFiles("*.trn", 50);

			if (PeekedTransmissions is not null)
			{
				lock (_peekLockObj)
				{
					foreach (var file in files)
					{
						try
						{
							// if a file was peeked before, skip it (wait until it is disposed).  
							if (PeekedTransmissions.ContainsKey(file) == false &&
								_deletedFilesQueue.Contains(file) == false)
							{
								// Load the transmission from disk.
								var storageTransmissionItem = LoadTransmissionFromFileAsync(file)
									.ConfigureAwait(false).GetAwaiter().GetResult();

								// when item is disposed it should be removed from the peeked list.
								storageTransmissionItem.Disposing = item => OnPeekedItemDisposed(file);

								// add the transmission to the list.
								PeekedTransmissions.Add(file, storageTransmissionItem.FullFilePath);
								return storageTransmissionItem;
							}
						}
						catch (Exception e)
						{
							PersistenceChannelDebugLog.WriteException(
								e,
								"Failed to load an item from the storage. file: {0}",
								file);
							
							// Quarantine corrupted files by renaming to .corrupt
							RenameToCorrupted(file);
						}
					}
				}
			}

			return null;
		}

		internal override void Delete(StorageTransmission item)
		{
			try
			{
				if (StorageFolder == null)
				{
					return;
				}

				// Initial storage size calculation. 
				CalculateSize();

				var storageFolder = StorageFolder;
				var filePath = Path.Combine(storageFolder, item.FileName);
				
				// Get file size before any existence checks so storage accounting stays accurate.
				// If the file doesn't exist, GetSize returns 0, which is the correct behavior for storage tracking.
				var fileSize = GetSize(item.FileName);
				
				// Check if file exists before attempting operations
				// This prevents UnauthorizedAccessException when file doesn't exist
				// (race condition: Delete called before file created, or file already deleted)
				if (!File.Exists(filePath))
				{
					PersistenceChannelDebugLog.WriteLine(
						string.Format(CultureInfo.InvariantCulture,
							"File does not exist, skipping delete: {0}",
							item.FileName));
					return;
				}
				
				// Retry Delete operation for transient failures (concurrent access from other processes)
				// This handles cases where multiple instances access the same storage folder
				DeleteFileWithRetry(filePath);

				_deletedFilesQueue.Enqueue(item.FileName);

				// calculate size                
				Interlocked.Add(ref _storageSize, -fileSize);
				Interlocked.Decrement(ref _storageCountFiles);
			}
			catch (Exception e)
			{
				// Catch all exceptions including IOException, UnauthorizedAccessException, etc.
				// Telemetry operations must never crash the host app.
				PersistenceChannelDebugLog.WriteException(e, "Failed to delete a file. file: {0}", item == null ? "null" : item.FullFilePath);
			}
		}
		
		/// <summary>
		///     Attempts to delete a file with retry for transient failures.
		///     Handles concurrent access from multiple processes.
		///     Uses immediate retry (no delays) to avoid Thread.Sleep which isn't supported in WebAssembly.
		/// </summary>
		private void DeleteFileWithRetry(in string filePath)
		{
			const int maxRetries = 3;
			
			for (int attempt = 0; attempt < maxRetries; attempt++)
			{
				try
				{
					// Check if file still exists (may have been deleted by another process)
					if (!File.Exists(filePath))
					{
						return; // Already deleted, success
					}
					
					File.Delete(filePath);
					return; // Success
				}
				catch (IOException) when (attempt < maxRetries - 1)
				{
					// IOException can occur with concurrent access (file in use by another process)
					// Retry immediately - delays aren't needed for typical file lock scenarios
					// and Thread.Sleep is not supported in WebAssembly
				}
				catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
				{
					// Can occur with concurrent access or permission issues
					// Retry immediately - delays aren't needed for typical file lock scenarios
					// and Thread.Sleep is not supported in WebAssembly
				}
				// On last attempt (attempt == maxRetries - 1), exceptions will propagate naturally
			}
		}

		internal override async Task EnqueueAsync(Transmission transmission)
		{
			try
			{
				if (transmission == null || StorageFolder == null)
				{
					return;
				}

				// Initial storage size calculation. 
				CalculateSize();

				if ((ulong)_storageSize >= CapacityInBytes || _storageCountFiles >= MaxFiles)
				{
					// if max storage capacity has reached, drop the transmission (but log every 100 lost transmissions). 
					if (_transmissionsDropped++ % 100 == 0)
					{
						PersistenceChannelDebugLog.WriteLine("Total transmissions dropped: " + _transmissionsDropped);
					}

					return;
				}

				// Writes content to a temporary file and only then rename to avoid the Peek from reading the file before it is being written.
				// Creates the temp file name
				var tempFileName = Guid.NewGuid().ToString("N");

				// Now that the file got created we can increase the files count
				Interlocked.Increment(ref _storageCountFiles);

				// Saves transmission to the temp file
				await SaveTransmissionToFileAsync(transmission, tempFileName).ConfigureAwait(false);

				// Now that the file is written increase storage size. 
				var temporaryFileSize = GetSize(tempFileName);
				Interlocked.Add(ref _storageSize, temporaryFileSize);

				// Creates a new file name
				var now = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
				var newFileName = string.Format(CultureInfo.InvariantCulture, "{0}_{1}.trn", now, tempFileName);

				// Renames the file
				File.Move(Path.Combine(StorageFolder, tempFileName), Path.Combine(StorageFolder, newFileName));
			}
			catch (Exception e)
			{
				PersistenceChannelDebugLog.WriteException(e, "EnqueueAsync");
			}
		}

		private async Task SaveTransmissionToFileAsync(Transmission transmission, string file)
        {
            if (transmission == null || StorageFolder == null)
            {
                return;
            }

            try
            {
				// FileShare.Read allows other processes to read while we're writing
				// This supports multiple processes accessing the same storage folder
				using (Stream stream = File.Open(Path.Combine(StorageFolder, file), FileMode.Create, FileAccess.Write, FileShare.Read))
				{
					await StorageTransmission.SaveAsync(transmission, stream).ConfigureAwait(false);
				}
			}
			catch (UnauthorizedAccessException)
			{
				var message =
					string.Format(
						CultureInfo.InvariantCulture,
						"Failed to save transmission to file. UnauthorizedAccessException. File path: {0}, FileName: {1}",
						StorageFolder, file);
				PersistenceChannelDebugLog.WriteLine(message);
				throw;
			}
		}

		private async Task<StorageTransmission> LoadTransmissionFromFileAsync(string file)
        {

            try
            {
				if (StorageFolder is null)
				{
					throw new InvalidOperationException("The storage folder is not defined");
				}

				// FileShare.ReadWrite allows other processes to read/write while we're reading
				// FileShare.Delete allows other processes to delete while we're reading
				// This supports multiple processes accessing the same storage folder
				using (Stream stream = File.Open(Path.Combine(StorageFolder, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					var storageTransmissionItem =
						await StorageTransmission.CreateFromStreamAsync(stream, file).ConfigureAwait(false);
					return storageTransmissionItem;
				}
			}
			catch (Exception e)
			{
				var message =
					string.Format(
						CultureInfo.InvariantCulture,
						"Failed to load transmission from file. File path: {0}, FileName: {1}, Exception: {2}",
						"storageFolderName", file, e);
				PersistenceChannelDebugLog.WriteLine(message);
				throw;
			}
		}

		/// <summary>
		///     Get files from <see cref="storageFolder" />.
		/// </summary>
		/// <param name="filterByExtension">Defines a file extension. This method will return only files with this extension.</param>
		/// <param name="top">
		///     Define how many files to return. This can be useful when the directory has a lot of files, in that case
		///     GetFilesAsync will have a performance hit.
		/// </param>
		/// <returns>Returns only file names (not full paths).</returns>
		private IEnumerable<string> GetFiles(in string filterByExtension, int top)
		{
			try
			{
				if (StorageFolder != null)
				{
					return EnumerateFiles(filterByExtension).Take(top);
				}
			}
			catch (Exception e)
			{
				PersistenceChannelDebugLog.WriteException(e, "Peek failed while get files from storage.");
			}

			return Enumerable.Empty<string>();
		}

		/// <summary>
		///     Enumerates files from <see cref="storageFolder" /> without limiting the count.
		/// </summary>
		private IEnumerable<string> EnumerateFiles(in string filterByExtension)
		{
			try
			{
				if (StorageFolder != null)
				{
					return Directory.EnumerateFiles(StorageFolder, filterByExtension)
						.Select(file => Path.GetFileName(file) ?? string.Empty)
						.Where(file => file.Length > 0);
				}
			}
			catch (Exception e)
			{
				PersistenceChannelDebugLog.WriteException(e, "Peek failed while enumerate files from storage.");
			}

			return Enumerable.Empty<string>();
		}

		/// <summary>
		///     Gets a file's size.
		/// </summary>
		private long GetSize(in string file)
		{
			try
			{
				if (StorageFolder is not null)
				{
					// FileShare.ReadWrite | FileShare.Delete allows concurrent access from other processes
					using (var stream = File.Open(Path.Combine(StorageFolder, file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					{
						return stream.Length;
					}
				}
			}
			catch (Exception e)
			{
				// Guard against IO exceptions during size calculation
				PersistenceChannelDebugLog.WriteException(e, "Failed to get file size. file: {0}", file);
			}
			
			return 0;
		}
		
		/// <summary>
		///     Gets a file's size from a full path.
		/// </summary>
		private long GetSizeFromPath(in string filePath)
		{
			try
			{
				// FileShare.ReadWrite | FileShare.Delete allows concurrent access from other processes
				using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					return stream.Length;
				}
			}
			catch (Exception e)
			{
				// Guard against IO exceptions during size calculation
				PersistenceChannelDebugLog.WriteException(e, "Failed to get file size. file: {0}", Path.GetFileName(filePath) ?? filePath);
			}
			
			return 0;
		}

		/// <summary>
		///     Check the storage limits and return true if they reached.
		///     Storage limits are defined by the number of files and the total size on disk.
		/// </summary>
		private void CalculateSize()
		{
			try
			{
				if (StorageFolder is not null)
				{
					var storageFiles = Directory.GetFiles(StorageFolder, "*.*");

					_storageCountFiles = storageFiles.Length;

					long storageSizeInBytes = 0;
					foreach (var file in storageFiles)
					{
						storageSizeInBytes += GetSizeFromPath(file);
					}

					_storageSize = storageSizeInBytes;
				}
			}
			catch (Exception e)
			{
				// Guard against exceptions during size calculation
				PersistenceChannelDebugLog.WriteException(e, "Failed to calculate storage size");
			}
		}

		/// <summary>
		///     Enqueue is saving a transmission to a <c>tmp</c> file and after a successful write operation it renames it to a
		///     <c>trn</c> file.
		///     A file without a <c>trn</c> extension is ignored by Storage.Peek(), so if a process is taken down before rename
		///     happens
		///     it will stay on the disk forever.
		///     This method deletes:
		///     - tmp files older than 5 minutes
		///     - trn files older than TransmissionFileTtl (30 days)
		///     - corrupt files older than CorruptedFileTtl (7 days)
		/// </summary>
		private void DeleteObsoleteFiles()
		{
			try
			{
				if (StorageFolder is not null)
				{
					DeleteObsoleteFilesByExtension("*.tmp", TimeSpan.FromMinutes(5));
					DeleteObsoleteFilesByExtension("*.trn", TransmissionFileTtl);
					DeleteObsoleteFilesByExtension("*.corrupt", CorruptedFileTtl);
				}
			}
			catch (Exception e)
			{
				PersistenceChannelDebugLog.WriteException(e, "Failed to delete obsolete files.");
			}
		}

		private void DeleteObsoleteFilesByExtension(in string filterByExtension, in TimeSpan ttl)
		{
			if (StorageFolder is null)
			{
				return;
			}

			var storageFolder = StorageFolder;
			var now = DateTime.UtcNow;
			foreach (var file in EnumerateFiles(filterByExtension))
			{
				try
				{
					var filePath = Path.Combine(storageFolder, file);
					if (!File.Exists(filePath))
					{
						continue; // File was already deleted or doesn't exist
					}
					
					var creationTime = File.GetCreationTimeUtc(filePath);
					if (now - creationTime >= ttl)
					{
						DeleteFileWithRetry(filePath);
					}
				}
				catch (Exception e)
				{
					PersistenceChannelDebugLog.WriteException(e, "Failed to delete obsolete file: {0}", file);
				}
			}
		}
		
		/// <summary>
		///     Renames a corrupted .trn file to .corrupt for quarantine and diagnostics.
		/// </summary>
		private void RenameToCorrupted(string fileName)
		{
			try
			{
				if (StorageFolder is null || string.IsNullOrEmpty(fileName))
				{
					return;
				}
				
				var sourcePath = Path.Combine(StorageFolder, fileName);
				var corruptedPath = Path.ChangeExtension(sourcePath, "corrupt");
				
				if (File.Exists(sourcePath))
				{
					// Delete existing .corrupt file if present (from previous failure)
					// This handles the case where RenameToCorrupted is called multiple times
					if (File.Exists(corruptedPath))
					{
						DeleteFileWithRetry(corruptedPath);
					}
					
					File.Move(sourcePath, corruptedPath);
					PersistenceChannelDebugLog.WriteLine(
						string.Format(CultureInfo.InvariantCulture,
							"Renamed corrupted file: {0} -> {1}", 
							fileName, 
							Path.GetFileName(corruptedPath)));
				}
			}
			catch (Exception e)
			{
				// Telemetry must never crash the host app
				PersistenceChannelDebugLog.WriteException(e, "Failed to rename corrupted file: {0}", fileName);
			}
		}
	}
}
