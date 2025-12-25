using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentsService.Data;
using PaymentsService.Models;

namespace PaymentsService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly PaymentsDbContext _context;

    public AccountsController(PaymentsDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAccount([FromHeader(Name = "X-User-Id")] string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        var existingAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (existingAccount != null)
            return Conflict("Account already exists");

        var account = new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Balance = 0,
            Version = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Accounts.AddAsync(account);
        await _context.SaveChangesAsync();

        return Ok(new { accountId = account.Id, balance = account.Balance });
    }

    [HttpPost("deposit")]
    public async Task<IActionResult> Deposit([FromHeader(Name = "X-User-Id")] string userId, [FromBody] DepositRequest request)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        if (request.Amount <= 0)
            return BadRequest("Amount must be positive");

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null)
            return NotFound("Account not found");

        account.Balance += request.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Amount = request.Amount,
            Type = TransactionType.DEPOSIT,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Transactions.AddAsync(transaction);
        await _context.SaveChangesAsync();

        return Ok(new { balance = account.Balance, transactionId = transaction.Id });
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance([FromHeader(Name = "X-User-Id")] string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return BadRequest("User ID is required");

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null)
            return NotFound("Account not found");

        return Ok(new { balance = account.Balance });
    }
}

public class DepositRequest
{
    public decimal Amount { get; set; }
}