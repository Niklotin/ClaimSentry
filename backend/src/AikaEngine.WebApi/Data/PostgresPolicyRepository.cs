using AikaEngine.Application.Interfaces;
using AikaEngine.Domain.Entities;
using Dapper;
using Npgsql;

namespace AikaEngine.WebApi.Data;

public class PostgresPolicyRepository : IPolicyRepository
{
    private static readonly List<PolicyClause> FallbackClauses = new()
    {
        new PolicyClause
        {
            Id = Guid.NewGuid(),
            ClauseCode = "HOME_BIKE_01",
            Category = "BICYCLE",
            Title = "Polkupyörävarkaus ja vahinko (Koti)",
            CoverageDetails = "Korvaa lukitun polkupyörän varkauden tai äkillisen rikkoutumisen. Varkauksissa vaaditaan aina tehty rikosilmoitus poliisille. Ikävähennys 10% vuodessa toisesta vuodesta alkaen.",
            StandardDeductible = 150.00m,
            MaxCoverageLimit = 2500.00m,
            RequiresPoliceReport = true
        },
        new PolicyClause
        {
            Id = Guid.NewGuid(),
            ClauseCode = "HOME_ELEC_01",
            Category = "ELECTRONICS",
            Title = "Kodinelektroniikan rikkoutuminen",
            CoverageDetails = "Korvaa äkillisen ja ennalta-arvaamattoman älypuhelimen, kannettavan tai kodinkoneen rikkoutumisen. Ikävähennys 15% vuodessa toisesta vuodesta alkaen.",
            StandardDeductible = 150.00m,
            MaxCoverageLimit = 2000.00m,
            RequiresPoliceReport = false
        },
        new PolicyClause
        {
            Id = Guid.NewGuid(),
            ClauseCode = "TRAVEL_LUGG_01",
            Category = "LUGGAGE",
            Title = "Matkatavaravahinko ja rikkoutuminen",
            CoverageDetails = "Korvaa matkan aikana vaurioituneet tai kuljetuksessa rikkoutuneet matkatavarat.",
            StandardDeductible = 100.00m,
            MaxCoverageLimit = 3000.00m,
            RequiresPoliceReport = false
        },
        new PolicyClause
        {
            Id = Guid.NewGuid(),
            ClauseCode = "TRAVEL_LUGG_THEFT",
            Category = "LUGGAGE",
            Title = "Matkatavaravarkaus ulkomailla",
            CoverageDetails = "Korvaa matkalla anastetut matkatavarat. Varkaudesta on aina esitettävä paikallispoliisille tehty rikosilmoitus.",
            StandardDeductible = 100.00m,
            MaxCoverageLimit = 3000.00m,
            RequiresPoliceReport = true
        },
        new PolicyClause
        {
            Id = Guid.NewGuid(),
            ClauseCode = "HOME_LIAB_01",
            Category = "LIABILITY",
            Title = "Yksityishenkilön vastuuvahinko",
            CoverageDetails = "Korvaa toiselle aiheutetun esine- tai henkilövahingon, josta vakuutuksenottaja on lain mukaan korvausvastuussa.",
            StandardDeductible = 200.00m,
            MaxCoverageLimit = 50000.00m,
            RequiresPoliceReport = false
        }
    };

    private readonly IDatabaseStatusService _dbStatus;

    public PostgresPolicyRepository(IDatabaseStatusService dbStatus)
    {
        _dbStatus = dbStatus;
    }

    public async Task<IReadOnlyList<PolicyClause>> GetClausesAsync(string? category = null)
    {
        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                string sql = string.IsNullOrWhiteSpace(category)
                    ? "SELECT id, clause_code AS ClauseCode, category, title, coverage_details AS CoverageDetails, standard_deductible AS StandardDeductible, max_coverage_limit AS MaxCoverageLimit, requires_police_report AS RequiresPoliceReport FROM policy_clauses"
                    : "SELECT id, clause_code AS ClauseCode, category, title, coverage_details AS CoverageDetails, standard_deductible AS StandardDeductible, max_coverage_limit AS MaxCoverageLimit, requires_police_report AS RequiresPoliceReport FROM policy_clauses WHERE LOWER(category) = LOWER(@Category)";

                var rows = await connection.QueryAsync<PolicyClause>(sql, new { Category = category });
                var list = rows.ToList();
                if (list.Count > 0) return list;
            }
            catch
            {
                // Fall back to seed clauses if DB becomes offline
            }
        }

        return string.IsNullOrWhiteSpace(category)
            ? FallbackClauses
            : FallbackClauses.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<PolicyClause?> GetByClauseCodeAsync(string clauseCode)
    {
        if (_dbStatus.IsAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_dbStatus.ConnectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT id, clause_code AS ClauseCode, category, title, coverage_details AS CoverageDetails, 
                           standard_deductible AS StandardDeductible, max_coverage_limit AS MaxCoverageLimit, 
                           requires_police_report AS RequiresPoliceReport 
                    FROM policy_clauses 
                    WHERE UPPER(clause_code) = UPPER(@ClauseCode)
                    LIMIT 1";

                var result = await connection.QuerySingleOrDefaultAsync<PolicyClause>(sql, new { ClauseCode = clauseCode });
                if (result != null) return result;
            }
            catch
            {
                // Fall back
            }
        }

        return FallbackClauses.FirstOrDefault(c => c.ClauseCode.Equals(clauseCode, StringComparison.OrdinalIgnoreCase));
    }
}
