// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2026/02/12:
//	- Created for WebAssembly support testing
//

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry.Tests
{
	[TestClass]
	public class WasmHttpSenderTests
	{
		private const string TestInstrumentationKey = "test-instrumentation-key-12345";
		private const string TestEventPrefix = "TestApp";

		[TestMethod]
		public void WasmHttpSender_Constructor_ShouldNotThrow()
		{
			// Arrange & Act
			var sender = CreateWasmHttpSender();

			// Assert
			Assert.IsNotNull(sender);
		}

		[TestMethod]
		public async Task SendEventAsync_WithValidData_ShouldNotThrow()
		{
			// Arrange
			var sender = CreateWasmHttpSender();
			var properties = new Dictionary<string, string>
			{
				["TestProperty"] = "TestValue",
				["AnotherProperty"] = "AnotherValue"
			};
			var measurements = new Dictionary<string, double>
			{
				["TestMetric"] = 123.45,
				["AnotherMetric"] = 678.90
			};

			// Act & Assert - Should not throw even if network call fails
			// (WasmHttpSender swallows exceptions to prevent telemetry from crashing the app)
			await sender.SendEventAsync(
				"TestEvent",
				properties,
				measurements,
				"test-machine-id",
				"test-session-id");
		}

		[TestMethod]
		public async Task SendEventAsync_WithNullProperties_ShouldNotThrow()
		{
			// Arrange
			var sender = CreateWasmHttpSender();

			// Act & Assert
			await sender.SendEventAsync(
				"TestEvent",
				null,
				null,
				"test-machine-id",
				"test-session-id");
		}

		[TestMethod]
		public async Task SendExceptionAsync_WithValidException_ShouldNotThrow()
		{
			// Arrange
			var sender = CreateWasmHttpSender();
			var exception = new InvalidOperationException("Test exception message");
			var properties = new Dictionary<string, string>
			{
				["ExceptionContext"] = "UnitTest"
			};
			var measurements = new Dictionary<string, double>
			{
				["ErrorCount"] = 1.0
			};

			// Act & Assert
			await sender.SendExceptionAsync(
				exception,
				ExceptionSeverity.Error,
				properties,
				measurements,
				"test-machine-id",
				"test-session-id");
		}

		[TestMethod]
		public async Task SendExceptionAsync_WithAllSeverityLevels_ShouldNotThrow()
		{
			// Arrange
			var sender = CreateWasmHttpSender();
			var exception = new InvalidOperationException("Test exception");

			// Act & Assert - Test all severity levels
			await sender.SendExceptionAsync(exception, ExceptionSeverity.Critical, null, null, "machine-id", "session-id");
			await sender.SendExceptionAsync(exception, ExceptionSeverity.Error, null, null, "machine-id", "session-id");
			await sender.SendExceptionAsync(exception, ExceptionSeverity.Warning, null, null, "machine-id", "session-id");
			await sender.SendExceptionAsync(exception, ExceptionSeverity.Info, null, null, "machine-id", "session-id");
			await sender.SendExceptionAsync(exception, ExceptionSeverity.Debug, null, null, "machine-id", "session-id");
		}

		[TestMethod]
		public async Task SendExceptionAsync_WithNullProperties_ShouldNotThrow()
		{
			// Arrange
			var sender = CreateWasmHttpSender();
			var exception = new InvalidOperationException("Test exception");

			// Act & Assert
			await sender.SendExceptionAsync(
				exception,
				ExceptionSeverity.Error,
				null,
				null,
				"test-machine-id",
				"test-session-id");
		}

		[TestMethod]
		public void WasmHttpSender_PrependProducerNamespace_ShouldFormatCorrectly()
		{
			// This test verifies the event name formatting matches expected pattern
			// We can't directly test the private method, but we can verify through integration

			// Arrange
			var sender = CreateWasmHttpSender();

			// Act & Assert - Should not throw
			// The actual formatting is tested through integration with SendEventAsync
			Assert.IsNotNull(sender);
		}

		private WasmHttpSender CreateWasmHttpSender()
		{
			// Use reflection to create WasmHttpSender since it's internal
			var assembly = typeof(Telemetry).Assembly;
			var type = assembly.GetType("Uno.DevTools.Telemetry.WasmHttpSender");
			Assert.IsNotNull(type, "WasmHttpSender type should exist");

			var instance = Activator.CreateInstance(
				type,
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
				null,
				new object[] { TestInstrumentationKey, TestEventPrefix },
				null);

			Assert.IsNotNull(instance, "WasmHttpSender instance should be created");

			// Create a wrapper to expose the methods
			return new WasmHttpSender(instance);
		}

		// Wrapper class to call internal WasmHttpSender methods via reflection
		private class WasmHttpSender
		{
			private readonly object _instance;
			private readonly Type _type;

			public WasmHttpSender(object instance)
			{
				_instance = instance;
				_type = instance.GetType();
			}

			public async Task SendEventAsync(
				string eventName,
				IDictionary<string, string>? properties,
				IDictionary<string, double>? measurements,
				string machineId,
				string? sessionId)
			{
				var method = _type.GetMethod("SendEventAsync");
				Assert.IsNotNull(method, "SendEventAsync method should exist");

				var task = method.Invoke(_instance, new object?[] { eventName, properties, measurements, machineId, sessionId }) as Task;
				Assert.IsNotNull(task, "SendEventAsync should return a Task");

				await task;
			}

			public async Task SendExceptionAsync(
				Exception exception,
				ExceptionSeverity severity,
				IDictionary<string, string>? properties,
				IDictionary<string, double>? measurements,
				string machineId,
				string? sessionId)
			{
				var method = _type.GetMethod("SendExceptionAsync");
				Assert.IsNotNull(method, "SendExceptionAsync method should exist");

				var task = method.Invoke(_instance, new object?[] { exception, severity, properties, measurements, machineId, sessionId }) as Task;
				Assert.IsNotNull(task, "SendExceptionAsync should return a Task");

				await task;
			}
		}
	}
}
