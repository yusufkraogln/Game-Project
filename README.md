# POLITICAL MAYHEM: Battle for the Cities

**Multiplayer Political RTS / Strategy Game** built for **CSE-377 Game Programming (2026)** using **Unity 6 (C#)**, **Photon Fusion 2 (Host Mode)**, a **Custom MCP Server**, **Dual-Provider LLM Agents**, and **Hybrid RAG**.

> Full game design specification: [OurGame.md](OurGame.md)

---

## Core Game Concept

Two political rivals compete across **6 fixed strategic cities** over **6 rally rounds** followed by **Election Night**:

| City (Canonical ID) | Display Name | Population |
|---|---|---:|
| `Baskent` | Başkent | 120,000 |
| `Demirkent` | Demirkent | 110,000 |
| `Limankent` | Limankent | 90,000 |
| `Yesilova` | Yeşilova | 80,000 |
| `Universitekent` | Üniversitekent | 70,000 |
| `Dagsehir` | Dağşehir | 60,000 |

- **5 Ideologies (Canonical Keys):** `nationalist` (*Milliyetçi*), `liberal` (*Liberal*), `democrat` (*Demokrat*), `communist` (*Komünist*), `imperialist` (*Emperyalist*).
- **Dynamic Overall Progression:** Players start without a fixed party ideology. After every speech, LLM evaluations update the five independent Overall ratings (`0–100`) deterministically:
  `New Overall = 0.80 * Previous Overall + 0.20 * Speech Ideology`
- **Donation Economy (`party_budget`):** Party money is earned solely from rally donations and is **never spent during rally rounds** (campaign units deploy via per-round capacity limits). All accumulated `party_budget` is saved for **Election Night**, where optional secret bribes can shift city vote shares by at most **2 percentage points**.
- **Victory Condition:** Win the majority of the 6 cities (ties at 3–3 are broken by total popular vote), concluding with a 60-second Balcony Speech.

---

## Architecture Overview

```text
PLAYER
  |
  v
Unity 6 RTS Client / Photon Fusion 2 Host
  |  (Fog-of-War filtered AiSafeSnapshot + player question)
  v
AI Gateway / Agent Runtime (ai/agent/)
  |
  +--> Dual-Provider LLM Agent (reason + dynamic tool calling)
         |
         v
       Custom MCP Server (ai/mcp-server/)
         +--> Live Safe State Tools (budget, overall, cities, units, visible opponent, events, bribery estimate)
         +--> Hybrid RAG Tool (search_game_knowledge -> ai/rag/)
  |
  v
Structured ActionPlan JSON (ai/agent/schemas/action_plan.schema.json)
  |
  v
Unity Human-in-the-Loop Approval UI (APPROVE / REJECT)
  |
  v
Deterministic GameActionValidator -> Photon Fusion 2 Host Authority
```

### Security & Authority Principles
1. **Fog of War Before the Model:** `AiSafeSnapshot` (`ai/agent/schemas/safe_snapshot.schema.json`) strips `opponent_budget`, `opponent_overall`, `hidden_support_rates`, and private opponent plans before any data reaches the AI Gateway or MCP tools.
2. **Server-Side Identity Binding:** `match_id` and `player_id` come from the authenticated session; the LLM cannot request opponent data by passing a different player ID.
3. **Human-in-the-Loop & Double Validation:** The AI only proposes schema-validated `ActionPlan` JSON. No gameplay mutation happens without explicit player approval and deterministic server-side re-validation.

---

## Project Structure

```text
Proje/
├── UnityRTS/
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── Core/
│   │   │   ├── Units/
│   │   │   ├── Combat/
│   │   │   ├── Rally/
│   │   │   ├── Election/
│   │   │   ├── Networking/
│   │   │   ├── AI/
│   │   │   └── UI/
│   │   └── Editor/
│   ├── Packages/
│   └── ProjectSettings/
├── ai/
│   ├── agent/
│   │   └── schemas/
│   │       ├── action_plan.schema.json
│   │       ├── safe_snapshot.schema.json
│   │       └── speech_eval.schema.json
│   ├── mcp-server/
│   │   ├── tools/
│   │   └── tests/
│   └── rag/
│       ├── knowledge/
│       │   ├── units.json
│       │   ├── technologies.json
│       │   └── rules.md
│       ├── index/
│       │   └── knowledge_index.json
│       ├── text_utils.py
│       ├── build_index.py
│       └── retriever.py
├── evaluation/
├── docs/
│   └── report/
├── .env.example
├── .gitignore
├── pyproject.toml
└── OurGame.md
```

---

## Dual-Provider LLM Setup

Copy `.env.example` to `.env` and configure your preferred provider (`LLM_PROVIDER`):

```bash
cp .env.example .env
```

- **`auto` (default):** Uses local **Ollama** (`qwen3:8b` + optional `nomic-embed-text`) when reachable at `OLLAMA_BASE_URL`, or falls back to a configured cloud provider (`gemini`, `openai`, or `groq`).
- **`ollama`:** Offline-capable local inference (`OLLAMA_MODEL=qwen3:8b`, `OLLAMA_EMBED_MODEL=nomic-embed-text`).
- **`gemini`:** Google Gemini (`GEMINI_API_KEY`, `GEMINI_MODEL=gemini-2.5-flash`).
- **`openai`:** OpenAI (`OPENAI_API_KEY`, `OPENAI_MODEL=gpt-4o-mini`).
- **`groq`:** Groq (`GROQ_API_KEY`, `GROQ_MODEL=llama-3.3-70b-versatile`).

> Never commit `.env` or API keys to Git.

---

## Quickstart (Python AI & RAG Workspace)

Requires Python 3.10+ and [`uv`](https://docs.astral.sh/uv/).

```bash
# 1. Create virtual environment and install dependencies
uv sync

# 2. Build the local hybrid RAG index (BM25 + TF-IDF)
uv run python ai/rag/build_index.py

# Optional: include local Ollama embeddings (requires Ollama + nomic-embed-text)
uv run python ai/rag/build_index.py --use-ollama-embeddings

# 3. Run built-in English & Turkish RAG verification queries
uv run python ai/rag/retriever.py

# Or run a custom query
uv run python ai/rag/retriever.py --query "How does Election Night bribery work?" --top-k 3
```
