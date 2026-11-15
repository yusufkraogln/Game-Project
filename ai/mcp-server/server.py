"""Custom MCP Server exposing 8 Fog-of-War safe Political Mayhem tools."""

from __future__ import annotations

import math
import sys
import threading
import time
from pathlib import Path
from typing import Any, Callable

from mcp.server.mcpserver import MCPServer
from mcp.server.mcpserver.exceptions import ToolError

_MCP_DIR = Path(__file__).resolve().parent
_ROOT_DIR = _MCP_DIR.parent.parent
for _path in (str(_MCP_DIR), str(_ROOT_DIR)):
    if _path not in sys.path:
        sys.path.insert(0, _path)

from ai.rag.retriever import KnowledgeRetriever  # noqa: E402
from logging_utils import (  # noqa: E402
    DEFAULT_SECURITY_LOG_PATH,
    DEFAULT_TOOL_LOG_PATH,
    log_security_event,
    log_tool_call,
)
from state_store import (  # noqa: E402
    CANONICAL_CITIES,
    CANONICAL_IDEOLOGIES,
    IDENTITY_SPOOF_ARG_KEYS,
    SafeSessionStore,
    SecurityViolationError,
    VisibilityService,
    normalize_city_filter,
)

MAX_BRIBE_SWING_PCT = 2.0

DEFAULT_SESSION_STORE = SafeSessionStore()

_RETRIEVER_LOCK = threading.Lock()
_RETRIEVER: KnowledgeRetriever | None = None


def get_retriever() -> KnowledgeRetriever:
    """Lazily load the singleton hybrid KnowledgeRetriever."""
    global _RETRIEVER
    with _RETRIEVER_LOCK:
        if _RETRIEVER is None:
            _RETRIEVER = KnowledgeRetriever()
        return _RETRIEVER


def _get_snapshot(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
) -> tuple[dict[str, Any], str]:
    active_match, active_player = session.get_active_session_ids()
    target_match = match_id or active_match
    target_player = player_id or active_player
    snapshot = session.get_snapshot(match_id=target_match, authenticated_player_id=target_player)
    return snapshot, f"{target_match}:{target_player}"


def _tool_get_player_budget(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
) -> dict[str, Any]:
    snap, _ = _get_snapshot(session, match_id, player_id)
    phase = snap["phase"]
    can_spend_now = phase == "ELECTION_BRIBE"
    donations = [
        ev
        for ev in snap.get("recent_events", [])
        if "DONATION" in ev.get("event_type", "").upper()
        or "donation" in ev.get("summary", "").lower()
        or "party_budget" in ev.get("summary", "").lower()
    ]
    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "round_number": snap["round_number"],
        "phase": phase,
        "party_budget": float(snap["party_budget"]),
        "can_spend_bribe_now": can_spend_now,
        "phase_restriction_note": (
            "Party budget can ONLY be spent on Election Night Phase 2 (ELECTION_BRIBE) bribery; "
            "it is never spent on unit deployments or buildings during rally rounds."
        ),
        "recent_donation_summary": donations,
    }


def _tool_get_player_overall(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
) -> dict[str, Any]:
    snap, _ = _get_snapshot(session, match_id, player_id)
    overall = dict(snap["player_overall"])
    dominant = max(CANONICAL_IDEOLOGIES, key=lambda key: float(overall.get(key, 0.0)))
    unlocked = list(snap.get("unlocked_bonuses", []))
    below_40 = {
        ideology: round(40.0 - float(overall.get(ideology, 0.0)), 2)
        for ideology in CANONICAL_IDEOLOGIES
        if float(overall.get(ideology, 0.0)) < 40.0
    }
    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "round_number": snap["round_number"],
        "player_overall": overall,
        "dominant_ideology": dominant,
        "unlocked_bonuses": unlocked,
        "points_needed_for_40_threshold": below_40,
    }


