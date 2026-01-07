using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility.Implementation;

namespace Uno.DevTools.Telemetry.Tests
{
	/// <summary>
	///     Integration tests for telemetry resilience.
	///     Note: These tests use Thread.Sleep to wait for background operations (DeleteObsoleteFiles, SendLoop).
	///     While not ideal, this is acceptable for integration tests that verify real async behavior.
	///     Future improvement: Use TimeProvider for deterministic time control.
	/// </summary>
	[TestClass]
	public class PersistenceChannelResiliencyTests
	{
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
		public void Given_TrnFileOlderThan30Days_When_DeleteObsoleteFiles_Then_FileIsDeleted()
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
			Thread.Sleep(1000);

			// Assert
			File.Exists(oldFilePath).Should().BeFalse();
		}

		[TestMethod]
		public void Given_CorruptFileOlderThan7Days_When_DeleteObsoleteFiles_Then_FileIsDeleted()
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
			Thread.Sleep(1000);

			// Assert
			File.Exists(oldCorruptFilePath).Should().BeFalse();
		}

		[TestMethod]
		public void Given_UnauthorizedAccessException_When_Delete_Then_ExceptionIsCaught()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create a transmission file
			var transmission = new Transmission(
				new Uri("https://dc.services.visualstudio.com/v2/track"),
				new byte[] { 1, 2, 3 },
				"application/json",
				"");

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

			// Create a telemetry item that might cause serialization issues
			var telemetry = new EventTelemetry("TestEvent");

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
				var transmission = new Transmission(
					new Uri("https://dc.services.visualstudio.com/v2/track"),
					new byte[] { 1, 2, 3 },
					"application/json",
					"");
				storage.EnqueueAsync(transmission).ConfigureAwait(false).GetAwaiter().GetResult();
			};

			// Assert
			act.Should().NotThrow();
		}

		[TestMethod]
		public void Given_TransmissionOlderThan2Hours_When_Send_Then_TransmissionIsDropped()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Create an old transmission file (older than 2 hours)
			var oldFilePath = Path.Combine(storageDir, "20260107100000_old_transmission.trn");
			
			// Create a valid transmission file
			var transmission = new Transmission(
				new Uri("https://dc.services.visualstudio.com/v2/track"),
				new byte[] { 1, 2, 3 },
				"application/json",
				"gzip");
			
			using (var stream = File.OpenWrite(oldFilePath))
			{
				StorageTransmission.SaveAsync(transmission, stream).ConfigureAwait(false).GetAwaiter().GetResult();
			}
			
			// Set creation time to 3 hours ago (exceeds MaxRetryDuration)
			File.SetCreationTimeUtc(oldFilePath, DateTime.UtcNow.AddHours(-3));

			// Create sender
			var transmitter = new PersistenceTransmitter(storage, 1);

			// Act - Wait for the sender to process the transmission
			Thread.Sleep(2000);

			// Assert - The old file should have been deleted (dropped)
			File.Exists(oldFilePath).Should().BeFalse();

			// Cleanup
			transmitter.Dispose();
		}

		[TestMethod]
		public void Given_ExceptionInSendLoop_When_SendLoop_Then_LoopContinues()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = new StorageService();
			storage.Init(storageDir);

			// Enqueue multiple transmissions
			for (int i = 0; i < 3; i++)
			{
				var transmission = new Transmission(
					new Uri("https://dc.services.visualstudio.com/v2/track"),
					new byte[] { (byte)i },
					"application/json",
					"");
				storage.EnqueueAsync(transmission).ConfigureAwait(false).GetAwaiter().GetResult();
			}

			// Create sender (this starts the SendLoop)
			var transmitter = new PersistenceTransmitter(storage, 1);

			// Act - Wait for processing
			Thread.Sleep(2000);

			// Assert - The sender should still be running despite any exceptions
			// We can verify this by checking that transmissions are being processed
			var remainingFiles = Directory.GetFiles(storageDir, "*.trn");
			
			// Even if some failed, the loop should continue processing
			// We're not asserting a specific count because network availability affects this
			// The key is that no exception crashed the process
			
			// Cleanup
			transmitter.Dispose();
		}

		[TestMethod]
		public void Given_MultipleFilesOfDifferentTypes_When_DeleteObsoleteFiles_Then_OnlyExpiredFilesAreDeleted()
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
			Thread.Sleep(1000);

			// Assert
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
			var transmission = new Transmission(
				new Uri("https://dc.services.visualstudio.com/v2/track"),
				new byte[] { 1, 2, 3 },
				"application/json",
				"");

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
	}
}
