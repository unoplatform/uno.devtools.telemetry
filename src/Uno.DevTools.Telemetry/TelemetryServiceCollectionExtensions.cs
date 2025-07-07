using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System;

namespace Uno.DevTools.Telemetry
{
    public static class TelemetryServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Uno.DevTools.Telemetry to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="instrumentationKey">The Application Insights instrumentation key.</param>
        /// <param name="eventNamePrefix">Event name prefix (context).</param>
        /// <param name="versionAssembly">Optional assembly for version info. If null, uses calling assembly.</param>
        /// <param name="sessionId">Optional session id.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddTelemetry(
            this IServiceCollection services,
            string instrumentationKey,
            string eventNamePrefix,
            Assembly? versionAssembly = null,
            string? sessionId = null)
        {
            versionAssembly ??= Assembly.GetCallingAssembly();

            // Check for file-based telemetry override
            var filePath = Environment.GetEnvironmentVariable("UNO_PLATFORM_TELEMETRY_FILE");
            if (!string.IsNullOrEmpty(filePath))
            {
                services.AddSingleton<ITelemetry>(sp =>
                {
#if NET8_0_OR_GREATER
                    var timeProvider = sp.GetService<TimeProvider>();
                    return new FileTelemetry(filePath, eventNamePrefix, timeProvider);
#else
                    return new FileTelemetry(filePath, eventNamePrefix);
#endif
                });
            }
            else
            {
                services.AddSingleton<ITelemetry>(sp => new Telemetry(
                    instrumentationKey,
                    eventNamePrefix,
                    versionAssembly,
                    sessionId));
            }
            return services;
        }
    }
}
