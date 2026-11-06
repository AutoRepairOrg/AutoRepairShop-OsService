using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OsService.Application.Interfaces;
using OsService.Domain.Events;
using RabbitMQ.Client;

namespace OsService.Infrastructure.Messaging;

/// <summary>
/// Implementação do IEventPublisher usando RabbitMQ.
/// Troque por SqsEventPublisher se preferir AWS SQS.
/// </summary>
public class RabbitMqEventPublisher(IConnection connection, ILogger<RabbitMqEventPublisher>? logger = null)
    : IEventPublisher, IAsyncDisposable, IDisposable
{
    private IChannel? _channel;

    private async Task<IChannel> GetChannelAsync()
    {
        if (_channel is null || !_channel.IsOpen)
            _channel = await connection.CreateChannelAsync();
        return _channel;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        var channel = await GetChannelAsync();
        var exchangeName = "os-service-events";
        // Usar GetType() (não typeof(TEvent)): quando chamado via DomainEvent base, TEvent é a base.
        var routingKey = @event.GetType().Name; // e.g. "OsSentForApprovalEvent"

        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );

        using var activity = RabbitMqSagaActivity.StartProducer(routingKey);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", exchangeName);
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("event_type", routingKey);
        activity?.SetTag("aggregate_id", @event.AggregateId.ToString());

        var headers = new Dictionary<string, object?>();
        RabbitMqSagaActivity.Inject(headers);
        var correlationId = headers[RabbitMqSagaActivity.CorrelationHeader]?.ToString();

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = correlationId,
            Headers = headers
        };

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct
        );

        RabbitMqSagaActivity.RecordPublished(routingKey);
        logger?.LogInformation(
            "Saga event published {event_type} {aggregate_id} {correlation_id}",
            routingKey,
            @event.AggregateId,
            correlationId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        _channel = null;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
