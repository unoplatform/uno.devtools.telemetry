// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// 2026/02/12:
//	- Created for WebAssembly support
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Uno.DevTools.Telemetry
{
	/// <summary>
	/// WASM-compatible HTTP sender for Application Insights telemetry.
	/// Sends telemetry directly to the Application Insights REST endpoint without using the SDK.
	/// </summary>
	internal sealed class WasmHttpSender
	{
		// Static HttpClient is intentional: reuse a single handler per app instance.
		private static readonly HttpClient HttpClient = new();
		private const string EndpointUrl = "https://dc.services.visualstudio.com/v2/track";

		private readonly string _instrumentationKey;
		private readonly string _eventNamePrefix;

		public WasmHttpSender(string instrumentationKey, string eventNamePrefix)
		{
			_instrumentationKey = instrumentationKey;
			_eventNamePrefix = eventNamePrefix;
		}

		public async Task SendEventAsync(
			string eventName,
			IDictionary<string, string>? properties,
			IDictionary<string, double>? measurements,
			string machineId,
			string? sessionId)
		{
			// Callers discard the returned task; an exception outside this try would go unobserved.
			try
			{
				var envelope = CreateEventEnvelope(eventName, properties, measurements, machineId, sessionId);
				await SendAsync(envelope);
			}
			catch (Exception ex)
			{
				LogFailure("WASM telemetry event envelope failed", ex);
			}
		}

		public async Task SendExceptionAsync(
			Exception exception,
			ExceptionSeverity severity,
			IDictionary<string, string>? properties,
			IDictionary<string, double>? measurements,
			string machineId,
			string? sessionId)
		{
			// Callers discard the returned task; an exception outside this try would go unobserved.
			try
			{
				var envelope = CreateExceptionEnvelope(exception, severity, properties, measurements, machineId, sessionId);
				await SendAsync(envelope);
			}
			catch (Exception ex)
			{
				LogFailure("WASM telemetry exception envelope failed", ex);
			}
		}

		internal object CreateEventEnvelope(
			string eventName,
			IDictionary<string, string>? properties,
			IDictionary<string, double>? measurements,
			string machineId,
			string? sessionId)
		{
			return new
			{
				name = $"Microsoft.ApplicationInsights.{_instrumentationKey}.Event",
				time = DateTime.UtcNow.ToString("o"),
				iKey = _instrumentationKey,
				tags = CreateTags(machineId, sessionId),
				data = new
				{
					baseType = "EventData",
					baseData = new
					{
						ver = 2,
						name = PrependProducerNamespace(eventName),
						properties = properties ?? new Dictionary<string, string>(),
						measurements = measurements ?? new Dictionary<string, double>()
					}
				}
			};
		}

		internal object CreateExceptionEnvelope(
			Exception exception,
			ExceptionSeverity severity,
			IDictionary<string, string>? properties,
			IDictionary<string, double>? measurements,
			string machineId,
			string? sessionId)
		{
			return new
			{
				name = $"Microsoft.ApplicationInsights.{_instrumentationKey}.Exception",
				time = DateTime.UtcNow.ToString("o"),
				iKey = _instrumentationKey,
				tags = CreateTags(machineId, sessionId),
				data = new
				{
					baseType = "ExceptionData",
					baseData = new
					{
						ver = 2,
						exceptions = new[]
						{
							new
							{
								typeName = exception.GetType().FullName,
								message = exception.Message,
								hasFullStack = !string.IsNullOrEmpty(exception.StackTrace),
								stack = exception.StackTrace ?? ""
							}
						},
						severityLevel = severity switch
						{
							ExceptionSeverity.Critical => 4,
							ExceptionSeverity.Error => 3,
							ExceptionSeverity.Warning => 2,
							ExceptionSeverity.Info => 1,
							ExceptionSeverity.Debug => 0,
							_ => 3
						},
						properties = properties ?? new Dictionary<string, string>(),
						measurements = measurements ?? new Dictionary<string, double>()
					}
				}
			};
		}

		private static Dictionary<string, string> CreateTags(string machineId, string? sessionId)
		{
			// Capacity 5: four fixed tags plus the optional authenticated user id, avoiding a resize per envelope.
			var tags = new Dictionary<string, string>(5)
			{
				["ai.user.id"] = machineId,
				["ai.session.id"] = sessionId ?? Guid.NewGuid().ToString(),
				["ai.device.os"] = "Browser",
				["ai.device.osVersion"] = "WebAssembly"
			};

			// Parity with the desktop AuthenticatedUserTelemetryInitializer: the tag is omitted entirely
			// when no user is authenticated, never sent empty.
			var authenticatedUserId = TelemetryUserContext.AuthenticatedUserId;
			if (authenticatedUserId is not null)
			{
				tags["ai.user.authUserId"] = authenticatedUserId;
			}

			return tags;
		}

		private async Task SendAsync(object envelope)
		{
			try
			{
				var json = JsonSerializer.Serialize(envelope);
				using var content = new StringContent(json, Encoding.UTF8, "application/json");
				using var response = await HttpClient.PostAsync(EndpointUrl, content).ConfigureAwait(false);
				response.EnsureSuccessStatusCode();
			}
			catch (HttpRequestException ex)
			{
				// Network failures should not crash the app
				LogFailure("WASM telemetry HTTP request failed", ex);
			}
			catch (Exception ex)
			{
				// Any other failures should not crash the app
				LogFailure("WASM telemetry send failed", ex);
			}
		}

		private static void LogFailure(string message, Exception exception)
		{
			var logMessage = $"{message}: {exception.Message}";
			Debug.WriteLine(logMessage);
			Trace.WriteLine(logMessage);
		}

		private string PrependProducerNamespace(string eventName)
		{
			return _eventNamePrefix + "/" + eventName;
		}
	}
}
