namespace AikaEngine.Domain.Entities;

public class ClaimAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClaimId { get; set; } = string.Empty;
    public string ClaimantId { get; set; } = string.Empty;
    public decimal ClaimedAmount { get; set; }
    public decimal CalculatedPayout { get; set; }
    public string Decision { get; set; } = string.Empty; // 'AUTO_APPROVED', 'REJECTED', 'ESCALATED_HITL'
    public string? AiReasoning { get; set; }
    public string? HumanNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
