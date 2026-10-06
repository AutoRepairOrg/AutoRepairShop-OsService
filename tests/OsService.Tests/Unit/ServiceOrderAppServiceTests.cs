using FluentAssertions;
using Moq;
using OsService.Application.DTOs;
using OsService.Application.Events;
using OsService.Application.Interfaces;
using OsService.Application.Services;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Events;
using OsService.Domain.Interfaces;

namespace OsService.Tests.Unit;

public class ServiceOrderAppServiceTests
{
    private readonly Mock<IServiceOrderRepository> _repo = new();
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly ServiceOrderAppService _sut;

    public ServiceOrderAppServiceTests()
    {
        _sut = new ServiceOrderAppService(_repo.Object, _publisher.Object);
    }

    private static ServiceOrder MakeOrder() =>
        ServiceOrder.Create("João Silva", "11999999999", "ABC1234", "Toyota Corolla");

    // ── CreateAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldPersistAndPublishEvent()
    {
        var request = new CreateServiceOrderRequest(
            "João Silva", "11999999999", "ABC1234", "Toyota Corolla", null);

        _repo.Setup(r => r.AddAsync(It.IsAny<ServiceOrder>(), default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.CustomerName.Should().Be("João Silva");
        result.Status.Should().Be(ServiceOrderStatus.Received.ToString());

        _repo.Verify(r => r.AddAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithMissingName_ShouldThrow()
    {
        var request = new CreateServiceOrderRequest("", "11999999999", "ABC1234", "Toyota", null);

        var act = async () => await _sut.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        _repo.Verify(r => r.AddAsync(It.IsAny<ServiceOrder>(), default), Times.Never);
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingOrder_ShouldReturnResponse()
    {
        var order = MakeOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);

        var result = await _sut.GetByIdAsync(order.Id);

        result.Id.Should().Be(order.Id);
        result.VehiclePlate.Should().Be("ABC1234");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ShouldThrowKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var act = async () => await _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── GetByStatusAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStatusAsync_ShouldReturnMappedList()
    {
        var orders = new List<ServiceOrder> { MakeOrder(), MakeOrder() };
        _repo.Setup(r => r.GetByStatusAsync(ServiceOrderStatus.Received, default)).ReturnsAsync(orders);

        var result = await _sut.GetByStatusAsync(ServiceOrderStatus.Received);

        result.Should().HaveCount(2);
        result.All(r => r.Status == ServiceOrderStatus.Received.ToString()).Should().BeTrue();
    }

    // ── AddItemAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddItemAsync_ShouldAddItemAndPersist()
    {
        var order = MakeOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var request = new AddItemRequest("Troca de óleo", 150m, 1);
        var result = await _sut.AddItemAsync(order.Id, request);

        result.Items.Should().HaveCount(1);
        result.Total.Should().Be(150m);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task AddItemAsync_OrderNotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((ServiceOrder?)null);

        var act = async () => await _sut.AddItemAsync(Guid.NewGuid(), new AddItemRequest("Serviço", 100m, 1));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── StartDiagnosisAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task StartDiagnosisAsync_FromReceived_ShouldTransitionAndPersist()
    {
        var order = MakeOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var result = await _sut.StartDiagnosisAsync(order.Id);

        result.Status.Should().Be(ServiceOrderStatus.InDiagnosis.ToString());
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ServiceOrder>(), default), Times.Once);
    }

    [Fact]
    public async Task StartDiagnosisAsync_InvalidTransition_ShouldThrow()
    {
        var order = MakeOrder();
        order.StartDiagnosis(); // já está em diagnóstico
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);

        var act = async () => await _sut.StartDiagnosisAsync(order.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── SendForApprovalAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task SendForApprovalAsync_FromInDiagnosis_ShouldTransition()
    {
        var order = MakeOrder();
        order.StartDiagnosis();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var result = await _sut.SendForApprovalAsync(order.Id);

        result.Status.Should().Be(ServiceOrderStatus.WaitingApproval.ToString());
    }

    // ── StartExecutionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task StartExecutionAsync_FromWaitingApproval_ShouldTransition()
    {
        var order = MakeOrder();
        order.StartDiagnosis();
        order.SendForApproval();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var result = await _sut.StartExecutionAsync(order.Id);

        result.Status.Should().Be(ServiceOrderStatus.InExecution.ToString());
    }

    // ── FinishAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FinishAsync_FromInExecution_ShouldTransition()
    {
        var order = MakeOrder();
        order.StartDiagnosis();
        order.SendForApproval();
        order.StartExecution();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);

        var result = await _sut.FinishAsync(order.Id);

        result.Status.Should().Be(ServiceOrderStatus.Finished.ToString());
    }

    // ── CancelAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_ShouldCancelAndPublishEvent()
    {
        var order = MakeOrder();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);
        _repo.Setup(r => r.UpdateAsync(order, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);
        order.ClearDomainEvents(); // limpa OsCreatedEvent da criação antes de cancelar

        var result = await _sut.CancelAsync(order.Id, "Cliente desistiu");

        result.Status.Should().Be(ServiceOrderStatus.Canceled.ToString());
        // CancelAsync publica o OsCancelledEvent (tipado como DomainEvent no foreach)
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_AlreadyDelivered_ShouldThrow()
    {
        var order = MakeOrder();
        order.StartDiagnosis();
        order.SendForApproval();
        order.StartExecution();
        order.Finish();
        order.Deliver();
        _repo.Setup(r => r.GetByIdAsync(order.Id, default)).ReturnsAsync(order);

        var act = async () => await _sut.CancelAsync(order.Id, "Motivo");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
