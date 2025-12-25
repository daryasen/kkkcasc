using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using OrdersService.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared;
using System.Text;
using System.Text.Json;

namespace OrdersService.Services;

public class OrderStatusUpdater : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderStatusUpdater> _logger;
    private readonly IConfiguration _configuration;

    public OrderStatusUpdater(IServiceProvider serviceProvider, ILogger<OrderStatusUpdater> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order Status Updater started");

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            DispatchConsumersAsync = true
        };

        var connection = factory.CreateConnection();
        var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "payment_results", durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var paymentResult = JsonSerializer.Deserialize<PaymentResultMessage>(message);

                if (paymentResult != null)
                {
                    await UpdateOrderStatus(paymentResult);
                }

                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment result");
                channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        channel.BasicConsume(queue: "payment_results", autoAck: false, consumer: consumer);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task UpdateOrderStatus(PaymentResultMessage result)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == result.OrderId);
        if (order == null)
        {
            _logger.LogWarning($"Order {result.OrderId} not found");
            return;
        }

        order.Status = result.Success ? OrderStatus.FINISHED : OrderStatus.CANCELLED;
        order.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        _logger.LogInformation($"Order {result.OrderId} status updated to {order.Status}");
    }
}