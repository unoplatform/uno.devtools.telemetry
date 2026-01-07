using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Uno.DevTools.Telemetry.Tests
{
	using AiTelemetry = Microsoft.ApplicationInsights.Channel.ITelemetry;

	/// <summary>
	///     Integration tests for telemetry resilience.
	///     Note: These tests poll for background operations (DeleteObsoleteFiles, SendLoop) instead of using Thread.Sleep.
	///     This keeps the tests more deterministic and reduces flakiness.
	///     Future improvement: Use TimeProvider for deterministic time control.
	/// </summary>
	[TestClass]
	public class PersistenceChannelResiliencyTests
	{
		private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

		private readonly List<string> _directoriesToCleanup = new List<string>();

		private string GetTempStorageDirectory()
		{
			var dir = Path.Combine(Path.GetTempPath(), $"telemetry_resilience_test_{Guid.NewGuid():N}");
			_directoriesToCleanup.Add(dir);
			Directory.CreateDirectory(dir);
			return dir;
		}

		[TestCleanup]
		public void Cleanup()
		{
			foreach (var dir in _directoriesToCleanup.Where(Directory.Exists))
			{
				try
				{
					Directory.Delete(dir, true);
				}
				catch
				{
					// Best effort cleanup
				}
			}

			_directoriesToCleanup.Clear();
		}

		[TestMethod]
		public void Given_CorruptedTrnFile_When_Peek_Then_FileIsRenamedToCorrupt()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var corruptedFilePath = Path.Combine(storageDir, "20260107120000_corrupt_test.trn");
			
			// Create a corrupted .trn file (invalid format)
			File.WriteAllText(corruptedFilePath, "This is not a valid transmission file");

			var storage = new StorageService();
			storage.Init(storageDir);

			// Act
			var transmission = storage.Peek();

			// Assert
			transmission.Should().BeNull(); // Corrupted file should not be returned
			
			// The corrupted file should have been renamed to .corrupt
			var corruptedFiles = Directory.GetFiles(storageDir, "*.corrupt");
			corruptedFiles.Should().HaveCount(1);
			corruptedFiles[0].Should().EndWith(".corrupt");
		}

		[TestMethod]
		public async Task Given_TrnFileOlderThan30Days_When_DeleteObsoleteFiles_Then_FileIsDeleted()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var oldFilePath = Path.Combine(storageDir, "20250101120000_old_test.trn");
			
			// Create an old .trn file
			File.WriteAllText(oldFilePath, "old transmission");
			
			// Set creation time to 31 days ago
			File.SetCreationTimeUtc(oldFilePath, DateTime.UtcNow.AddDays(-31));

			var storage = new StorageService();
			storage.Init(storageDir);

			// Act - Wait for DeleteObsoleteFiles to run (it's called in Init as a background task)
			var deleted = await WaitUntilAsync(
				() => !File.Exists(oldFilePath),
				TimeSpan.FromSeconds(10),
				DefaultPollInterval).ConfigureAwait(false);

			// Assert
			deleted.Should().BeTrue("old .trn files should be deleted");
		}

		[TestMethod]
		public async Task Given_CorruptFileOlderThan7Days_When_DeleteObsoleteFiles_Then_FileIsDeleted()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var oldCorruptFilePath = Path.Combine(storageDir, "20250101120000_old_corrupt.corrupt");
			
			// Create an old .corrupt file
			File.WriteAllText(oldCorruptFilePath, "old corrupted transmission");
			
			// Set creation time to 8 days ago
			File.SetCreationTimeUtc(oldCorruptFilePath, DateTime.UtcNow.AddDays(-8));

			var storage = new StorageService();
			storage.Init(storageDir);

			// Act - Wait for DeleteObsoleteFiles to run
			var deleted = await WaitUntilAsync(
				() => !File.Exists(oldCorruptFilePath),
				TimeSpan.FromSeconds(10),
				DefaultPollInterval).ConfigureAwait(false);

			// Assert
			deleted.Should().BeTrue("old .corrupt files should be deleted");
		}

		[TestMethod]
		public void Given_UnauthorizedAccessException_When_Delete_Then_ExceptionIsCaught()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create a transmission file
			var transmission = CreateTransmission(new byte[] { 1, 2, 3 }, "");

			// Enqueue it
			storage.EnqueueAsync(transmission).ConfigureAwait(false).GetAwaiter().GetResult();

			// Peek it to get the StorageTransmission
			var storageTransmission = storage.Peek();
			storageTransmission.Should().NotBeNull();

			// On Windows, make the file read-only to simulate permission issues
			// On Unix, we'll test that exception handling works even if file doesn't exist
			var filePath = Path.Combine(storageDir, storageTransmission!.FileName);
			
			if (OperatingSystem.IsWindows())
			{
				var fileInfo = new FileInfo(filePath);
				fileInfo.IsReadOnly = true;
			}
			else
			{
				// On Unix, delete the file to cause an exception during Delete
				File.Delete(filePath);
			}

			// Act - This should not throw even if file operations fail
			Action act = () => storage.Delete(storageTransmission);

			// Assert
			act.Should().NotThrow();

			// Cleanup
			if (OperatingSystem.IsWindows() && File.Exists(filePath))
			{
				var fileInfo = new FileInfo(filePath);
				fileInfo.IsReadOnly = false;
			}
		}

		[TestMethod]
		public void Given_SerializationException_When_Flush_Then_ExceptionIsCaught()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var channel = new PersistenceChannel.PersistenceChannel(storageDir, 1);

			// Create a telemetry item that will throw on serialization
			var telemetry = new ThrowingTelemetry("denim-42-ga-blue");

			// Act - This should not throw even if serialization fails internally
			Action act = () => channel.Send(telemetry);

			// Assert
			act.Should().NotThrow();
			
			// Cleanup
			channel.Dispose();
		}

		[TestMethod]
		public void Given_IoExceptionDuringGetSize_When_CalculateSize_Then_ExceptionIsCaught()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create a file and then delete it while it's being accessed
			var testFilePath = Path.Combine(storageDir, "test.trn");
			File.WriteAllText(testFilePath, "test");

			// Act - Delete the file to cause IO exceptions during size calculation
			File.Delete(testFilePath);

			// This should not throw even though GetSize will fail
			Action act = () =>
			{
				var transmission = CreateTransmission(new byte[] { 1, 2, 3 }, "");
				storage.EnqueueAsync(transmission).ConfigureAwait(false).GetAwaiter().GetResult();
			};

			// Assert
			act.Should().NotThrow();
		}

		[TestMethod]
		public async Task Given_TransmissionOlderThan2Hours_When_Send_Then_TransmissionIsDropped()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create an old transmission file (older than 2 hours)
			var oldFilePath = Path.Combine(storageDir, "20260107100000_old_transmission.trn");
			
			// Create a valid transmission file
			var transmission = CreateTransmission(new byte[] { 1, 2, 3 }, "gzip");
			
			using (var stream = File.OpenWrite(oldFilePath))
			{
				StorageTransmission.SaveAsync(transmission, stream).ConfigureAwait(false).GetAwaiter().GetResult();
			}
			
			// Set creation time to 3 hours ago (exceeds MaxRetryDuration)
			File.SetCreationTimeUtc(oldFilePath, DateTime.UtcNow.AddHours(-3));

			// Create sender
			var transmitter = new PersistenceTransmitter(storage, 1);

			// Act - Wait for the sender to process the transmission
			var deleted = await WaitUntilAsync(
				() => !File.Exists(oldFilePath),
				TimeSpan.FromSeconds(10),
				DefaultPollInterval).ConfigureAwait(false);

			// Assert - The old file should have been deleted (dropped)
			deleted.Should().BeTrue("the old transmission file should be deleted by the persistence transmitter");

			// Cleanup
			transmitter.Dispose();
		}

		[TestMethod]
		public async Task Given_ExceptionInSendLoop_When_SendLoop_Then_LoopContinues()
		{
			// Arrange
			var storage = new ThrowingPeekStorageService();
			var transmitter = new PersistenceTransmitter(storage, 1, createSenders: false);
			using var sender = new Sender(storage, transmitter);

			// Act - Wait for the sender loop to recover and call Peek again
			var loopContinued = await WaitUntilAsync(
				() => storage.PeekCalls >= 2,
				TimeSpan.FromSeconds(10),
				DefaultPollInterval).ConfigureAwait(false);

			// Assert
			loopContinued.Should().BeTrue("the send loop should continue after a peek exception");
		}

		[TestMethod]
		public async Task Given_MultipleFilesOfDifferentTypes_When_DeleteObsoleteFiles_Then_OnlyExpiredFilesAreDeleted()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			
			// Create files with different ages
			var recentTrn = Path.Combine(storageDir, "recent.trn");
			var oldTrn = Path.Combine(storageDir, "old.trn");
			var recentCorrupt = Path.Combine(storageDir, "recent.corrupt");
			var oldCorrupt = Path.Combine(storageDir, "old.corrupt");
			var oldTmp = Path.Combine(storageDir, "old.tmp");
			var recentTmp = Path.Combine(storageDir, "recent.tmp");
			
			File.WriteAllText(recentTrn, "recent");
			File.WriteAllText(oldTrn, "old");
			File.WriteAllText(recentCorrupt, "recent");
			File.WriteAllText(oldCorrupt, "old");
			File.WriteAllText(oldTmp, "old");
			File.WriteAllText(recentTmp, "recent");
			
			// Set creation times
			File.SetCreationTimeUtc(recentTrn, DateTime.UtcNow.AddDays(-1));
			File.SetCreationTimeUtc(oldTrn, DateTime.UtcNow.AddDays(-31));
			File.SetCreationTimeUtc(recentCorrupt, DateTime.UtcNow.AddDays(-1));
			File.SetCreationTimeUtc(oldCorrupt, DateTime.UtcNow.AddDays(-8));
			File.SetCreationTimeUtc(oldTmp, DateTime.UtcNow.AddMinutes(-10));
			File.SetCreationTimeUtc(recentTmp, DateTime.UtcNow.AddMinutes(-1));

			var storage = new StorageService();
			storage.Init(storageDir);

			// Act - Wait for DeleteObsoleteFiles to run
			var deleted = await WaitUntilAsync(
				() => !File.Exists(oldTrn) && !File.Exists(oldCorrupt) && !File.Exists(oldTmp),
				TimeSpan.FromSeconds(10),
				DefaultPollInterval).ConfigureAwait(false);

			// Assert
			deleted.Should().BeTrue("expired files should be deleted");
			File.Exists(recentTrn).Should().BeTrue("recent .trn files should be kept");
			File.Exists(oldTrn).Should().BeFalse("old .trn files (>30 days) should be deleted");
			File.Exists(recentCorrupt).Should().BeTrue("recent .corrupt files should be kept");
			File.Exists(oldCorrupt).Should().BeFalse("old .corrupt files (>7 days) should be deleted");
			File.Exists(oldTmp).Should().BeFalse("old .tmp files (>5 minutes) should be deleted");
			File.Exists(recentTmp).Should().BeTrue("recent .tmp files should be kept");
		}

		[TestMethod]
		public void Given_NonExistentFile_When_Delete_Then_NoExceptionThrown()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create a transmission file
			var transmission = CreateTransmission(new byte[] { 1, 2, 3 }, "");

			// Enqueue it
			storage.EnqueueAsync(transmission).ConfigureAwait(false).GetAwaiter().GetResult();

			// Peek it to get the StorageTransmission
			var storageTransmission = storage.Peek();
			storageTransmission.Should().NotBeNull();

			// Delete the file manually to simulate race condition
			var filePath = Path.Combine(storageDir, storageTransmission!.FileName);
			File.Delete(filePath);

			// Act - This should not throw when file doesn't exist
			Action act = () => storage.Delete(storageTransmission);

			// Assert
			act.Should().NotThrow("Delete should handle non-existent files gracefully");
		}

		private static async Task<bool> WaitUntilAsync(
			Func<bool> condition,
			TimeSpan timeout,
			TimeSpan pollInterval)
		{
			var stopAt = DateTime.UtcNow + timeout;
			while (DateTime.UtcNow < stopAt)
			{
				if (condition())
				{
					return true;
				}

				await Task.Delay(pollInterval).ConfigureAwait(false);
			}

			return condition();
		}

		private static Transmission CreateTransmission(in byte[] content, in string contentEncoding)
		{
			return new Transmission(
				new Uri("https://dc.services.visualstudio.com/v2/track"),
				content,
				"application/json",
				contentEncoding);
		}

		private sealed class ThrowingTelemetry : AiTelemetry
		{
			private readonly string _telemetryName;

			public ThrowingTelemetry(in string telemetryName)
			{
				_telemetryName = telemetryName;
				Context = new TelemetryContext();
				Timestamp = DateTimeOffset.UtcNow;
			}

			public DateTimeOffset Timestamp { get; set; }

			public TelemetryContext Context { get; }

			public IExtension? Extension { get; set; }

			public string? Sequence { get; set; }

			public void Sanitize()
			{
			}

			public AiTelemetry DeepClone()
			{
				return new ThrowingTelemetry(_telemetryName)
				{
					Extension = Extension,
					Sequence = Sequence,
					Timestamp = Timestamp
				};
			}

			public void SerializeData(ISerializationWriter serializationWriter)
			{
				throw new InvalidOperationException($"Serialization failed for {_telemetryName}.");
			}
		}

		private sealed class ThrowingPeekStorageService : BaseStorageService
		{
			private int _peekCalls;

			public int PeekCalls => _peekCalls;

			internal override string? StorageDirectoryPath => null;

			internal override void Init(string? desireStorageDirectoryPath)
			{
			}

			internal override StorageTransmission? Peek()
			{
				var calls = Interlocked.Increment(ref _peekCalls);
				if (calls == 1)
				{
					throw new IOException("Peek failed for testing resilience.");
				}

				return null;
			}

			internal override void Delete(StorageTransmission transmission)
			{
			}

			internal override Task EnqueueAsync(Transmission transmission)
			{
				return Task.CompletedTask;
			}
		}
	}
}
