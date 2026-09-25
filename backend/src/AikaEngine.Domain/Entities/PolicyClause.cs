namespace AikaEngine.Domain.Entities;

public class PolicyClause
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClauseCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CoverageDetails { get; set; } = string.Empty;
    public decimal StandardDeductible { get; set; } = 150.00m;
    public decimal MaxCoverageLimit { get; set; } = 2000.00m;
    public bool RequiresPoliceReport { get; set; } = false;
}