def _tool_get_city_intelligence(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
    *,
    city_name: str = "ALL",
) -> dict[str, Any]:
    target = normalize_city_filter(city_name, allow_all=True)
    snap, _ = _get_snapshot(session, match_id, player_id)
    cities = snap.get("cities", [])
    if target != "ALL":
        cities = [c for c in cities if c["city_name"] == target]

    enriched_cities = []
    for city in cities:
        dist = city["ideology_distribution"]
        top_ideology = max(CANONICAL_IDEOLOGIES, key=lambda key: float(dist.get(key, 0.0)))
        enriched_cities.append({**city, "dominant_ideology": top_ideology})

    remaining = [c["city_name"] for c in snap.get("cities", []) if not c.get("rally_done")]
    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "round_number": snap["round_number"],
        "phase": snap["phase"],
        "filter_city": target,
        "cities": enriched_cities,
        "remaining_rally_cities": remaining,
    }


def _tool_get_owned_units(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
    *,
    city_name: str = "ALL",
) -> dict[str, Any]:
    target = normalize_city_filter(city_name, allow_all=True)
    snap, _ = _get_snapshot(session, match_id, player_id)
    limits = session.get_action_limits(snap)
    units = snap.get("own_units", [])
    if target != "ALL":
        units = [u for u in units if u["city_name"] == target]

    totals_by_type: dict[str, int] = {}
    for unit in units:
        utype = unit["unit_type"]
        totals_by_type[utype] = totals_by_type.get(utype, 0) + int(unit["count"])

    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "round_number": snap["round_number"],
        "filter_city": target,
        "own_units": units,
        "total_owned_units": sum(totals_by_type.values()),
        "unit_totals_by_type": totals_by_type,
        "per_round_deployment_limits": limits["deploy_limits"],
    }


def _tool_get_visible_opponent_activity(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
) -> dict[str, Any]:
    snap, _ = _get_snapshot(session, match_id, player_id)
    activity = list(snap.get("visible_opponent_activity", []))
    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "round_number": snap["round_number"],
        "visible_opponent_activity": activity,
        "visible_count": len(activity),
        "fog_of_war_note": (
            "Only publicly observable opponent movements and speech excerpts are available. "
            "Opponent budget, exact Overall scores, private plans, and hidden support rates are restricted under Fog of War."
        ),
    }


def _coerce_int_arg(value: Any, error_msg: str) -> int:
    """Coerce int or clean integer numeric string (e.g. '3') while rejecting bools/non-integers."""
    if isinstance(value, bool):
        raise ValueError(error_msg)
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        text = value.strip()
        if not text:
            raise ValueError(error_msg)
        try:
            return int(text)
        except ValueError as exc:
            raise ValueError(error_msg) from exc
    raise ValueError(error_msg)


def _coerce_non_negative_float_arg(value: Any, error_msg: str) -> float:
    """Coerce finite non-negative float/int or numeric string (e.g. '4000') while rejecting bools/NaNs."""
    if isinstance(value, bool):
        raise ValueError(error_msg)
    if isinstance(value, (int, float)):
        num = float(value)
    elif isinstance(value, str):
        text = value.strip()
        if not text:
            raise ValueError(error_msg)
        try:
            num = float(text)
        except ValueError as exc:
            raise ValueError(error_msg) from exc
    else:
        raise ValueError(error_msg)

    if not math.isfinite(num) or num < 0.0:
        raise ValueError(error_msg)
    return num


def _tool_get_recent_events(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
    *,
    last_seconds: int | str = 120,
) -> dict[str, Any]:
    parsed_seconds = _coerce_int_arg(last_seconds, "last_seconds must be an integer between 1 and 3600.")
    if parsed_seconds < 1 or parsed_seconds > 3600:
        raise ValueError(f"last_seconds={parsed_seconds} out of valid range [1, 3600].")

    snap, _ = _get_snapshot(session, match_id, player_id)
    events = [
        ev
        for ev in snap.get("recent_events", [])
        if int(ev.get("seconds_ago", 0)) <= parsed_seconds
    ]
    events.sort(key=lambda ev: int(ev.get("seconds_ago", 0)))
    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "last_seconds": parsed_seconds,
        "event_count": len(events),
        "events": events,
    }


