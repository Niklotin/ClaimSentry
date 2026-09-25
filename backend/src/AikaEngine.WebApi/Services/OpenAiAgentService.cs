using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AikaEngine.Application.Interfaces;
using AikaEngine.Application.Models;
using AikaEngine.Application.Services;
using AikaEngine.Domain.Entities;

namespace AikaEngine.WebApi.Services;

public record AgentTriageRequest(
    string? ApiKey,
    string? Model,
    string ClaimId,
    string ClaimantId,
    string Category,
    string IncidentDescription,
    decimal ClaimedAmount,
    string ClauseCode,
    int ItemAgeYears,
    bool HasPoliceReport
);

public record AgentStepTrace(
    string StepType, // 'TOOL_CALL', 'TOOL_RESULT', 'THINKING'
    string Name,
    string Details,
    DateTime Timestamp
);

public record AgentTriageResult(
    string ClaimId,
    string Recommendation, // 'APPROVE', 'REJECT', 'ESCALATE'
    bool RequiresHumanReview,
    decimal CalculatedPayout,
    string PolicyCode,
    string Justification,
    decimal FraudRiskScore,
    Guid AuditId,
    List<AgentStepTrace> Trace
);

public interface IOpenAiAgentService
{
    Task<bool> ValidateApiKeyAsync(string apiKey);
    Task<AgentTriageResult> RunTriageAsync(AgentTriageRequest request);
}

public class OpenAiAgentService : IOpenAiAgentService
{
    private readonly HttpClient _httpClient;
    private readonly IPolicyRepository _policyRepo;
    private readonly IFraudIndicatorRepository _fraudRepo;
    private readonly IPayoutCalculator _calculator;
    private readonly IFraudDetectionEngine _fraudEngine;
    private readonly IClaimAuditRepository _auditRepo;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAiAgentService> _logger;

