using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;

namespace OsService.Domain.Interfaces;

public interface IServiceOrderRepository
{
    Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<ServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default);
    Task<IEnumerable<ServiceOrder>> GetByVehiclePlateAsync(string plate, CancellationToken ct = default);
    Task AddAsync(ServiceOrder order, CancellationToken ct = default);
    Task UpdateAsync(ServiceOrder order, CancellationToken ct = default);
}
