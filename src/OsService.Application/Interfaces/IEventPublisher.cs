using OsService.Domain.Events;

namespace OsService.Application.Interfaces;

/// <summary>
/// Abstração para publicação de eventos de domínio no message broker (RabbitMQ/SQS).
/// Implementado em OsService.Infrastructure.Messaging.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DomainEvent;
}
