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

// ── Novos eventos para o Saga coreografado ────────────────────────────────────

/// <summary>
/// Item da OS enviado como payload do OsSentForApprovalEvent.
/// Permite que o BillingService auto-popule o Budget sem duplicação manual.
/// </summary>
public record OsItemPayload(string ServiceName, decimal Price, int Quantity);

/// <summary>
/// Publicado quando a OS é enviada para aprovação de orçamento.
/// Carrega os itens diagnosticados para que o BillingService popule o Budget automaticamente.
/// </summary>
public record OsSentForApprovalEvent(
    Guid OsId,
    string CustomerName,
    string VehiclePlate,
    IReadOnlyList<OsItemPayload> Items)
    : DomainEvent(OsId);

/// <summary>
/// Publicado quando a execução do serviço é iniciada (após aprovação do orçamento).
/// Consumido pelo ExecutionService para criar a tarefa de execução.
/// </summary>
public record OsExecutionStartedEvent(Guid OsId, string CustomerName, string VehiclePlate, string? Description)
    : DomainEvent(OsId);
