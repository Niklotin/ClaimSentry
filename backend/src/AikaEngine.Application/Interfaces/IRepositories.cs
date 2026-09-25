using AikaEngine.Domain.Entities;

namespace AikaEngine.Application.Interfaces;

public interface IPolicyRepository
{
    Task<IReadOnlyList<PolicyClause>> GetClausesAsync(string? category = null);
    Task<PolicyClause?> GetByClauseCodeAsync(string clauseCode);
}

public interface IFraudIndicatorRepository
{
    Task<IReadOnlyList<FraudIndicator>> GetAllAsync();
}

public interface IClaimAuditRepository
{
    Task<Guid> SaveAuditLogAsync(ClaimAuditLog auditLog);
    Task<ClaimAuditLog?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<ClaimAuditLog>> GetAllAuditLogsAsync(string? decisionFilter = null);
    Task<bool> UpdateAuditLogDecisionAsync(Guid id, string decision, decimal finalPayout, string humanNotes);
}
