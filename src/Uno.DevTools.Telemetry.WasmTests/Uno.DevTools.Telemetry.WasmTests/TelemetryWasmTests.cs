using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.DevTools.Telemetry.WasmTests;

[TestClass]
public class TelemetryWasmTests
{
	// Read from environment variable (for CI/integration testing), fallback to dummy key for local testing
	private static readonly string TestInstrumentationKey =
		Environment.GetEnvironmentVariable("UNO_TEST_APPINSIGHTS_KEY")
		?? "00000000-0000-0000-0000-000000000000";
	private const string TestEventPrefix = "WasmTest";
	private string? _telemetryOptOutOriginal;
	private bool _telemetryOptOutTouched;

	[TestCleanup]
	public void Cleanup()
	{
		if (_telemetryOptOutTouched)
		{
			Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", _telemetryOptOutOriginal);
			_telemetryOptOutTouched = false;
			_telemetryOptOutOriginal = null;
		}
	}

	[TestMethod]
	public async Task Telemetry_InitializesOnWasm_WithoutErrors()
	{
		// Arrange & Act
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Assert
		Assert.IsTrue(telemetry.Enabled, "Telemetry should be enabled");

		// Verify machine ID can be retrieved
		var machineId = await telemetry.GetMachineIdAsync(CancellationToken.None);
		Assert.IsNotNull(machineId, "Machine ID should be generated");

		// Machine ID should be a valid GUID on WASM
		Assert.IsTrue(Guid.TryParse(machineId, out _), "Machine ID should be a valid GUID");
	}

	[TestMethod]
	public void Telemetry_TrackEvent_OnWasm_DoesNotThrow()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Act & Assert - Should not throw (using Dictionary to avoid ambiguity)
		telemetry.TrackEvent("TestEvent",
			new Dictionary<string, string> { ["key"] = "value" },
			(IDictionary<string, double>?)null);
		telemetry.TrackEvent("TestEvent2",
			(IDictionary<string, string>?)null,
			new Dictionary<string, double> { ["metric"] = 1.0 });
		telemetry.TrackEvent("TestEvent3",
			new Dictionary<string, string> { ["prop1"] = "val1", ["prop2"] = "val2" },
			new Dictionary<string, double> { ["measure1"] = 10.5, ["measure2"] = 20.0 });
	}

	[TestMethod]
	public void Telemetry_TrackException_OnWasm_DoesNotThrow()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);
		var exception = new InvalidOperationException("Test exception");

		// Act & Assert - Should not throw
		telemetry.TrackException(exception, severity: ExceptionSeverity.Warning);
		telemetry.TrackException(
			new ArgumentException("Another test"),
			new Dictionary<string, string> { ["context"] = "unit-test" },
			new Dictionary<string, double> { ["count"] = 1.0 },
			ExceptionSeverity.Error
		);
	}

	[TestMethod]
	public async Task Telemetry_FlushAsync_OnWasm_CompletesSuccessfully()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Track some events
		telemetry.TrackEvent("Event1", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);
		telemetry.TrackEvent("Event2", (IDictionary<string, string>?)null, (IDictionary<string, double>?)null);

		// Act & Assert - Should complete without throwing
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await telemetry.FlushAsync(cts.Token);
	}

	[TestMethod]
	public void Telemetry_DisabledViaEnvironmentVariable_OnWasm()
	{
		// Arrange
		_telemetryOptOutOriginal = Environment.GetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT");
		_telemetryOptOutTouched = true;
		Environment.SetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_OPTOUT", "true");

		// Act
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Assert
		Assert.IsFalse(telemetry.Enabled, "Telemetry should be disabled");
	}

	[TestMethod]
	public void Telemetry_MultipleEvents_OnWasm_DoesNotCauseThreadingIssues()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Act - Track many events rapidly (tests the lock-free chaining)
		for (int i = 0; i < 100; i++)
		{
			telemetry.TrackEvent($"Event{i}",
				new Dictionary<string, string> { ["index"] = i.ToString() },
				(IDictionary<string, double>?)null);
		}

		// Assert - Should complete without threading errors
		// In WASM, this should work even though Thread.Yield() is skipped
		Assert.IsTrue(true, "Completed without threading issues");
	}

	[TestMethod]
	public void Telemetry_AllExceptionSeverities_OnWasm()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		// Act & Assert - Test all severity levels
		telemetry.TrackException(new Exception("Critical"), severity: ExceptionSeverity.Critical);
		telemetry.TrackException(new Exception("Error"), severity: ExceptionSeverity.Error);
		telemetry.TrackException(new Exception("Warning"), severity: ExceptionSeverity.Warning);
		telemetry.TrackException(new Exception("Info"), severity: ExceptionSeverity.Info);
		telemetry.TrackException(new Exception("Debug"), severity: ExceptionSeverity.Debug);

		// Should not throw - verifies severity mapping works on WASM
		Assert.IsTrue(true, "All severity levels handled without errors");
	}

	[TestMethod]
	public void Telemetry_AuthenticatedUserId_SetTrackClear_OnWasm_DoesNotThrow()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		try
		{
			// Act & Assert - Set, round-trip, track, clear
			telemetry.AuthenticatedUserId = "wasm-test-user";
			Assert.AreEqual("wasm-test-user", telemetry.AuthenticatedUserId, "Authenticated user id should round-trip");

			telemetry.TrackEvent("AuthenticatedEvent",
				new Dictionary<string, string> { ["key"] = "value" },
				(IDictionary<string, double>?)null);
			telemetry.TrackException(new InvalidOperationException("Authenticated exception"), severity: ExceptionSeverity.Warning);

			telemetry.AuthenticatedUserId = null;
			Assert.IsNull(telemetry.AuthenticatedUserId, "Authenticated user id should be cleared");

			telemetry.TrackEvent("SignedOutEvent",
				(IDictionary<string, string>?)null,
				(IDictionary<string, double>?)null);
		}
		finally
		{
			// The value is process-wide ambient state - never leak it into other tests.
			TelemetryUserContext.AuthenticatedUserId = null;
		}
	}

	[TestMethod]
	public void Telemetry_ThreadBlockingTrackEvent_OnWasm()
	{
		// Arrange
		var telemetry = new Telemetry(
			instrumentationKey: TestInstrumentationKey,
			eventNamePrefix: TestEventPrefix,
			versionAssembly: typeof(TelemetryWasmTests).Assembly
		);

		var properties = new Dictionary<string, string> { ["test"] = "blocking" };
		var measurements = new Dictionary<string, double> { ["value"] = 42.0 };

		// Act & Assert - Should work on WASM despite being "thread blocking"
		telemetry.ThreadBlockingTrackEvent("BlockingEvent", properties, measurements);
		Assert.IsTrue(true, "Thread blocking event tracked successfully");
	}
}