    public OpenAiAgentService(
        HttpClient httpClient,
        IPolicyRepository policyRepo,
        IFraudIndicatorRepository fraudRepo,
        IPayoutCalculator calculator,
        IFraudDetectionEngine fraudEngine,
        IClaimAuditRepository auditRepo,
        IConfiguration config,
        ILogger<OpenAiAgentService> logger)
    {
        _httpClient = httpClient;
        _policyRepo = policyRepo;
        _fraudRepo = fraudRepo;
        _calculator = calculator;
        _fraudEngine = fraudEngine;
        _auditRepo = auditRepo;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using var resp = await _httpClient.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate OpenAI API key.");
            return false;
        }
    }

    public async Task<AgentTriageResult> RunTriageAsync(AgentTriageRequest request)
    {
        var trace = new List<AgentStepTrace>();
        string activeKey = !string.IsNullOrWhiteSpace(request.ApiKey)
            ? request.ApiKey.Trim()
            : _config["OPENAI_API_KEY"] ?? string.Empty;

        string model = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : "gpt-4o-mini";

        trace.Add(new AgentStepTrace("THINKING", "Agent Initialized", $"Claim {request.ClaimId} received for claimant {request.ClaimantId}. Amount: {request.ClaimedAmount:F2} EUR.", DateTime.UtcNow));

        // If no API key is provided, execute deterministic fallback triage
        if (string.IsNullOrWhiteSpace(activeKey))
        {
            trace.Add(new AgentStepTrace("THINKING", "Deterministic Fallback", "No OpenAI API key provided. Executing local deterministic rule pipeline.", DateTime.UtcNow));
            return await ExecuteLocalFallbackTriage(request, trace);
        }

        try
        {
            return await RunOpenAiAgentLoopAsync(activeKey, model, request, trace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI agent loop failed. Falling back to local deterministic rules.");
            trace.Add(new AgentStepTrace("THINKING", "API Error & Fallback", $"OpenAI API call failed ({ex.Message}). Executing deterministic fallback.", DateTime.UtcNow));
            return await ExecuteLocalFallbackTriage(request, trace);
        }
    }

    private async Task<AgentTriageResult> RunOpenAiAgentLoopAsync(
        string apiKey,
        string model,
        AgentTriageRequest claimReq,
        List<AgentStepTrace> trace)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = "You are the ClaimSentry Autonomous Claims Triaging Assistant. Your job is to analyze the user's incident report, call the appropriate tools to find policy coverage, compute accurate deterministic payouts, and evaluate fraud risk.\n" +
                              "NEVER invent policy rules or calculate deductions in your head; ALWAYS use the calculation tool.\n" +
                              "IF the net payout exceeds 500 EUR OR the fraud risk score is >= 0.30 OR a mandatory police report is missing, you MUST set 'requires_human_review': true.\n" +
                              "In your final response, provide a valid JSON object matching this schema: " +
                              "{\"claim_id\": string, \"recommendation\": \"APPROVE\"|\"REJECT\"|\"ESCALATE\", \"requires_human_review\": bool, \"calculated_payout\": number, \"policy_code\": string, \"justification\": string}"
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = $"Triage this claim:\n" +
                              $"Claim ID: {claimReq.ClaimId}\n" +
                              $"Claimant ID: {claimReq.ClaimantId}\n" +
                              $"Category: {claimReq.Category}\n" +
                              $"Incident Description: {claimReq.IncidentDescription}\n" +
                              $"Claimed Amount: {claimReq.ClaimedAmount:F2}\n" +
                              $"Clause Code: {claimReq.ClauseCode}\n" +
                              $"Item Age (Years): {claimReq.ItemAgeYears}\n" +
                              $"Has Police Report: {claimReq.HasPoliceReport}"
            }
        };

        var tools = GetToolDefinitions();
        decimal calculatedPayout = 0.0m;
        decimal fraudScore = 0.0m;
        string policyCode = claimReq.ClauseCode;

        // Up to 5 tool calling iterations
        for (int iter = 0; iter < 5; iter++)
        {
            var reqPayload = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["tools"] = tools,
                ["temperature"] = 0.1
            };

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpReq.Content = new StringContent(reqPayload.ToJsonString(), Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(httpReq);
            var respStr = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI returned {resp.StatusCode}: {respStr}");
            }

            var respJson = JsonNode.Parse(respStr);
            var choice = respJson?["choices"]?[0];
            var finishReason = choice?["finish_reason"]?.ToString();
            var messageNode = choice?["message"];

            if (messageNode == null) break;

            messages.Add(messageNode.DeepClone());

            // Check if tools were called
            var toolCalls = messageNode["tool_calls"]?.AsArray();
            if (toolCalls != null && toolCalls.Count > 0)
            {
                foreach (var tc in toolCalls)
                {
                    string callId = tc?["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    string fnName = tc?["function"]?["name"]?.ToString() ?? "";
                    string fnArgs = tc?["function"]?["arguments"]?.ToString() ?? "{}";

                    trace.Add(new AgentStepTrace("TOOL_CALL", fnName, fnArgs, DateTime.UtcNow));

                    var argsObj = JsonNode.Parse(fnArgs);
                    string toolResultStr = "{}";

                    if (fnName == "get_policy_clauses")
                    {
                        string? cat = argsObj?["category"]?.ToString();
                        var clauses = await _policyRepo.GetClausesAsync(cat);
                        toolResultStr = JsonSerializer.Serialize(clauses);
                        trace.Add(new AgentStepTrace("TOOL_RESULT", fnName, $"Retrieved {clauses.Count} clauses for category '{cat}'.", DateTime.UtcNow));
                    }
                    else if (fnName == "calculate_payout")
                    {
                        decimal amount = decimal.TryParse(argsObj?["claimed_amount"]?.ToString(), out var a) ? a : claimReq.ClaimedAmount;
                        string code = argsObj?["clause_code"]?.ToString() ?? claimReq.ClauseCode;
                        int age = int.TryParse(argsObj?["item_age_years"]?.ToString(), out var ag) ? ag : claimReq.ItemAgeYears;
                        bool police = bool.TryParse(argsObj?["has_police_report"]?.ToString(), out var p) ? p : claimReq.HasPoliceReport;

                        var clause = await _policyRepo.GetByClauseCodeAsync(code);
                        if (clause != null)
                        {
                            var payoutRes = _calculator.Calculate(new CalculatePayoutRequest(amount, code, age, police), clause);
                            calculatedPayout = payoutRes.EligibleAmount;
                            policyCode = code;
                            toolResultStr = JsonSerializer.Serialize(payoutRes);
                            trace.Add(new AgentStepTrace("TOOL_RESULT", fnName, $"Calculated net payout: {calculatedPayout:F2} EUR. Breakdown: {payoutRes.CalculationBreakdown}", DateTime.UtcNow));
                        }
                    }
                    else if (fnName == "check_fraud_risk")
                    {
                        string cId = argsObj?["claimant_id"]?.ToString() ?? claimReq.ClaimantId;
                        string desc = argsObj?["incident_description"]?.ToString() ?? claimReq.IncidentDescription;
                        decimal amt = decimal.TryParse(argsObj?["claimed_amount"]?.ToString(), out var am) ? am : claimReq.ClaimedAmount;

                        var indicators = await _fraudRepo.GetAllAsync();
                        var fraudRes = _fraudEngine.Evaluate(new FraudCheckRequest(cId, desc, amt), indicators);
                        fraudScore = fraudRes.RiskScore;
                        toolResultStr = JsonSerializer.Serialize(fraudRes);
                        trace.Add(new AgentStepTrace("TOOL_RESULT", fnName, $"Evaluated risk score: {fraudRes.RiskScore:F2}. Triggered: {string.Join(", ", fraudRes.TriggeredFlags)}", DateTime.UtcNow));
                    }

                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = callId,
                        ["content"] = toolResultStr
                    });
                }
            }
            else
            {
                // Finished
                string content = messageNode["content"]?.ToString() ?? "";
                trace.Add(new AgentStepTrace("THINKING", "Final Decision Generated", content, DateTime.UtcNow));

                return ParseAgentResponse(content, claimReq, calculatedPayout, fraudScore, policyCode, trace);
            }
        }

        return await ExecuteLocalFallbackTriage(claimReq, trace);
    }

    private AgentTriageResult ParseAgentResponse(
        string content,
        AgentTriageRequest claimReq,
        decimal calculatedPayout,
        decimal fraudScore,
        string policyCode,
        List<AgentStepTrace> trace)
    {
        string recommendation = "ESCALATE";
        bool requiresHumanReview = true;
        string justification = content;

        try
        {
            // Extract JSON if wrapped in markdown code blocks
            int jsonStart = content.IndexOf('{');
            int jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                string jsonStr = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonNode.Parse(jsonStr);

                recommendation = parsed?["recommendation"]?.ToString() ?? recommendation;
                requiresHumanReview = parsed?["requires_human_review"]?.GetValue<bool>() ?? requiresHumanReview;
                if (parsed?["calculated_payout"] != null)
                {
                    calculatedPayout = parsed["calculated_payout"]!.GetValue<decimal>();
                }
                policyCode = parsed?["policy_code"]?.ToString() ?? policyCode;
                justification = parsed?["justification"]?.ToString() ?? justification;
            }
        }
        catch
        {
            // Fall back to rule-based escalation
        }

        // Enforce hard deterministic gate
        if (calculatedPayout > 500.00m || fraudScore >= 0.30m || (!claimReq.HasPoliceReport && policyCode.Contains("BIKE")))
        {
            requiresHumanReview = true;
            recommendation = "ESCALATE";
        }

        string decision = requiresHumanReview
            ? "ESCALATED_HITL"
            : (recommendation == "APPROVE" ? "AUTO_APPROVED" : "REJECTED");

        var auditLog = new ClaimAuditLog
        {
            Id = Guid.NewGuid(),
            ClaimId = claimReq.ClaimId,
            ClaimantId = claimReq.ClaimantId,
            ClaimedAmount = claimReq.ClaimedAmount,
            CalculatedPayout = calculatedPayout,
            Decision = decision,
            AiReasoning = justification,
            HumanNotes = requiresHumanReview ? "Tekoälyagentin eskalaatio: siirretty käsittelijän tarkastukseen." : "Tekoälyagentin automaattinen suoraprosessointi (STP).",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _auditRepo.SaveAuditLogAsync(auditLog);

        return new AgentTriageResult(
            ClaimId: claimReq.ClaimId,
            Recommendation: recommendation,
            RequiresHumanReview: requiresHumanReview,
            CalculatedPayout: calculatedPayout,
            PolicyCode: policyCode,
            Justification: justification,
            FraudRiskScore: fraudScore,
            AuditId: auditLog.Id,
            Trace: trace
        );
    }

    private async Task<AgentTriageResult> ExecuteLocalFallbackTriage(AgentTriageRequest req, List<AgentStepTrace> trace)
    {
        var clause = await _policyRepo.GetByClauseCodeAsync(req.ClauseCode);
        if (clause == null)
        {
            var clauses = await _policyRepo.GetClausesAsync(req.Category);
            clause = clauses.FirstOrDefault();
        }

        decimal payout = 0.0m;
        string justification = "";
        if (clause != null)
        {
            var pRes = _calculator.Calculate(new CalculatePayoutRequest(req.ClaimedAmount, clause.ClauseCode, req.ItemAgeYears, req.HasPoliceReport), clause);
            payout = pRes.EligibleAmount;
            justification = pRes.CalculationBreakdown;
            trace.Add(new AgentStepTrace("TOOL_RESULT", "calculate_payout", justification, DateTime.UtcNow));
        }

        var fraudIndicators = await _fraudRepo.GetAllAsync();
        var fraudRes = _fraudEngine.Evaluate(
            new FraudCheckRequest(req.ClaimantId, req.IncidentDescription, req.ClaimedAmount),
            fraudIndicators
        );

        trace.Add(new AgentStepTrace("TOOL_RESULT", "check_fraud_risk", fraudRes.RiskExplanation, DateTime.UtcNow));

        bool requiresReview = (payout > 500.00m) || fraudRes.IsHighRisk || (clause?.RequiresPoliceReport == true && !req.HasPoliceReport);
        string recommendation = requiresReview ? "ESCALATE" : (payout > 0 ? "APPROVE" : "REJECT");
        string decision = requiresReview ? "ESCALATED_HITL" : (recommendation == "APPROVE" ? "AUTO_APPROVED" : "REJECTED");

        string fullJustification = $"{justification} | Petosarvio: {fraudRes.RiskExplanation}";

        var auditLog = new ClaimAuditLog
        {
            Id = Guid.NewGuid(),
            ClaimId = req.ClaimId,
            ClaimantId = req.ClaimantId,
            ClaimedAmount = req.ClaimedAmount,
            CalculatedPayout = payout,
            Decision = decision,
            AiReasoning = fullJustification,
            HumanNotes = requiresReview ? "Deterministinen sääntöeskalaatio (HitL)." : "Automaattinen STP-läpimeno.",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _auditRepo.SaveAuditLogAsync(auditLog);

        return new AgentTriageResult(
            ClaimId: req.ClaimId,
            Recommendation: recommendation,
            RequiresHumanReview: requiresReview,
            CalculatedPayout: payout,
            PolicyCode: clause?.ClauseCode ?? req.ClauseCode,
            Justification: fullJustification,
            FraudRiskScore: fraudRes.RiskScore,
            AuditId: auditLog.Id,
            Trace: trace
        );
    }

    private static JsonArray GetToolDefinitions()
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_policy_clauses",
                    ["description"] = "Look up active insurance coverage clauses, deductibles, limits, and documentation requirements (e.g. category 'BICYCLE', 'ELECTRONICS', 'LUGGAGE').",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["category"] = new JsonObject { ["type"] = "string", ["description"] = "Policy category" }
                        }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "calculate_payout",
                    ["description"] = "Deterministic financial math engine. Computes depreciation, deductibles, coverage caps, and mandatory police report compliance. ALWAYS call this tool for math.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["claimed_amount"] = new JsonObject { ["type"] = "number" },
                            ["clause_code"] = new JsonObject { ["type"] = "string" },
                            ["item_age_years"] = new JsonObject { ["type"] = "integer" },
                            ["has_police_report"] = new JsonObject { ["type"] = "boolean" }
                        },
                        ["required"] = new JsonArray { "claimed_amount", "clause_code", "item_age_years", "has_police_report" }
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "check_fraud_risk",
                    ["description"] = "Evaluates suspicious patterns, fraud keywords, blacklisted claimants, and high claim amount anomalies. Returns risk_score and is_high_risk.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["claimant_id"] = new JsonObject { ["type"] = "string" },
                            ["incident_description"] = new JsonObject { ["type"] = "string" },
                            ["claimed_amount"] = new JsonObject { ["type"] = "number" }
                        },
                        ["required"] = new JsonArray { "claimant_id", "incident_description", "claimed_amount" }
                    }
                }
            }
        };
    }
}
