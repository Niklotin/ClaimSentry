namespace AikaEngine.Application.Models;

public record CalculatePayoutRequest(
    decimal ClaimedAmount,
    string ClauseCode,
    int ItemAgeYears,
    bool HasPoliceReport
);

public record CalculatePayoutResponse(
    decimal EligibleAmount,
    decimal DeductibleApplied,
    decimal DepreciationApplied,
    decimal MaxLimitApplied,
    bool RequiresPoliceReport,
    bool PoliceReportProvided,
    string CalculationBreakdown,
    bool IsEligible
);

public record FraudCheckRequest(
    string ClaimantId,
    string IncidentDescription,
    decimal ClaimedAmount
);

public record FraudCheckResponse(
    decimal RiskScore,
    bool IsHighRisk,
    List<string> TriggeredFlags,
    string RiskExplanation
);

public record DecisionCallbackRequest(
    string ClaimId,
    string ClaimantId,
    decimal ClaimedAmount,
    decimal CalculatedPayout,
    string Decision,
    string? AiReasoning,
    string? HumanNotes
);

public record DecisionCallbackResponse(
    Guid AuditId,
    string Status,
    DateTimeOffset Timestamp
);

public record HitlReviewRequest(
    Guid AuditId,
    string Decision,
    decimal FinalPayout,
    string HumanNotes
);

public record HitlReviewResponse(
    Guid AuditId,
    string Decision,
    decimal FinalPayout,
    string Status,
    DateTimeOffset UpdatedAt
);
