using System.Text;
using System.Text.Json;
using OsService.Application.Interfaces;
using OsService.Domain.Events;
using RabbitMQ.Client;

namespace OsService.Infrastructure.Messaging;

/// <summary>
/// Implementação do IEventPublisher usando RabbitMQ.
/// Troque por SqsEventPublisher se preferir AWS SQS.
/// </summary>
public class RabbitMqEventPublisher(IConnection connection) : IEventPublisher, IAsyncDisposable
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
        var routingKey = @event.GetType().Name; // e.g. "OsCreatedEvent"

        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            body: body,
            cancellationToken: ct
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
