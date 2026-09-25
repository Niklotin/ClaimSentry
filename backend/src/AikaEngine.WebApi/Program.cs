using System.Text.Json;
using AikaEngine.Application.Interfaces;
using AikaEngine.Application.Models;
using AikaEngine.Application.Services;
using AikaEngine.Domain.Entities;
using AikaEngine.WebApi.Data;
using AikaEngine.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// JSON formatting: snake_case support for n8n AI Agent and REST integration
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "AIKA Claims & Deterministic Rules Engine",
        Version = "v1",
        Description = "Deterministic financial calculations, coverage evaluations, and fraud risk scoring for ClaimSentry Claims Triaging Platform."
    });
});

// Dependency Injection
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IDatabaseStatusService, DatabaseStatusService>();
builder.Services.AddSingleton<IPolicyRepository, PostgresPolicyRepository>();
builder.Services.AddSingleton<IFraudIndicatorRepository, PostgresFraudIndicatorRepository>();
builder.Services.AddSingleton<IClaimAuditRepository, PostgresClaimAuditRepository>();
builder.Services.AddSingleton<IPayoutCalculator, PayoutCalculator>();
builder.Services.AddSingleton<IFraudDetectionEngine, FraudDetectionEngine>();
builder.Services.AddSingleton<IOpenAiAgentService, OpenAiAgentService>();

// CORS to allow n8n and frontend calls
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AIKA Engine API v1");
    c.RoutePrefix = "swagger";
});

// Root and dashboard routes
app.MapGet("/", () => Results.Redirect("/dashboard"));
app.MapGet("/dashboard", () => Results.Redirect("/index.html"));

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow }))
   .WithName("HealthCheck")
   .WithSummary("System health check")
   .WithTags("System");

// 1. GET /tools/policy-clauses?category=...
app.MapGet("/tools/policy-clauses", async (
    [FromQuery] string? category, 
    IPolicyRepository policyRepo) =>
{
    var clauses = await policyRepo.GetClausesAsync(category);
    return Results.Ok(clauses);
})
.WithName("GetPolicyClauses")
.WithSummary("Retrieve coverage details, deductibles and limits for a category")
.WithTags("Tools");

// 2. POST /tools/calculate-payout
app.MapPost("/tools/calculate-payout", async (
    [FromBody] CalculatePayoutRequest request,
    IPolicyRepository policyRepo,
    IPayoutCalculator calculator) =>
{
    var clause = await policyRepo.GetByClauseCodeAsync(request.ClauseCode);
    if (clause == null)
    {
        return Results.NotFound(new { error = $"Policy clause '{request.ClauseCode}' not found." });
    }

    var result = calculator.Calculate(request, clause);
    return Results.Ok(result);
})
.WithName("CalculatePayout")
.WithSummary("Deterministic calculation of eligible payout, depreciation, and deductibles")
.WithTags("Tools");

// 3. POST /tools/fraud-check
app.MapPost("/tools/fraud-check", async (
    [FromBody] FraudCheckRequest request,
    IFraudIndicatorRepository fraudRepo,
    IFraudDetectionEngine fraudEngine) =>
{
    var indicators = await fraudRepo.GetAllAsync();
    var result = fraudEngine.Evaluate(request, indicators);
    return Results.Ok(result);
})
.WithName("FraudCheck")
.WithSummary("Evaluates fraud indicators, suspicious keywords, and history")
.WithTags("Tools");

// 4. POST /api/claims/decision-callback
app.MapPost("/api/claims/decision-callback", async (
    [FromBody] DecisionCallbackRequest request,
    IClaimAuditRepository auditRepo) =>
{
    var auditEntry = new ClaimAuditLog
    {
        Id = Guid.NewGuid(),
        ClaimId = request.ClaimId,
        ClaimantId = request.ClaimantId,
        ClaimedAmount = request.ClaimedAmount,
        CalculatedPayout = request.CalculatedPayout,
        Decision = request.Decision,
        AiReasoning = request.AiReasoning,
        HumanNotes = request.HumanNotes,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var id = await auditRepo.SaveAuditLogAsync(auditEntry);

    return Results.Ok(new DecisionCallbackResponse(
        AuditId: id,
        Status: "RECORDED",
        Timestamp: auditEntry.CreatedAt
    ));
})
.WithName("RecordDecisionCallback")
.WithSummary("Audit logging and final decision callback")
.WithTags("Claims");

// Audit retrieval endpoints
app.MapGet("/api/claims/audit-log", async (
    [FromQuery] string? decision,
    IClaimAuditRepository auditRepo) =>
{
    var logs = await auditRepo.GetAllAuditLogsAsync(decision);
    return Results.Ok(logs);
})
.WithName("GetAllAuditLogs")
.WithSummary("Retrieve all audit log entries, optionally filtered by decision")
.WithTags("Claims");

app.MapGet("/api/claims/audit-log/{id:guid}", async (
    Guid id,
    IClaimAuditRepository auditRepo) =>
{
    var entry = await auditRepo.GetByIdAsync(id);
    return entry is not null ? Results.Ok(entry) : Results.NotFound();
})
.WithName("GetAuditLogEntry")
.WithSummary("Retrieve audit log entry by ID")
.WithTags("Claims");

// HitL Claims Adjuster Review Endpoint
app.MapPost("/api/claims/hitl-review", async (
    [FromBody] HitlReviewRequest request,
    IClaimAuditRepository auditRepo) =>
{
    var existing = await auditRepo.GetByIdAsync(request.AuditId);
    if (existing == null)
    {
        return Results.NotFound(new { error = $"Audit log with ID {request.AuditId} not found." });
    }

    string normalizedDecision = request.Decision.ToUpperInvariant() switch
    {
        "APPROVE" or "APPROVED" => "MANUALLY_APPROVED",
        "REJECT" or "REJECTED" => "MANUALLY_REJECTED",
        _ => request.Decision
    };

    bool success = await auditRepo.UpdateAuditLogDecisionAsync(
        request.AuditId,
        normalizedDecision,
        request.FinalPayout,
        request.HumanNotes
    );

    return success
        ? Results.Ok(new HitlReviewResponse(
            AuditId: request.AuditId,
            Decision: normalizedDecision,
            FinalPayout: request.FinalPayout,
            Status: "UPDATED",
            UpdatedAt: DateTimeOffset.UtcNow
          ))
        : Results.BadRequest(new { error = "Failed to update claim decision." });
});
// Agent Triaging Endpoints (In-App AI Execution)
app.MapPost("/api/agent/validate-key", async (
    [FromBody] JsonElement body,
    IOpenAiAgentService agentService) =>
{
    string key = body.TryGetProperty("apiKey", out var prop) ? prop.GetString() ?? "" : "";
    bool isValid = await agentService.ValidateApiKeyAsync(key);
    return Results.Ok(new { isValid });
})
.WithName("ValidateApiKey")
.WithSummary("Validates an OpenAI API key against the OpenAI API")
.WithTags("Agent");

app.MapPost("/api/agent/triage", async (
    [FromBody] AikaEngine.WebApi.Services.AgentTriageRequest request,
    IOpenAiAgentService agentService) =>
{
    var result = await agentService.RunTriageAsync(request);
    return Results.Ok(result);
})
.WithName("RunAgentTriage")
.WithSummary("Runs the full AI Agent tool-calling loop on an insurance claim")
.WithTags("Agent");

app.Run();
