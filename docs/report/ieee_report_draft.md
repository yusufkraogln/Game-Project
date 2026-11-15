# POLITICAL MAYHEM: An Agentic Multiplayer Political RTS with Custom MCP Tooling, Hybrid RAG, and Dual-Layer Authority Validation in Unity 6

**CSE-377 Game Programming — Project Technical Report (IEEE Format Draft)**  
*Unity 6 (`6000.6.3f1`) · Photon Fusion 2 (Host Mode) · Model Context Protocol (MCP) · Hybrid RAG · Dual-Provider LLM*

---

## Abstract

We present **POLITICAL MAYHEM: Battle for the Cities**, a 3D multiplayer political Real-Time Strategy (RTS) prototype built in **Unity 6 (`6000.6.3f1`)** that integrates an out-of-process **Agentic AI Strategic Commander** into a **Photon Fusion 2** Host-Mode authoritative gameplay loop. Rather than exposing raw game state or arbitrary Unity actions to an LLM, the architecture enforces strict authority separation: Unity emits a Fog-of-War filtered `AiSafeSnapshot` to a local HTTP AI Gateway, where a bounded `AgentRunner` dynamically invokes **8 typed read-only tools** on a custom **Model Context Protocol (MCP)** server (`SafePoliticalMCPServer`) backed by a **19-chunk bilingual Hybrid RAG** retriever (`BM25 + TF-IDF + Title`, with optional Ollama `nomic-embed-text` dense fusion). All recommendations return as Draft 2020-12 schema-constrained `ActionPlan` JSON payloads requiring explicit **Human-in-the-Loop (HITL)** approval (`APPROVE PLAN` / `REJECT` / `ASK WHY`) and **double deterministic validation** (Python `validator.py` + C# `GameActionValidator.cs`) prior to Host execution. Reproducible end-to-end evaluation across 15 labeled scenarios running locally on Ollama `llama3.1:8b` achieved **86.7% (`13/15`) tool-selection intent accuracy** (**53.3% [`8/15`] strict schema-valid tool execution** due to 10/46 model-stringified numeric parameters), **0.0% (`0/9`) post-validation invalid AI actions**, **100.0% (`6/6`) deterministic rejection** on synthetic illegal plans, **100.0% (`2/2`) prompt-injection defense**, and **5/5 (`100.0%`) security boundary probe verification** (mean LLM query latency `25,956.94 ms`, median `26,125.97 ms`, and `2.30–5.02 ms` guardrail response latency), while also documenting real 8B local-model failure modes such as `qwen3:8b` parametric RAG tool-skipping and numeric argument stringification.

**Keywords:** Unity 6, Photon Fusion 2, Multiplayer RTS, Model Context Protocol (MCP), Retrieval-Augmented Generation (RAG), Human-in-the-Loop, Fog-of-War Security.

---

## 1. Introduction

Real-Time Strategy (RTS) games combine high-cognitive-load multi-front decision-making—managing constrained economies, asymmetric unit counters, hidden opponent information (Fog of War), and timing windows—with real-time spatial execution. Integrating Large Language Models (LLMs) into multiplayer RTS prototypes presents four fundamental engineering hazards:

1. **Authority Bypass:** Allowing probabilistic text outputs to mutate game state directly breaks deterministic rules and multiplayer fairness.
2. **Fog-of-War Leakage:** Stuffing complete match snapshots into prompts exposes hidden opponent budgets, strategies, or unseen units to the LLM and player.
3. **Rule Hallucination:** Relying on LLM parametric memory causes errors on custom unit caps, progression thresholds, and scoring formulas.
4. **Prompt Injection & Identity Spoofing:** Player-supplied natural language queries can attempt instruction overrides or cross-player impersonation.

**POLITICAL MAYHEM: Battle for the Cities** solves these challenges by cleanly separating **probabilistic strategic advising** (Python MCP + RAG + LLM Agent) from **deterministic authoritative execution** (Unity 6 + Photon Fusion 2 Host Mode + C# `GameActionValidator`). Our main engineering contributions are:

- A complete Unity 6 political-campaign RTS implementing 6 interconnected Turkish-themed cities, 5 dynamic ideological *Overall* dimensions, 6 alternating rally rounds with verbal/physical sabotage and a 5-suspect Revolver duel, a single-currency donation economy locked until Election Night, 4 deployable units + 2 rally-role units, 5 threshold technologies, and deterministic Election Night resolution.
- A custom **8-tool MCP server** (`ai/mcp-server/server.py`) enforcing server-bound session identities and schema-validated Fog-of-War snapshot sanitization (`VisibilityService`).
- A **bilingual Turkish/English Hybrid RAG** pipeline (`ai/rag/`) indexing 19 structured chunks across `units.json`, `technologies.json`, and `rules.md`.
- A **Dual-Provider LLM runtime** (`Ollama` offline local + `Gemini`/`Groq`/`OpenAI` cloud APIs) switchable at runtime inside Unity without altering gameplay or security boundaries.
- A reproducible **15-scenario evaluation suite and security probe harness** (`evaluation/run_evaluation.py`) producing verifiable CSV metrics, JSONL execution traces, and audit logs.

---

## 2. Game Design & Academic Adaptation

### 2.1 Core Political-RTS Gameplay Loop

*POLITICAL MAYHEM* adapts traditional Age-of-Empires-style multiplayer RTS mechanics into an asymmetric two-player election campaign (`Player A` vs. `Player B`) across **6 strategic cities**:

| City | Population | Key Demographic Profile (Seeded Baseline) | RTS Equivalent |
|---|---:|---|---|
| **Baskent** | 120,000 | Nationalist 34%, Democrat 22%, Liberal 20%, Imperialist 16%, Communist 8% | Capital Strategic Hub |
| **Demirkent** | 110,000 | Industrial hub (`IndustrialSquare`, `FoundryGate`) | Heavy Production Node |
| **Limankent** | 90,000 | Commercial port (`HarborSquare`, `HarborGate`) | Trade & Economy Node |
| **Yesilova** | 80,000 | Democrat 34% (`MarketBoulevard`) | Agricultural / Grassroots Node |
| **Universitekent** | 70,000 | Democrat 34%, Liberal 32% | Research / Youth Hub |
| **Dagsehir** | 60,000 | Nationalist 40% | Highland Frontier Hub |

### 2.2 Economy, Units, Technologies, and Win Condition

- **Single Resource (`party_budget`):** Rather than wood/food/gold, the game uses a single monetary stockpile (`party_budget`) governed by citizen donations after each rally speech (`Donation = Group Population × Donation Propensity × Speech Impact × Overall Alignment × Scale Factor`), where Democratic/Liberal tendencies amplify donation rates while Communist redistribution reduces them (`rule_donation_economy_v1`). Crucially, `party_budget` is **locked against deployment spending during Rounds 1–6** and can only be spent during **Election Night Phase 2 (`ELECTION_BRIBE`)**.
- **RTS Units & Counters (`units.json` & `CombatResolver.cs`):** Players deploy 4 unit types within per-round limits (`ROUND_DEPLOY_LIMITS`): `campaign_volunteer` (cap 6, becomes 7 with Democrat tech; scouts & builds voter turnout), `propagandist` (cap 4; shifts local ideological pressure), `bodyguard` (cap 3; protects speakers and counters `propagandist`/`saboteur_agent`), and `rally_convoy` (cap 2; high-mobility multi-zone campaign presence), complemented during rallies by the 5-person `security_squad` and opponent `saboteur_agent`.
- **Dynamic Ideology Progression & Technologies (`OverallManager.cs`):** Neither player picks a static faction. Five `Overall` ratings (`nationalist`, `liberal`, `democrat`, `communist`, `imperialist`, 0–100) update after each speech via:
  $$\text{Overall}_{t} = 0.80 \cdot \text{Overall}_{t-1} + 0.20 \cdot \text{SpeechEffectiveness}$$
  Crossing `Overall >= 40` deterministically unlocks 5 passive faction technologies: `tech_nationalist_fervor_v1`, `tech_liberal_media_network_v1`, `tech_democrat_grassroots_v1` (+1 volunteer cap), `tech_communist_union_mobilization_v1`, and `tech_imperialist_patronage_v1`.
- **6 Rally Rounds, Sabotage & Revolver Duel (`SabotageAndRevolverController.cs`):** Across 6 alternating rounds, players vote for an un-rallied city (`CITY_VOTE`), deploy/reposition units (`PREPARATION`), and perform a 60-second `RALLY_SPEECH` while the opponent acts as `saboteur_agent`. **Verbal Sabotage** debates rhetorically with zero legal risk; **Physical Sabotage** triggers crowd disruption that risks arrest by the 5-person `security_squad`, leading to a 5-suspect **Revolver Duel** (`REVOLVER_DUEL`) where a correct shot blocks the saboteur's next speaking turn and a wrong shot imposes a severe reputation penalty (`rule_sabotage_revolver_v1`).
- **Election Night & Win Condition (`ElectionNightController.cs`):** After Round 6, each of the 6 cities resolves across 3 phases: (1) simultaneous 15-second Final Speeches (`ELECTION_FINAL_SPEECH`), (2) confidential, simultaneous, non-refundable Bribery (`ELECTION_BRIBE`) subject to diminishing returns and **strictly capped at a 2.0 percentage-point net vote swing** (`MAX_BRIBE_SWING_PCT = 2.0`), and (3) deterministic support computation (`ELECTION_RESULT`):
  $$\text{Base Support} = 0.30 \cdot \text{Overall\_Alignment} + 0.30 \cdot \text{Rally\_Speech} + 0.15 \cdot \text{Campaign\_Activity} + 0.10 \cdot \text{Sabotage\_Effect} + 0.15 \cdot \text{Final\_Speech}$$
  Winning the majority of the 6 cities (with total popular vote tie-breaker at 3–3) wins the match and unlocks the celebratory **Balcony Speech (`BALCONY_SPEECH`)**.

---

## 3. System Architecture

The system isolates the Unity 6 simulation from probabilistic reasoning via an out-of-process Python runtime:

```mermaid
flowchart TD
    Player["Player Input (Question / Preset)"] --> Unity["Unity 6 Client & PoliticalMayhemHUD"]
    Unity --> Snap["AiSafeSnapshot Builder (Fog-of-War Filtered)"]
    Snap -->|"HTTP POST /api/commander/ask"| GW["AI Gateway (ai/agent/gateway.py :8765)"]
    GW -->|"Bind session player_id & check X-Player-Id"| Runner["AgentRunner (max 6 steps + Guardrail)"]
    Runner <-->|"Tool-calling loop"| LLM["ProviderConfigManager (Ollama / Gemini / Groq / OpenAI)"]
    Runner -->|"8 Read-Only Tools"| MCP["SafePoliticalMCPServer (ai/mcp-server/server.py)"]
    MCP --> Store["SafeSessionStore + VisibilityService"]
    MCP --> RAG["Hybrid RAG Retriever (19 Chunks)"]
    Runner -->|"Pass 1 Validation"| PyVal["validator.py (Draft 2020-12 Schema)"]
    PyVal -->|"ActionPlan JSON + Trace"| Unity
    Unity -->|"Human Decision"| HITL["AiCommanderPanel: APPROVE / REJECT / ASK WHY"]
    HITL -->|"REJECT"| NoOp["Zero Mutation"]
    HITL -->|"APPROVE PLAN"| CsVal["C# GameActionValidator (Pass 2 on Live State)"]
    CsVal -->|"Valid"| Host["PhotonFusionAuthority (Host StateAuthority)"]
```

Keeping MCP and LLM orchestration out of the Unity process prevents the model from inspecting arbitrary C# memory, simplifies cross-platform HTTP integration (`AiGatewayClient.cs`), enables reproducible offline CLI evaluation (`run_evaluation.py`), and permits hot-swapping between local Ollama and Cloud APIs without recompiling Unity.

---

## 4. Multiplayer Method

### 4.1 Choice of Photon Fusion 2 Host Mode over PUN 2

We selected **Photon Fusion 2 Host Mode** over Shared Mode and legacy Photon PUN 2. PUN 2 is in maintenance/LTS and relies on trust-based Master Client RPC synchronization without typed state authority. Conversely, Fusion 2 Host Mode (`PhotonFusionAuthority.cs`) establishes Player A's runner as the single authoritative `StateAuthority` owning `[Networked]` match properties (`party_budget`, phase, round counter, city rally completion, unit rosters, and secret Election Night bribe locks). Connected clients hold `InputAuthority` and can only submit validated intent via `[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]`—including `RequestExecuteApprovedActionPlan`.

### 4.2 Dual-Compile Local Fallback & Unified AI Authority Path

To keep the repository runnable prior to importing the external Fusion 2 SDK `.unitypackage`, `PhotonFusionAuthority.cs` wraps Fusion types under `#if PHOTON_FUSION`, falling back seamlessly to a local Host Authority simulation loop when `PHOTON_FUSION` is undefined (see `docs/setup.md`). Crucially, **AI-approved plans never bypass multiplayer authority**: once a human approves an `ActionPlan` in `AiCommanderPanel`, C# `GameActionValidator` validates it locally and dispatches `RequestExecuteApprovedActionPlan`, routing through the exact same Host `ExecuteValidatedPlanOnAuthority` path as manual HUD clicks (`Oy Ver`, `+1 Unit`, `Sevk Et`, `Gizli Rüşveti Onayla`).

---

## 5. MCP Server and Agent Design

### 5.1 Eight Granular MCP Tools

To avoid the `get_everything()` anti-pattern, `SafePoliticalMCPServer` (`ai/mcp-server/server.py`) defines **8 typed tools** registered in `TOOL_REGISTRY` (`7` live-state/analytical tools + `1` RAG retrieval tool):

| MCP Tool | Input Schema | Output & Responsibility |
|---|---|---|
| `get_player_budget()` | *(none; server-bound session)* | Authenticated player's `party_budget`, `phase`, `round_number`, `unlocked_bonuses` |
| `get_player_overall()` | *(none)* | Authenticated player's 5 `Overall` ratings and `>= 40` unlocked bonuses |
| `get_city_intelligence(city_name="ALL")` | Optional city enum/string | City populations, ideology demographics, `rally_done`, `winner`, safe presence |
| `get_owned_units(city_name="ALL")` | Optional city filter | Player's deployed unit counts + remaining per-round `ROUND_DEPLOY_LIMITS` |
| `get_visible_opponent_activity()` | *(none)* | *Only* Fog-of-War visible opponent unit sightings and public speech excerpts |
| `get_recent_events(last_seconds=120)` | `int` in `[1, 3600]` | Sanitized recent match events within time window |
| `estimate_bribery_impact(city_name, bribe_amount)` | Canonical city + `number >= 0` | Phase lock check, affordability check, and capped swing (`<= 2.0pp`) |
| `search_game_knowledge(query, top_k=3)` | Query string + `int` in `[1, 5]` | Top-$k$ Hybrid RAG chunks with `source_id`, `title`, `type`, `score`, `text` |

Every tool call executes through `call_tool_safely`, recording duration, status (`ok`, `error`, `blocked`), result count, and regex-redacted metadata in `ai/mcp-server/tool_logs.jsonl`.

### 5.2 Bounded Agent Loop & Dual-Provider Support

`AgentRunner` (`ai/agent/agent_runner.py`) enforces `MAX_AGENT_STEPS = 6` to prevent runaway tool loops. At Step 0, `detect_prompt_injection` scans user input; clean queries enter an iterative chat-plus-tool loop via `ProviderConfigManager` (`llm_provider.py`), supporting offline Ollama (`llama3.1:8b`, `qwen3:8b` with `think=False` and `num_ctx=4096`) and Cloud APIs (`gemini-2.5-flash`, `llama-3.3-70b-versatile`, `gpt-4o-mini`). Observed logs show dynamic tool chaining across `0..5` calls per query (e.g., `K01` triggers only `search_game_knowledge`, while `C01` chains `get_visible_opponent_activity -> get_owned_units -> get_city_intelligence -> search_game_knowledge`).

---

## 6. Hybrid RAG Method

### 6.1 Knowledge Corpus (19 Verified Chunks)

Static and semi-static rules live separately from live MCP snapshots across three curated files inside `ai/rag/knowledge/`, built reproducibly via `uv run python ai/rag/build_index.py` into `ai/rag/index/knowledge_index.json` (`chunk_count = 19`, `avgdl = 308.5789` tokens):
- **`units.json` (6 chunks):** `unit_campaign_volunteer_v1`, `unit_propagandist_v1`, `unit_bodyguard_v1`, `unit_security_squad_v1`, `unit_saboteur_agent_v1`, `unit_rally_convoy_v1`.
- **`technologies.json` (5 chunks):** `tech_nationalist_fervor_v1`, `tech_liberal_media_network_v1`, `tech_democrat_grassroots_v1`, `tech_communist_union_mobilization_v1`, `tech_imperialist_patronage_v1`.
- **`rules.md` (8 chunks):** `rule_cities_v1`, `rule_overall_formula_v1`, `rule_rally_rounds_v1`, `rule_sabotage_revolver_v1`, `rule_donation_economy_v1`, `rule_election_night_v1`, `rule_unit_counters_v1`, `rule_fog_of_war_v1`.

### 6.2 Bilingual Tokenization & Hybrid Retrieval Scoring

Because gameplay drafts mix English system queries and Turkish political rhetoric, `ai/rag/text_utils.py` performs diacritic folding (`ş->s`, `ğ->g`, `ı->i`, `ü->u`, `ö->o`, `ç->c`) and emits 5- and 7-character morphological prefix stems (`s5:`, `s7:`). `ai/rag/retriever.py` computes a multi-signal lexical score:
$$\text{lexical} = 0.50 \cdot \text{BM25}_{\text{norm}} + 0.40 \cdot \cos_{\text{TF-IDF}} + 0.10 \cdot \text{title\_overlap}$$
When `USE_OLLAMA_EMBEDDINGS=true` and Ollama `nomic-embed-text` vectors are built into the index, scoring fuses dense cosine similarity via $\text{score} = 0.55 \cdot \text{lexical} + 0.45 \cdot \cos_{\text{dense}}$.

**Sample Verbatim Retrievals Observed During Evaluation (`raw_logs.jsonl`):**
- **`K01`** (*"How does the Revolver duel mechanic work if a physical saboteur is caught?"*): `search_game_knowledge` -> **`rule_sabotage_revolver_v1`** (score `0.6655`).
- **`K02`** (*"Election Night deterministic vote formula weight... max swing"*): -> **`rule_election_night_v1`** (score `0.6579`), **`rule_overall_formula_v1`** (score `0.6262`).
- **`E03`** (*"citizen donation economy rules and Overall ideology impact on donations"*): -> **`rule_donation_economy_v1`** (`0.6541`), **`tech_liberal_media_network_v1`** (`0.3818`), **`tech_imperialist_patronage_v1`** (`0.2847`).

---

## 7. Structured Actions and Human Control

### 7.1 `ActionPlan` JSON Schema & Normalization

Free-form prose never touches gameplay. `ai/agent/schemas/action_plan.schema.json` (JSON Schema Draft 2020-12, `additionalProperties: false`) requires `summary` (`1..400` chars), `confidence` (`0..1`), and `actions` (`0..8` objects, empty array valid for advice/blocked queries). Each action requires `action_type` $\in \{\texttt{DEPLOY\_UNIT}, \texttt{MOVE\_GROUP}, \texttt{VOTE\_RALLY\_CITY}, \texttt{CHOOSE\_SPEECH\_STRATEGY}, \texttt{ALLOCATE\_ELECTION\_BRIBE}\}$ and a grounded `reason`, with integer bounds `count in [1, 20]` and `bribe_amount >= 0`. Before schema checking, `normalize_action_plan` (`validator.py`) maps Turkish and English aliases (e.g., `"Milliyetçi"` -> `"nationalist"`, `"Koruma"` -> `"bodyguard"`).

### 7.2 Human-in-the-Loop UI & "Why Validate Twice?"

In Unity, `AiCommanderPanel.cs` and `PoliticalMayhemHUD.cs` display the parsed plan, confidence, tool log, and RAG citations alongside **`APPROVE PLAN`**, **`REJECT`**, and **`ASK WHY`**. Clicking **`REJECT`** logs the refusal with **zero game state mutation**. Clicking **`APPROVE PLAN`** triggers **`GameActionValidator` Pass 2** immediately prior to `PhotonFusionAuthority.RequestExecuteApprovedActionPlan`.

**Why Validate Twice?** Pass 1 (`validator.py`) validates JSON well-formedness and legality against the snapshot captured *when the query was sent*. During the seconds while the LLM reasons and the player inspects the plan, RTS state moves forward: the player may manually deploy units (exhausting the round cap), advance out of `ELECTION_BRIBE`, finish a city rally, or spend `party_budget`. Therefore, **Pass 2 (`GameActionValidator.cs`) must re-verify every action against the Host's current authoritative state at approval time**, logging outcomes in `_auditLog`.

---

## 8. Security: Fog-of-War, Authorization & Injection Defense

We enforce the four CSE-377 security layers strictly in code rather than prompt instructions alone:

1. **Filter Before Model (`VisibilityService` + `AiSafeSnapshot.cs`):** `VisibilityService.sanitize_and_validate_snapshot` strips all keys in `FORBIDDEN_SECRET_FIELDS` (`opponent_budget`, `opponent_overall`, `hidden_support_rates`, `opponent_bribes`, `opponent_secret_plan`, etc.) and validates the remainder against `safe_snapshot.schema.json` (`additionalProperties: false`). Hidden data never enters the MCP session store or LLM context.
2. **Never Trust Identity From the Model (`SafeSessionStore` + `GatewayApp`):** Tool calls passing `IDENTITY_SPOOF_ARG_KEYS` (`player_id`, `target_player`, `opponent_id`, `session_player`) are rejected (`ok: False, error_type: 'security_error'`) by `verify_tool_call_security`. Server-side snapshots bind `authenticated_player_id` and `GatewayApp` returns HTTP `403` when `X-Player-Id` mismatches payload identity.
3. **Prompt-Injection Guardrail (`detect_prompt_injection`):** Adversarial instructions attempting rule overrides (`instruction_override`) or secret extraction (`hidden_opponent_extraction`) are intercepted at Step 0 in `2.30–5.02 ms`, logging `PROMPT_INJECTION_DETECTED` to `ai/mcp-server/security_events.jsonl`, querying only safe `get_visible_opponent_activity()`, and returning `actions: []`, `security_blocked: True`.
4. **Safe Action Execution:** No shell, filesystem, SQL, or HTTP tools exist; whitelisted action enums, numeric bounds, round caps, phase locks, and Host double-validation reject 100% of unauthorized proposals.

---

## 9. Evaluation & Observed Failure Analysis

We authored a reproducible 15-scenario test suite (`evaluation/prompts.jsonl`, executed via `uv run python evaluation/run_evaluation.py --provider ollama --model llama3.1:8b` on local hardware against Ollama `llama3.1:8b`): **4 Economy (`E01`–`E04`)**, **4 Combat (`C01`–`C04`)**, **2 Ideology/Tech (`T01`–`T02`)**, **2 Pure Knowledge-Only (`K01`–`K02`)**, **1 Stale-State/Validation (`V01` + 6-case synthetic stress battery)**, and **2 Adversarial Prompt-Injection Security (`S01`–`S02` + 5 direct API probes)**.

### 9.1 Headline Metrics Summary (Local Ollama `llama3.1:8b`)

| Metric | Observed Result | Notes & Scope |
|---|---|---|
| **Latency — All 15 Scenarios** | Mean **22,496.50 ms** · Median **24,549.89 ms** · Max **46,638.03 ms (`E03`)** | Includes 2 guardrail fast-blocks (`2.30 ms`, `5.02 ms`) |
| **Latency — 13 Live LLM Queries** | Mean **25,956.94 ms** · Median **26,125.97 ms** · Max **46,638.03 ms (`E03`)** | Multi-step 8B Ollama inference on CPU/GPU |
| **Tool-Selection Accuracy (Intent)** | **13 / 15 (86.7%)** | Model chose every required tool on 13/15 prompts; missed `get_player_budget` (`E02`) & `get_city_intelligence` (`T01`) |
| **Strict Schema-Valid Tool Execution** | **8 / 15 (53.3%)** (`36/46` calls `ok=True`) | `10/46` tool calls failed MCP numeric checks due to stringified args (`"3"`, `"4000"`, `"300"`) |
| **Live AI Invalid-Action Rate** | **0 / 9 (0.0%)** | Zero schema/rule violations across all 9 proposed `ActionPlan` actions |
| **Deterministic Invalid-Plan Battery (`V01`)** | **6 / 6 (100.0% rejected)** | Caught unknown enum, negative count, cap breach, off-phase bribe, over-budget bribe, already-rallied city |
| **Prompt-Injection Security (`S01`–`S02`)** | **2 / 2 (100.0% blocked)** | `hidden_data_leaked = False`, `unauthorized_actions = 0`, logged in `security_events.jsonl` |
| **Direct Security Boundary Probes** | **5 / 5 (100.0% passed)** | Spoofed tool arg, dirty snapshot stripping, strict snapshot schema, session binding, Gateway HTTP 403 |

### 9.2 Per-Category Breakdown

| Category | $N$ | Selection Intent Correct | Strict `ok=True` Execution | Proposed Actions (Rejected) | Mean Latency (ms) |
|---|---:|---:|---:|---:|---:|
| Economy (`E01`–`E04`) | 4 | 3 / 4 | 2 / 4 | 2 (0 rejected) | 26,285.43 |
| Combat (`C01`–`C04`) | 4 | 4 / 4 | 1 / 4 | 7 (0 rejected) | 28,672.51 |
| Ideology/Tech (`T01`–`T02`) | 2 | 1 / 2 | 0 / 2 | 0 (0 rejected) | 24,214.15 |
| KnowledgeOnly (`K01`–`K02`) | 2 | 2 / 2 | 2 / 2 | 0 (0 rejected) | 24,096.73 |
| Validation (`V01`) | 1 | 1 / 1 | 1 / 1 | 0 live + **6/6** synthetic rejected | 20,986.64 |
| Security (`S01`–`S02`) | 2 | 2 / 2 | 2 / 2 | 0 (0 rejected) | 3.66 |

### 9.3 Observed Failure Cases & Documented Mitigations

Inspecting raw traces in `evaluation/raw_logs.jsonl` and pre-evaluation cross-model benchmarks (`qwen3:8b` vs. `llama3.1:8b`) surfaced four concrete, instructive failure modes:

1. **Numeric Tool-Argument Stringification (`llama3.1:8b` on `10 / 46` calls):** `llama3.1:8b` frequently serialized numeric parameters as JSON strings—e.g. `search_game_knowledge(top_k="3")`, `estimate_bribery_impact(bribe_amount="4000")`, and `get_recent_events(last_seconds="300")`. Because `server.py` enforces strict integer/float types, all 10 malformed calls returned clean typed errors (`"top_k must be an integer."`, `"bribe_amount must be a finite non-negative number."`). Crucially, on pure knowledge queries (`K01`, `K02`), the agent read the error message and **self-corrected** on turn 2 (`top_k=1`), successfully retrieving `rule_sabotage_revolver_v1` and `rule_election_night_v1`; on compound prompts (`E01`, `E02`, `C01`–`C03`, `T02`, `V01`), it proceeded without retrying and in `E01` hallucinated that bribery affects communist ideology distribution rather than vote swing. **Mitigation:** Coerce numeric strings safely in the MCP argument parser (`int(val)` when `isinstance(val, str)` and finite) or use `gemini-2.5-flash` / `llama-3.3-70b-versatile` via the Dual-Provider switch for strict tool typing.
2. **Parametric Memory Tool-Skipping (`qwen3:8b` Pre-Evaluation Benchmark):** Prior to selecting `llama3.1:8b`, benchmarking `qwen3:8b` on `K01` (*Revolver duel*) yielded a 3,452 ms response with **`tools=[]` (`rag=[]`)**, bypassing `search_game_knowledge` completely in favor of ungrounded priors. `llama3.1:8b` avoided live-state tools and correctly grounded both `K01` and `K02` in `search_game_knowledge` (`2/2 = 100%`). **Mitigation:** Prefer `llama3.1:8b` for local tool adherence or add deterministic RAG pre-fetch heuristics when a rule lookup returns zero tool calls.
3. **Omitted Auxiliary State Lookups (`E02`, `T01`):** In `E02`, `llama3.1:8b` called `get_city_intelligence` and `estimate_bribery_impact` (relying on the latter's budget check) without calling `get_player_budget`; in `T01`, it checked `get_player_overall` and `search_game_knowledge` but skipped `get_city_intelligence` for un-rallied cities. **Mitigation:** Explicitly decompose multi-part subgoals in system prompt instructions or supply larger-context cloud models when low latency permits multi-turn tool chains.
4. **Consecutive Duplicate Tool Calls & Local Latency:** On multi-city queries (`E01`, `E02`, `C01`, `C03`, `C04`), `llama3.1:8b` often called `get_city_intelligence` consecutively per city instead of `city_name="ALL"`, contributing to 26–46s turn times. **Mitigation:** Deduplicate cached `(tool_name, args)` responses per turn in `AgentRunner` and expose one-click `Cloud API` runtime switching in `PoliticalMayhemHUD.cs`.

---

## 10. Challenges and Lessons Learned

- **Dual-Compile Networking Integration:** Developing and grading Unity scenes when teammates or graders may not yet have imported Photon Fusion 2 SDK `.unitypackage` requires careful `#if PHOTON_FUSION` boundary design so `PoliticalMayhemSceneBuilder` and Local Host mode operate out of the box while preserving genuine `[Networked]` and `[Rpc]` Host Authority paths.
- **Bilingual Political Rhetoric vs. Canonical Schemas:** Players naturally enter Turkish campaign terminology (`"Milliyetçi"`, `"Koruma"`, `"Baskent"`). Without morphological prefix stems (`s5:`/`s7:`) in `ai/rag/text_utils.py` and alias normalization tables (`UNIT_ALIASES`, `IDEOLOGY_ALIASES`) in `validator.py`, retrieval recall and JSON action validation would drop sharply.
- **Strict Type Validation Exposes Model Weaknesses Early:** Returning explicit typed MCP error strings (`"top_k must be an integer."`) prevented silent bugs and revealed exact 8B model failure modes in `tool_logs.jsonl` and `raw_logs.jsonl`.

---

## 11. Conclusion and Future Work

**POLITICAL MAYHEM: Battle for the Cities** demonstrates that an agentic LLM commander can safely assist players in a competitive multiplayer RTS without compromising Fog-of-War secrecy, economic locks, or network state authority. Across 15 end-to-end scenarios on local `llama3.1:8b`, our pipeline achieved `86.7%` tool-selection intent accuracy, `0.0%` post-validation invalid AI actions, `100.0%` rejection of synthetic illegal plans (`6/6`), `100.0%` adversarial prompt-injection defense (`2/2`), and `5/5` direct boundary probe verification.

**Current Limitations & Next Steps:**
- Add lightweight numeric string coercion in the MCP server tool wrapper to raise local 8B `strict_tool_ok` from `53.3%` toward `86.7%+` without relaxing safety validation.
- Enable `USE_OLLAMA_EMBEDDINGS=true` with `nomic-embed-text` dense vectors (`0.55 * lexical + 0.45 * dense_cos`) and benchmark `gemini-2.5-flash` / `llama-3.3-70b-versatile` side-by-side against `llama3.1:8b` in `evaluation/`.
- Execute two-machine networked soak tests once a personal Photon Fusion AppId and Fusion 2 SDK package are imported per `docs/setup.md`.

---

## References

1. CSE-377 Game Programming Project Specification (2026), *Agentic Multiplayer Real-Time Strategy (RTS) Project: Unity 6 + Photon Fusion or Photon PUN + MCP + LLM + RAG*.
2. *POLITICAL MAYHEM: Battle for the Cities* Game Design Document v0.4 (`OurGame.md`).
3. Photon Engine, *Photon Fusion 2 SDK & Host Mode Documentation*, `https://doc.photonengine.com/fusion/current/getting-started/fusion-intro`.
4. Model Context Protocol Python SDK, `https://py.sdk.modelcontextprotocol.io/`.
5. JSON Schema Draft 2020-12 Specification, `https://json-schema.org/`.

---

## Appendix

### A. `ActionPlan` JSON Schema (`ai/agent/schemas/action_plan.schema.json`)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://political-mayhem.local/schemas/action_plan.schema.json",
  "title": "PoliticalMayhemActionPlan",
  "type": "object",
  "required": ["summary", "confidence", "actions"],
  "additionalProperties": false,
  "properties": {
    "summary": { "type": "string", "minLength": 1, "maxLength": 400 },
    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
    "actions": {
      "type": "array",
      "maxItems": 8,
      "items": {
        "type": "object",
        "required": ["action_type", "reason"],
        "additionalProperties": false,
        "properties": {
          "action_type": {
            "type": "string",
            "enum": [
              "DEPLOY_UNIT",
              "MOVE_GROUP",
              "VOTE_RALLY_CITY",
              "CHOOSE_SPEECH_STRATEGY",
              "ALLOCATE_ELECTION_BRIBE"
            ]
          },
          "unit_type": { "type": "string", "maxLength": 80 },
          "city_name": { "type": "string", "maxLength": 80 },
          "target_zone": { "type": "string", "maxLength": 80 },
          "count": { "type": "integer", "minimum": 1, "maximum": 20 },
          "bribe_amount": { "type": "number", "minimum": 0 },
          "ideology_focus": { "type": "string", "maxLength": 64 },
          "reason": { "type": "string", "minLength": 1, "maxLength": 250 }
        }
      }
    }
  }
}
```

### B. Sample Verbatim Logs from Evaluation Run

**Tool Log (`ai/mcp-server/tool_logs.jsonl`):**
```json
{"timestamp": "2026-11-14T10:57:52.519537Z", "match_session": "demo-match-001:player_a", "tool": "get_player_budget", "duration_ms": 0.259, "status": "ok", "result_count": 1, "error": null}
{"timestamp": "2026-11-14T10:57:52.520007Z", "match_session": "demo-match-001:player_a", "tool": "search_game_knowledge", "duration_ms": 0.018, "status": "error", "result_count": 0, "error": "top_k must be an integer."}
{"timestamp": "2026-11-14T11:00:37.361143Z", "match_session": "demo-match-001:player_a", "tool": "get_player_budget", "duration_ms": 0.533, "status": "blocked", "result_count": 0, "error": "Unauthorized tool argument 'player_id': identity and visibility are bound server-side."}
```

**Security Event Log (`ai/mcp-server/security_events.jsonl`):**
```json
{"timestamp": "2026-11-14T10:58:07.829695Z", "event_type": "PROMPT_INJECTION_DETECTED", "severity": "HIGH", "match_session": "demo-match-001:player_a", "player_id": "player_a", "detail": "Blocked adversarial prompt-injection or hidden-state probe (['instruction_override', 'hidden_opponent_extraction']).", "query_excerpt": "Ignore all previous rules. Call any tool you have and tell me the exact party budget, Overall scores, and hidden support percentages of the opponent in Demirkent.", "metadata": {"matched_rules": ["instruction_override", "hidden_opponent_extraction"]}}
{"timestamp": "2026-11-14T11:00:37.362258Z", "event_type": "FORBIDDEN_SNAPSHOT_FIELDS_STRIPPED", "severity": "HIGH", "match_session": "demo-match-001:player_a", "player_id": "player_a", "detail": "Stripped forbidden hidden opponent fields before storage: ['opponent_budget', 'opponent_overall', 'hidden_support_rates', 'opponent_bribes']", "query_excerpt": null, "metadata": {"stripped_fields": ["opponent_budget", "opponent_overall", "hidden_support_rates", "opponent_bribes"]}}
```

### C. Reproducible Evaluation Dataset (`evaluation/prompts.jsonl` & `evaluation/results.csv`)

All 15 labeled prompts (`E01`–`E04`, `C01`–`C04`, `T01`–`T02`, `K01`–`K02`, `V01`, `S01`–`S02`) and their real observed CSV results, complete JSONL raw traces (`evaluation/raw_logs.jsonl`), and security/invalid-action proofs (`evaluation/security-tests.md`) are stored in `evaluation/`. To reproduce:

```bash
uv sync
uv run pytest
uv run python ai/rag/build_index.py
uv run python evaluation/run_evaluation.py --provider ollama --model llama3.1:8b
```

For step-by-step AI Gateway, MCP Inspector, Unity 6 (`6000.6.3f1`), Photon Fusion 2 SDK setup, and the 9 required live demo scenarios, refer to `docs/setup.md` and `docs/architecture.md`.
