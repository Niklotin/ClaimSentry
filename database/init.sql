-- PostgreSQL Initial Schema & Seed Data for AIKA Claims Platform

-- Enable pgcrypto if needed (gen_random_uuid is built-in in PG 13+)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- 1. Policy Clauses Table
CREATE TABLE IF NOT EXISTS policy_clauses (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clause_code VARCHAR(30) UNIQUE NOT NULL,
    category VARCHAR(50) NOT NULL, -- e.g., 'BICYCLE', 'LUGGAGE', 'ELECTRONICS'
    title VARCHAR(255) NOT NULL,
    coverage_details TEXT NOT NULL,
    standard_deductible NUMERIC(10,2) NOT NULL DEFAULT 150.00,
    max_coverage_limit NUMERIC(10,2) NOT NULL DEFAULT 2000.00,
    requires_police_report BOOLEAN NOT NULL DEFAULT FALSE
);

-- 2. Fraud Indicators Table
CREATE TABLE IF NOT EXISTS fraud_indicators (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    indicator_type VARCHAR(50) NOT NULL, -- 'KEYWORD', 'CLAIMANT_FLAG', 'HIGH_FREQUENCY'
    value VARCHAR(255) NOT NULL,
    risk_weight NUMERIC(3,2) NOT NULL -- 0.10 to 1.00
);

-- 3. Claim Audit Log Table
CREATE TABLE IF NOT EXISTS claim_audit_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    claim_id VARCHAR(50) NOT NULL,
    claimant_id VARCHAR(50) NOT NULL,
    claimed_amount NUMERIC(10,2) NOT NULL,
    calculated_payout NUMERIC(10,2) NOT NULL,
    decision VARCHAR(30) NOT NULL, -- 'AUTO_APPROVED', 'REJECTED', 'ESCALATED_HITL'
    ai_reasoning TEXT,
    human_notes TEXT,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- SEED DATA
-- ============================================================================

-- Seed Policy Clauses
INSERT INTO policy_clauses (clause_code, category, title, coverage_details, standard_deductible, max_coverage_limit, requires_police_report)
VALUES
(
    'HOME_BIKE_01',
    'BICYCLE',
    'Polkupyörävarkaus ja vahinko (Koti)',
    'Korvaa lukitun polkupyörän varkauden tai äkillisen rikkoutumisen. Varkauksissa vaaditaan aina tehty rikosilmoitus poliisille. Ikävähennys 10% vuodessa toisesta vuodesta alkaen.',
    150.00,
    2500.00,
    TRUE
),
(
    'HOME_ELEC_01',
    'ELECTRONICS',
    'Kodinelektroniikan rikkoutuminen',
    'Korvaa äkillisen ja ennalta-arvaamattoman älypuhelimen, kannettavan tai kodinkoneen rikkoutumisen (esim. putoaminen). Ikävähennys 15% vuodessa toisesta vuodesta alkaen.',
    150.00,
    2000.00,
    FALSE
),
(
    'TRAVEL_LUGG_01',
    'LUGGAGE',
    'Matkatavaravahinko ja rikkoutuminen',
    'Korvaa matkan aikana vaurioituneet tai kuljetuksessa rikkoutuneet matkatavarat. Vaatii matkalipun ja vahinkotodistuksen kuljetusyhtiöltä mikäli kuljetuksessa.',
    100.00,
    3000.00,
    FALSE
),
(
    'TRAVEL_LUGG_THEFT',
    'LUGGAGE',
    'Matkatavaravarkaus ulkomailla',
    'Korvaa matkalla anastetut matkatavarat. Varkaudesta on aina esitettävä paikallispoliisille tehty rikosilmoitus.',
    100.00,
    3000.00,
    TRUE
),
(
    'HOME_LIAB_01',
    'LIABILITY',
    'Yksityishenkilön vastuuvahinko',
    'Korvaa toiselle aiheutetun esine- tai henkilövahingon, josta vakuutuksenottaja on lain mukaan korvausvastuussa. Vaatii aina erillisen vahinkoselvityksen.',
    200.00,
    50000.00,
    FALSE
)
ON CONFLICT (clause_code) DO NOTHING;

-- Seed Fraud Indicators
INSERT INTO fraud_indicators (indicator_type, value, risk_weight)
VALUES
    ('KEYWORD', 'käteinen', 0.20),
    ('KEYWORD', 'ei kuittia', 0.25),
    ('KEYWORD', 'kadonnut kuitti', 0.25),
    ('KEYWORD', 'perintä', 0.35),
    ('KEYWORD', 'pimeä', 0.50),
    ('KEYWORD', 'urgently need cash', 0.30),
    ('KEYWORD', 'no receipt', 0.25),
    ('CLAIMANT_FLAG', 'BLACKLISTED_FRAUD_HISTORY', 0.95),
    ('CLAIMANT_FLAG', 'FLAGGED_CLAIMANT_FI123', 0.75),
    ('CLAIMANT_FLAG', 'HIGH_FREQUENCY_30D', 0.40)
ON CONFLICT DO NOTHING;
