# ClaimSentry | AIKA Claims & Fraud Triaging Platform

> Enterprise Agentic Automation & Deterministic Claims Processing Platform  
> Target Architecture: .NET 10 Core Engine + n8n Orchestrator / In-App AI Agent + PostgreSQL 16 + DeepEval CI Gate + Model Context Protocol (MCP)

---

## 1. Overview

ClaimSentry is an enterprise insurance platform designed for autonomous claims triaging, deterministic financial calculations, and fraud detection. The system combines large language model (LLM) reasoning with strictly deterministic financial math and human-in-the-loop (HitL) adjuster oversight.

### Architectural Principles

1. **Separation of Reasoning and Calculation:** LLMs are used exclusively for semantic comprehension, policy extraction, and tool orchestration. All financial mathematics (depreciation, deductibles, coverage caps) and legal compliance rules execute deterministically in high-performance C#. LLMs are never permitted to compute payout amounts independently.
2. **Deterministic Financial Math:** Depreciation curves, coverage limits, and deductible subtractions produce identical, verifiable financial results on every execution.
3. **Hybrid Automation (STP & HitL):**
   - **Straight-Through Processing (STP):** Small, low-risk claims below the configured threshold (default: 500.00 EUR) with clean risk assessments are approved and paid out automatically.
   - **Human-in-the-Loop (HitL) Escalation:** Claims exceeding financial caps, showing fraud indicators (risk score >= 0.30), or missing mandatory police reports are escalated to the adjuster workbench with full reasoning traces.
4. **Resilience & Circuit-Breaker Architecture:** The engine monitors database and integration availability. If external dependencies (PostgreSQL, n8n, OpenAI) are unreachable, the system instantly activates local deterministic fallbacks with sub-2ms response times without TCP connection timeouts.
5. **Auditability & Regulatory Compliance:** Every triage step, tool call, adjuster decision, and calculation breakdown is permanently stored with timestamps in an immutable audit trail compliant with financial supervision requirements.

---

## 2. System Architecture

```
                                      +---------------------------------+
                                      |   Claimant / API Client (JSON)  |
                                      +----------------+----------------+
                                                       |
                                                       | POST /api/agent/triage
                                                       v
+-------------------------------------------------------------------------------------------------------+
| ClaimSentry Platform                                                                                  |
|                                                                                                       |
|  +-------------------------------------------------------------------------------------------------+  |
|  | ClaimSentry Workbench (Web UI - Port 5000)                                                      |  |
|  |  * Two-Pane Master-Detail Inspection & Queue Interface                                          |  |
|  |  * In-App AI Agent Execution & Live Tool-Calling Trace Viewer                                   |  |
|  |  * Comprehensive System Settings (Models, API Keys, STP Limits, Fraud Thresholds, DB Latency)  |  |
|  |  * Adjuster Review & Decision Actions (Approve, Reject, Override Payout)                        |  |
|  +-----------------------------------------------+-------------------------------------------------+  |
|                                                  |                                                    |
|                                                  v                                                    |
|  +-------------------------------------------------------------------------------------------------+  |
|  | Autonomous Agent Orchestration Layer                                                           |  |
|  |  * In-App OpenAI ReAct Tool-Calling Loop (OpenAiAgentService)                                   |  |
|  |  * External n8n Workflow Automation Engine (Port 5678, Optional)                                 |  |
|  |  * Circuit-Breaker Fallback Engine (Immediate Local Rule Execution)                             |  |
|  +-----------------------------------------------+-------------------------------------------------+  |
|                                                  | Tool Calls                                         |
|                                                  v                                                    |
|  +-------------------------------------------------------------------------------------------------+  |
|  | .NET 10 Deterministic Rules & Calculation Engine                                                |  |
|  |  * PayoutCalculator (Age Depreciation, Deductibles, Policy Limits)                              |  |
|  |  * FraudDetectionEngine (Rule-Based Indicator Evaluation, Keyword Scanning, Anomaly Weights)   |  |
|  |  * DatabaseStatusService (Circuit Breaker & Connectivity Probing)                               |  |
|  +-----------------------------------------------+-------------------------------------------------+  |
|                                                  | Persistence                                        |
|                                                  v                                                    |
|  +-------------------------------------------------------------------------------------------------+  |
|  | PostgreSQL 16 (Port 5432) / In-Memory Protected Cache                                           |  |
|  |  * Tables: policy_clauses, fraud_indicators, claim_audit_log                                    |  |
|  +-------------------------------------------------------------------------------------------------+  |
+-------------------------------------------------------------------------------------------------------+
| External Integrations & Tooling                                                                       |
|  * Model Context Protocol (MCP) Server (mcp/aika_mcp_server.py) for Claude Desktop, Cursor, IDEs      |
|  * DeepEval / Pytest Quality & Safety Gate (evals/test_n8n_agent_quality.py)                         |
|  * GitHub Actions CI/CD Pipeline (.github/workflows/ci.yml)                                           |
+-------------------------------------------------------------------------------------------------------+
```

