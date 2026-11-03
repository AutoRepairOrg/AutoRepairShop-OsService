using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Infrastructure.Persistence;

namespace OsService.Tests.Unit;

public class ServiceOrderRepositoryTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ServiceOrderRepositoryTests() => _connection.Open();

    private OsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OsDbContext>()
            .UseSqlite(_connection)
            .Options;

        var ctx = new OsDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetByStatusAsync_ShouldReturnMatchingOrders()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            await repo.AddAsync(ServiceOrder.Create("Ana", "11999999999", "ABC1D23", "Gol", "Revisão"));
            var other = ServiceOrder.Create("Bruno", "11888888888", "XYZ9Z99", "Uno", "Freio");
            other.StartDiagnosis();
            await repo.AddAsync(other);
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var received = (await repo.GetByStatusAsync(ServiceOrderStatus.Received)).ToList();
            var inDiagnosis = (await repo.GetByStatusAsync(ServiceOrderStatus.InDiagnosis)).ToList();

            received.Should().ContainSingle(o => o.CustomerName == "Ana");
            inDiagnosis.Should().ContainSingle(o => o.CustomerName == "Bruno");
        }
    }

    [Fact]
    public async Task GetByVehiclePlateAsync_ShouldReturnOrdersForPlate()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            await repo.AddAsync(ServiceOrder.Create("Ana", "11999999999", "ABC1D23", "Gol", "Revisão"));
            await repo.AddAsync(ServiceOrder.Create("Bruno", "11888888888", "XYZ9Z99", "Uno", "Freio"));
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var result = (await repo.GetByVehiclePlateAsync("abc1d23")).ToList();

            result.Should().ContainSingle();
            result[0].VehiclePlate.Should().Be("ABC1D23");
        }
    }
}
