using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Infrastructure.Persistence;

namespace OsService.Tests.Unit;

/// <summary>
/// Reproduz o fluxo que gerava DbUpdateConcurrencyException no UpdateAsync.
/// </summary>
public class ServiceOrderRepositoryConcurrencyTests : IAsyncLifetime
{
    private readonly string _dbName = $"OsServiceConcurrency_{Guid.NewGuid():N}";

    private OsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OsDbContext>()
            .UseSqlServer(
                $"Server=(localdb)\\MSSQLLocalDB;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        var ctx = new OsDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public async Task DisposeAsync()
    {
        await using var ctx = CreateContext();
        await ctx.Database.EnsureDeletedAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpdateAsync_StartDiagnosis_ShouldPersistWithoutConcurrencyException()
    {
        Guid orderId;
        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var order = ServiceOrder.Create("Ana", "11999999999", "ABC1D23", "Gol", "Revisão");
            await repo.AddAsync(order);
            orderId = order.Id;
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var loaded = await repo.GetByIdAsync(orderId);
            loaded.Should().NotBeNull();
            loaded!.StartDiagnosis();

            var act = async () => await repo.UpdateAsync(loaded);
            await act.Should().NotThrowAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var reloaded = await repo.GetByIdAsync(orderId);
            reloaded!.Status.Should().Be(ServiceOrderStatus.InDiagnosis);
            reloaded.History.Should().HaveCountGreaterThan(1);
        }
    }

    [Fact]
    public async Task UpdateAsync_AddItem_ShouldPersistWithoutConcurrencyException()
    {
        Guid orderId;
        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var order = ServiceOrder.Create("Ana", "11999999999", "ABC1D23", "Gol", "Revisão");
            await repo.AddAsync(order);
            orderId = order.Id;
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var loaded = await repo.GetByIdAsync(orderId);
            loaded!.AddItem("Troca de óleo", 150m, 1);

            var act = async () => await repo.UpdateAsync(loaded);
            await act.Should().NotThrowAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var reloaded = await repo.GetByIdAsync(orderId);
            reloaded!.Items.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task UpdateAsync_FinishAndDeliver_ShouldPersistWithoutConcurrencyException()
    {
        Guid orderId;
        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var order = ServiceOrder.Create("Ana", "11999999999", "ABC1D23", "Gol", "Revisão");
            await repo.AddAsync(order);
            orderId = order.Id;
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var loaded = await repo.GetByIdAsync(orderId);
            loaded!.StartDiagnosis();
            loaded.SendForApproval();
            loaded.StartExecution();
            await repo.UpdateAsync(loaded);
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var forPayment = await repo.GetByIdAsync(orderId);
            forPayment!.Finish();
            forPayment.Deliver();

            var act = async () => await repo.UpdateAsync(forPayment);
            await act.Should().NotThrowAsync();
        }

        await using (var ctx = CreateContext())
        {
            var repo = new ServiceOrderRepository(ctx);
            var reloaded = await repo.GetByIdAsync(orderId);
            reloaded!.Status.Should().Be(ServiceOrderStatus.Delivered);
        }
    }
}
