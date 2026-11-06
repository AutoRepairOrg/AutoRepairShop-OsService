using FluentAssertions;
using OsService.Application.DTOs;
using OsService.Application.Events;
using OsService.Application.Interfaces;
using OsService.Application.Services;
using OsService.Domain.Entities.ServiceOrder;
using OsService.Domain.Enums;
using OsService.Domain.Events;
using OsService.Domain.Interfaces;
using Reqnroll;

namespace OsService.Tests.BDD;

[Binding]
public class SagaDaOrdemSteps
{
    private readonly InMemoryOrders _orders = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly ServiceOrderAppService _ordersApp;
    private readonly BudgetApprovedHandler _budgetApproved;
    private readonly BudgetRejectedHandler _budgetRejected;
    private readonly ExecutionCompletedHandler _executionCompleted;
    private readonly ExecutionFailedHandler _executionFailed;
    private readonly PaymentConfirmedHandler _paymentConfirmed;
    private ServiceOrderResponse _order = null!;

    public SagaDaOrdemSteps()
    {
        _ordersApp = new ServiceOrderAppService(_orders, _publisher);
        _budgetApproved = new BudgetApprovedHandler(_orders, _publisher);
        _budgetRejected = new BudgetRejectedHandler(_orders);
        _executionCompleted = new ExecutionCompletedHandler(_orders);
        _executionFailed = new ExecutionFailedHandler(_orders, _publisher);
        _paymentConfirmed = new PaymentConfirmedHandler(_orders);
    }

    [Given(@"uma ordem recebida do cliente ""(.*)"" para o veículo ""(.*)""")]
    public async Task DadoOrdemRecebida(string cliente, string placa)
    {
        _order = await _ordersApp.CreateAsync(
            new CreateServiceOrderRequest(cliente, "11999999999", placa, "Corolla", null));
    }

    [When(@"o diagnóstico começa")]
    public async Task QuandoDiagnosticoComeca()
    {
        _order = await _ordersApp.StartDiagnosisAsync(_order.Id);
    }

    [When(@"o item ""(.*)"" no valor de (.*) é incluído")]
    public async Task QuandoItemIncluido(string servico, decimal valor)
    {
        _order = await _ordersApp.AddItemAsync(_order.Id, new AddItemRequest(servico, valor, 1));
    }

    [When(@"a ordem é enviada para aprovação")]
    public async Task QuandoEnviadaParaAprovacao()
    {
        _order = await _ordersApp.SendForApprovalAsync(_order.Id);
    }

    [When(@"o orçamento é aprovado")]
    public async Task QuandoOrcamentoAprovado()
    {
        await _budgetApproved.HandleAsync(_order.Id);
        _order = await _ordersApp.GetByIdAsync(_order.Id);
    }

    [When(@"o orçamento é recusado pelo motivo ""(.*)""")]
    public async Task QuandoOrcamentoRecusado(string motivo)
    {
        await _budgetRejected.HandleAsync(_order.Id, motivo);
        _order = await _ordersApp.GetByIdAsync(_order.Id);
    }

    [When(@"a execução na oficina é concluída")]
    public async Task QuandoExecucaoConcluida()
    {
        await _executionCompleted.HandleAsync(_order.Id);
        _order = await _ordersApp.GetByIdAsync(_order.Id);
    }

    [When(@"a execução falha pelo motivo ""(.*)""")]
    public async Task QuandoExecucaoFalha(string motivo)
    {
        await _executionFailed.HandleAsync(_order.Id, motivo);
        _order = await _ordersApp.GetByIdAsync(_order.Id);
    }

    [When(@"o pagamento é confirmado")]
    public async Task QuandoPagamentoConfirmado()
    {
        await _paymentConfirmed.HandleAsync(_order.Id);
        _order = await _ordersApp.GetByIdAsync(_order.Id);
    }

    [Then(@"o status da ordem é ""(.*)""")]
    public void EntaoStatus(string status)
    {
        _order.Status.Should().Be(status);
    }

    [Then(@"o evento ""(.*)"" foi publicado")]
    public void EntaoEventoPublicado(string nome)
    {
        _publisher.Events.Should().Contain(e => e.GetType().Name == nome);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<DomainEvent> Events { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : DomainEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryOrders : IServiceOrderRepository
    {
        private readonly Dictionary<Guid, ServiceOrder> _items = [];

        public Task AddAsync(ServiceOrder order, CancellationToken ct = default)
        {
            _items[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ServiceOrder order, CancellationToken ct = default)
        {
            _items[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<IEnumerable<ServiceOrder>> GetByStatusAsync(ServiceOrderStatus status, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.Where(o => o.Status == status).AsEnumerable());

        public Task<IEnumerable<ServiceOrder>> GetByVehiclePlateAsync(string plate, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.Where(o => o.VehiclePlate == plate.ToUpperInvariant()).AsEnumerable());
    }
}
