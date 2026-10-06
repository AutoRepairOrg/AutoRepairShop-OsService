using Microsoft.Extensions.Logging;
using OsService.Application.Interfaces;
using OsService.Domain.Events;

namespace OsService.Infrastructure.Messaging;

/// <summary>
/// Publisher fake para desenvolvimento local sem RabbitMQ.
/// Apenas loga o evento no console. Ativado via appsettings: "Messaging:UseStub": true
/// </summary>
public class StubEventPublisher(ILogger<StubEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        logger.LogInformation(
            "[STUB EVENT] {EventType} publicado — AggregateId: {Id} — OccurredAt: {At}",
            typeof(TEvent).Name,
            @event.AggregateId,
            @event.OccurredAt);

        return Task.CompletedTask;
    }
}