def _tool_estimate_bribery_impact(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
    *,
    city_name: str,
    bribe_amount: float | int | str,
) -> dict[str, Any]:
    canonical_city = normalize_city_filter(city_name, allow_all=False)
    amount = _coerce_non_negative_float_arg(
        bribe_amount, f"bribe_amount={bribe_amount!r} must be a finite non-negative number."
    )

    snap, _ = _get_snapshot(session, match_id, player_id)
    budget = float(snap["party_budget"])
    phase = snap["phase"]
    can_afford = amount <= budget

    city_obj = next((c for c in snap.get("cities", []) if c["city_name"] == canonical_city), None)
    population = int(city_obj["population"]) if city_obj is not None else 80000
    unlocked = set(snap.get("unlocked_bonuses", []))

    # Aligned with Unity OverallManager.GetBribeEfficiencyMultiplier (1.20) & ElectionNightController.DiminishingBribeCurve (6000)
    efficiency = 1.20 if "tech_imperialist_patronage_v1" in unlocked else 1.0
    half_saturation = 6000.0
    effective_amount = amount * efficiency
    estimated_shift = MAX_BRIBE_SWING_PCT * (effective_amount / (effective_amount + half_saturation))
    estimated_shift = round(min(MAX_BRIBE_SWING_PCT, max(0.0, estimated_shift)), 4)

    is_bribe_phase = phase == "ELECTION_BRIBE"
    warning = None
    if not is_bribe_phase:
        warning = (
            f"Bribery is NOT executable in current phase {phase!r}. "
            "Bribery is only allowed during Election Night Phase 2 (ELECTION_BRIBE)."
        )
    elif not can_afford:
        warning = f"Insufficient party_budget ({budget:.2f}) for requested bribe_amount ({amount:.2f})."

    return {
        "match_id": snap["match_id"],
        "player_id": snap["player_id"],
        "city_name": canonical_city,
        "city_population": population,
        "bribe_amount": amount,
        "party_budget": budget,
        "can_afford": can_afford,
        "remaining_budget_after_bribe": round(max(0.0, budget - amount), 2),
        "estimated_vote_shift_pct_points": estimated_shift,
        "max_vote_swing_cap_pct_points": MAX_BRIBE_SWING_PCT,
        "imperialist_efficiency_bonus_active": efficiency > 1.0,
        "current_phase": phase,
        "is_election_bribe_phase": is_bribe_phase,
        "warning": warning,
    }


def _tool_search_game_knowledge(
    session: SafeSessionStore,
    match_id: str | None = None,
    player_id: str | None = None,
    *,
    query: str,
    top_k: int | str = 3,
) -> dict[str, Any]:
    del session, match_id, player_id  # Static game knowledge only.
    if not isinstance(query, str) or not query.strip():
        raise ValueError("query must be a non-empty string.")
    parsed_top_k = _coerce_int_arg(top_k, "top_k must be an integer.")

    clamped_top_k = max(1, min(parsed_top_k, 5))
    retriever = get_retriever()
    chunks = retriever.search(query.strip(), top_k=clamped_top_k)
    return {
        "query": query.strip(),
        "top_k_requested": parsed_top_k,
        "top_k_used": clamped_top_k,
        "result_count": len(chunks),
        "results": chunks,
    }


