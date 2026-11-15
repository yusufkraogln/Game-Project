"""Comprehensive tests for Political Mayhem Custom MCP Server, Security Layer, and Logging."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import anyio
import pytest
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT_DIR = Path(__file__).resolve().parents[3]
MCP_DIR = ROOT_DIR / "ai" / "mcp-server"
if str(MCP_DIR) not in sys.path:
    sys.path.insert(0, str(MCP_DIR))

from logging_utils import log_security_event, log_tool_call  # noqa: E402
from server import TOOL_REGISTRY, call_tool_safely  # noqa: E402
from state_store import (  # noqa: E402
    CANONICAL_CITIES,
    FORBIDDEN_SECRET_FIELDS,
    SafeSessionStore,
    SecurityViolationError,
    VisibilityService,
    build_default_demo_snapshot,
)


def _read_jsonl(path: Path) -> list[dict]:
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def _make_isolated_store(tmp_path: Path) -> tuple[SafeSessionStore, Path, Path]:
    tool_log = tmp_path / "tool_logs.jsonl"
    sec_log = tmp_path / "security_events.jsonl"
    store = SafeSessionStore(security_log_path=sec_log)
    return store, tool_log, sec_log


def test_all_eight_tools_valid_calls_and_logging(tmp_path: Path) -> None:
    store, tool_log, sec_log = _make_isolated_store(tmp_path)

    # 1. get_player_budget
    res = call_tool_safely("get_player_budget", {}, store, tool_log_path=tool_log, security_log_path=sec_log)
    assert res["ok"] is True
    assert res["party_budget"] == 14500.0
    assert res["can_spend_bribe_now"] is False
    assert "ELECTION_BRIBE" in res["phase_restriction_note"]
    assert res["recent_donation_summary"]

    # 2. get_player_overall
    res = call_tool_safely("get_player_overall", {}, store, tool_log_path=tool_log, security_log_path=sec_log)
    assert res["ok"] is True
    assert set(res["player_overall"].keys()) == {"nationalist", "liberal", "democrat", "communist", "imperialist"}
    assert res["dominant_ideology"] == "nationalist"
    assert "tech_democrat_grassroots_v1" in res["unlocked_bonuses"]

    # 3. get_city_intelligence (ALL, canonical, Turkish spelling)
    res_all = call_tool_safely(
        "get_city_intelligence", {"city_name": "ALL"}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert res_all["ok"] is True
    assert len(res_all["cities"]) == 6
    assert {c["city_name"] for c in res_all["cities"]} == set(CANONICAL_CITIES)
    assert set(res_all["remaining_rally_cities"]) == {"Yesilova", "Universitekent", "Dagsehir"}

    res_tr = call_tool_safely(
        "get_city_intelligence", {"city_name": "Başkent"}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert res_tr["ok"] is True
    assert len(res_tr["cities"]) == 1
    assert res_tr["cities"][0]["city_name"] == "Baskent"

    # 4. get_owned_units
    res_units = call_tool_safely(
        "get_owned_units", {"city_name": "Yesilova"}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert res_units["ok"] is True
    assert all(u["city_name"] == "Yesilova" for u in res_units["own_units"])
    assert res_units["total_owned_units"] == 7
    # tech_democrat_grassroots_v1 grants +1 campaign_volunteer cap (6 -> 7)
    assert res_units["per_round_deployment_limits"]["campaign_volunteer"] == 7

    # 5. get_visible_opponent_activity
    res_opp = call_tool_safely(
        "get_visible_opponent_activity", {}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert res_opp["ok"] is True
    assert res_opp["visible_count"] >= 2
    assert not VisibilityService.contains_forbidden_fields(res_opp)

    # 6. get_recent_events
    res_ev_60 = call_tool_safely(
        "get_recent_events", {"last_seconds": 60}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    res_ev_300 = call_tool_safely(
        "get_recent_events", {"last_seconds": 300}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert res_ev_60["ok"] is True and res_ev_300["ok"] is True
    assert res_ev_300["event_count"] > res_ev_60["event_count"]

    # 7. estimate_bribery_impact (bounded at <= 2.0 pp)
    res_bribe = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "Yesilova", "bribe_amount": 5000},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert res_bribe["ok"] is True
    assert res_bribe["can_afford"] is True
    assert 0.0 < res_bribe["estimated_vote_shift_pct_points"] <= 2.0
    assert res_bribe["max_vote_swing_cap_pct_points"] == 2.0
    assert res_bribe["is_election_bribe_phase"] is False
    assert "ELECTION_BRIBE" in res_bribe["warning"]

    res_huge_bribe = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "Baskent", "bribe_amount": 9999999},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert res_huge_bribe["ok"] is True
    assert res_huge_bribe["can_afford"] is False
    assert res_huge_bribe["estimated_vote_shift_pct_points"] <= 2.0

    # 8. search_game_knowledge
    res_rag = call_tool_safely(
        "search_game_knowledge",
        {"query": "What counters Propagandist units?", "top_k": 3},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert res_rag["ok"] is True
    assert 1 <= res_rag["result_count"] <= 3
    first = res_rag["results"][0]
    assert {"source_id", "score", "title", "type", "text"} <= set(first.keys())

    # Verify structured JSONL logs written for every call
    logs = _read_jsonl(tool_log)
    assert len(logs) == 11
    assert all(entry["status"] == "ok" for entry in logs)
    assert all(
        {"timestamp", "match_session", "tool", "duration_ms", "status", "result_count", "error"} <= set(entry.keys())
        for entry in logs
    )


def test_invalid_inputs_and_top_k_clamping(tmp_path: Path) -> None:
    store, tool_log, sec_log = _make_isolated_store(tmp_path)

    bad_city = call_tool_safely(
        "get_city_intelligence", {"city_name": "Atlantis"}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert bad_city["ok"] is False and bad_city["error_type"] == "validation_error"

    bad_units_city = call_tool_safely(
        "get_owned_units", {"city_name": "Atlantis"}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert bad_units_city["ok"] is False and bad_units_city["error_type"] == "validation_error"

    neg_bribe = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "Baskent", "bribe_amount": -100},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert neg_bribe["ok"] is False and neg_bribe["error_type"] == "validation_error"

    all_bribe = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "ALL", "bribe_amount": 100},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert all_bribe["ok"] is False and all_bribe["error_type"] == "validation_error"

    bad_seconds = call_tool_safely(
        "get_recent_events", {"last_seconds": -5}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert bad_seconds["ok"] is False and bad_seconds["error_type"] == "validation_error"

    too_big_seconds = call_tool_safely(
        "get_recent_events", {"last_seconds": 7200}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert too_big_seconds["ok"] is False and too_big_seconds["error_type"] == "validation_error"

    empty_query = call_tool_safely(
        "search_game_knowledge", {"query": "   ", "top_k": 2}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert empty_query["ok"] is False and empty_query["error_type"] == "validation_error"

    # top_k=99 is strictly clamped to 5
    clamped = call_tool_safely(
        "search_game_knowledge",
        {"query": "Election Night bribery percentage points", "top_k": 99},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert clamped["ok"] is True
    assert clamped["top_k_used"] == 5
    assert 1 <= len(clamped["results"]) <= 5

    unknown_tool = call_tool_safely(
        "reveal_opponent_budget", {}, store, tool_log_path=tool_log, security_log_path=sec_log
    )
    assert unknown_tool["ok"] is False and unknown_tool["error_type"] == "unknown_tool"


def test_security_rules_identity_binding_and_fog_of_war(tmp_path: Path) -> None:
    store, tool_log, sec_log = _make_isolated_store(tmp_path)

    # Rule 2: Passing unexpected player_id="opponent" must be blocked & logged
    spoof = call_tool_safely(
        "get_player_budget",
        {"player_id": "opponent"},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert spoof["ok"] is False
    assert spoof["error_type"] == "security_error"

    # Rule 1: Filter before the model strips forbidden secret fields before storage
    leaked = build_default_demo_snapshot("match-sec-1", "player_a")
    leaked["opponent_budget"] = 99999.0
    leaked["opponent_overall"] = {"nationalist": 90}
    leaked["hidden_support_rates"] = {"Baskent": 0.61}
    leaked["opponent_private_plan"] = {"target": "Yesilova"}

    stored, stripped = store.upsert_snapshot(leaked, authenticated_player_id="player_a")
    assert set(stripped) == {
        "opponent_budget",
        "opponent_overall",
        "hidden_support_rates",
        "opponent_private_plan",
    }
    assert not VisibilityService.contains_forbidden_fields(stored)

    # Ensure none of the 8 tools expose forbidden fields
    for tool_name in TOOL_REGISTRY:
        args = {}
        if tool_name == "estimate_bribery_impact":
            args = {"city_name": "Yesilova", "bribe_amount": 1000}
        elif tool_name == "search_game_knowledge":
            args = {"query": "fog of war opponent budget", "top_k": 2}
        out = call_tool_safely(tool_name, args, store, tool_log_path=tool_log, security_log_path=sec_log)
        assert out["ok"] is True
        assert not VisibilityService.contains_forbidden_fields(out)
        for key in FORBIDDEN_SECRET_FIELDS:
            assert key not in out

    # Rule 2 on upsert_snapshot: mismatched authenticated_player_id rejected
    other_player_snap = build_default_demo_snapshot("match-sec-1", "player_b")
    with pytest.raises(SecurityViolationError):
        store.upsert_snapshot(other_player_snap, authenticated_player_id="player_a")

    sec_events = _read_jsonl(sec_log)
    event_types = [e["event_type"] for e in sec_events]
    assert "UNAUTHORIZED_TOOL_ARGUMENT" in event_types
    assert "FORBIDDEN_SNAPSHOT_FIELDS_STRIPPED" in event_types
    assert "UNAUTHORIZED_IDENTITY_OVERRIDE" in event_types


def test_logging_redacts_secrets_and_keys(tmp_path: Path) -> None:
    tool_log = tmp_path / "tool_logs.jsonl"
    sec_log = tmp_path / "security_events.jsonl"

    log_tool_call(
        tool="get_player_budget",
        duration_ms=1.5,
        status="error",
        error="Failed with Bearer supersecrettoken12345 and sk-testsecretkey1234567890",
        log_path=tool_log,
    )
    log_security_event(
        event_type="TEST_REDACTION",
        severity="LOW",
        detail="Attempted leak AIzaSyDummyKeyValue123456789012345",
        metadata={"opponent_budget": 12345, "api_key": "gsk_1234567890abcdef123456"},
        log_path=sec_log,
    )

    tool_raw = tool_log.read_text(encoding="utf-8")
    sec_raw = sec_log.read_text(encoding="utf-8")
    assert "supersecrettoken" not in tool_raw
    assert "sk-testsecretkey" not in tool_raw
    assert "AIzaSyDummyKeyValue" not in sec_raw
    assert "12345" not in sec_raw
    assert "gsk_1234567890" not in sec_raw


def test_mcp_stdio_handshake_list_and_call_tools() -> None:
    async def _run_stdio_check() -> None:
        params = StdioServerParameters(
            command=sys.executable,
            args=[str(MCP_DIR / "server.py")],
        )
        async with stdio_client(params) as (read_stream, write_stream):
            async with ClientSession(read_stream, write_stream) as session:
                init_result = await session.initialize()
                assert init_result.server_info.name == "PoliticalMayhemCommander"

                tools_resp = await session.list_tools()
                names = {t.name for t in tools_resp.tools}
                assert names == set(TOOL_REGISTRY.keys())
                assert len(names) == 8

                budget_res = await session.call_tool("get_player_budget", {})
                assert budget_res.is_error is False
                budget_payload = json.loads(budget_res.content[0].text)
                assert budget_payload["party_budget"] == 14500.0

                rag_res = await session.call_tool(
                    "search_game_knowledge", {"query": "Liberal Media Network", "top_k": 2}
                )
                assert rag_res.is_error is False
                rag_payload = json.loads(rag_res.content[0].text)
                assert rag_payload["results"][0]["source_id"] == "tech_liberal_media_network_v1"

                bad_city_res = await session.call_tool("get_city_intelligence", {"city_name": "Atlantis"})
                assert bad_city_res.is_error is True

                spoof_res = await session.call_tool("get_player_budget", {"player_id": "opponent"})
                assert spoof_res.is_error is True

    anyio.run(_run_stdio_check)


def test_stringified_numeric_argument_coercion_and_bribery_math(tmp_path: Path) -> None:
    store, tool_log, sec_log = _make_isolated_store(tmp_path)

    # Stringified integers/numbers from 8B models succeed cleanly
    rag_str = call_tool_safely(
        "search_game_knowledge",
        {"query": "revolver duel", "top_k": "3"},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert rag_str["ok"] is True
    assert rag_str["top_k_requested"] == 3
    assert rag_str["top_k_used"] == 3
    assert len(rag_str["results"]) == 3

    ev_str = call_tool_safely(
        "get_recent_events",
        {"last_seconds": "300"},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert ev_str["ok"] is True
    assert ev_str["last_seconds"] == 300

    bribe_str = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "Demirkent", "bribe_amount": "4000"},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert bribe_str["ok"] is True
    assert bribe_str["bribe_amount"] == 4000.0
    # Aligned Unity DiminishingBribeCurve: 2.0 * (4000 / (4000 + 6000)) = 0.8
    assert bribe_str["estimated_vote_shift_pct_points"] == pytest.approx(0.8, abs=1e-4)

    # Verify 1.20x multiplier when tech_imperialist_patronage_v1 is unlocked
    snap = build_default_demo_snapshot("match-bribe-1", "player_a")
    snap["unlocked_bonuses"] = ["tech_imperialist_patronage_v1"]
    store.upsert_snapshot(snap, authenticated_player_id="player_a", activate=True)
    bribe_imp = call_tool_safely(
        "estimate_bribery_impact",
        {"city_name": "Demirkent", "bribe_amount": "4000"},
        store,
        tool_log_path=tool_log,
        security_log_path=sec_log,
    )
    assert bribe_imp["ok"] is True
    assert bribe_imp["imperialist_efficiency_bonus_active"] is True
    # 2.0 * ((4000 * 1.20) / (4800 + 6000)) = 2.0 * 4800 / 10800 = 0.8889
    assert bribe_imp["estimated_vote_shift_pct_points"] == pytest.approx(0.8889, abs=1e-4)

    # Booleans, non-numeric strings, NaNs, and negative values remain rejected
    for tool_name, bad_args in (
        ("search_game_knowledge", {"query": "revolver", "top_k": True}),
        ("search_game_knowledge", {"query": "revolver", "top_k": "abc"}),
        ("estimate_bribery_impact", {"city_name": "Demirkent", "bribe_amount": False}),
        ("estimate_bribery_impact", {"city_name": "Demirkent", "bribe_amount": "nan"}),
        ("estimate_bribery_impact", {"city_name": "Demirkent", "bribe_amount": "-50"}),
        ("get_recent_events", {"last_seconds": "-5"}),
    ):
        bad_res = call_tool_safely(tool_name, bad_args, store, tool_log_path=tool_log, security_log_path=sec_log)
        assert bad_res["ok"] is False and bad_res["error_type"] == "validation_error"
