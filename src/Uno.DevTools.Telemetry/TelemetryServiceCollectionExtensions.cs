using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System;

namespace Uno.DevTools.Telemetry;

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

    /// <summary>
    /// Adds Uno.DevTools.Telemetry for a generic context type to the service collection.
    /// </summary>
    /// <typeparam name="T">The context type for telemetry (usually your main class).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddTelemetry<T>(this IServiceCollection services)
    {
        services.AddSingleton<ITelemetry<T>>(sp => TelemetryFactory.Create<T>());
        return services;
    }

    /// <summary>
    /// Registers ITelemetry<T> for all T in the DI container, using assembly-level configuration.
    /// </summary>
    public static IServiceCollection AddTelemetry(this IServiceCollection services)
    {
        services.AddTransient(typeof(ITelemetry<>), typeof(TelemetryGenericFactory<>));
        return services;
    }

    private class TelemetryGenericFactory<T> : ITelemetry<T>
    {
        private readonly ITelemetry<T> _inner;

        public TelemetryGenericFactory(IServiceProvider services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            _inner = TelemetryFactory.Create<T>();
        }

        public bool Enabled => _inner.Enabled;
        public void Dispose() => _inner.Dispose();
        public void Flush() => _inner.Flush();
        public Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
        public Task<string?> GetMachineIdAsync(CancellationToken ct) => _inner.GetMachineIdAsync(ct);
        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements) => _inner.ThreadBlockingTrackEvent(eventName, properties, measurements);
        public void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements) => _inner.TrackEvent(eventName, properties, measurements);
        public void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements) => _inner.TrackEvent(eventName, properties, measurements);
    }
}
