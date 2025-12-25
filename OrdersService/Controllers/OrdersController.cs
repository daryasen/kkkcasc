using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrdersService.Data;
using OrdersService.Models;
using Shared;
using System.Text.Json;

namespace OrdersService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrdersDbContext _context;

    public OrdersController(OrdersDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromHeader(Name = "X-User-Id")] string userId, [FromBody] CreateOrderRequest request)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Amount = request.Amount,
            Description = request.Description,
            Status = OrderStatus.NEW,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var paymentMessage = new PaymentRequestMessage
        {
            MessageId = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = userId,
            Amount = request.Amount,
            CreatedAt = DateTime.UtcNow
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "PaymentRequest",
            Payload = JsonSerializer.Serialize(paymentMessage),
            Processed = false,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Orders.AddAsync(order);
        await _context.OutboxMessages.AddAsync(outboxMessage);
        await _context.SaveChangesAsync();

        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromHeader(Name = "X-User-Id")] string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        var orders = await _context.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.Amount,
                o.Description,
                Status = o.Status.ToString(),
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrderStatus([FromHeader(Name = "X-User-Id")] string userId, Guid orderId)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        var order = await _context.Orders
            .Where(o => o.Id == orderId && o.UserId == userId)
            .FirstOrDefaultAsync();

        if (order == null)
            return NotFound();

        return Ok(new
        {
            order.Id,
            order.Amount,
            order.Description,
            Status = order.Status.ToString(),
            order.CreatedAt,
            order.UpdatedAt
        });
    }
}

public class CreateOrderRequest
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
}