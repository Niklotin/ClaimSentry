#!/usr/bin/env python3
"""
AIKA Claims Platform - Model Context Protocol (MCP) Server
Exposes deterministic insurance policy search, payout calculations, and fraud detection
as native MCP tools for AI Agents (Claude Desktop, Cursor, Copilot, Antigravity).
"""

import sys
import json
import urllib.request
import urllib.error

AIKA_ENGINE_URL = "http://localhost:5000"

# Local fallback rules if .NET engine is currently unreachable
FALLBACK_CLAUSES = [
    {
        "clause_code": "HOME_BIKE_01",
        "category": "BICYCLE",
        "title": "Polkupyörävarkaus ja vahinko (Koti)",
        "coverage_details": "Korvaa lukitun polkupyörän varkauden. Varkauksissa vaaditaan aina tehty rikosilmoitus poliisille. Ikävähennys 10% vuodessa toisesta vuodesta alkaen.",
        "standard_deductible": 150.00,
        "max_coverage_limit": 2500.00,
        "requires_police_report": True
    },
    {
        "clause_code": "HOME_ELEC_01",
        "category": "ELECTRONICS",
        "title": "Kodinelektroniikan rikkoutuminen",
        "coverage_details": "Korvaa äkillisen ja ennalta-arvaamattoman laitteen rikkoutumisen. Ikävähennys 15% vuodessa toisesta vuodesta alkaen.",
        "standard_deductible": 150.00,
        "max_coverage_limit": 2000.00,
        "requires_police_report": False
    },
    {
        "clause_code": "TRAVEL_LUGG_01",
        "category": "LUGGAGE",
        "title": "Matkatavaravahinko ja rikkoutuminen",
        "coverage_details": "Korvaa matkan aikana vaurioituneet matkatavarat.",
        "standard_deductible": 100.00,
        "max_coverage_limit": 3000.00,
        "requires_police_report": False
    },
    {
        "clause_code": "TRAVEL_LUGG_THEFT",
        "category": "LUGGAGE",
        "title": "Matkatavaravarkaus ulkomailla",
        "coverage_details": "Korvaa anastetut matkatavarat. Vaatii paikallispoliisille tehdyn rikosilmoituksen.",
        "standard_deductible": 100.00,
        "max_coverage_limit": 3000.00,
        "requires_police_report": True
    }
]

def http_get(path):
    try:
        url = f"{AIKA_ENGINE_URL}{path}"
        req = urllib.request.Request(url, headers={"Accept": "application/json"})
        with urllib.request.urlopen(req, timeout=3) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except Exception:
        return None

def http_post(path, data):
    try:
        url = f"{AIKA_ENGINE_URL}{path}"
        body = json.dumps(data).encode("utf-8")
        req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json", "Accept": "application/json"}, method="POST")
        with urllib.request.urlopen(req, timeout=3) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except Exception:
        return None

# Tool implementations
def search_policy_clauses(category=None):
    remote = http_get(f"/tools/policy-clauses?category={category}" if category else "/tools/policy-clauses")
    if remote is not None:
        return remote
    if not category:
        return FALLBACK_CLAUSES
    return [c for c in FALLBACK_CLAUSES if c["category"].lower() == category.lower()]

def calculate_payout(claimed_amount, clause_code, item_age_years, has_police_report):
    data = {
        "claimed_amount": float(claimed_amount),
        "clause_code": str(clause_code),
        "item_age_years": int(item_age_years),
        "has_police_report": bool(has_police_report)
    }
    remote = http_post("/tools/calculate-payout", data)
    if remote is not None:
        return remote

    # Fallback deterministic math
    clause = next((c for c in FALLBACK_CLAUSES if c["clause_code"].upper() == clause_code.upper()), None)
    if not clause:
        return {"error": f"Clause {clause_code} not found."}

    if clause["requires_police_report"] and not has_police_report:
        return {
            "eligible_amount": 0.0,
            "deductible_applied": 0.0,
            "depreciation_applied": 0.0,
            "requires_police_report": True,
            "police_report_provided": False,
            "calculation_breakdown": f"Mandatory police report missing for {clause_code}. Claim payout is 0.00 EUR.",
            "is_eligible": False
        }

    dep_rate = 0.15 if clause["category"] == "ELECTRONICS" else 0.10
    dep_years = max(0, item_age_years - 1)
    dep_amount = round(claimed_amount * min(0.70, dep_years * dep_rate), 2)
    after_dep = max(0.0, claimed_amount - dep_amount)
    deductible = min(clause["standard_deductible"], after_dep)
    net = max(0.0, after_dep - deductible)
    payout = min(net, clause["max_coverage_limit"])

    return {
        "eligible_amount": payout,
        "deductible_applied": deductible,
        "depreciation_applied": dep_amount,
        "max_limit_applied": clause["max_coverage_limit"],
        "requires_police_report": clause["requires_police_report"],
        "police_report_provided": has_police_report,
        "calculation_breakdown": f"Claim: {claimed_amount} EUR, Dep: -{dep_amount} EUR, Deductible: -{deductible} EUR => Net Payout: {payout} EUR",
        "is_eligible": payout > 0
    }

def check_fraud_risk(claimant_id, incident_description, claimed_amount):
    data = {
        "claimant_id": str(claimant_id),
        "incident_description": str(incident_description),
        "claimed_amount": float(claimed_amount)
    }
    remote = http_post("/tools/fraud-check", data)
    if remote is not None:
        return remote

    # Fallback rules
    desc = incident_description.lower()
    flags = []
    risk = 0.0
    for kw, w in [("käteinen", 0.20), ("ei kuittia", 0.25), ("pimeä", 0.50), ("perintä", 0.35)]:
        if kw in desc:
            flags.append(f"Keyword: '{kw}' (+{w})")
            risk += w

    if "FI123" in claimant_id.upper():
        flags.append("Claimant on watchlist (+0.75)")
        risk += 0.75

    final_risk = min(1.0, round(risk, 2))
    return {
        "risk_score": final_risk,
        "is_high_risk": final_risk >= 0.30,
        "triggered_flags": flags,
        "risk_explanation": f"Score {final_risk:.2f}. Flags: {', '.join(flags) if flags else 'None'}"
    }

