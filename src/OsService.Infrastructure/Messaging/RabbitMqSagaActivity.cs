using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using RabbitMQ.Client;

namespace OsService.Infrastructure.Messaging;

/// <summary>
/// Correlação e spans da saga. O traceparent viaja no header da mensagem
/// para o Datadog montar um único trace entre os três serviços.
/// </summary>
public static class RabbitMqSagaActivity
{
    public const string SourceName = "AutoRepair.Saga";
    public const string CorrelationHeader = "correlation-id";
    public const string TraceParentHeader = "traceparent";

    public static readonly ActivitySource Source = new(SourceName);
    public static string ServiceName { get; set; } = "os-service";

    private static readonly AsyncLocal<string?> Correlation = new();
    private static readonly Meter Meter = new("AutoRepair");
    private static readonly Counter<long> Published = Meter.CreateCounter<long>("autorepair.saga.published");
    private static readonly Counter<long> Consumed = Meter.CreateCounter<long>("autorepair.saga.consumed");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("autorepair.saga.failed");

    public static string? Current => Correlation.Value;

    public static IDisposable Begin(string correlationId)
    {
        var previous = Correlation.Value;
        Correlation.Value = correlationId;
        return new PopScope(() => Correlation.Value = previous);
    }

    public static void Inject(IDictionary<string, object?> headers)
    {
        headers[CorrelationHeader] = Correlation.Value ?? Guid.NewGuid().ToString("N");
        if (Activity.Current is not { } activity)
            return;

        var flags = (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        headers[TraceParentHeader] = $"00-{activity.TraceId}-{activity.SpanId}-{flags}";
    }

    public static string Extract(IReadOnlyBasicProperties? properties)
    {
        var fromHeader = ReadHeader(properties, CorrelationHeader);
        if (!string.IsNullOrWhiteSpace(fromHeader))
            return fromHeader;

        if (!string.IsNullOrWhiteSpace(properties?.CorrelationId))
            return properties.CorrelationId;

        return Guid.NewGuid().ToString("N");
    }

    public static Activity? StartProducer(string eventName) =>
        Source.StartActivity($"saga publish {eventName}", ActivityKind.Producer);

    public static Activity? StartConsumer(string eventName, IReadOnlyBasicProperties? properties)
    {
        var traceparent = ReadHeader(properties, TraceParentHeader);
        if (!string.IsNullOrWhiteSpace(traceparent) &&
            ActivityContext.TryParse(traceparent, null, out var parent))
        {
            return Source.StartActivity($"saga consume {eventName}", ActivityKind.Consumer, parent);
        }

        return Source.StartActivity($"saga consume {eventName}", ActivityKind.Consumer);
    }

    public static void RecordPublished(string eventName) => Add(Published, eventName);

    public static void RecordConsumed(string eventName) => Add(Consumed, eventName);

    public static void RecordFailed(string eventName) => Add(Failed, eventName);

    private static void Add(Counter<long> counter, string eventName)
    {
        counter.Add(
            1,
            new KeyValuePair<string, object?>("event", eventName),
            new KeyValuePair<string, object?>("service", ServiceName));
    }

    private static string ReadHeader(IReadOnlyBasicProperties? properties, string key)
    {
        if (properties?.Headers is null || !properties.Headers.TryGetValue(key, out var raw) || raw is null)
            return string.Empty;

        return raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            string text => text,
            _ => raw.ToString() ?? string.Empty
        };
    }

    private sealed class PopScope(Action pop) : IDisposable
    {
        public void Dispose() => pop();
    }
}