---

## 3. Repository Structure

```
ClaimSentry/
|-- docker-compose.yml                      # Container definitions for postgres, backend, and n8n
|-- .env.example                            # Configuration environment template
|-- .env                                    # Local runtime configuration
|-- README.md                               # System documentation and operational runbook
|
|-- .github/workflows/
|   `-- ci.yml                              # GitHub Actions continuous integration & test pipeline
|
|-- database/
|   `-- init.sql                            # PostgreSQL relational schema and seed clauses/indicators
|
|-- backend/                                # .NET 10 Clean Architecture Microservice
|   |-- AikaEngine.slnx                     # Modern XML-based .NET Solution
|   |-- Dockerfile                          # Multi-stage container build and test runner
|   |-- src/
|   |   |-- AikaEngine.Domain/              # Entities: PolicyClause, FraudIndicator, ClaimAuditLog
|   |   |-- AikaEngine.Application/         # PayoutCalculator, FraudDetectionEngine, Service Interfaces
|   |   `-- AikaEngine.WebApi/              # ASP.NET Core Minimal API, Services, OpenAPI Swagger
|   |       |-- Services/                   # OpenAiAgentService, DatabaseStatusService
|   |       |-- Data/                       # Postgres and in-memory fallback repositories
|   |       `-- wwwroot/                    # ClaimSentry Adjuster Workbench & System Settings View
|   `-- tests/
|       `-- AikaEngine.UnitTests/           # Unit tests for financial math, depreciation, and fraud logic
|
|-- mcp/                                    # Model Context Protocol Server
|   |-- aika_mcp_server.py                  # Standalone JSON-RPC MCP server for IDEs & LLMs
|   |-- claude_desktop_config.example.json  # Configuration snippet for Claude Desktop
|   `-- README.md                           # MCP tool descriptions and setup guide
|
|-- workflows/                              # n8n Orchestration Workflow
|   |-- claims_triage_agent.json            # Complete n8n LangChain agent definition
|   `-- seed_data/                          # Test scenarios (approved, high-value, fraud, missing report)
|
`-- evals/                                  # Automated Quality & Safety Gate
    |-- Dockerfile.evals                    # Containerized evaluation environment
    |-- requirements.txt                    # Pytest, DeepEval, Requests dependencies
    |-- golden_dataset.json                 # 15 synthetic claims testing edge cases and prompt injection
    `-- test_n8n_agent_quality.py           # Evaluation test assertions with cached health checks
```

---

## 4. Deterministic Financial Math & Business Logic

Insurance calculations follow exact statutory rules implemented in `PayoutCalculator.cs`:

1. **Age Depreciation Formula:**
   - **Year 1:** 0% depreciation.
   - **Year 2+:** 10%/year for bicycles and general property (`HOME_BIKE_01`), 15%/year for consumer electronics (`HOME_ELEC_01`).
   - Total depreciation is capped at 70% of the original purchase value.
