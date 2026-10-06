using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;

namespace OsService.Infrastructure.Persistence;

/// <summary>
/// DbContext exclusivo do OsService — banco próprio, sem acesso a outros serviços.
/// </summary>
public class OsDbContext(DbContextOptions<OsDbContext> options) : DbContext(options)
{
    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();
    public DbSet<ServiceOrderHistory> ServiceOrderHistories => Set<ServiceOrderHistory>();
    public DbSet<ServiceOrderItem> ServiceOrderItems => Set<ServiceOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CustomerPhone).IsRequired().HasMaxLength(20);
            entity.Property(e => e.VehiclePlate).IsRequired().HasMaxLength(10);
            entity.Property(e => e.VehicleModel).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Status).IsRequired();

            entity.HasMany(e => e.History)
                  .WithOne()
                  .HasForeignKey(h => h.ServiceOrderId);

            entity.HasMany(e => e.Items)
                  .WithOne()
                  .HasForeignKey(i => i.ServiceOrderId);

            // Ignorar eventos de domínio — não persistir
            entity.Ignore(e => e.DomainEvents);
        });

        modelBuilder.Entity<ServiceOrderHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).IsRequired().HasMaxLength(500);
        });

        modelBuilder.Entity<ServiceOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServiceName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
        });
    }
}