TOOL_REGISTRY: dict[str, dict[str, Any]] = {
    "get_player_budget": {
        "fn": _tool_get_player_budget,
        "description": (
            "Return the authenticated player's own party_budget, Election Night phase restriction note, "
            "and recent donation summary."
        ),
        "parameters": {"type": "object", "properties": {}, "required": [], "additionalProperties": False},
        "allowed_args": frozenset(),
    },
    "get_player_overall": {
        "fn": _tool_get_player_overall,
        "description": (
            "Return the authenticated player's 5 ideological Overall scores (nationalist, liberal, "
            "democrat, communist, imperialist) and unlocked technology bonuses."
        ),
        "parameters": {"type": "object", "properties": {}, "required": [], "additionalProperties": False},
        "allowed_args": frozenset(),
    },
    "get_city_intelligence": {
        "fn": _tool_get_city_intelligence,
        "description": (
            "Return population, ideology distribution, rally_done status, and visible campaign presence "
            "for ALL cities or one canonical city (Baskent, Limankent, Demirkent, Yesilova, Universitekent, Dagsehir)."
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "city_name": {
                    "type": "string",
                    "description": "Either 'ALL' or one canonical city: Baskent, Limankent, Demirkent, Yesilova, Universitekent, Dagsehir.",
                    "default": "ALL",
                }
            },
            "required": [],
            "additionalProperties": False,
        },
        "allowed_args": frozenset({"city_name"}),
    },
    "get_owned_units": {
        "fn": _tool_get_owned_units,
        "description": (
            "Return the player's own deployed campaign units (campaign_volunteer, propagandist, bodyguard, "
            "rally_convoy, etc.) and per-round deployment caps, optionally filtered by canonical city_name or ALL."
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "city_name": {
                    "type": "string",
                    "description": "Either 'ALL' or a canonical city name.",
                    "default": "ALL",
                }
            },
            "required": [],
            "additionalProperties": False,
        },
        "allowed_args": frozenset({"city_name"}),
    },
    "get_visible_opponent_activity": {
        "fn": _tool_get_visible_opponent_activity,
        "description": (
            "Return ONLY Fog-of-War visible opponent sightings and public speech excerpts. "
            "Never exposes hidden opponent budget, Overall scores, or vote support rates."
        ),
        "parameters": {"type": "object", "properties": {}, "required": [], "additionalProperties": False},
        "allowed_args": frozenset(),
    },
    "get_recent_events": {
        "fn": _tool_get_recent_events,
        "description": "Return recent public match events observed within last_seconds (1 to 3600).",
        "parameters": {
            "type": "object",
            "properties": {
                "last_seconds": {
                    "type": "integer",
                    "description": "Time window in seconds (1..3600).",
                    "default": 120,
                    "minimum": 1,
                    "maximum": 3600,
                }
            },
            "required": [],
            "additionalProperties": False,
        },
        "allowed_args": frozenset({"last_seconds"}),
    },
    "estimate_bribery_impact": {
        "fn": _tool_estimate_bribery_impact,
        "description": (
            "Deterministically estimate Election Night Phase 2 bribery affordability and diminishing-returns "
            "vote percentage shift (strictly capped at 2.0 percentage points) for a canonical city."
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "city_name": {
                    "type": "string",
                    "description": "Canonical city name (Baskent, Limankent, Demirkent, Yesilova, Universitekent, Dagsehir).",
                },
                "bribe_amount": {
                    "type": "number",
                    "description": "Proposed non-negative party_budget spend.",
                    "minimum": 0,
                },
            },
            "required": ["city_name", "bribe_amount"],
            "additionalProperties": False,
        },
        "allowed_args": frozenset({"city_name", "bribe_amount"}),
    },
    "search_game_knowledge": {
        "fn": _tool_search_game_knowledge,
        "description": (
            "Search the Political Mayhem Hybrid RAG knowledge base (units, counters, technologies/Overall "
            "thresholds, donation economy, Election Night rules, sabotage/revolver mechanics, Fog of War)."
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search query in English or Turkish.",
                },
                "top_k": {
                    "type": "integer",
                    "description": "Number of chunks to return (clamped to 1..5).",
                    "default": 3,
                    "minimum": 1,
                    "maximum": 5,
                },
            },
            "required": ["query"],
            "additionalProperties": False,
        },
        "allowed_args": frozenset({"query", "top_k"}),
    },
}


def get_openai_tools_schema() -> list[dict[str, Any]]:
    """Return tool schemas formatted for OpenAI / Ollama / Groq function calling."""
    return [
        {
            "type": "function",
            "function": {
                "name": name,
                "description": spec["description"],
                "parameters": spec["parameters"],
            },
        }
        for name, spec in TOOL_REGISTRY.items()
    ]


def _count_results(tool_name: str, payload: dict[str, Any]) -> int:
    if tool_name == "get_city_intelligence":
        return len(payload.get("cities", []))
    if tool_name == "get_owned_units":
        return len(payload.get("own_units", []))
    if tool_name == "get_visible_opponent_activity":
        return len(payload.get("visible_opponent_activity", []))
    if tool_name == "get_recent_events":
        return len(payload.get("events", []))
    if tool_name == "search_game_knowledge":
        return len(payload.get("results", []))
    return 1


