using OsService.Application.Interfaces;
using OsService.Domain.Interfaces;

namespace OsService.Application.Events;

/// <summary>
/// Handlers para eventos consumidos do broker (Saga coreografada).
/// Registrados como consumers do RabbitMQ/SQS na Infrastructure.
/// </summary>
public class PaymentConfirmedHandler(
    IServiceOrderRepository repository,
    IEventPublisher eventPublisher)
{
    /// <summary>
    /// Consumido quando BillingService confirma pagamento.
    /// Finaliza o serviço (Finish) e registra a entrega (Deliver).
    /// TODO: quando ExecutionService existir, Finish() deve ser acionado por
    ///       ExecutionCompletedEvent (via ExecutionCompletedHandler) e este handler
    ///       deve chamar apenas Deliver().
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar PaymentConfirmed.");

        order.Finish();
        order.Deliver();
        await repository.UpdateAsync(order, ct);

        // Publica OsClosedEvent (gerado por Deliver())
        foreach (var @event in order.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);
        order.ClearDomainEvents();
    }
}

public class BudgetRejectedHandler(IServiceOrderRepository repository)
{
    /// <summary>
    /// Consumido quando BillingService informa rejeição do orçamento.
    /// Cancela a OS com compensação do Saga.
    /// </summary>
    public async Task HandleAsync(Guid osId, string reason, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar BudgetRejected.");

        order.Cancel($"Orçamento rejeitado pelo cliente: {reason}");
        await repository.UpdateAsync(order, ct);
    }
}

public class BudgetApprovedHandler(
    IServiceOrderRepository repository,
    IEventPublisher eventPublisher)
{
    /// <summary>
    /// Consumido quando BillingService confirma aprovação do orçamento pelo cliente.
    /// Inicia automaticamente a execução do serviço e publica OsExecutionStartedEvent,
    /// que será consumido pelo ExecutionService.
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar BudgetApproved.");

        order.StartExecution();
        await repository.UpdateAsync(order, ct);

        // Publica OsExecutionStartedEvent (gerado por StartExecution())
        foreach (var @event in order.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);
        order.ClearDomainEvents();
    }
}