2. **Deductible Application:**
   - Standard deductibles (e.g., 150.00 EUR for home contents, 100.00 EUR for luggage) are subtracted after depreciation is applied.
3. **Coverage Caps:**
   - The payout cannot exceed the policy limit (e.g., 2,000.00 EUR for bicycles, 3,000.00 EUR for electronics).
4. **Mandatory Documentation Verification:**
   - When `requires_police_report == true`, claims with `has_police_report == false` result in zero calculated payout and are flagged for mandatory adjuster review.
5. **Fraud Scoring Heuristics (`FraudDetectionEngine.cs`):**
   - High-velocity claims, blacklisted claimants, high claimed amounts without documentation, and suspicious keywords (`käteinen`, `ei kuittia`, `perintä`, `pimeä`, `väärennetty`) produce risk scores between 0.00 and 1.00.
   - Scores >= 0.30 trigger HitL review and Special Investigation Unit (SIU) routing.

---

## 5. Endpoints & API Reference

All endpoints support JSON formatting with snake_case and camelCase property names.

### Tools (Deterministic Endpoints)

| Method | Path | Description |
|---|---|---|
| `GET` | `/tools/policy-clauses` | Retrieve active policy terms, deductibles, limits, and documentation requirements. Accepts optional `?category=` filter. |
| `POST` | `/tools/calculate-payout` | Calculate depreciation, deductibles, coverage caps, and documentation compliance deterministically. |
| `POST` | `/tools/fraud-check` | Evaluate risk score (0.00 - 1.00), keyword heuristics, and high-risk flags. |

### Claims & Workflow Operations

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/claims/decision-callback` | Persist audit log entry for a completed claim triage. |
| `GET` | `/api/claims/audit-log` | Retrieve all audit log entries. Accepts optional `?decision=` filter. |
| `GET` | `/api/claims/audit-log/{id}` | Retrieve a specific audit log record by GUID. |
| `POST` | `/api/claims/hitl-review` | Human-in-the-Loop decision endpoint for adjusters to approve, reject, or adjust payout. |

### AI Agent Endpoints

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/agent/validate-key` | Validates an OpenAI API key against the OpenAI API. Returns `{"is_valid": boolean}`. |
| `POST` | `/api/agent/triage` | Executes the complete autonomous agent tool-calling loop (ReAct) with fallback to deterministic local rules. Returns recommendation, calculated payout, fraud score, audit ID, and full step trace. |

### System & Health

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Health check endpoint returning status and current UTC timestamp. |
| `GET` | `/swagger` | Interactive OpenAPI / Swagger UI. |
| `GET` | `/dashboard` | Redirects to the ClaimSentry Workbench UI. |

---

## 6. Setup and Execution

### Prerequisites

- .NET 10 SDK (or .NET 9 with compatible SDK installed)
- Python 3.10+ (for evaluations and MCP server)
- Docker Desktop (optional, for containerized execution)

### Option 1: Running Standalone Locally