def call_tool_safely(
    tool_name: str,
    arguments: dict[str, Any] | None = None,
    session: SafeSessionStore | None = None,
    *,
    match_id: str | None = None,
    player_id: str | None = None,
    tool_log_path: Path | None = None,
    security_log_path: Path | None = None,
) -> dict[str, Any]:
    """Execute one of the 8 registered MCP tools with strict validation, security checks, and logging."""
    t0 = time.perf_counter()
    active_session = session or DEFAULT_SESSION_STORE
    active_match, active_player = active_session.get_active_session_ids()
    bound_match = match_id or active_match
    bound_player = player_id or active_player
    session_tag = f"{bound_match}:{bound_player}"
    args = dict(arguments) if arguments is not None else {}

    if tool_name not in TOOL_REGISTRY:
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        err = f"Unknown tool: {tool_name!r}."
        log_tool_call(
            tool=tool_name,
            duration_ms=elapsed_ms,
            status="error",
            result_count=0,
            error=err,
            match_session=session_tag,
            log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
        )
        return {"ok": False, "error": err, "error_type": "unknown_tool", "tool": tool_name}

    try:
        VisibilityService.verify_tool_call_security(
            tool_name=tool_name,
            arguments=args,
            authenticated_player_id=bound_player,
            session_match_id=bound_match,
            security_log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
        )
    except SecurityViolationError as exc:
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        log_tool_call(
            tool=tool_name,
            duration_ms=elapsed_ms,
            status="blocked",
            result_count=0,
            error=str(exc),
            match_session=session_tag,
            log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
        )
        return {
            "ok": False,
            "error": str(exc),
            "error_type": "security_error",
            "tool": tool_name,
        }

    spec = TOOL_REGISTRY[tool_name]
    allowed_args: frozenset[str] = spec["allowed_args"]
    unexpected = sorted(set(args.keys()) - allowed_args)
    if unexpected:
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        err = f"Unexpected argument(s) for {tool_name}: {unexpected}"
        if any(k.lower() in IDENTITY_SPOOF_ARG_KEYS for k in unexpected):
            log_security_event(
                event_type="UNAUTHORIZED_TOOL_ARGUMENT",
                severity="CRITICAL",
                detail=err,
                match_session=session_tag,
                player_id=bound_player,
                log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
            )
            error_type = "security_error"
            status = "blocked"
        else:
            error_type = "validation_error"
            status = "error"
        log_tool_call(
            tool=tool_name,
            duration_ms=elapsed_ms,
            status=status,
            result_count=0,
            error=err,
            match_session=session_tag,
            log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
        )
        return {"ok": False, "error": err, "error_type": error_type, "tool": tool_name}

    fn: Callable[..., dict[str, Any]] = spec["fn"]
    try:
        payload = fn(active_session, bound_match, bound_player, **args)
    except (ValueError, TypeError, KeyError, ToolError) as exc:
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        log_tool_call(
            tool=tool_name,
            duration_ms=elapsed_ms,
            status="error",
            result_count=0,
            error=str(exc),
            match_session=session_tag,
            log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
        )
        return {
            "ok": False,
            "error": str(exc),
            "error_type": "validation_error",
            "tool": tool_name,
        }

    leaked = VisibilityService.contains_forbidden_fields(payload)
    if leaked:
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        err = f"Fog-of-War barrier blocked leaked hidden fields: {leaked}"
        log_security_event(
            event_type="OUTPUT_LEAK_BLOCKED",
            severity="CRITICAL",
            detail=err,
            match_session=session_tag,
            player_id=bound_player,
            log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
        )
        log_tool_call(
            tool=tool_name,
            duration_ms=elapsed_ms,
            status="blocked",
            result_count=0,
            error=err,
            match_session=session_tag,
            log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
        )
        return {"ok": False, "error": err, "error_type": "security_error", "tool": tool_name}

    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    result_count = _count_results(tool_name, payload)
    log_tool_call(
        tool=tool_name,
        duration_ms=elapsed_ms,
        status="ok",
        result_count=result_count,
        error=None,
        match_session=session_tag,
        log_path=tool_log_path or DEFAULT_TOOL_LOG_PATH,
    )
    return {"ok": True, **payload}


# ---------------------------------------------------------------------------
# Official Python MCP SDK Server (`MCPServer` in mcp==2.3.0)
# ---------------------------------------------------------------------------


