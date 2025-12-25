using Microsoft.EntityFrameworkCore;
using PaymentsService.Data;
using PaymentsService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared;
using System.Text;
using System.Text.Json;

namespace PaymentsService.Services;

public class InboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxProcessor> _logger;
    private readonly IConfiguration _configuration;

    public InboxProcessor(IServiceProvider serviceProvider, ILogger<InboxProcessor> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inbox Processor started");

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            DispatchConsumersAsync = true
        };

        var connection = factory.CreateConnection();
        var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "payment_requests", durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var paymentRequest = JsonSerializer.Deserialize<PaymentRequestMessage>(message);

                if (paymentRequest != null)
                {
                    await ProcessPaymentRequest(paymentRequest);
                }

                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment request");
                channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: "payment_requests", autoAck: false, consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessPaymentRequest(PaymentRequestMessage request)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var existingInbox = await context.InboxMessages.FirstOrDefaultAsync(m => m.Id == request.MessageId);
        if (existingInbox != null && existingInbox.Processed)
        {
            _logger.LogInformation($"Message {request.MessageId} already processed");
            return;
        }

        var existingTransaction = await context.Transactions.FirstOrDefaultAsync(t => t.OrderId == request.OrderId);
        if (existingTransaction != null)
        {
            _logger.LogInformation($"Order {request.OrderId} already has transaction");

            await CreatePaymentResult(context, request.OrderId, true, "Already processed");

            if (existingInbox == null)
            {
                await context.InboxMessages.AddAsync(new InboxMessage
                {
                    Id = request.MessageId,
                    Type = "PaymentRequest",
                    Payload = JsonSerializer.Serialize(request),
                    Processed = true,
                    CreatedAt = DateTime.UtcNow,
                    ProcessedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingInbox.Processed = true;
                existingInbox.ProcessedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
            return;
        }

        var account = await context.Accounts.FirstOrDefaultAsync(a => a.UserId == request.UserId);

        bool success;
        string reason;

        if (account == null)
        {
            success = false;
            reason = "Account not found";
        }
        else if (account.Balance < request.Amount)
        {
            success = false;
            reason = "Insufficient funds";
        }
        else
        {
            account.Balance -= request.Amount;
            account.UpdatedAt = DateTime.UtcNow;

            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                OrderId = request.OrderId,
                Amount = request.Amount,
                Type = TransactionType.WITHDRAWAL,
                CreatedAt = DateTime.UtcNow
            };

            await context.Transactions.AddAsync(transaction);

            success = true;
            reason = "Payment successful";
        }

        await CreatePaymentResult(context, request.OrderId, success, reason);

        if (existingInbox == null)
        {
            await context.InboxMessages.AddAsync(new InboxMessage
            {
                Id = request.MessageId,
                Type = "PaymentRequest",
                Payload = JsonSerializer.Serialize(request),
                Processed = true,
                CreatedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow
            });
        }
        else
        {
            existingInbox.Processed = true;
            existingInbox.ProcessedAt = DateTime.UtcNow;
        }

        try
        {
            await context.SaveChangesAsync();
            _logger.LogInformation($"Payment for order {request.OrderId}: {(success ? "SUCCESS" : "FAILED")} - {reason}");
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning($"Concurrency conflict for user {request.UserId}");
            throw;
        }
    }

    private async Task CreatePaymentResult(PaymentsDbContext context, Guid orderId, bool success, string reason)
    {
        var resultMessage = new PaymentResultMessage
        {
            MessageId = Guid.NewGuid(),
            OrderId = orderId,
            Success = success,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "PaymentResult",
            Payload = JsonSerializer.Serialize(resultMessage),
            Processed = false,
            CreatedAt = DateTime.UtcNow
        };

        await context.OutboxMessages.AddAsync(outboxMessage);
    }
}