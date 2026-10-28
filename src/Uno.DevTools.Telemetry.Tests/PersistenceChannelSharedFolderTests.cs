using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;

namespace Uno.DevTools.Telemetry.Tests
{
	/// <summary>
	///     Several channels, in one process or many, share the same storage folder. Each transmission must be sent once.
	/// </summary>
	[TestClass]
	public class PersistenceChannelSharedFolderTests
	{
		private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

		private readonly List<string> _directoriesToCleanup = new List<string>();

		private string GetTempStorageDirectory()
		{
			var dir = Path.Combine(Path.GetTempPath(), $"telemetry_shared_test_{Guid.NewGuid():N}");
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
		public void Given_TwoStoragesOnTheSameFolder_When_BothPeek_Then_OnlyOneGetsTheTransmission()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var first = CreateStorage(storageDir);
			var second = CreateStorage(storageDir);
			first.EnqueueAsync(CreateTransmission()).GetAwaiter().GetResult();

			// Act
			using var peekedByFirst = first.Peek();
			using var peekedBySecond = second.Peek();

			// Assert
			peekedByFirst.Should().NotBeNull();
			peekedBySecond.Should().BeNull("the first storage is already sending the only transmission");
		}

		[TestMethod]
		public void Given_PeekedTransmissionNotDeleted_When_Disposed_Then_AnotherStorageCanPeekIt()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var first = CreateStorage(storageDir);
			var second = CreateStorage(storageDir);
			first.EnqueueAsync(CreateTransmission()).GetAwaiter().GetResult();

			// Act - a retryable failure: the sender disposes the transmission without deleting it
			first.Peek()!.Dispose();
			using var peekedBySecond = second.Peek();

			// Assert
			peekedBySecond.Should().NotBeNull("a transmission that was not sent goes back to the queue");
		}

		[TestMethod]
		public void Given_FileThatCannotBeClaimed_When_Peek_Then_ItIsSkipped()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = CreateStorage(storageDir);
			storage.EnqueueAsync(CreateTransmission()).GetAwaiter().GetResult();
			var file = Path.GetFileName(Directory.GetFiles(storageDir, "*.trn").Single());

			// Act
			StorageTransmission? peeked;
			using (DenyWrite(Path.Combine(storageDir, file)))
			{
				peeked = storage.Peek();
			}