class SafePoliticalMCPServer(MCPServer):
    """MCPServer subclass enforcing Fog-of-War & identity checks on wire arguments."""

    async def call_tool(
        self,
        name: str,
        arguments: dict[str, Any],
        context: Any | None = None,
    ) -> Any:
        t0 = time.perf_counter()
        active_match, active_player = DEFAULT_SESSION_STORE.get_active_session_ids()
        session_tag = f"{active_match}:{active_player}"
        args = arguments or {}
        try:
            VisibilityService.verify_tool_call_security(
                tool_name=name,
                arguments=args,
                authenticated_player_id=active_player,
                session_match_id=active_match,
                security_log_path=DEFAULT_SECURITY_LOG_PATH,
            )
            if name in TOOL_REGISTRY:
                unexpected = sorted(set(args.keys()) - TOOL_REGISTRY[name]["allowed_args"])
                if unexpected:
                    raise ValueError(f"Unexpected argument(s) for {name}: {unexpected}")
        except SecurityViolationError as exc:
            elapsed_ms = (time.perf_counter() - t0) * 1000.0
            log_tool_call(
                tool=name,
                duration_ms=elapsed_ms,
                status="blocked",
                result_count=0,
                error=str(exc),
                match_session=session_tag,
                log_path=DEFAULT_TOOL_LOG_PATH,
            )
            raise ToolError(str(exc)) from exc
        except ValueError as exc:
            elapsed_ms = (time.perf_counter() - t0) * 1000.0
            log_tool_call(
                tool=name,
                duration_ms=elapsed_ms,
                status="error",
                result_count=0,
                error=str(exc),
                match_session=session_tag,
                log_path=DEFAULT_TOOL_LOG_PATH,
            )
            raise ToolError(str(exc)) from exc
        return await super().call_tool(name, args, context)


mcp = SafePoliticalMCPServer(
    "PoliticalMayhemCommander",
    instructions=(
        "Fog-of-War safe tactical MCP server for Political Mayhem: Battle for the Cities. "
        "Provides authenticated player state, city intelligence, unit counts, visible opponent "
        "activity, recent events, deterministic bribery estimation, and Hybrid RAG rule search."
    ),
)


def _run_mcp_tool(tool_name: str, arguments: dict[str, Any] | None = None) -> dict[str, Any]:
    result = call_tool_safely(tool_name, arguments=arguments, session=DEFAULT_SESSION_STORE)
    if not result.get("ok"):
        raise ToolError(result.get("error", f"Tool {tool_name} failed."))
    result.pop("ok", None)
    return result


@mcp.tool()
def get_player_budget() -> dict[str, Any]:
    """Return the authenticated player's own party_budget, phase restriction note, and recent donations."""
    return _run_mcp_tool("get_player_budget")


@mcp.tool()
def get_player_overall() -> dict[str, Any]:
    """Return the authenticated player's 5 ideological Overall scores and unlocked technology bonuses."""
    return _run_mcp_tool("get_player_overall")


@mcp.tool()
def get_city_intelligence(city_name: str = "ALL") -> dict[str, Any]:
    """Return population, ideology distribution, rally_done status, and visible presence for ALL or one canonical city."""
    return _run_mcp_tool("get_city_intelligence", {"city_name": city_name})


@mcp.tool()
def get_owned_units(city_name: str = "ALL") -> dict[str, Any]:
    """Return the player's own campaign units and per-round deployment caps, filtered by ALL or canonical city_name."""
    return _run_mcp_tool("get_owned_units", {"city_name": city_name})


@mcp.tool()
def get_visible_opponent_activity() -> dict[str, Any]:
    """Return ONLY Fog-of-War visible opponent unit sightings and public speech excerpts."""
    return _run_mcp_tool("get_visible_opponent_activity")


@mcp.tool()
def get_recent_events(last_seconds: int = 120) -> dict[str, Any]:
    """Return public match events within last_seconds (1 to 3600)."""
    return _run_mcp_tool("get_recent_events", {"last_seconds": last_seconds})


@mcp.tool()
def estimate_bribery_impact(city_name: str, bribe_amount: float) -> dict[str, Any]:
    """Estimate Election Night Phase 2 bribery affordability and vote shift (capped at 2.0 percentage points)."""
    return _run_mcp_tool(
        "estimate_bribery_impact",
        {"city_name": city_name, "bribe_amount": bribe_amount},
    )


@mcp.tool()
def search_game_knowledge(query: str, top_k: int = 3) -> dict[str, Any]:
    """Search the Political Mayhem Hybrid RAG knowledge base (top_k clamped to 1..5)."""
    return _run_mcp_tool("search_game_knowledge", {"query": query, "top_k": top_k})


if __name__ == "__main__":
    mcp.run(transport="stdio")
