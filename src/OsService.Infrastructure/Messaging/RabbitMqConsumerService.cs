using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OsService.Application.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OsService.Infrastructure.Messaging;

/// <summary>
/// Consome eventos do BillingService e do ExecutionService (Saga coreografada).
///
/// Exchanges / filas:
///   billing-service-events     → os-service-billing-events
///     PaymentConfirmedEvent, BudgetRejectedEvent, BudgetApprovedEvent
///   execution-service-events   → os-service-execution-events
///     ExecutionCompletedEvent, ExecutionFailedEvent
/// </summary>
public class RabbitMqConsumerService(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private const string BillingExchange = "billing-service-events";
    private const string BillingQueue = "os-service-billing-events";
    private const string ExecExchange = "execution-service-events";
    private const string ExecQueue = "os-service-execution-events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareAndBindAsync(
            BillingExchange, BillingQueue,
            ["PaymentConfirmedEvent", "BudgetRejectedEvent", "BudgetApprovedEvent"],
            stoppingToken);

        await DeclareAndBindAsync(
            ExecExchange, ExecQueue,
            ["ExecutionCompletedEvent", "ExecutionFailedEvent"],
            stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(BillingQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        await _channel.BasicConsumeAsync(ExecQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation(
            "OsService RabbitMQ consumer iniciado — queues {BillingQueue}, {ExecQueue}",
            BillingQueue, ExecQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown normal
        }
    }

    private async Task DeclareAndBindAsync(
        string exchange, string queue, string[] routingKeys, CancellationToken ct)
    {
        await _channel!.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        foreach (var key in routingKeys)
            await _channel.QueueBindAsync(queue, exchange, key, cancellationToken: ct);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var routingKey = ea.RoutingKey;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            switch (routingKey)
            {
                case "PaymentConfirmedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingPaymentConfirmed>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload PaymentConfirmedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<PaymentConfirmedHandler>();
                    await handler.HandleAsync(msg.OsId);
                    break;
                }
                case "BudgetRejectedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingBudgetRejected>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload BudgetRejectedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<BudgetRejectedHandler>();
                    await handler.HandleAsync(msg.OsId, msg.Reason);
                    break;
                }
                case "BudgetApprovedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingBudgetApproved>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload BudgetApprovedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<BudgetApprovedHandler>();
                    await handler.HandleAsync(msg.OsId);
                    break;
                }
                case "ExecutionCompletedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingExecutionCompleted>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload ExecutionCompletedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<ExecutionCompletedHandler>();
                    await handler.HandleAsync(msg.OsId);
                    break;
                }
                case "ExecutionFailedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingExecutionFailed>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload ExecutionFailedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<ExecutionFailedHandler>();
                    await handler.HandleAsync(msg.OsId, msg.Reason);
                    break;
                }
                default:
                    logger.LogWarning("Routing key ignorada: {RoutingKey}", routingKey);
                    break;
            }

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            logger.LogInformation("Evento {RoutingKey} processado com sucesso", routingKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao processar {RoutingKey}: {Body}", routingKey, body);
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.DisposeAsync();

        await base.StopAsync(cancellationToken);
    }

    // DTOs mínimos para deserializar eventos publicados pelo BillingService (sem acoplar os Domain projects)
    private sealed record IncomingPaymentConfirmed(Guid OsId);
    private sealed record IncomingBudgetRejected(Guid OsId, string Reason);
    private sealed record IncomingBudgetApproved(Guid OsId);
    private sealed record IncomingExecutionCompleted(Guid OsId);
    private sealed record IncomingExecutionFailed(Guid OsId, string Reason);
}
