namespace PaymentsService.Models;

public class Transaction
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum TransactionType
{
    DEPOSIT,
    WITHDRAWAL
}