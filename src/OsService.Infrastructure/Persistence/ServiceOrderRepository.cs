using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Infrastructure.Persistence;

public class ServiceOrderRepository(OsDbContext context) : IServiceOrderRepository
{
    public async Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("_history")
            .Include("_items")
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IEnumerable<ServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("_history")
            .Include("_items")
            .Where(o => o.Status == status)
            .ToListAsync(ct);

    public async Task<IEnumerable<ServiceOrder>> GetByVehiclePlateAsync(string plate, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("_history")
            .Include("_items")
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
        // Garante que novos itens/histórico adicionados ao aggregate sejam rastreados pelo EF Core.
        // IReadOnlyCollection não é detectado automaticamente pelo change tracker — adicionamos explicitamente.
        foreach (var h in order.History)
            if (context.Entry(h).State == EntityState.Detached)
                context.Add(h);

        foreach (var i in order.Items)
            if (context.Entry(i).State == EntityState.Detached)
                context.Add(i);

        await context.SaveChangesAsync(ct);
    }
}
