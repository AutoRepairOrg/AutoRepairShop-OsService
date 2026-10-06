using OsService.Domain.Interfaces;

namespace OsService.Application.Events;

/// <summary>
/// Handlers para eventos consumidos do broker (Saga coreografada).
/// Registrados como consumers do RabbitMQ/SQS na Infrastructure.
/// </summary>
public class PaymentConfirmedHandler(IServiceOrderRepository repository)
{
    /// <summary>
    /// Consumido quando BillingService confirma pagamento.
    /// Fecha a OS e registra entrega.
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"OS {osId} não encontrada ao processar PaymentConfirmed.");

        order.Finish();
        order.Deliver();
        await repository.UpdateAsync(order, ct);
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
