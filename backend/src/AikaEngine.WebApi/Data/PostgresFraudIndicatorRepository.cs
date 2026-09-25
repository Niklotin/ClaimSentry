using AikaEngine.Application.Interfaces;
using AikaEngine.Domain.Entities;
using Dapper;
using Npgsql;

namespace AikaEngine.WebApi.Data;

public class PostgresFraudIndicatorRepository : IFraudIndicatorRepository
{
    private static readonly List<FraudIndicator> FallbackIndicators = new()
    {
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "käteinen", RiskWeight = 0.20m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "ei kuittia", RiskWeight = 0.25m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "kadonnut kuitti", RiskWeight = 0.25m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "perintä", RiskWeight = 0.35m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "pimeä", RiskWeight = 0.50m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "urgently need cash", RiskWeight = 0.30m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "no receipt", RiskWeight = 0.25m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "BLACKLISTED_FRAUD_HISTORY", RiskWeight = 0.95m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "FLAGGED_CLAIMANT_FI123", RiskWeight = 0.75m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "FI123", RiskWeight = 0.75m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "HIGH_FREQUENCY_30D", RiskWeight = 0.40m }
    };

    private readonly IDatabaseStatusService _dbStatus;

    public PostgresFraudIndicatorRepository(IDatabaseStatusService dbStatus)
    {
        _dbStatus = dbStatus;
    }

    public async Task<IReadOnlyList<FraudIndicator>> GetAllAsync()
    {
        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                const string sql = "SELECT id, indicator_type AS IndicatorType, value, risk_weight AS RiskWeight FROM fraud_indicators";
                var rows = await connection.QueryAsync<FraudIndicator>(sql);
                var list = rows.ToList();
                if (list.Count > 0) return list;
            }
            catch
            {
                // Fall back
            }
        }

        return FallbackIndicators;
    }
}