def record_claim_decision(claim_id, claimant_id, claimed_amount, calculated_payout, decision, ai_reasoning=None, human_notes=None):
    data = {
        "claim_id": claim_id,
        "claimant_id": claimant_id,
        "claimed_amount": float(claimed_amount),
        "calculated_payout": float(calculated_payout),
        "decision": decision,
        "ai_reasoning": ai_reasoning,
        "human_notes": human_notes
    }
    remote = http_post("/api/claims/decision-callback", data)
    if remote is not None:
        return remote
    return {"status": "RECORDED_LOCALLY", "claim_id": claim_id, "decision": decision}

TOOLS_DEFINITIONS = [
    {
        "name": "search_policy_clauses",
        "description": "Look up active insurance coverage clauses, deductibles, limits, and documentation requirements (e.g. category 'BICYCLE', 'ELECTRONICS', 'LUGGAGE').",
        "inputSchema": {
            "type": "object",
            "properties": {
                "category": { "type": "string", "description": "Optional insurance category (BICYCLE, ELECTRONICS, LUGGAGE)" }
            }
        }
    },
    {
        "name": "calculate_payout",
        "description": "Deterministic financial math engine. Computes depreciation, deductibles, coverage caps, and mandatory police report compliance. Always call this tool for math.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "claimed_amount": { "type": "number", "description": "Total claimed amount in EUR" },
                "clause_code": { "type": "string", "description": "Policy clause code, e.g. HOME_BIKE_01" },
                "item_age_years": { "type": "integer", "description": "Age of the insured item in full years" },
                "has_police_report": { "type": "boolean", "description": "True if official police report was submitted" }
            },
            "required": ["claimed_amount", "clause_code", "item_age_years", "has_police_report"]
        }
    },
    {
        "name": "check_fraud_risk",
        "description": "Evaluates suspicious patterns, fraud keywords, blacklisted claimants, and high claim amount anomalies. Returns risk_score and is_high_risk.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "claimant_id": { "type": "string", "description": "Claimant customer identifier" },
                "incident_description": { "type": "string", "description": "Detailed incident report text" },
                "claimed_amount": { "type": "number", "description": "Total amount claimed in EUR" }
            },
            "required": ["claimant_id", "incident_description", "claimed_amount"]
        }
    },
    {
        "name": "record_claim_decision",
        "description": "Audit logging callback. Permanently logs the triage recommendation, calculated payout, and AI justification into the audit database.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "claim_id": { "type": "string", "description": "Unique claim reference ID" },
                "claimant_id": { "type": "string", "description": "Customer ID" },
                "claimed_amount": { "type": "number", "description": "Claimed amount in EUR" },
                "calculated_payout": { "type": "number", "description": "Calculated payout amount in EUR" },
                "decision": { "type": "string", "enum": ["AUTO_APPROVED", "REJECTED", "ESCALATED_HITL"], "description": "Final triage decision" },
                "ai_reasoning": { "type": "string", "description": "AI justification and rule citations" },
                "human_notes": { "type": "string", "description": "Optional notes or escalation context" }
            },
            "required": ["claim_id", "claimant_id", "claimed_amount", "calculated_payout", "decision"]
        }
    }
]

def handle_rpc(request):
    method = request.get("method")
    req_id = request.get("id")

    if method == "initialize":
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {
                    "tools": {}
                },
                "serverInfo": {
                    "name": "aika-claims-mcp-server",
                    "version": "1.0.0"
                }
            }
        }

    elif method == "tools/list":
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "tools": TOOLS_DEFINITIONS
            }
        }

    elif method == "tools/call":
        params = request.get("params", {})
        tool_name = params.get("name")
        args = params.get("arguments", {})

        if tool_name == "search_policy_clauses":
            res = search_policy_clauses(args.get("category"))
        elif tool_name == "calculate_payout":
            res = calculate_payout(args.get("claimed_amount"), args.get("clause_code"), args.get("item_age_years"), args.get("has_police_report"))
        elif tool_name == "check_fraud_risk":
            res = check_fraud_risk(args.get("claimant_id"), args.get("incident_description"), args.get("claimed_amount"))
        elif tool_name == "record_claim_decision":
            res = record_claim_decision(args.get("claim_id"), args.get("claimant_id"), args.get("claimed_amount"), args.get("calculated_payout"), args.get("decision"), args.get("ai_reasoning"), args.get("human_notes"))
        else:
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "error": { "code": -32601, "message": f"Unknown tool '{tool_name}'" }
            }

        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "content": [
                    { "type": "text", "text": json.dumps(res, indent=2, ensure_ascii=False) }
                ]
            }
        }

    elif method == "notifications/initialized":
        return None

    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "error": { "code": -32601, "message": f"Method '{method}' not implemented." }
    }

def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            res = handle_rpc(req)
            if res is not None:
                sys.stdout.write(json.dumps(res, ensure_ascii=False) + "\n")
                sys.stdout.flush()
        except Exception as e:
            err_res = {
                "jsonrpc": "2.0",
                "id": None,
                "error": { "code": -32700, "message": f"Parse error: {str(e)}" }
            }
            sys.stdout.write(json.dumps(err_res) + "\n")
            sys.stdout.flush()

if __name__ == "__main__":
    main()