			// Assert
			peeked.Should().BeNull("a sender that can't claim a file must not send it");
			File.Exists(Path.Combine(storageDir, file)).Should().BeTrue("the file is still queued for a sender that can claim it");
			Directory.GetFiles(storageDir, "*.corrupt").Should().BeEmpty();
		}

		[TestMethod]
		public void Given_DeleteFailsAfterSend_When_PeekingAgain_Then_TheTransmissionIsNotSentAgain()
		{
			// Arrange
			var storageDir = GetTempStorageDirectory();
			var storage = CreateStorage(storageDir);
			var other = CreateStorage(storageDir);
			storage.EnqueueAsync(CreateTransmission()).GetAwaiter().GetResult();
			var transmission = storage.Peek()!;

			using (DenyDelete(storageDir, transmission.FileName))
			{
				// Act - the transmission was sent, but its file can't be removed
				storage.Delete(transmission);
				transmission.Dispose();

				// Assert
				storage.Peek().Should().BeNull("this storage already sent it");
				other.Peek().Should().BeNull("another storage must not send it either");
			}

			storage.Peek().Should().BeNull();
			Directory.GetFiles(storageDir).Should().BeEmpty("the deletion is retried once it can succeed");
		}

		[TestMethod]
		public async Task Given_ChannelsSharingAFolder_When_OneTracksEvents_Then_EachEventIsPostedOnce()
		{
			// Arrange - one channel tracks events, the other two only send what they find in the folder
			const int EventCount = 20;
			var storageDir = GetTempStorageDirectory();
			using var endpoint = new CountingHttpEndpoint();
			var channels = Enumerable.Range(0, 3)
				.Select(_ => new PersistenceChannel.PersistenceChannel(storageDir)
				{
					EndpointAddress = endpoint.Address,
					SendingInterval = TimeSpan.FromMilliseconds(1),
				})
				.ToArray();

			try
			{
				// Act
				for (var i = 0; i < EventCount; i++)
				{
					channels[0].Send(new EventTelemetry($"event-{i}"));
				}

				var drained = await WaitUntilAsync(
					() => endpoint.RequestCount >= EventCount && Directory.GetFiles(storageDir).Length == 0,
					TimeSpan.FromSeconds(30),
					DefaultPollInterval).ConfigureAwait(false);

				// Give a sender that read a file before it was deleted the time to post its copy
				await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

				// Assert
				drained.Should().BeTrue("every event should be sent and its file deleted");
				endpoint.RequestCount.Should().Be(EventCount, "each event must be posted exactly once");
			}
			finally
			{
				foreach (var channel in channels)
				{
					channel.Dispose();
				}
			}
		}

		private static StorageService CreateStorage(string storageDir)
		{
			var storage = new StorageService();
			storage.Init(storageDir);
			return storage;
		}

		private static Transmission CreateTransmission()
		{
			return new Transmission(
				new Uri("https://dc.services.visualstudio.com/v2/track"),
				new byte[] { 1, 2, 3 },
				"application/json",
				"");
		}

		/// <summary>
		///     Until disposed, <paramref name="path" /> can't be opened for writing, as when it belongs to another user.
		/// </summary>
		private static IDisposable DenyWrite(string path)
		{
			File.SetAttributes(path, FileAttributes.ReadOnly);
			return EnforcedOrInconclusive(
				new DisposableAction(() => File.SetAttributes(path, FileAttributes.Normal)),
				() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
		}

		/// <summary>
		///     Until disposed, <paramref name="file" /> can't be deleted: on Windows the file is made read-only, elsewhere its folder is.
		/// </summary>
		private static IDisposable DenyDelete(string storageDir, string file)
		{
			if (OperatingSystem.IsWindows())
			{
				return DenyWrite(Path.Combine(storageDir, file));
			}

			var mode = File.GetUnixFileMode(storageDir);
			File.SetUnixFileMode(storageDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
			return EnforcedOrInconclusive(
				new DisposableAction(() => File.SetUnixFileMode(storageDir, mode)),
				() => File.Create(Path.Combine(storageDir, "probe")).Dispose());
		}

		private static IDisposable EnforcedOrInconclusive(IDisposable restore, Action probe)
		{
			try
			{
				probe();
			}
			catch (Exception e) when (e is UnauthorizedAccessException or IOException)
			{
				return restore;
			}

			restore.Dispose();
			Assert.Inconclusive("File permissions are not enforced for this user (root?).");
			return restore;
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

		private sealed class DisposableAction(Action action) : IDisposable
		{
			public void Dispose() => action();
		}

		/// <summary>
		///     Answers 200 to every HTTP request on a loopback port, and counts them.
		/// </summary>
		private sealed class CountingHttpEndpoint : IDisposable
		{
			private static readonly byte[] Response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

			private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
			private int _requestCount;

			public CountingHttpEndpoint()
			{
				_listener.Start();
				_ = AcceptLoopAsync();
			}

			public string Address => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v2/track";

			public int RequestCount => Volatile.Read(ref _requestCount);

			public void Dispose() => _listener.Stop();

			private async Task AcceptLoopAsync()
			{
				while (true)
				{
					TcpClient client;
					try
					{
						client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
					}
					catch (Exception)
					{
						return; // Stopped
					}

					_ = Task.Run(() => ServeAsync(client));
				}
			}

			private async Task ServeAsync(TcpClient client)
			{
				using (client)
				{
					var stream = client.GetStream();
					if (await ReadRequestAsync(stream).ConfigureAwait(false))
					{
						Interlocked.Increment(ref _requestCount);
						await stream.WriteAsync(Response).ConfigureAwait(false);
					}
				}
			}

			private static async Task<bool> ReadRequestAsync(NetworkStream stream)
			{
				// Headers end with a blank line; the body is then skipped using its Content-Length.
				var headers = new StringBuilder();
				var oneByte = new byte[1];
				while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
				{
					if (await stream.ReadAsync(oneByte).ConfigureAwait(false) == 0)
					{
						return false;
					}

					headers.Append((char)oneByte[0]);
				}

				var contentLength = Regex.Match(headers.ToString(), @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
				var remaining = contentLength.Success ? int.Parse(contentLength.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
				var body = new byte[4096];
				while (remaining > 0)
				{
					var read = await stream.ReadAsync(body.AsMemory(0, Math.Min(body.Length, remaining))).ConfigureAwait(false);
					if (read == 0)
					{
						return false;
					}

					remaining -= read;
				}

				return true;
			}
		}
	}
}
