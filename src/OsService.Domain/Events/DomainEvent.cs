namespace OsService.Domain.Events;

public abstract record DomainEvent(Guid AggregateId, DateTime OccurredAt)
{
    protected DomainEvent(Guid aggregateId) : this(aggregateId, DateTime.UtcNow) { }
}

public record OsCreatedEvent(Guid OsId, string CustomerName, string VehiclePlate)
    : DomainEvent(OsId);

public record OsCancelledEvent(Guid OsId, string Reason)
    : DomainEvent(OsId);

public record OsClosedEvent(Guid OsId)
    : DomainEvent(OsId);
