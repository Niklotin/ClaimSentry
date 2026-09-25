using AikaEngine.Application.Models;
using AikaEngine.Application.Services;
using AikaEngine.Domain.Entities;
using Xunit;

namespace AikaEngine.UnitTests;

public class PayoutCalculatorTests
{
    private readonly PayoutCalculator _calculator = new();

    private readonly PolicyClause _bikeClause = new()
    {
        Id = Guid.NewGuid(),
        ClauseCode = "HOME_BIKE_01",
        Category = "BICYCLE",
        Title = "Polkupyörävarkaus",
        CoverageDetails = "Lukitun pyörän varkaus, vaatii rikosilmoituksen.",
        StandardDeductible = 150.00m,
        MaxCoverageLimit = 2500.00m,
        RequiresPoliceReport = true
    };

    private readonly PolicyClause _electronicsClause = new()
    {
        Id = Guid.NewGuid(),
        ClauseCode = "HOME_ELEC_01",
        Category = "ELECTRONICS",
        Title = "Elektroniikan rikkoutuminen",
        CoverageDetails = "Kodinelektroniikan rikkoutuminen.",
        StandardDeductible = 150.00m,
        MaxCoverageLimit = 2000.00m,
        RequiresPoliceReport = false
    };

    [Fact]
    public void Calculate_SpecExample_ReturnsExactExpectedPayout()
    {
        // Arrange: 800 claimed, 2 yrs old (10% dep = 80), deductible 150 -> 570 EUR net
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 800.00m,
            ClauseCode: "HOME_BIKE_01",
            ItemAgeYears: 2,
            HasPoliceReport: true
        );

        // Act
        var result = _calculator.Calculate(request, _bikeClause);

        // Assert
        Assert.True(result.IsEligible);
        Assert.Equal(570.00m, result.EligibleAmount);
        Assert.Equal(150.00m, result.DeductibleApplied);
        Assert.Equal(80.00m, result.DepreciationApplied);
        Assert.True(result.PoliceReportProvided);
        Assert.Contains("570.00 EUR", result.CalculationBreakdown);
    }

    [Fact]
    public void Calculate_MissingMandatoryPoliceReport_RejectsPayout()
    {
        // Arrange: Bike theft requires police report, but claimant has none
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 800.00m,
            ClauseCode: "HOME_BIKE_01",
            ItemAgeYears: 2,
            HasPoliceReport: false
        );

        // Act
        var result = _calculator.Calculate(request, _bikeClause);

        // Assert
        Assert.False(result.IsEligible);
        Assert.Equal(0m, result.EligibleAmount);
        Assert.True(result.RequiresPoliceReport);
        Assert.False(result.PoliceReportProvided);
        Assert.Contains("requires a mandatory police report", result.CalculationBreakdown);
    }

    [Fact]
    public void Calculate_BrandNewItem_NoDepreciationApplied()
    {
        // Arrange: 1 year old item (within 1st year) -> 0% depreciation
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 450.00m,
            ClauseCode: "HOME_BIKE_01",
            ItemAgeYears: 1,
            HasPoliceReport: true
        );

        // Act
        var result = _calculator.Calculate(request, _bikeClause);

        // Assert: 450 - 0 dep - 150 deductible = 300 EUR
        Assert.True(result.IsEligible);
        Assert.Equal(300.00m, result.EligibleAmount);
        Assert.Equal(0m, result.DepreciationApplied);
        Assert.Equal(150.00m, result.DeductibleApplied);
    }

    [Fact]
    public void Calculate_ElectronicsCategory_Applies15PercentDepreciation()
    {
        // Arrange: Electronics item, 3 years old -> 2 years after yr 1 -> 30% dep
        // Claimed: 1000 EUR -> Dep: 300 EUR -> Remaining: 700 -> Deductible 150 -> 550 EUR
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 1000.00m,
            ClauseCode: "HOME_ELEC_01",
            ItemAgeYears: 3,
            HasPoliceReport: false
        );

        // Act
        var result = _calculator.Calculate(request, _electronicsClause);

        // Assert
        Assert.True(result.IsEligible);
        Assert.Equal(300.00m, result.DepreciationApplied);
        Assert.Equal(150.00m, result.DeductibleApplied);
        Assert.Equal(550.00m, result.EligibleAmount);
    }

    [Fact]
    public void Calculate_HighClaimExceedingCoverageCap_CappedAtLimit()
    {
        // Arrange: Claimed 4000 EUR for electronics, cap is 2000 EUR
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 4000.00m,
            ClauseCode: "HOME_ELEC_01",
            ItemAgeYears: 1,
            HasPoliceReport: false
        );

        // Act
        var result = _calculator.Calculate(request, _electronicsClause);

        // Assert: 4000 - 150 = 3850 -> capped at 2000 EUR
        Assert.True(result.IsEligible);
        Assert.Equal(2000.00m, result.EligibleAmount);
    }

    [Fact]
    public void Calculate_ClaimBelowDeductible_ReturnsZeroPayout()
    {
        // Arrange: Claimed 120 EUR, deductible is 150 EUR
        var request = new CalculatePayoutRequest(
            ClaimedAmount: 120.00m,
            ClauseCode: "HOME_BIKE_01",
            ItemAgeYears: 1,
            HasPoliceReport: true
        );

        // Act
        var result = _calculator.Calculate(request, _bikeClause);

        // Assert
        Assert.False(result.IsEligible);
        Assert.Equal(0m, result.EligibleAmount);
        Assert.Equal(120.00m, result.DeductibleApplied);
    }
}
