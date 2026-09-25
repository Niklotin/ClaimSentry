using AikaEngine.Application.Models;
using AikaEngine.Application.Services;
using AikaEngine.Domain.Entities;
using Xunit;

namespace AikaEngine.UnitTests;

public class FraudDetectionEngineTests
{
    private readonly FraudDetectionEngine _engine = new();

    private readonly List<FraudIndicator> _indicators = new()
    {
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "käteinen", RiskWeight = 0.20m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "ei kuittia", RiskWeight = 0.25m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "pimeä", RiskWeight = 0.50m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "KEYWORD", Value = "perintä", RiskWeight = 0.35m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "FLAGGED_CLAIMANT_FI123", RiskWeight = 0.75m },
        new FraudIndicator { Id = Guid.NewGuid(), IndicatorType = "CLAIMANT_FLAG", Value = "FI123", RiskWeight = 0.75m }
    };

    [Fact]
    public void Evaluate_CleanClaim_ReturnsLowRisk()
    {
        // Arrange
        var request = new FraudCheckRequest(
            ClaimantId: "CUST-LEGIT-101",
            IncidentDescription: "Polkupyöräni vietiin lukittuna pyörävarastosta eilen illalla.",
            ClaimedAmount: 400.00m
        );

        // Act
        var result = _engine.Evaluate(request, _indicators);

        // Assert
        Assert.False(result.IsHighRisk);
        Assert.Equal(0.00m, result.RiskScore);
        Assert.Empty(result.TriggeredFlags);
    }

    [Fact]
    public void Evaluate_SuspiciousKeyword_FlagsHighRiskWhenOverThreshold()
    {
        // Arrange: Contains 'perintä' (+0.35) -> exceeds 0.30 threshold
        var request = new FraudCheckRequest(
            ClaimantId: "CUST-REGULAR-202",
            IncidentDescription: "Tarvitsen korvauksen nopeasti, koska minulla on perintä päällä.",
            ClaimedAmount: 500.00m
        );

        // Act
        var result = _engine.Evaluate(request, _indicators);

        // Assert
        Assert.True(result.IsHighRisk);
        Assert.True(result.RiskScore >= 0.30m);
        Assert.Contains(result.TriggeredFlags, f => f.Contains("perintä"));
    }

    [Fact]
    public void Evaluate_MultipleKeywords_AccumulatesRiskProperly()
    {
        // Arrange: Contains 'käteinen' (0.20) + 'ei kuittia' (0.25) -> 0.45 risk
        var request = new FraudCheckRequest(
            ClaimantId: "CUST-UNKNOWN-303",
            IncidentDescription: "Ostin puhelimen ja maksoin käteinen kaupalla, joten ei kuittia ole tallella.",
            ClaimedAmount: 600.00m
        );

        // Act
        var result = _engine.Evaluate(request, _indicators);

        // Assert
        Assert.True(result.IsHighRisk);
        Assert.Equal(0.45m, result.RiskScore);
        Assert.Equal(2, result.TriggeredFlags.Count);
    }

    [Fact]
    public void Evaluate_FlaggedClaimant_TriggerHighRisk()
    {
        // Arrange: ClaimantId contains 'FI123' (+0.75)
        var request = new FraudCheckRequest(
            ClaimantId: "CUST-FI123-BAD",
            IncidentDescription: "Normaali vahinkoilmoitus.",
            ClaimedAmount: 300.00m
        );

        // Act
        var result = _engine.Evaluate(request, _indicators);

        // Assert
        Assert.True(result.IsHighRisk);
        Assert.True(result.RiskScore >= 0.75m);
        Assert.Contains(result.TriggeredFlags, f => f.Contains("FI123"));
    }

    [Fact]
    public void Evaluate_ExtremeAnomaly_CapsRiskScoreAtOne()
    {
        // Arrange: Contains all high risk keywords and flagged claimant
        var request = new FraudCheckRequest(
            ClaimantId: "FI123",
            IncidentDescription: "Ostin pimeä käteinen kaupalla, ei kuittia, perintä odottaa!",
            ClaimedAmount: 5000.00m
        );

        // Act
        var result = _engine.Evaluate(request, _indicators);

        // Assert
        Assert.True(result.IsHighRisk);
        Assert.Equal(1.00m, result.RiskScore);
    }
}
