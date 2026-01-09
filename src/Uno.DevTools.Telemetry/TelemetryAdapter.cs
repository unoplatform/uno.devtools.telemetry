using Microsoft.Extensions.DependencyInjection;

namespace Uno.DevTools.Telemetry;

public record TelemetryAdapter<T> : ITelemetry<T>
{
    private readonly ITelemetry Inner;

    public TelemetryAdapter(ITelemetry inner)
    {
        Inner = inner;
    }

    public TelemetryAdapter(IServiceProvider services)
    {
        Inner = services.GetRequiredService<ITelemetry>();
    }

    /// <inheritdoc />
    public void Dispose()
        => Inner.Dispose();

    /// <inheritdoc />
    public void Flush()
        => Inner.Flush();

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken ct)
        => Inner.FlushAsync(ct);

    /// <inheritdoc />
    public Task<string?> GetMachineIdAsync(CancellationToken ct)
        => Inner.GetMachineIdAsync(ct);

    /// <inheritdoc />
    public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        => Inner.ThreadBlockingTrackEvent(eventName, properties, measurements);

    /// <inheritdoc />
    public void TrackEvent(string eventName, (string key, string value)[]? properties, (string key, double value)[]? measurements)
        => Inner.TrackEvent(eventName, properties, measurements);

    /// <inheritdoc />
    public void TrackEvent(string eventName, IDictionary<string, string>? properties, IDictionary<string, double>? measurements)
        => Inner.TrackEvent(eventName, properties, measurements);

    /// <inheritdoc />
    public ITelemetry CreateScope(IReadOnlyDictionary<string, string>? properties = null, IReadOnlyDictionary<string, double>? measurements = null)
        => Inner.CreateScope(properties, measurements);

    /// <inheritdoc />
    public void TrackException(Exception exception, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyDictionary<string, double>? measurements = null, TelemetrySeverity severity = TelemetrySeverity.Error)
        => Inner.TrackException(exception, properties, measurements, severity);

    /// <inheritdoc />
    public bool Enabled => Inner.Enabled;
}
