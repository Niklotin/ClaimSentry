# AIKA Claims MCP Server (Model Context Protocol)

Tämä MCP-palvelin tuo AIKA Claims -alustan deterministiset vakuutustyökalut suoraan tekoälyagenttien (Claude Desktop, Cursor, VS Code Copilot, Antigravity) käyttöön standardin Model Context Protocolin kautta.

---

## Tarjotut MCP-työkalut

1. `search_policy_clauses`
   - Hakee vakuutusehdot, omavastuut ja korvausrajat kategorioittain (esim. `BICYCLE`, `ELECTRONICS`, `LUGGAGE`).
2. `calculate_payout`
   - Laskee korvauksen deterministisesti (vuosittaiset ikävähennykset, omavastuut, korvauskatot ja pakollisen rikosilmoituksen).
3. `check_fraud_risk`
   - Arvioi petosindikaattorit, riskisanat (`käteinen`, `ei kuittia`, `pimeä`), estolistatut asiakkaat ja poikkeavat summat.
4. `record_claim_decision`
   - Tallentaa lopullisen korvauspäätöksen ja tekoälyn perustelut auditointilokiin.

---

## Käyttöönotto Claude Desktopissa

Lisää tiedostoon `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "aika-claims": {
      "command": "python",
      "args": [
        "/path/to/ClaimSentry/mcp/aika_mcp_server.py"
      ]
    }
  }
}
```

Tämän jälkeen Claude osaa automaattisesti kutsua AIKA Claims -työkaluja vahinkoilmoitusten käsittelyssä.
