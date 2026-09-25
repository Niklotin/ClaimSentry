using AikaEngine.Application.Interfaces;
using AikaEngine.Domain.Entities;
using Dapper;
using Npgsql;

namespace AikaEngine.WebApi.Data;

public class PostgresClaimAuditRepository : IClaimAuditRepository
{
    private readonly IDatabaseStatusService _dbStatus;
    private static readonly List<ClaimAuditLog> InMemAuditLogs = new();

    public PostgresClaimAuditRepository(IDatabaseStatusService dbStatus)
    {
        _dbStatus = dbStatus;
    }

    public async Task<Guid> SaveAuditLogAsync(ClaimAuditLog auditLog)
    {
        if (auditLog.Id == Guid.Empty)
        {
            auditLog.Id = Guid.NewGuid();
        }

        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                const string sql = @"
                    INSERT INTO claim_audit_log (id, claim_id, claimant_id, claimed_amount, calculated_payout, decision, ai_reasoning, human_notes, created_at)
                    VALUES (@Id, @ClaimId, @ClaimantId, @ClaimedAmount, @CalculatedPayout, @Decision, @AiReasoning, @HumanNotes, @CreatedAt)";

                await connection.ExecuteAsync(sql, auditLog);
            }
            catch
            {
                // Fall back
            }
        }

        lock (InMemAuditLogs)
        {
            InMemAuditLogs.Add(auditLog);
        }

        return auditLog.Id;
    }

    public async Task<ClaimAuditLog?> GetByIdAsync(Guid id)
    {
        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT id, claim_id AS ClaimId, claimant_id AS ClaimantId, claimed_amount AS ClaimedAmount,
                           calculated_payout AS CalculatedPayout, decision AS Decision, ai_reasoning AS AiReasoning,
                           human_notes AS HumanNotes, created_at AS CreatedAt
                    FROM claim_audit_log
                    WHERE id = @Id";

                var result = await connection.QuerySingleOrDefaultAsync<ClaimAuditLog>(sql, new { Id = id });
                if (result != null) return result;
            }
            catch
            {
                // Fall back
            }
        }

        lock (InMemAuditLogs)
        {
            return InMemAuditLogs.FirstOrDefault(l => l.Id == id);
        }
    }

    public async Task<IReadOnlyList<ClaimAuditLog>> GetAllAuditLogsAsync(string? decisionFilter = null)
    {
        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                string sql = string.IsNullOrWhiteSpace(decisionFilter)
                    ? @"SELECT id, claim_id AS ClaimId, claimant_id AS ClaimantId, claimed_amount AS ClaimedAmount,
                               calculated_payout AS CalculatedPayout, decision AS Decision, ai_reasoning AS AiReasoning,
                               human_notes AS HumanNotes, created_at AS CreatedAt
                        FROM claim_audit_log
                        ORDER BY created_at DESC"
                    : @"SELECT id, claim_id AS ClaimId, claimant_id AS ClaimantId, claimed_amount AS ClaimedAmount,
                               calculated_payout AS CalculatedPayout, decision AS Decision, ai_reasoning AS AiReasoning,
                               human_notes AS HumanNotes, created_at AS CreatedAt
                        FROM claim_audit_log
                        WHERE UPPER(decision) = UPPER(@DecisionFilter)
                        ORDER BY created_at DESC";

                var rows = await connection.QueryAsync<ClaimAuditLog>(sql, new { DecisionFilter = decisionFilter });
                var list = rows.ToList();
                if (list.Count > 0) return list;
            }
            catch
            {
                // Fall back
            }
        }

        lock (InMemAuditLogs)
        {
            var query = InMemAuditLogs.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(decisionFilter))
            {
                query = query.Where(l => l.Decision.Equals(decisionFilter, StringComparison.OrdinalIgnoreCase));
            }
            return query.OrderByDescending(l => l.CreatedAt).ToList();
        }
    }

    public async Task<bool> UpdateAuditLogDecisionAsync(Guid id, string decision, decimal finalPayout, string humanNotes)
    {
        bool updated = false;

        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                const string sql = @"
                    UPDATE claim_audit_log
                    SET decision = @Decision,
                        calculated_payout = @FinalPayout,
                        human_notes = @HumanNotes
                    WHERE id = @Id";

                int affected = await connection.ExecuteAsync(sql, new { Id = id, Decision = decision, FinalPayout = finalPayout, HumanNotes = humanNotes });
                if (affected > 0) updated = true;
            }
            catch
            {
                // Fall back
            }
        }

        lock (InMemAuditLogs)
        {
            var entry = InMemAuditLogs.FirstOrDefault(l => l.Id == id);
            if (entry != null)
            {
                entry.Decision = decision;
                entry.CalculatedPayout = finalPayout;
                entry.HumanNotes = humanNotes;
                updated = true;
            }
        }

        return updated;
    }
}
