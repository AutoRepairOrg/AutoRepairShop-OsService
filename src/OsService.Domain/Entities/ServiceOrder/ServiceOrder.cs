using OsService.Domain.Enums;
using OsService.Domain.Events;

namespace OsService.Domain.Entities.ServiceOrder;

/// <summary>
/// Aggregate root da Ordem de Serviço.
/// Extraído do monolito AutoRepairShop-Api e adaptado para microsserviço independente.
/// </summary>
public class ServiceOrder
{
    public Guid Id { get; private set; }
    public string CustomerName { get; private set; }
    public string CustomerPhone { get; private set; }
    public string VehiclePlate { get; private set; }
    public string VehicleModel { get; private set; }
    public string? Description { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<ServiceOrderHistory> _history = [];
    private readonly List<ServiceOrderItem> _items = [];
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyCollection<ServiceOrderHistory> History => _history.AsReadOnly();
    public IReadOnlyCollection<ServiceOrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Construtor para EF Core
#pragma warning disable CS8618
    protected ServiceOrder() { }
#pragma warning restore CS8618

    private ServiceOrder(
        string customerName,
        string customerPhone,
        string vehiclePlate,
        string vehicleModel,
        string? description)
    {
        Id = Guid.NewGuid();
        CustomerName = customerName;
        CustomerPhone = customerPhone;
        VehiclePlate = vehiclePlate.ToUpper();
        VehicleModel = vehicleModel;
        Description = description;
        Status = ServiceOrderStatus.Received;
        CreatedAt = DateTime.UtcNow;

        AddHistory("OS criada e recebida.");
        AddDomainEvent(new OsCreatedEvent(Id, customerName, vehiclePlate));
    }

    public static ServiceOrder Create(
        string customerName,
        string customerPhone,
        string vehiclePlate,
        string vehicleModel,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPhone);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehiclePlate);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleModel);

        return new ServiceOrder(customerName, customerPhone, vehiclePlate, vehicleModel, description);
    }

    public void StartDiagnosis()
    {
        EnsureStatus(ServiceOrderStatus.Received, "Diagnóstico só pode iniciar a partir do status Received.");
        UpdateStatus(ServiceOrderStatus.InDiagnosis, "Diagnóstico iniciado.");
    }

    public void SendForApproval()
    {
        EnsureStatus(ServiceOrderStatus.InDiagnosis, "Aprovação só pode ser solicitada após o diagnóstico.");
        UpdateStatus(ServiceOrderStatus.WaitingApproval, "Orçamento gerado. Aguardando aprovação do cliente.");
    }

    public void StartExecution()
    {
        EnsureStatus(ServiceOrderStatus.WaitingApproval, "Execução só inicia após aprovação do orçamento.");
        UpdateStatus(ServiceOrderStatus.InExecution, "Execução do serviço iniciada.");
    }

    public void Finish()
    {
        EnsureStatus(ServiceOrderStatus.InExecution, "Finalização só possível durante execução.");
        UpdateStatus(ServiceOrderStatus.Finished, "Serviço concluído. Aguardando entrega.");
    }

    public void Deliver()
    {
        EnsureStatus(ServiceOrderStatus.Finished, "Entrega só possível após conclusão do serviço.");
        UpdateStatus(ServiceOrderStatus.Delivered, "Veículo entregue ao cliente.");
    }

    public void Cancel(string reason)
    {
        if (Status is ServiceOrderStatus.Delivered)
            throw new InvalidOperationException("Não é possível cancelar uma OS já entregue.");

        AddHistory($"OS cancelada. Motivo: {reason}");
        Status = ServiceOrderStatus.Canceled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new OsCancelledEvent(Id, reason));
    }

    public void AddItem(string serviceName, decimal price, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price), "Preço deve ser maior que zero.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantidade deve ser maior que zero.");

        _items.Add(new ServiceOrderItem(Id, serviceName, price, quantity));
    }

    public decimal CalculateTotal() => _items.Sum(i => i.Price * i.Quantity);

    public void ClearDomainEvents() => _domainEvents.Clear();

    // ── privados ──────────────────────────────────────────────────────────────

    private void EnsureStatus(ServiceOrderStatus expected, string message)
    {
        if (Status != expected)
            throw new InvalidOperationException(message);
    }

    private void UpdateStatus(ServiceOrderStatus newStatus, string historyNote)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
        AddHistory(historyNote);
    }

    private void AddHistory(string note) =>
        _history.Add(new ServiceOrderHistory(Id, Status, note));

    private void AddDomainEvent(DomainEvent @event) =>
        _domainEvents.Add(@event);
}
