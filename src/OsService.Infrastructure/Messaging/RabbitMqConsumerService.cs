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
/// Consome eventos do BillingService (Saga coreografada).
/// Exchange: billing-service-events → Queue: os-service-billing-events
/// </summary>
public class RabbitMqConsumerService(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private const string ExchangeName = "billing-service-events";
    private const string QueueName = "os-service-billing-events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(QueueName, ExchangeName, "PaymentConfirmedEvent", cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, ExchangeName, "BudgetRejectedEvent", cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "OsService RabbitMQ consumer iniciado — queue {Queue}, exchange {Exchange}",
            QueueName, ExchangeName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown normal
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var routingKey = ea.RoutingKey;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            using var scope = scopeFactory.CreateScope();

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
}
