using AikaEngine.Application.Models;
using AikaEngine.Domain.Entities;

namespace AikaEngine.Application.Services;

public interface IPayoutCalculator
{
    CalculatePayoutResponse Calculate(CalculatePayoutRequest request, PolicyClause clause);
}

public class PayoutCalculator : IPayoutCalculator
{
    public CalculatePayoutResponse Calculate(CalculatePayoutRequest request, PolicyClause clause)
    {
        if (request.ClaimedAmount <= 0)
        {
            return new CalculatePayoutResponse(
                EligibleAmount: 0m,
                DeductibleApplied: 0m,
                DepreciationApplied: 0m,
                MaxLimitApplied: clause.MaxCoverageLimit,
                RequiresPoliceReport: clause.RequiresPoliceReport,
                PoliceReportProvided: request.HasPoliceReport,
                CalculationBreakdown: "Claimed amount must be greater than 0.",
                IsEligible: false
            );
        }

        // Check mandatory police report
        if (clause.RequiresPoliceReport && !request.HasPoliceReport)
        {
            return new CalculatePayoutResponse(
                EligibleAmount: 0m,
                DeductibleApplied: 0m,
                DepreciationApplied: 0m,
                MaxLimitApplied: clause.MaxCoverageLimit,
                RequiresPoliceReport: true,
                PoliceReportProvided: false,
                CalculationBreakdown: $"Policy clause {clause.ClauseCode} requires a mandatory police report for theft. No report was provided. Claim cannot be approved.",
                IsEligible: false
            );
        }

        // Calculate depreciation:
        // After year 1: 10% per full year after year 1 (15% for ELECTRONICS), capped at 70%
        decimal depreciationRatePerYear = clause.Category.Equals("ELECTRONICS", StringComparison.OrdinalIgnoreCase) 
            ? 0.15m 
            : 0.10m;

        int depreciableYears = Math.Max(0, request.ItemAgeYears - 1);
        decimal totalDepreciationRate = Math.Min(0.70m, depreciableYears * depreciationRatePerYear);
        decimal depreciationApplied = Math.Round(request.ClaimedAmount * totalDepreciationRate, 2);

        decimal depreciatedValue = Math.Max(0m, request.ClaimedAmount - depreciationApplied);

        // Deductible
        decimal deductibleApplied = Math.Min(clause.StandardDeductible, depreciatedValue);
        decimal amountAfterDeductible = Math.Max(0m, depreciatedValue - deductibleApplied);

        // Max coverage limit cap
        decimal eligibleAmount = Math.Min(amountAfterDeductible, clause.MaxCoverageLimit);

        string breakdown = FormattableString.Invariant(
            $"Original Claim: {request.ClaimedAmount:F2} EUR | Depreciation ({totalDepreciationRate * 100:F0}% for {depreciableYears} yrs after yr 1): -{depreciationApplied:F2} EUR | Value after depreciation: {depreciatedValue:F2} EUR | Deductible: -{deductibleApplied:F2} EUR | Cap at limit ({clause.MaxCoverageLimit:F2} EUR): {eligibleAmount:F2} EUR | Net Eligible Payout: {eligibleAmount:F2} EUR");

        return new CalculatePayoutResponse(
            EligibleAmount: eligibleAmount,
            DeductibleApplied: deductibleApplied,
            DepreciationApplied: depreciationApplied,
            MaxLimitApplied: clause.MaxCoverageLimit,
            RequiresPoliceReport: clause.RequiresPoliceReport,
            PoliceReportProvided: request.HasPoliceReport,
            CalculationBreakdown: breakdown,
            IsEligible: eligibleAmount > 0
        );
    }
}
