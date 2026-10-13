using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Infrastructure.Persistence;

public class ServiceOrderRepository(OsDbContext context) : IServiceOrderRepository
{
    public async Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("History")
            .Include("Items")
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IEnumerable<ServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("History")
            .Include("Items")
            .Where(o => o.Status == status)
            .ToListAsync(ct);

    public async Task<IEnumerable<ServiceOrder>> GetByVehiclePlateAsync(string plate, CancellationToken ct = default)
        => await context.ServiceOrders
            .Include("History")
            .Include("Items")
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
        // Filhos novos (Guid já preenchido): forçar Added.
        // Sem isso o EF pode tratá-los como Modified/Unchanged → UPDATE 0 rows → concurrency exception.
        foreach (var h in order.History)
        {
            var entry = context.Entry(h);
            if (entry.State is EntityState.Detached)
            {
                context.ServiceOrderHistories.Add(h);
                context.Entry(h).State = EntityState.Added;
            }
        }

        foreach (var i in order.Items)
        {
            var entry = context.Entry(i);
            if (entry.State is EntityState.Detached)
            {
                context.ServiceOrderItems.Add(i);
                context.Entry(i).State = EntityState.Added;
            }
        }

        // NÃO usar Entry(order).State = Modified nem Update(order):
        // isso marca o grafo inteiro e gera UPDATE em filhos novos (0 rows).
        // Status/UpdatedAt já estão tracked a partir do GetById — DetectChanges basta.
        await context.SaveChangesAsync(ct);
    }
}
