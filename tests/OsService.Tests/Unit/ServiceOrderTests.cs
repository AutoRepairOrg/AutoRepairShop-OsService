using FluentAssertions;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Events;

namespace OsService.Tests.Unit;

public class ServiceOrderTests
{
    private static ServiceOrder CreateValidOrder() =>
        ServiceOrder.Create("João Silva", "11999999999", "ABC1234", "Toyota Corolla");

    // Criação 

    [Fact]
    public void Create_WithValidData_ShouldCreateWithReceivedStatus()
    {
        var order = CreateValidOrder();

        order.Status.Should().Be(ServiceOrderStatus.Received);
        order.Id.Should().NotBeEmpty();
        order.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Create_ShouldPublishOsCreatedEvent()
    {
        var order = CreateValidOrder();

        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OsCreatedEvent>();
    }

    [Theory]
    [InlineData("", "11999", "ABC1234", "Toyota")]
    [InlineData("João", "", "ABC1234", "Toyota")]
    [InlineData("João", "11999", "", "Toyota")]
    [InlineData("João", "11999", "ABC1234", "")]
    public void Create_WithMissingRequiredFields_ShouldThrow(
        string name, string phone, string plate, string model)
    {
        var act = () => ServiceOrder.Create(name, phone, plate, model);
        act.Should().Throw<ArgumentException>();
    }

    // Fluxo de status 

    [Fact]
    public void StartDiagnosis_FromReceived_ShouldTransition()
    {
        var order = CreateValidOrder();
        order.StartDiagnosis();
        order.Status.Should().Be(ServiceOrderStatus.InDiagnosis);
    }

    [Fact]
    public void StartDiagnosis_FromWrongStatus_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartDiagnosis();

        var act = order.StartDiagnosis;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FullHappyPath_ShouldTransitionAllStatuses()
    {
        var order = CreateValidOrder();

        order.StartDiagnosis();
        order.Status.Should().Be(ServiceOrderStatus.InDiagnosis);

        order.SendForApproval();
        order.Status.Should().Be(ServiceOrderStatus.WaitingApproval);

        order.StartExecution();
        order.Status.Should().Be(ServiceOrderStatus.InExecution);

        order.Finish();
        order.Status.Should().Be(ServiceOrderStatus.Finished);

        order.Deliver();
        order.Status.Should().Be(ServiceOrderStatus.Delivered);
    }

    // Cancelamento / Compensação 

    [Fact]
    public void Cancel_WithReason_ShouldPublishOsCancelledEvent()
    {
        var order = CreateValidOrder();
        order.ClearDomainEvents();

        order.Cancel("Orçamento rejeitado");

        order.Status.Should().Be(ServiceOrderStatus.Canceled);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OsCancelledEvent>();
    }

    [Fact]
    public void Cancel_AlreadyDelivered_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartDiagnosis();
        order.SendForApproval();
        order.StartExecution();
        order.Finish();
        order.Deliver();

        var act = () => order.Cancel("Arrependimento");
        act.Should().Throw<InvalidOperationException>();
    }

    // Itens 

    [Fact]
    public void AddItem_ShouldCalculateTotal()
    {
        var order = CreateValidOrder();
        order.AddItem("Troca de óleo", 150m, 1);
        order.AddItem("Filtro de ar", 80m, 2);

        order.CalculateTotal().Should().Be(310m); // 150 + (80 * 2)
    }

    [Fact]
    public void AddItem_WithNegativePrice_ShouldThrow()
    {
        var order = CreateValidOrder();
        var act = () => order.AddItem("Serviço", -10m, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Histórico 

    [Fact]
    public void Create_ShouldAddInitialHistoryEntry()
    {
        var order = CreateValidOrder();
        order.History.Should().HaveCount(1);
    }

    [Fact]
    public void EachStatusChange_ShouldAddHistoryEntry()
    {
        var order = CreateValidOrder(); // +1 história

        order.StartDiagnosis();        // +1
        order.SendForApproval();       // +1

        order.History.Should().HaveCount(3);
    }
}
