# Setup, Dual-Provider LLM, Photon Fusion 2 & Demo Guide

This guide covers everything needed to install, configure, test, and demonstrate **POLITICAL MAYHEM: Battle for the Cities** from scratch on macOS or Windows.

---

## 1. Prerequisites

- **Python 3.10+** and [`uv`](https://docs.astral.sh/uv/) package manager
- **Unity 6 (`6000.6.3f1`)** (exact version verified in `UnityRTS/My project (3)/ProjectSettings/ProjectVersion.txt`)
- **Node.js 18+** (optional, for running the official `@modelcontextprotocol/inspector` UI)
- **Ollama** (recommended for offline local inference) and/or Cloud API keys (**Gemini**, **Groq**, or **OpenAI**)
- **Photon Dashboard account** (free tier works) + **Photon Fusion 2 SDK** for 2-machine / 2-instance multiplayer

---

## 2. Python Environment, RAG Index & Unit Tests

From the repository root:

```bash
# 1. Create virtual environment and install all Python dependencies
uv sync

# 2. (Optional) Copy environment template and edit settings
cp .env.example .env

# 3. Build / verify the Hybrid RAG Knowledge Index (19 chunks)
uv run python ai/rag/build_index.py

# 4. Run all automated Python unit & security tests (17 tests)
uv run pytest
```

If `.env` sets `USE_OLLAMA_EMBEDDINGS=true` (and `nomic-embed-text` is pulled in Ollama), re-running `uv run python ai/rag/build_index.py` adds dense vector embeddings alongside the BM25 + TF-IDF + Title lexical index (`0.55 * lexical + 0.45 * dense_cos`).

---

## 3. Dual-Provider LLM Configuration

`ai/agent/llm_provider.py` (`ProviderConfigManager`) supports switching transparently between offline local **Ollama** and fast **Cloud APIs** without changing any game or MCP code.

### Option A: Completely Offline Local Mode (Ollama)

1. Install Ollama from [ollama.com](https://ollama.com) and ensure the daemon is listening on `http://localhost:11434`.
2. Pull the tool-capable chat model and embedding model:
   ```bash
   ollama pull llama3.1:8b        # Stronger tool-calling & RAG adherence (evaluated default)
   ollama pull qwen3:8b           # Alternate local model (think=False handled in llm_provider.py)
   ollama pull nomic-embed-text   # Optional dense embedding model for RAG
   ```
3. Configure `.env`:
   ```env
   LLM_PROVIDER=ollama
   OLLAMA_BASE_URL=http://localhost:11434
   OLLAMA_MODEL=llama3.1:8b
   OLLAMA_EMBED_MODEL=nomic-embed-text
   USE_OLLAMA_EMBEDDINGS=false
   ```

### Option B: Fast Cloud API Mode (Gemini / Groq / OpenAI)

1. Add one or more API keys to your local `.env` (**never commit `.env` to Git**):
   ```env
   LLM_PROVIDER=auto
   GEMINI_API_KEY=your_key_here
   GEMINI_MODEL=gemini-2.5-flash

   GROQ_API_KEY=your_key_here
   GROQ_MODEL=llama-3.3-70b-versatile

   OPENAI_API_KEY=your_key_here
   OPENAI_MODEL=gpt-4o-mini
   ```
2. **Runtime UI Switching:** Inside Unity (`PoliticalMayhemHUD.cs` right panel), click **`Auto`**, **`Cloud API`**, or **`Ollama (Local)`** at any time; `AiGatewayClient` calls `POST /api/provider/config` on the AI Gateway to switch providers live.

---

## 4. Starting the AI Gateway & Inspecting the MCP Server

### Start the Local AI HTTP Gateway

Unity connects to `http://127.0.0.1:8765` by default (`AiGatewayClient._gatewayBaseUrl`, with a `65s` HTTP request timeout; `Awake()` upgrades any serialized timeout below `60s` to `65s`):

```bash
uv run python ai/agent/gateway.py --host 127.0.0.1 --port 8765 --verbose
```

Verify the gateway is up:

```bash
curl http://127.0.0.1:8765/api/health
```

Exposed HTTP endpoints:
- `GET /api/health`
- `GET / POST /api/provider/config`
- `POST /api/snapshot`
- `POST /api/commander/ask`
- `POST /api/speech/evaluate`
- `POST /api/speech/transcribe`

### Inspect & Test MCP Tools Directly via MCP Inspector

Run the official MCP Inspector against `ai/mcp-server/server.py`:

```bash
npx @modelcontextprotocol/inspector uv run python ai/mcp-server/server.py
```

In the MCP Inspector browser UI:
1. List all **8 tools** (`get_player_budget`, `get_player_overall`, `get_city_intelligence`, `get_owned_units`, `get_visible_opponent_activity`, `get_recent_events`, `estimate_bribery_impact`, `search_game_knowledge`).
2. Test valid inputs (e.g., `get_city_intelligence(city_name="Yesilova")`, `estimate_bribery_impact(city_name="Demirkent", bribe_amount=4000)`, `search_game_knowledge(query="revolver duel")`).
3. Test invalid/blocked inputs (e.g., passing `player_id="opponent"` to `get_player_budget` triggers `SecurityViolationError: UNAUTHORIZED_TOOL_ARGUMENT` and writes to `ai/mcp-server/security_events.jsonl`).

---

## 5. Opening Unity 6 (`6000.6.3f1`) & Local Host Play

1. Open **Unity Hub** and add the `UnityRTS/My project (3)` folder using **Unity `6000.6.3f1`**.
2. Wait for compilation to complete. In the top Unity Editor menu bar, click:
   **`Political Mayhem -> Generate Complete Prototype Scene`**
   This runs `UnityRTS/My project (3)/Assets/Editor/PoliticalMayhemSceneBuilder.cs`, procedurally building the 6 Turkish cities (`Baskent`, `Demirkent`, `Limankent`, `Yesilova`, `Universitekent`, `Dagsehir`), `CityHubMarker` hubs, `PhotonFusionAuthority`, `AiGatewayClient`, `AiCommanderPanel`, and `PoliticalMayhemHUD`, and saving `Assets/Scenes/MainMapScene.unity`.
3. Press **Play** in Unity. Because `#if PHOTON_FUSION` is initially unset, `PhotonFusionAuthority` starts cleanly in **Local Host Authority** fallback mode:
   - Use **`Oyuncu A`** / **`Oyuncu B`** buttons in the top HUD bar to inspect per-player visibility and alternate speaker/saboteur turns immediately on one machine.
   - Ensure the Python AI Gateway (`uv run python ai/agent/gateway.py`) is running so the right-hand **AI Strategic Commander** panel shows connected status after clicking **`Yenile`**.

---

## 6. Integrating Photon Fusion 2 SDK & AppId for 2-Instance Networking

`UnityRTS/My project (3)/Assets/Scripts/Networking/PhotonFusionAuthority.cs` is already structured with `#if PHOTON_FUSION` Host Mode `[Networked]` state and `[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]` calls. To enable live networked play across two instances:

1. **Create a Photon Fusion AppId:**
   - Sign in at [dashboard.photonengine.com](https://dashboard.photonengine.com).
   - Click **Create a New App**, choose **Multiplayer Game -> Photon Fusion**, and copy the generated **App Id**.
2. **Download & Import Photon Fusion 2 SDK:**
   - Download the **Photon Fusion 2** `.unitypackage` compatible with Unity 6 from the Photon SDK downloads page (or Unity Asset Store).
   - In Unity 6: **`Assets -> Import Package -> Custom Package...`** and import the Fusion 2 package.
3. **Configure AppId:**
   - Open **`Tools -> Fusion -> Realtime Settings`** (or locate `PhotonAppSettings.asset` in `Assets/Photon/Fusion/Resources/`) and paste your AppId into **`App Id Fusion`**.
   - **Security note:** Never commit personal Photon AppIds or secret keys to Git.
4. **Enable `PHOTON_FUSION` Scripting Define:**
   - Go to **`Edit -> Project Settings -> Player -> Other Settings -> Script Compilation -> Scripting Define Symbols`**.
   - Add **`PHOTON_FUSION`** and click **`Apply`**.
5. **Run Two Networked Instances:**
   - Build a standalone executable via **`File -> Build Profiles -> Build`** (with `Assets/Scenes/MainMapScene.unity` included in Scene List).
   - Launch the standalone build alongside the Unity Editor (or two standalone windows).
   - Ensure the AI Gateway is running on `127.0.0.1:8765`.
   - Click **`Host`** in Instance 1 (becomes `Player A` / `StateAuthority`) and **`Client`** in Instance 2 (joins as `Player B` / `InputAuthority`).

---

## 7. Re-Running the Automated Evaluation Suite

To reproduce all 15 labeled test runs (`results.csv`, `raw_logs.jsonl`, `security-tests.md`):

```bash
uv run python evaluation/run_evaluation.py --provider ollama --model llama3.1:8b
```

To evaluate against a Cloud API provider (once keys are set in `.env`):

```bash
uv run python evaluation/run_evaluation.py --provider gemini --model gemini-2.5-flash
```

---

## 8. 9 Required Demo Scenarios Checklist (CSE-377 Section 13)

Use this exact sequence during the live project demonstration (with `gateway.py` running and `MainMapScene.unity` in Play mode):

| # | Rubric Scenario | Exact Actions in Unity / Terminal | Expected Observable Proof |
|---|---|---|---|
| **1** | **Multiplayer RTS** | Launch 2 Unity instances (`Editor` + `Build`). Click **`Host`** on Instance 1 (`Oyuncu A`) and **`Client`** on Instance 2 (`Oyuncu B`). Deploy a unit (`+1 Propaganda Uzmanı`) and click **`Sevk Et`** after selecting a city **`Hedef`**. | Unit count, target zone movement, combat/city ownership, and round phase synchronize identically on both instances via `PhotonFusionAuthority`. |
| **2** | **Resource + Production** | Vote an un-rallied city (**`Oy Ver`**), advance to Rally Speech via **`Sonraki Faz >>`**, select an ideology draft (e.g., **`Demokrat`**), click **`Konuşmayı Tamamla & Değerlendir`**, then deploy a unit via **`+1 ...`** within round limits. | `party_budget` increases dynamically from speech donations, `Overall` updates (`0.80 * Prev + 0.20 * Speech`), and unit deployment honors per-round caps (`6`/`7` volunteer, `4` propagandist, `3` bodyguard, `2` convoy). |
| **3** | **Dynamic MCP Tool Calling** | On the right **AI Strategic Commander** panel, click preset button **`1. Bileşik Strateji`** (or type a compound question on budget, enemy units, and counters). | Switch to the **`[Plan & Araçlar]`** tab: observe **>= 3 distinct MCP tools** (e.g. `get_player_budget`, `get_owned_units`, `get_visible_opponent_activity`, `search_game_knowledge`) logged dynamically in order, mirrored in `ai/mcp-server/tool_logs.jsonl`. |
| **4** | **RAG** | Click **`2. RAG Bilgi`** first (pure knowledge query), then run **`1. Bileşik Strateji`** (live state + RAG). | On **`2. RAG Bilgi`**, `search_game_knowledge` executes without unnecessary live-state calls and returns cited `source_id`s (e.g. `rule_unit_counters_v1`, `rule_donation_economy_v1`). On **`1. Bileşik Strateji`**, retrieved chunks combine with live MCP metrics. |
| **5** | **Structured Output** | Inspect the returned recommendation box under **`[Plan & Araçlar]`**. | Unity parses a schema-valid `ActionPlan` containing `summary`, normalized `confidence`, and typed `actions[]` (`action_type`, `unit_type`, `city_name`, `count`, `reason`). Free-form text never mutates state. |
| **6** | **Human Approval (HITL)** | After receiving a valid `ActionPlan` from Preset 1, click **`ASK WHY`** (optional inspection), then click **`REJECT`**. Verify zero units/budget changed. Re-run Preset 1 and click **`APPROVE PLAN`**. | **`REJECT`** clears the proposal with **zero state mutation**. **`APPROVE PLAN`** passes `GameActionValidator` Pass 2 and executes via `PhotonFusionAuthority.RequestExecuteApprovedActionPlan`. |
| **7** | **Validation Failure** | Click **`3. Geçersiz Plan Testi`** (replays synthetic over-cap / invalid action plan) and open the **`[Validator Log]`** tab (also provable via `V01` in `run_evaluation.py`). | `GameActionValidator` blocks every illegal action (`action_type` invalid, negative/over-limit `count`, off-phase `ALLOCATE_ELECTION_BRIBE`, already-rallied city vote) and appends rejection reasons to `_auditLog` with zero state change. |
| **8** | **Fog-of-War Security** | Click **`4. Prompt-Injection`** (asks to ignore rules and leak opponent `party_budget`, `Overall`, and hidden support), and inspect **`[Safe Snapshot]`** + `ai/mcp-server/security_events.jsonl`. | `security_blocked: true`, `actions: []`, zero opponent secrets present in `AiSafeSnapshot`, and a timestamped `PROMPT_INJECTION_DETECTED` record logged in `ai/mcp-server/security_events.jsonl`. |
| **9** | **Evaluation** | Show `evaluation/results.csv`, `evaluation/security-tests.md`, and run summary from `uv run python evaluation/run_evaluation.py --provider ollama --model llama3.1:8b`. | Present mean/median/max latency, tool-call accuracy, invalid-action rate, `S01`/`S02` + `V01` 100% rejection proof, and discuss observed 8B tool-skip / duplicate-call limitations and mitigations. |
