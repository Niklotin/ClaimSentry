using AikaEngine.Application.Models;
using AikaEngine.Domain.Entities;

namespace AikaEngine.Application.Services;

public interface IFraudDetectionEngine
{
    FraudCheckResponse Evaluate(FraudCheckRequest request, IEnumerable<FraudIndicator> indicators);
}

public class FraudDetectionEngine : IFraudDetectionEngine
{
    private const decimal HighRiskThreshold = 0.30m;

    public FraudCheckResponse Evaluate(FraudCheckRequest request, IEnumerable<FraudIndicator> indicators)
    {
        var triggeredFlags = new List<string>();
        decimal accumulatedRisk = 0.0m;

        string normalizedDescription = request.IncidentDescription.ToLowerInvariant();
        string normalizedClaimantId = request.ClaimantId.Trim().ToUpperInvariant();

        foreach (var indicator in indicators)
        {
            switch (indicator.IndicatorType.ToUpperInvariant())
            {
                case "KEYWORD":
                    if (normalizedDescription.Contains(indicator.Value.ToLowerInvariant()))
                    {
                        triggeredFlags.Add(FormattableString.Invariant($"Keyword flagged: '{indicator.Value}' (weight: +{indicator.RiskWeight:F2})"));
                        accumulatedRisk += indicator.RiskWeight;
                    }
                    break;

                case "CLAIMANT_FLAG":
                    if (normalizedClaimantId.Contains(indicator.Value.ToUpperInvariant()) || 
                        indicator.Value.ToUpperInvariant().Contains(normalizedClaimantId))
                    {
                        triggeredFlags.Add(FormattableString.Invariant($"Claimant flagged: '{indicator.Value}' (weight: +{indicator.RiskWeight:F2})"));
                        accumulatedRisk += indicator.RiskWeight;
                    }
                    break;

                case "HIGH_FREQUENCY":
                    // Used if claimant matches frequency watch
                    if (normalizedClaimantId.Contains("HIGH_FREQ") || normalizedClaimantId.Contains("FREQ"))
                    {
                        triggeredFlags.Add(FormattableString.Invariant($"High velocity claim frequency indicator: {indicator.Value} (weight: +{indicator.RiskWeight:F2})"));
                        accumulatedRisk += indicator.RiskWeight;
                    }
                    break;
            }
        }

        // Additional heuristic: unusually high single claim amount (> 3000 EUR)
        if (request.ClaimedAmount > 3000.00m)
        {
            decimal highAmountRisk = 0.20m;
            triggeredFlags.Add(FormattableString.Invariant($"High amount anomaly: {request.ClaimedAmount:F2} EUR > 3000 EUR threshold (weight: +{highAmountRisk:F2})"));
            accumulatedRisk += highAmountRisk;
        }

        // Clamp risk score to [0.00, 1.00]
        decimal finalRiskScore = Math.Min(1.00m, Math.Round(accumulatedRisk, 2));
        bool isHighRisk = finalRiskScore >= HighRiskThreshold;

        string explanation = isHighRisk
            ? FormattableString.Invariant($"High risk detected (Score: {finalRiskScore:F2} >= {HighRiskThreshold:F2}). Triggered flags: {string.Join("; ", triggeredFlags)}")
            : (triggeredFlags.Count > 0 
                ? FormattableString.Invariant($"Low/Medium risk detected (Score: {finalRiskScore:F2} < {HighRiskThreshold:F2}). Triggered flags: {string.Join("; ", triggeredFlags)}")
                : "Clean risk assessment (Score: 0.00). No indicators triggered.");

        return new FraudCheckResponse(
            RiskScore: finalRiskScore,
            IsHighRisk: isHighRisk,
            TriggeredFlags: triggeredFlags,
            RiskExplanation: explanation
        );
    }
}
