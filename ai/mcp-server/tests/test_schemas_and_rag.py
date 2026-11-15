"""Verification tests for Political Mayhem JSON Schemas (Draft 2020-12) and Hybrid RAG."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, ValidationError

ROOT_DIR = Path(__file__).resolve().parents[3]
SCHEMAS_DIR = ROOT_DIR / "ai" / "agent" / "schemas"
RAG_INDEX_PATH = ROOT_DIR / "ai" / "rag" / "index" / "knowledge_index.json"

from ai.rag.retriever import KnowledgeRetriever  # noqa: E402


def _load_schema(name: str) -> dict:
    return json.loads((SCHEMAS_DIR / name).read_text(encoding="utf-8"))


@pytest.mark.parametrize(
    "schema_file,expected_title",
    [
        ("action_plan.schema.json", "PoliticalMayhemActionPlan"),
        ("speech_eval.schema.json", "PoliticalMayhemSpeechEvaluation"),
        ("safe_snapshot.schema.json", "PoliticalMayhemAiSafeSnapshot"),
    ],
)
def test_schemas_are_valid_draft_2020_12(schema_file: str, expected_title: str) -> None:
    schema = _load_schema(schema_file)
    assert schema["$schema"] == "https://json-schema.org/draft/2020-12/schema"
    assert schema["title"] == expected_title
    Draft202012Validator.check_schema(schema)


def test_action_plan_accepts_valid_and_rejects_invalid() -> None:
    validator = Draft202012Validator(_load_schema("action_plan.schema.json"))

    valid_plan = {
        "summary": "Deploy 2 Bodyguards to protect Propagandists in Demirkent before the rally.",
        "confidence": 0.88,
        "actions": [
            {
                "action_type": "DEPLOY_UNIT",
                "unit_type": "bodyguard",
                "city_name": "Demirkent",
                "target_zone": "IndustrialSquare",
                "count": 2,
                "reason": "Counter opponent volunteers spotted near Demirkent.",
            },
            {
                "action_type": "CHOOSE_SPEECH_STRATEGY",
                "city_name": "Demirkent",
                "ideology_focus": "communist",
                "reason": "Demirkent has strong union voter share.",
            },
        ],
    }
    validator.validate(valid_plan)

    invalid_unknown_action = {
        "summary": "Unauthorized command",
        "confidence": 0.5,
        "actions": [{"action_type": "REVEAL_HIDDEN_BUDGET", "reason": "Illegal action"}],
    }
    with pytest.raises(ValidationError):
        validator.validate(invalid_unknown_action)

    invalid_negative_count = {
        "summary": "Negative unit count",
        "confidence": 0.5,
        "actions": [
            {
                "action_type": "DEPLOY_UNIT",
                "unit_type": "campaign_volunteer",
                "count": -3,
                "reason": "Invalid negative count",
            }
        ],
    }
    with pytest.raises(ValidationError):
        validator.validate(invalid_negative_count)


def test_speech_eval_accepts_valid_and_rejects_invalid() -> None:
    validator = Draft202012Validator(_load_schema("speech_eval.schema.json"))

    valid_eval = {
        "speech_effectiveness": 82.5,
        "ideology_scores": {
            "nationalist": 35.0,
            "liberal": 78.0,
            "democrat": 64.0,
            "communist": 10.0,
            "imperialist": 22.0,
        },
        "persuasion_impact": 76.0,
        "sabotage_effectiveness": 0.0,
        "crowd_reaction": "CHEER",
        "argument_quality": 84.0,
        "consistency_score": 89.0,
        "feedback_summary": "Strong market-freedom messaging resonated well with Limankent merchants.",
    }
    validator.validate(valid_eval)

    invalid_reaction = {**valid_eval, "crowd_reaction": "THROW_BRICKS"}
    with pytest.raises(ValidationError):
        validator.validate(invalid_reaction)


def test_safe_snapshot_forbids_hidden_opponent_fields() -> None:
    validator = Draft202012Validator(_load_schema("safe_snapshot.schema.json"))

    valid_snapshot = {
        "match_id": "match-001",
        "player_id": "player_a",
        "round_number": 3,
        "phase": "PREPARATION",
        "current_role": "SPEAKER",
        "party_budget": 14500.0,
        "player_overall": {
            "nationalist": 28.0,
            "liberal": 54.0,
            "democrat": 41.0,
            "communist": 12.0,
            "imperialist": 19.0,
        },
        "cities": [
            {
                "city_name": "Limankent",
                "population": 90000,
                "ideology_distribution": {
                    "nationalist": 15.0,
                    "liberal": 38.0,
                    "democrat": 25.0,
                    "communist": 12.0,
                    "imperialist": 10.0,
                },
                "rally_done": False,
                "visible_campaign_presence": {
                    "own_unit_count": 4,
                    "visible_opponent_unit_count": 2,
                    "contested": True,
                },
            }
        ],
        "own_units": [
            {
                "unit_type": "propagandist",
                "count": 2,
                "city_name": "Limankent",
                "zone": "HarborSquare",
                "avg_health": 70.0,
            }
        ],
        "visible_opponent_activity": [
            {
                "activity_type": "UNIT_SPOTTED",
                "city_name": "Limankent",
                "unit_type": "campaign_volunteer",
                "count": 2,
                "zone": "HarborGate",
                "seconds_ago": 6,
            }
        ],
        "recent_events": [
            {
                "event_type": "CITY_SELECTED",
                "summary": "Limankent selected for Round 3 rally.",
                "city_name": "Limankent",
                "seconds_ago": 15,
            }
        ],
        "unlocked_bonuses": ["tech_liberal_media_network_v1", "tech_democrat_grassroots_v1"],
    }
    validator.validate(valid_snapshot)

    for forbidden_key, forbidden_val in [
        ("opponent_budget", 25000),
        ("opponent_overall", {"nationalist": 50}),
        ("hidden_support_rates", {"Limankent": 0.52}),
    ]:
        leaked_snapshot = {**valid_snapshot, forbidden_key: forbidden_val}
        with pytest.raises(ValidationError):
            validator.validate(leaked_snapshot)


def test_rag_retriever_english_and_turkish_queries() -> None:
    retriever = KnowledgeRetriever(RAG_INDEX_PATH)

    r1 = retriever.search("What counters Propagandist units?", top_k=3)
    assert r1
    assert r1[0]["source_id"] in {"unit_propagandist_v1", "rule_unit_counters_v1"}

    r2 = retriever.search("Kampanya Gönüllüsü zayıf yönleri nelerdir?", top_k=3)
    assert r2
    assert r2[0]["source_id"] == "unit_campaign_volunteer_v1"

    r3 = retriever.search("Parti bütçesi miting sırasında harcanabilir mi?", top_k=3)
    assert r3
    assert r3[0]["source_id"] == "rule_donation_economy_v1"

    r4 = retriever.search("Election Night bribery max percentage swing", top_k=3)
    assert r4
    assert r4[0]["source_id"] == "rule_election_night_v1"
