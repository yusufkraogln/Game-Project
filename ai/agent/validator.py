"""JSON Schema and deterministic gameplay ActionPlan validator for Political Mayhem.

Canonical identifiers enforced before Photon Fusion 2 Host execution:
- ``city_name``: ``Baskent``, ``Limankent``, ``Demirkent``, ``Yesilova``, ``Universitekent``, ``Dagsehir``
- ``unit_type``: ``campaign_volunteer``, ``propagandist``, ``bodyguard``, ``rally_convoy``
  (Turkish display aliases such as ``Kampanya Gönüllüsü``, ``Propagandacı``, ``Koruma``,
  and ``Miting Konvoyu`` are normalized cleanly to these canonical IDs.)
- ``ideology_focus``: ``nationalist``, ``liberal``, ``democrat``, ``communist``, ``imperialist``
- ``action_type``: ``DEPLOY_UNIT``, ``MOVE_GROUP``, ``VOTE_RALLY_CITY``,
  ``CHOOSE_SPEECH_STRATEGY``, ``ALLOCATE_ELECTION_BRIBE``
"""

from __future__ import annotations

import copy
import json
import math
import sys
import unicodedata
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

_AGENT_DIR = Path(__file__).resolve().parent
_ROOT_DIR = _AGENT_DIR.parent.parent
_MCP_DIR = _ROOT_DIR / "ai" / "mcp-server"
for _p in (str(_MCP_DIR), str(_ROOT_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

from state_store import (  # noqa: E402
    CANONICAL_CITIES,
    CANONICAL_IDEOLOGIES,
    CANONICAL_UNITS,
    SafeSessionStore,
)

ACTION_PLAN_SCHEMA_PATH = _AGENT_DIR / "schemas" / "action_plan.schema.json"
ACTION_PLAN_SCHEMA: dict[str, Any] = json.loads(ACTION_PLAN_SCHEMA_PATH.read_text(encoding="utf-8"))
_SCHEMA_VALIDATOR = Draft202012Validator(ACTION_PLAN_SCHEMA)

CANONICAL_ACTION_TYPES: tuple[str, ...] = (
    "DEPLOY_UNIT",
    "MOVE_GROUP",
    "VOTE_RALLY_CITY",
    "CHOOSE_SPEECH_STRATEGY",
    "ALLOCATE_ELECTION_BRIBE",
)

ELECTION_NIGHT_PHASES: frozenset[str] = frozenset(
    {
        "ELECTION_FINAL_SPEECH",
        "ELECTION_BRIBE",
        "ELECTION_RESULT",
        "BALCONY_SPEECH",
    }
)

_ALLOWED_ACTION_KEYS: frozenset[str] = frozenset(
    {
        "action_type",
        "unit_type",
        "city_name",
        "target_zone",
        "count",
        "bribe_amount",
        "ideology_focus",
        "reason",
    }
)


def _fold_key(value: str) -> str:
    tr_map = str.maketrans("çğıİöşüÇĞÖŞÜ", "cgiiosuCGOSU")
    folded = value.translate(tr_map)
    normalized = unicodedata.normalize("NFKD", folded)
    ascii_only = "".join(ch for ch in normalized if not unicodedata.combining(ch))
    return " ".join(ascii_only.strip().lower().replace("_", " ").replace("-", " ").split())


# Canonical unit IDs and accepted Turkish / display aliases.
UNIT_ALIASES: dict[str, str] = {
    "campaign volunteer": "campaign_volunteer",
    "kampanya gonullusu": "campaign_volunteer",
    "gonullu": "campaign_volunteer",
    "volunteer": "campaign_volunteer",
    "propagandist": "propagandist",
    "propagandaci": "propagandist",
    "bodyguard": "bodyguard",
    "koruma": "bodyguard",
    "rally convoy": "rally_convoy",
    "miting konvoyu": "rally_convoy",
    "mobil saha ekibi": "rally_convoy",
    "convoy": "rally_convoy",
}

CITY_ALIASES: dict[str, str] = {
    "baskent": "Baskent",
    "limankent": "Limankent",
    "demirkent": "Demirkent",
    "yesilova": "Yesilova",
    "universitekent": "Universitekent",
    "dagsehir": "Dagsehir",
}

IDEOLOGY_ALIASES: dict[str, str] = {
    "nationalist": "nationalist",
    "milliyetci": "nationalist",
    "milliyetcilik": "nationalist",
    "liberal": "liberal",
    "liberalizm": "liberal",
    "democrat": "democrat",
    "demokrat": "democrat",
    "demokratlik": "democrat",
    "communist": "communist",
    "komunist": "communist",
    "komunizm": "communist",
    "imperialist": "imperialist",
    "emperyalist": "imperialist",
    "emperyalizm": "imperialist",
}


def normalize_unit_type(unit_type: Any) -> str | None:
    """Resolve canonical unit ID or accepted Turkish alias; return ``None`` if unknown."""
    if not isinstance(unit_type, str) or not unit_type.strip():
        return None
    if unit_type in CANONICAL_UNITS:
        return unit_type
    return UNIT_ALIASES.get(_fold_key(unit_type))


def normalize_city_name(city_name: Any) -> str | None:
    """Resolve canonical city ID or Turkish diacritic spelling; return ``None`` if unknown."""
    if not isinstance(city_name, str) or not city_name.strip():
        return None
    if city_name in CANONICAL_CITIES:
        return city_name
    return CITY_ALIASES.get(_fold_key(city_name))


def normalize_ideology(ideology: Any) -> str | None:
    """Resolve canonical ideology ID or Turkish alias; return ``None`` if unknown."""
    if not isinstance(ideology, str) or not ideology.strip():
        return None
    if ideology in CANONICAL_IDEOLOGIES:
        return ideology
    return IDEOLOGY_ALIASES.get(_fold_key(ideology))


@dataclass
class ValidationResult:
    """Result of schema + deterministic gameplay validation for an ActionPlan."""

    valid: bool
    errors: list[str] = field(default_factory=list)
    rejected_actions: list[dict[str, Any]] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "valid": self.valid,
            "errors": list(self.errors),
            "rejected_actions": list(self.rejected_actions),
        }


def normalize_action_plan(raw_plan: dict[str, Any]) -> dict[str, Any]:
    """Normalize LLM-produced ActionPlan fields (aliases, null stripping, string bounds) before validation."""
    if not isinstance(raw_plan, dict):
        return {}

    summary = str(raw_plan.get("summary") or "No tactical summary provided.").strip()
    if not summary:
        summary = "No tactical summary provided."
    summary = summary[:400]

    raw_conf = raw_plan.get("confidence", 0.5)
    try:
        conf = float(raw_conf) if not isinstance(raw_conf, bool) else 0.5
    except (TypeError, ValueError):
        conf = 0.5
    if not math.isfinite(conf):
        conf = 0.5
    conf = round(max(0.0, min(1.0, conf)), 3)

    normalized_actions: list[dict[str, Any]] = []
    raw_actions = raw_plan.get("actions")
    if isinstance(raw_actions, list):
        for raw_action in raw_actions[:8]:
            if not isinstance(raw_action, dict):
                continue
            cleaned: dict[str, Any] = {}
            for key in _ALLOWED_ACTION_KEYS:
                if key in raw_action and raw_action[key] is not None:
                    cleaned[key] = copy.deepcopy(raw_action[key])

            if isinstance(cleaned.get("action_type"), str):
                cleaned["action_type"] = cleaned["action_type"].strip().upper()

            if "city_name" in cleaned:
                norm_city = normalize_city_name(cleaned["city_name"])
                if norm_city is not None:
                    cleaned["city_name"] = norm_city

            if "unit_type" in cleaned:
                norm_unit = normalize_unit_type(cleaned["unit_type"])
                if norm_unit is not None:
                    cleaned["unit_type"] = norm_unit

            if "ideology_focus" in cleaned:
                norm_ideo = normalize_ideology(cleaned["ideology_focus"])
                if norm_ideo is not None:
                    cleaned["ideology_focus"] = norm_ideo

            if "target_zone" in cleaned:
                cleaned["target_zone"] = str(cleaned["target_zone"]).strip()[:80]
                if not cleaned["target_zone"]:
                    cleaned.pop("target_zone")

            if "reason" in cleaned:
                cleaned["reason"] = str(cleaned["reason"]).strip()[:250]
            if not cleaned.get("reason"):
                cleaned["reason"] = "Grounded in live match snapshot and game rules."

            if "count" in cleaned and not isinstance(cleaned["count"], bool):
                if isinstance(cleaned["count"], float) and cleaned["count"].is_integer():
                    cleaned["count"] = int(cleaned["count"])

            if "bribe_amount" in cleaned and not isinstance(cleaned["bribe_amount"], bool):
                try:
                    cleaned["bribe_amount"] = round(float(cleaned["bribe_amount"]), 2)
                except (TypeError, ValueError):
                    pass

            normalized_actions.append(cleaned)

    return {
        "summary": summary,
        "confidence": conf,
        "actions": normalized_actions,
    }


def validate_action_plan(plan: dict[str, Any], snapshot: dict[str, Any]) -> ValidationResult:
    """Validate ``plan`` against ``action_plan.schema.json`` and deterministic gameplay rules."""
    errors: list[str] = []
    rejected_actions: list[dict[str, Any]] = []

    if not isinstance(plan, dict):
        return ValidationResult(valid=False, errors=["ActionPlan must be a JSON object."])

    schema_errors = sorted(_SCHEMA_VALIDATOR.iter_errors(plan), key=lambda e: list(e.path))
    for err in schema_errors:
        loc = ".".join(str(p) for p in err.path) or "<root>"
        errors.append(f"Schema error at {loc}: {err.message}")

    limits = SafeSessionStore.get_action_limits(snapshot)
    phase = str(limits["phase"])
    party_budget = float(limits["party_budget"])
    deploy_limits: dict[str, int] = limits["deploy_limits"]
    owned_by_type: dict[str, int] = limits["owned_by_type"]
    city_map = {c["city_name"]: c for c in snapshot.get("cities", [])}

    deployed_this_plan: dict[str, int] = {}
    moved_this_plan: dict[str, int] = {}
    total_bribe_this_plan = 0.0

    actions = plan.get("actions")
    if isinstance(actions, list):
        for idx, action in enumerate(actions):
            action_errors: list[str] = []
            if not isinstance(action, dict):
                msg = f"Action[{idx}] must be an object."
                errors.append(msg)
                rejected_actions.append({"index": idx, "action": action, "errors": [msg]})
                continue

            action_type = action.get("action_type")
            if action_type not in CANONICAL_ACTION_TYPES:
                action_errors.append(
                    f"Action[{idx}] has invalid action_type {action_type!r}. "
                    f"Allowed: {list(CANONICAL_ACTION_TYPES)}."
                )

            raw_city = action.get("city_name")
            if raw_city is not None and raw_city not in CANONICAL_CITIES:
                action_errors.append(
                    f"Action[{idx}] has non-canonical city_name {raw_city!r}. "
                    f"Allowed: {list(CANONICAL_CITIES)}."
                )

            raw_unit = action.get("unit_type")
            resolved_unit: str | None = None
            if raw_unit is not None:
                resolved_unit = normalize_unit_type(raw_unit)
                if resolved_unit is None:
                    action_errors.append(
                        f"Action[{idx}] has invalid unit_type {raw_unit!r}. "
                        f"Canonical units: {list(CANONICAL_UNITS)}."
                    )

            raw_ideo = action.get("ideology_focus")
            if raw_ideo is not None and raw_ideo not in CANONICAL_IDEOLOGIES:
                action_errors.append(
                    f"Action[{idx}] has invalid ideology_focus {raw_ideo!r}. "
                    f"Allowed: {list(CANONICAL_IDEOLOGIES)}."
                )

            if action_type == "DEPLOY_UNIT":
                if phase in ELECTION_NIGHT_PHASES:
                    action_errors.append(
                        f"Action[{idx}] DEPLOY_UNIT is not allowed during Election Night phase {phase!r}."
                    )
                if resolved_unit is None:
                    action_errors.append(f"Action[{idx}] DEPLOY_UNIT requires a valid unit_type.")
                if raw_city not in CANONICAL_CITIES:
                    action_errors.append(f"Action[{idx}] DEPLOY_UNIT requires a valid canonical city_name.")
                count = action.get("count")
                if isinstance(count, bool) or not isinstance(count, int) or count < 1 or count > 20:
                    action_errors.append(f"Action[{idx}] DEPLOY_UNIT requires integer count in [1, 20].")
                elif resolved_unit is not None:
                    cap = deploy_limits.get(resolved_unit, 0)
                    new_total = deployed_this_plan.get(resolved_unit, 0) + count
                    if new_total > cap:
                        action_errors.append(
                            f"Action[{idx}] DEPLOY_UNIT exceeds per-round deployment cap for "
                            f"{resolved_unit}: requested {new_total} > cap {cap}."
                        )
                    else:
                        deployed_this_plan[resolved_unit] = new_total

            elif action_type == "MOVE_GROUP":
                if phase in ELECTION_NIGHT_PHASES:
                    action_errors.append(
                        f"Action[{idx}] MOVE_GROUP is not allowed during Election Night phase {phase!r}."
                    )
                if resolved_unit is None:
                    action_errors.append(f"Action[{idx}] MOVE_GROUP requires a valid unit_type.")
                if raw_city not in CANONICAL_CITIES:
                    action_errors.append(f"Action[{idx}] MOVE_GROUP requires a valid canonical destination city_name.")
                count = action.get("count")
                if isinstance(count, bool) or not isinstance(count, int) or count < 1 or count > 20:
                    action_errors.append(f"Action[{idx}] MOVE_GROUP requires integer count in [1, 20].")
                elif resolved_unit is not None:
                    owned = owned_by_type.get(resolved_unit, 0)
                    new_moved = moved_this_plan.get(resolved_unit, 0) + count
                    if new_moved > owned:
                        action_errors.append(
                            f"Action[{idx}] MOVE_GROUP requests {new_moved} {resolved_unit} units, "
                            f"but player only owns {owned}."
                        )
                    else:
                        moved_this_plan[resolved_unit] = new_moved

            elif action_type == "VOTE_RALLY_CITY":
                if phase in ELECTION_NIGHT_PHASES:
                    action_errors.append(
                        f"Action[{idx}] VOTE_RALLY_CITY is invalid during Election Night phase {phase!r}."
                    )
                if raw_city not in CANONICAL_CITIES:
                    action_errors.append(f"Action[{idx}] VOTE_RALLY_CITY requires a canonical city_name.")
                else:
                    city_info = city_map.get(raw_city)
                    if city_info and city_info.get("rally_done"):
                        action_errors.append(
                            f"Action[{idx}] VOTE_RALLY_CITY rejected: rally already completed in {raw_city}."
                        )

            elif action_type == "CHOOSE_SPEECH_STRATEGY":
                if raw_ideo not in CANONICAL_IDEOLOGIES:
                    action_errors.append(
                        f"Action[{idx}] CHOOSE_SPEECH_STRATEGY requires a canonical ideology_focus."
                    )

            elif action_type == "ALLOCATE_ELECTION_BRIBE":
                if phase != "ELECTION_BRIBE":
                    action_errors.append(
                        f"Action[{idx}] ALLOCATE_ELECTION_BRIBE is only allowed in phase 'ELECTION_BRIBE', "
                        f"not {phase!r}."
                    )
                if raw_city not in CANONICAL_CITIES:
                    action_errors.append(
                        f"Action[{idx}] ALLOCATE_ELECTION_BRIBE requires a canonical city_name."
                    )
                bribe = action.get("bribe_amount")
                if isinstance(bribe, bool) or not isinstance(bribe, (int, float)) or not math.isfinite(bribe) or bribe < 0:
                    action_errors.append(
                        f"Action[{idx}] ALLOCATE_ELECTION_BRIBE requires non-negative finite bribe_amount."
                    )
                else:
                    total_bribe_this_plan += float(bribe)
                    if total_bribe_this_plan > party_budget:
                        action_errors.append(
                            f"Action[{idx}] total ALLOCATE_ELECTION_BRIBE ({total_bribe_this_plan:.2f}) "
                            f"exceeds party_budget ({party_budget:.2f})."
                        )

            if action_errors:
                errors.extend(action_errors)
                rejected_actions.append({"index": idx, "action": action, "errors": action_errors})

    return ValidationResult(
        valid=len(errors) == 0,
        errors=errors,
        rejected_actions=rejected_actions,
    )
