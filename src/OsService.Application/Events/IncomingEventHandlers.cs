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
    /// A OS já está Finished (ExecutionCompletedHandler). Aqui só entrega o veículo.
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar PaymentConfirmed.");

        order.Deliver();
        await repository.UpdateAsync(order, ct);

        foreach (var @event in order.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);
        order.ClearDomainEvents();
    }
}

public class ExecutionCompletedHandler(
    IServiceOrderRepository repository,
    IEventPublisher eventPublisher)
{
    /// <summary>
    /// Consumido quando ExecutionService conclui o serviço na oficina.
    /// Finaliza a OS (InExecution → Finished). A entrega fica para o PaymentConfirmed.
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar ExecutionCompleted.");

        order.Finish();
        await repository.UpdateAsync(order, ct);

        foreach (var @event in order.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);
        order.ClearDomainEvents();
    }
}

public class ExecutionFailedHandler(
    IServiceOrderRepository repository,
    IEventPublisher eventPublisher)
{
    /// <summary>
    /// Consumido quando ExecutionService registra falha na oficina.
    /// Cancela a OS e publica OsCancelledEvent para compensar Billing e Execution.
    /// </summary>
    public async Task HandleAsync(Guid osId, string reason, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar ExecutionFailed.");

        order.Cancel($"Execução falhou: {reason}");
        await repository.UpdateAsync(order, ct);

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
    /// WaitingApproval → InExecution e publica OsExecutionStartedEvent para o ExecutionService.
    /// Este é o único caminho válido para iniciar execução.
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
