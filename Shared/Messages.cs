namespace Shared;

public class PaymentRequestMessage
{
    public Guid MessageId { get; set; }
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PaymentResultMessage
{
    public Guid MessageId { get; set; }
    public Guid OrderId { get; set; }
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}