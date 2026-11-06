using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OsService.Infrastructure.Messaging;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace OsService.Api.Observability;

public static class DatadogObservability
{
    private static readonly Meter Meter = new("AutoRepair");
    private static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>("autorepair.http.requests");
    private static readonly Histogram<double> HttpDuration = Meter.CreateHistogram<double>("autorepair.http.duration", unit: "ms");

    public static void Configure(LoggerConfiguration logger, IConfiguration configuration, string defaultService)
    {
        var options = Read(configuration, defaultService);
        logger
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With(new DatadogTraceEnricher())
            .Enrich.WithProperty("service", options.Service)
            .Enrich.WithProperty("env", options.Env)
            .Enrich.WithProperty("version", options.Version)
            .Enrich.WithProperty("dd.service", options.Service)
            .Enrich.WithProperty("dd.env", options.Env)
            .Enrich.WithProperty("dd.version", options.Version);

        if (options.ExportEnabled || IsProduction(configuration))
            logger.WriteTo.Console(new DatadogConsoleFormatter());
        else
            logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

        if (options.UseDirectIntake)
        {
            logger.WriteTo.DatadogLogs(
                options.ApiKey!,
                source: "csharp",
                service: options.Service,
                host: Environment.MachineName,
                tags: [$"env:{options.Env}", $"version:{options.Version}"],
                configuration: new Serilog.Sinks.Datadog.Logs.DatadogConfiguration
                {
                    Url = $"https://http-intake.logs.{options.Site}"
                });
        }
    }

    public static void AddDatadog(this IServiceCollection services, IConfiguration configuration, string defaultService)
    {
        var options = Read(configuration, defaultService);
        RabbitMqSagaActivity.ServiceName = options.Service;
        if (!options.ExportEnabled)
            return;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(options.Service, serviceVersion: options.Version)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", options.Env)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(asp =>
                {
                    asp.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/swagger");
                    asp.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                .AddSource(RabbitMqSagaActivity.SourceName)
                .AddOtlpExporter(exporter => ConfigureOtlp(exporter, options)))
            .WithMetrics(metrics => metrics
                .AddMeter("AutoRepair")
                .AddOtlpExporter(exporter => ConfigureOtlp(exporter, options)));
    }

    public static void UseDatadog(this WebApplication app)
    {
        var export = HasValue(app.Configuration["DD_API_KEY"])
            || HasValue(app.Configuration["Datadog:ApiKey"])
            || HasValue(app.Configuration["DD_AGENT_HOST"])
            || HasValue(app.Configuration["Datadog:AgentHost"]);

        app.Logger.LogInformation(
            "Observabilidade configurada para {service}. Envio ao Datadog: {datadog_export}",
            RabbitMqSagaActivity.ServiceName,
            export);

        app.UseRouting();
        app.Use(async (ctx, next) =>
        {
            var incoming = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            var correlationId = string.IsNullOrWhiteSpace(incoming)
                ? Guid.NewGuid().ToString("N")
                : incoming.Trim();
            ctx.Response.Headers["X-Correlation-Id"] = correlationId;

            using (RabbitMqSagaActivity.Begin(correlationId))
            using (LogContext.PushProperty("correlation_id", correlationId))
            {
                var started = Stopwatch.StartNew();
                try
                {
                    await next();
                }
                finally
                {
                    started.Stop();
                    if (!ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/swagger"))
                    {
                        var route = ctx.GetEndpoint() is RouteEndpoint endpoint &&
                                    endpoint.RoutePattern.RawText is { Length: > 0 } raw
                            ? raw
                            : "unmatched";
                        var tags = new TagList
                        {
                            { "http.method", ctx.Request.Method },
                            { "http.route", route },
                            { "http.status_code", ctx.Response.StatusCode.ToString() },
                            { "service", RabbitMqSagaActivity.ServiceName }
                        };
                        HttpRequests.Add(1, tags);
                        HttpDuration.Record(started.Elapsed.TotalMilliseconds, tags);
                    }
                }
            }
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (ctx, _, ex) =>
            {
                if (ctx.Request.Path.StartsWithSegments("/health") ||
                    ctx.Request.Path.StartsWithSegments("/swagger"))
                    return LogEventLevel.Verbose;

                if (ex is not null || ctx.Response.StatusCode >= 500)
                    return LogEventLevel.Error;

                if (ctx.Response.StatusCode >= 400)
                    return LogEventLevel.Warning;

                return LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnostic, ctx) =>
            {
                diagnostic.Set("event_type", "http.request");
                if (ctx.GetEndpoint() is RouteEndpoint endpoint &&
                    endpoint.RoutePattern.RawText is { Length: > 0 } raw)
                    diagnostic.Set("http.route", raw);
            };
        });
    }

    private static void ConfigureOtlp(OpenTelemetry.Exporter.OtlpExporterOptions exporter, DatadogOptions options)
    {
        exporter.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
        if (!string.IsNullOrWhiteSpace(options.AgentHost))
        {
            exporter.Endpoint = new Uri($"http://{options.AgentHost}:4318");
            return;
        }

        exporter.Endpoint = new Uri($"https://otlp.{options.Site}");
        exporter.Headers = $"dd-api-key={options.ApiKey}";
    }

    private static DatadogOptions Read(IConfiguration configuration, string defaultService)
    {
        var service = First(configuration["DD_SERVICE"], configuration["Datadog:Service"]) ?? defaultService;
        var env = First(configuration["DD_ENV"], configuration["Datadog:Env"]) ?? "local";
        var version = First(configuration["DD_VERSION"], configuration["Datadog:Version"]) ?? "1.0.0";
        var site = First(configuration["DD_SITE"], configuration["Datadog:Site"]) ?? "datadoghq.com";
        var apiKey = First(configuration["DD_API_KEY"], configuration["Datadog:ApiKey"]);
        if (apiKey is "SUBSTITUA_PELA_API_KEY")
            apiKey = null;

        var agentHost = First(configuration["DD_AGENT_HOST"], configuration["Datadog:AgentHost"]);
        return new DatadogOptions(service, env, version, site, apiKey, agentHost);
    }

    private static bool IsProduction(IConfiguration configuration) =>
        string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim() is not "SUBSTITUA_PELA_API_KEY";

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private sealed record DatadogOptions(
        string Service,
        string Env,
        string Version,
        string Site,
        string? ApiKey,
        string? AgentHost)
    {
        public bool UseDirectIntake => !string.IsNullOrWhiteSpace(ApiKey) && string.IsNullOrWhiteSpace(AgentHost);
        public bool ExportEnabled => !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(AgentHost);
    }
}

public sealed class DatadogTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.trace_id", activity.TraceId.ToString()));
        var spanId = Convert.ToUInt64(activity.SpanId.ToString(), 16).ToString();
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("dd.span_id", spanId));
    }
}
