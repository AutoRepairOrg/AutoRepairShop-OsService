using OsService.Domain.Enums;

namespace OsService.Domain.Entities.ServiceOrder;

public class ServiceOrderHistory
{
    public Guid Id { get; private set; }
    public Guid ServiceOrderId { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public string Note { get; private set; }
    public DateTime OccurredAt { get; private set; }

#pragma warning disable CS8618
    protected ServiceOrderHistory() { }
#pragma warning restore CS8618

    public ServiceOrderHistory(Guid serviceOrderId, ServiceOrderStatus status, string note)
    {
        Id = Guid.NewGuid();
        ServiceOrderId = serviceOrderId;
        Status = status;
        Note = note;
        OccurredAt = DateTime.UtcNow;
    }
}
