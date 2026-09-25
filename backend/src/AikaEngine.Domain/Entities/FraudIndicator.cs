namespace AikaEngine.Domain.Entities;

public class FraudIndicator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IndicatorType { get; set; } = string.Empty; // 'KEYWORD', 'CLAIMANT_FLAG', 'HIGH_FREQUENCY'
    public string Value { get; set; } = string.Empty;
    public decimal RiskWeight { get; set; } = 0.10m;
}
