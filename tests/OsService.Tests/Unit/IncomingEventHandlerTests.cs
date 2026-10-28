using FluentAssertions;
using Moq;
using OsService.Application.Events;
using OsService.Application.Interfaces;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Interfaces;

namespace OsService.Tests.Unit;

public class IncomingEventHandlerTests
{
    private readonly Mock<IServiceOrderRepository> _repo = new();
    private readonly Mock<IEventPublisher> _publisher = new();

    private static ServiceOrder MakeOrderAt(ServiceOrderStatus target)
    {
        var order = ServiceOrder.Create("Maria", "11988887777", "XYZ9999", "Honda Civic");
        if (target >= ServiceOrderStatus.InDiagnosis)
        {
            order.StartDiagnosis();
            // OsSentForApprovalEvent requer ao menos um item
            order.AddItem("Troca de óleo", 150m);
        }
        if (target >= ServiceOrderStatus.WaitingApproval) order.SendForApproval();
        // Limpa eventos pendentes para não interferir nas assertions
        order.ClearDomainEvents();
        if (target >= ServiceOrderStatus.InExecution) order.StartExecution();
        order.ClearDomainEvents();
        if (target >= ServiceOrderStatus.Finished) order.Finish();
        order.ClearDomainEvents();
        return order;
    }

    // ── PaymentConfirmedHandler ──────────────────────────────────────────────

    [Fact]
    public async Task PaymentConfirmedHandler_ShouldDeliverFinishedOrder()
    {
        var order = MakeOrderAt(ServiceOrderStatus.Finished);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<OsService.Domain.Events.DomainEvent>(), default))
                  .Returns(Task.CompletedTask);

        var handler = new PaymentConfirmedHandler(_repo.Object, _publisher.Object);
        await handler.HandleAsync(order.Id);

        order.Status.Should().Be(ServiceOrderStatus.Delivered);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task PaymentConfirmedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new PaymentConfirmedHandler(_repo.Object, _publisher.Object);
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

    // ── BudgetApprovedHandler ────────────────────────────────────────────────

    [Fact]
    public async Task BudgetApprovedHandler_ShouldStartExecution()
    {
        var order = MakeOrderAt(ServiceOrderStatus.WaitingApproval);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<OsService.Domain.Events.DomainEvent>(), default))
                  .Returns(Task.CompletedTask);

        var handler = new BudgetApprovedHandler(_repo.Object, _publisher.Object);
        await handler.HandleAsync(order.Id);

        order.Status.Should().Be(ServiceOrderStatus.InExecution);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
        // OsExecutionStartedEvent deve ter sido publicado
        _publisher.Verify(p => p.PublishAsync(It.IsAny<OsService.Domain.Events.DomainEvent>(), default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task BudgetApprovedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new BudgetApprovedHandler(_repo.Object, _publisher.Object);
        var act = async () => await handler.HandleAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── ExecutionCompletedHandler ────────────────────────────────────────────

    [Fact]
    public async Task ExecutionCompletedHandler_ShouldFinishOrder()
    {
        var order = MakeOrderAt(ServiceOrderStatus.InExecution);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<OsService.Domain.Events.DomainEvent>(), default))
                  .Returns(Task.CompletedTask);

        var handler = new ExecutionCompletedHandler(_repo.Object, _publisher.Object);
        await handler.HandleAsync(order.Id);

        order.Status.Should().Be(ServiceOrderStatus.Finished);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task ExecutionCompletedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new ExecutionCompletedHandler(_repo.Object, _publisher.Object);
        var act = async () => await handler.HandleAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── ExecutionFailedHandler ───────────────────────────────────────────────

    [Fact]
    public async Task ExecutionFailedHandler_ShouldCancelOsAndPublishOsCancelledEvent()
    {
        var order = MakeOrderAt(ServiceOrderStatus.InExecution);
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<OsService.Domain.Events.DomainEvent>(), default))
                  .Returns(Task.CompletedTask);

        var handler = new ExecutionFailedHandler(_repo.Object, _publisher.Object);
        await handler.HandleAsync(order.Id, "Peça indisponível");

        order.Status.Should().Be(ServiceOrderStatus.Canceled);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
        _publisher.Verify(p => p.PublishAsync(
            It.Is<OsService.Domain.Events.DomainEvent>(e =>
                e.GetType() == typeof(OsService.Domain.Events.OsCancelledEvent) &&
                ((OsService.Domain.Events.OsCancelledEvent)e).OsId == order.Id &&
                ((OsService.Domain.Events.OsCancelledEvent)e).Reason.Contains("Peça indisponível")),
            default), Times.Once);
    }

    [Fact]
    public async Task ExecutionFailedHandler_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var handler = new ExecutionFailedHandler(_repo.Object, _publisher.Object);
        var act = async () => await handler.HandleAsync(Guid.NewGuid(), "Motivo");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
