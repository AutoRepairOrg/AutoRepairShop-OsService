namespace OsService.Domain.Entities.ServiceOrder;

public class ServiceOrderItem
{
    public Guid Id { get; private set; }
    public Guid ServiceOrderId { get; private set; }
    public string ServiceName { get; private set; }
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }

#pragma warning disable CS8618
    protected ServiceOrderItem() { }
#pragma warning restore CS8618

    public ServiceOrderItem(Guid serviceOrderId, string serviceName, decimal price, int quantity)
    {
        Id = Guid.NewGuid();
        ServiceOrderId = serviceOrderId;
        ServiceName = serviceName;
        Price = price;
        Quantity = quantity;
    }
}