#### 1. Start the .NET Backend
```bash
cd backend
dotnet run --project src/AikaEngine.WebApi --urls "http://localhost:5000"
```
The application will launch and be accessible at:
- **ClaimSentry Workbench:** [http://localhost:5000](http://localhost:5000)
- **Swagger Documentation:** [http://localhost:5000/swagger](http://localhost:5000/swagger)

#### 2. Run .NET Unit Tests
```bash
dotnet test backend/AikaEngine.slnx
```
*Result: 11/11 tests pass (100% pass rate).*

#### 3. Run Quality and Safety Gate (Python)
```bash
python -m pytest evals/test_n8n_agent_quality.py -v
```
*Result: 17 passed, 1 skipped (live LLM evaluation requires OpenAI API key).*

### Option 2: Running with Docker Compose

```bash
docker compose up -d
```
Services launched:
- **.NET Engine:** [http://localhost:5000](http://localhost:5000)
- **n8n Orchestrator:** [http://localhost:5678](http://localhost:5678)
- **PostgreSQL:** `localhost:5432` (database: `aika_claims`, credentials: `postgres`/`postgres`)

---

## 7. ClaimSentry Workbench & In-App Settings

The web interface (`backend/src/AikaEngine.WebApi/wwwroot/index.html`) is an enterprise-grade single-page application built with clean HTML5, CSS3, and vanilla JavaScript without external UI framework dependencies.

### Work Queues & Detailed Inspection

- **Two-Pane Layout:** Filterable queue on the left (All claims, HitL review, STP auto-approved, Handled by adjuster) and detailed inspection on the right.
- **Financial Breakdown:** Visual verification of original claim amount, applied depreciation, deductible subtractions, and net payout.
- **Adjuster Controls:** Approve claims for payment, reject fraudulent or ineligible claims, and enter audit notes.

### In-App AI Agent Triaging

- Click **Aja tekoalyagentti** in the top navigation bar.
- Choose from pre-configured Finnish insurance claim scenarios (bicycle theft, damaged smartphone, fraud keywords, laptop coffee spill) or input custom claim details.
- Observe the live **Execution Trace** showing the agent's internal reasoning, tool calls to `get_policy_clauses`, `calculate_payout`, and `check_fraud_risk`, and the resulting decision.
- Click **Avaa vahinko tyopoydalle** to load the newly triaged claim directly into the review workbench.

### In-App System Settings

Access via **Asetukset** in the top navigation bar or **Jarjestelmaasetukset** in the sidebar:

1. **AI & Agents:** Enter and test OpenAI API keys (stored only in local browser `localStorage`), select models (`gpt-4o-mini`, `gpt-4o`, `gpt-4.1-mini`, `gpt-3.5-turbo`, or custom), configure reasoning temperature and maximum tool calling iterations, and test n8n integration.
2. **STP Limits & Rules:** Configure the Straight-Through Processing threshold (default: 500.00 EUR), fraud risk alert threshold (default: 0.30), mandatory police report requirements, age depreciation rates, and default deductibles. Changes update the sidebar rule limits immediately.
3. **Database & Systems:** Monitor PostgreSQL connectivity, measure live query latency with millisecond precision, configure audit trail retention periods (default: 365 days), and set Special Investigation Unit (SIU) routing addresses.
4. **Adjuster Profile:** Configure the adjuster name, employee ID, role, and approval limits. Updating the profile immediately refreshes the top navigation bar and user avatar.
5. **Data Management:** Generate batches of realistic test claims, restore factory defaults, or clear browser cache.

---

## 8. Model Context Protocol (MCP) Server

ClaimSentry includes an MCP server implementation (`mcp/aika_mcp_server.py`) that exposes the deterministic calculation and fraud detection tools to external AI coding assistants (Claude Desktop, Cursor, Antigravity).

### Running the MCP Server
```bash
python mcp/aika_mcp_server.py
```

### Claude Desktop Configuration
Add the server entry to `%APPDATA%\Claude\claude_desktop_config.json` (Windows) or `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS):
```json
{
  "mcpServers": {
    "claimsentry": {
      "command": "python",
      "args": [
        "/path/to/ClaimSentry/mcp/aika_mcp_server.py"
      ]
    }
  }
}
```

---

## 9. Continuous Integration & Quality Gate

The automated CI/CD pipeline ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) validates every pull request:

1. **.NET Build & Tests:** Compiles the solution and runs all 11 unit tests asserting deterministic financial calculations and fraud rules.
2. **DeepEval / Pytest Quality Gate:** Executes 18 automated validation tests against synthetic golden dataset scenarios, checking:
   - Escalation correctness (claims > 500 EUR or risk >= 0.30 must never auto-approve).
   - Documentation enforcement (theft claims without police reports must not receive payouts).
   - Adversarial prompt injection defense (attempts to bypass authorization or manipulate payouts are intercepted).
3. **Container Topology Validation:** Validates Docker Compose files and multi-stage container builds.
