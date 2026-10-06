using FluentAssertions;
using Moq;
using OsService.Application.Events;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Tests.Unit;

public class IncomingEventHandlerTests
{
    private readonly Mock<IServiceOrderRepository> _repo = new();

    private static ServiceOrder MakeOrderAt(ServiceOrderStatus target)
    {
        var order = ServiceOrder.Create("Maria", "11988887777", "XYZ9999", "Honda Civic");
        if (target >= ServiceOrderStatus.InDiagnosis) order.StartDiagnosis();
        if (target >= ServiceOrderStatus.WaitingApproval) order.SendForApproval();
        if (target >= ServiceOrderStatus.InExecution) order.StartExecution();
        if (target >= ServiceOrderStatus.Finished) order.Finish();
        return order;
    }

    // ── PaymentConfirmedHandler ──────────────────────────────────────────────

    [Fact]
    public async Task PaymentConfirmedHandler_ShouldFinishAndDeliverOrder()
    {
        var order = MakeOrderAt(ServiceOrderStatus.InExecution);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var handler = new PaymentConfirmedHandler(_repo.Object);
        await handler.HandleAsync(order.Id);

        order.Status.Should().Be(ServiceOrderStatus.Delivered);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task PaymentConfirmedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new PaymentConfirmedHandler(_repo.Object);
        var act = async () => await handler.HandleAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── BudgetRejectedHandler ────────────────────────────────────────────────

    [Fact]
    public async Task BudgetRejectedHandler_ShouldCancelOrderWithReason()
    {
        var order = MakeOrderAt(ServiceOrderStatus.WaitingApproval);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var handler = new BudgetRejectedHandler(_repo.Object);
        await handler.HandleAsync(order.Id, "Valor muito alto");

        order.Status.Should().Be(ServiceOrderStatus.Canceled);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task BudgetRejectedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new BudgetRejectedHandler(_repo.Object);
        var act = async () => await handler.HandleAsync(Guid.NewGuid(), "Motivo");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
