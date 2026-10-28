using OsService.Application.DTOs;
using OsService.Application.Interfaces;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Application.Services;

public class ServiceOrderAppService(
    IServiceOrderRepository repository,
    IEventPublisher eventPublisher)
{
    public async Task<ServiceOrderResponse> CreateAsync(CreateServiceOrderRequest request, CancellationToken ct = default)
    {
        var order = ServiceOrder.Create(
            request.CustomerName,
            request.CustomerPhone,
            request.VehiclePlate,
            request.VehicleModel,
            request.Description
        );

        await repository.AddAsync(order, ct);
        await PublishDomainEventsAsync(order, ct);

        return MapToResponse(order);
    }

    public async Task<ServiceOrderResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Ordem de Serviço {id} não encontrada.");

        return MapToResponse(order);
    }

    public async Task<IEnumerable<ServiceOrderResponse>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
    {
        var orders = await repository.GetByStatusAsync(status, ct);
        return orders.Select(MapToResponse);
    }

    public async Task<ServiceOrderResponse> AddItemAsync(Guid id, AddItemRequest request, CancellationToken ct = default)
    {
        var order = await repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Ordem de Serviço {id} não encontrada.");

        order.AddItem(request.ServiceName, request.Price, request.Quantity);
        await repository.UpdateAsync(order, ct);

        return MapToResponse(order);
    }

    public async Task<ServiceOrderResponse> StartDiagnosisAsync(Guid id, CancellationToken ct = default)
    {
        var order = await GetOrThrowAsync(id, ct);
        order.StartDiagnosis();
        await repository.UpdateAsync(order, ct);
        return MapToResponse(order);
    }

    /// <summary>
    /// Envia a OS para aprovação de orçamento.
    /// Publica OsSentForApprovalEvent com os itens diagnosticados —
    /// o BillingService consumirá esse evento para popular o Budget automaticamente.
    /// </summary>
    public async Task<ServiceOrderResponse> SendForApprovalAsync(Guid id, CancellationToken ct = default)
    {
        var order = await GetOrThrowAsync(id, ct);
        order.SendForApproval();
        await repository.UpdateAsync(order, ct);
        await PublishDomainEventsAsync(order, ct);
        return MapToResponse(order);
    }

    /// <summary>
    /// Endpoint HTTP não inicia a execução: isso só ocorre via BudgetApprovedEvent.
    /// Impede pular a aprovação do cliente enquanto a OS está em WaitingApproval.
    /// </summary>
    public async Task<ServiceOrderResponse> StartExecutionAsync(Guid id, CancellationToken ct = default)
    {
        await GetOrThrowAsync(id, ct);
        throw new InvalidOperationException(
            "A execução inicia automaticamente após a aprovação do orçamento pelo cliente (BudgetApprovedEvent). " +
            "Não é permitido iniciar a execução enquanto a OS aguarda essa aprovação.");
    }

    /// <summary>
    /// Finaliza o serviço (marca como concluído, aguardando entrega).
    /// No fluxo do Saga, será acionado pelo ExecutionCompletedEvent do ExecutionService.
    /// Pode ser chamado manualmente pelo Administrador como override.
    /// </summary>
    public async Task<ServiceOrderResponse> FinishAsync(Guid id, CancellationToken ct = default)
    {
        var order = await GetOrThrowAsync(id, ct);
        order.Finish();
        await repository.UpdateAsync(order, ct);
        await PublishDomainEventsAsync(order, ct);
        return MapToResponse(order);
    }

    /// <summary>
    /// Registra a entrega do veículo ao cliente.
    /// No fluxo do Saga, acionado pelo PaymentConfirmedEvent via PaymentConfirmedHandler.
    /// Publica OsClosedEvent.
    /// </summary>
    public async Task<ServiceOrderResponse> DeliverAsync(Guid id, CancellationToken ct = default)
    {
        var order = await GetOrThrowAsync(id, ct);
        order.Deliver();
        await repository.UpdateAsync(order, ct);
        await PublishDomainEventsAsync(order, ct);
        return MapToResponse(order);
    }

    public async Task<ServiceOrderResponse> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var order = await GetOrThrowAsync(id, ct);
        order.Cancel(reason);
        await repository.UpdateAsync(order, ct);
        await PublishDomainEventsAsync(order, ct);
        return MapToResponse(order);
    }

    // ── privados ──────────────────────────────────────────────────────────────

    private async Task<ServiceOrder> GetOrThrowAsync(Guid id, CancellationToken ct)
        => await repository.GetByIdAsync(id, ct)
           ?? throw new KeyNotFoundException($"Ordem de Serviço {id} não encontrada.");

    private async Task PublishDomainEventsAsync(ServiceOrder order, CancellationToken ct)
    {
        foreach (var @event in order.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);

        order.ClearDomainEvents();
    }

    private static ServiceOrderResponse MapToResponse(ServiceOrder order) => new(
        order.Id,
        order.CustomerName,
        order.CustomerPhone,
        order.VehiclePlate,
        order.VehicleModel,
        order.Description,
        order.Status.ToString(),
        order.CalculateTotal(),
        order.CreatedAt,
        order.UpdatedAt,
        order.History.Select(h => new ServiceOrderHistoryResponse(h.Status, h.Note, h.OccurredAt)),
        order.Items.Select(i => new ServiceOrderItemResponse(i.Id, i.ServiceName, i.Price, i.Quantity))
    );
}
