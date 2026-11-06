using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace OsService.Api.Observability;

public sealed class DatadogConsoleFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = logEvent.Timestamp.UtcDateTime.ToString("O"),
            ["level"] = logEvent.Level.ToString(),
            ["status"] = logEvent.Level.ToString(),
            ["message"] = logEvent.RenderMessage()
        };

        if (logEvent.Exception is not null)
            payload["exception"] = logEvent.Exception.ToString();

        foreach (var property in logEvent.Properties)
        {
            if (payload.ContainsKey(property.Key))
                continue;

            payload[property.Key] = Simplify(property.Value);
        }

        output.WriteLine(JsonSerializer.Serialize(payload));
    }

    private static object? Simplify(LogEventPropertyValue value) => value switch
    {
        ScalarValue scalar => scalar.Value,
        SequenceValue sequence => sequence.Elements.Select(Simplify).ToArray(),
        StructureValue structure => structure.Properties.ToDictionary(property => property.Name, property => Simplify(property.Value)),
        DictionaryValue dictionary => dictionary.Elements.ToDictionary(
            pair => pair.Key.Value?.ToString() ?? string.Empty,
            pair => Simplify(pair.Value)),
        _ => value.ToString().Trim('"')
    };
}
