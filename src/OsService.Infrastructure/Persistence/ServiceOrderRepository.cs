using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Infrastructure.Persistence;

public class ServiceOrderRepository(OsDbContext context) : IServiceOrderRepository
{
    public async Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include(o => o.History)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IEnumerable<ServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include(o => o.History)
            .Include(o => o.Items)
            .Where(o => o.Status == status)
            .ToListAsync(ct);

    public async Task<IEnumerable<ServiceOrder>> GetByVehiclePlateAsync(string plate, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include(o => o.History)
            .Include(o => o.Items)
            .Where(o => o.VehiclePlate == plate.ToUpper())
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ServiceOrder order, CancellationToken ct = default)
    {
        await context.ServiceOrders.AddAsync(order, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ServiceOrder order, CancellationToken ct = default)
    {
        context.ServiceOrders.Update(order);
        await context.SaveChangesAsync(ct);
    }
}
