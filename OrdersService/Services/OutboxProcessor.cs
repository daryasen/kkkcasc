using OrdersService.Data;
using RabbitMQ.Client;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace OrdersService.Services;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IConfiguration _configuration;

    public OutboxProcessor(IServiceProvider serviceProvider, ILogger<OutboxProcessor> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessages();
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessOutboxMessages()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var messages = await context.OutboxMessages
            .Where(m => !m.Processed)
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync();

        if (!messages.Any()) return;

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672")
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "payment_requests", durable: true, exclusive: false, autoDelete: false);

        foreach (var message in messages)
        {
            var body = Encoding.UTF8.GetBytes(message.Payload);
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;

            channel.BasicPublish(exchange: "", routingKey: "payment_requests", basicProperties: properties, body: body);

            message.Processed = true;
            message.ProcessedAt = DateTime.UtcNow;

            _logger.LogInformation($"Published message {message.Id} to RabbitMQ");
        }

        await context.SaveChangesAsync();
    }
}