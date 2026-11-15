"""Fog-of-War VisibilityService and SafeSessionStore for Political Mayhem MCP Server."""

from __future__ import annotations

import copy
import json
import sys
import threading
import unicodedata
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator, ValidationError

_MCP_DIR = Path(__file__).resolve().parent
_ROOT_DIR = _MCP_DIR.parent.parent
if str(_MCP_DIR) not in sys.path:
    sys.path.insert(0, str(_MCP_DIR))

from logging_utils import DEFAULT_SECURITY_LOG_PATH, log_security_event  # noqa: E402

SAFE_SNAPSHOT_SCHEMA_PATH = _ROOT_DIR / "ai" / "agent" / "schemas" / "safe_snapshot.schema.json"

CANONICAL_CITIES: tuple[str, ...] = (
    "Baskent",
    "Limankent",
    "Demirkent",
    "Yesilova",
    "Universitekent",
    "Dagsehir",
)

CANONICAL_CITY_POPULATIONS: dict[str, int] = {
    "Baskent": 120000,
    "Demirkent": 110000,
    "Limankent": 90000,
    "Yesilova": 80000,
    "Universitekent": 70000,
    "Dagsehir": 60000,
}

CANONICAL_IDEOLOGIES: tuple[str, ...] = (
    "nationalist",
    "liberal",
    "democrat",
    "communist",
    "imperialist",
)

# Deployable / player-commandable unit types in ActionPlan.
CANONICAL_UNITS: tuple[str, ...] = (
    "campaign_volunteer",
    "propagandist",
    "bodyguard",
    "rally_convoy",
)

ALL_KNOWN_UNITS: tuple[str, ...] = CANONICAL_UNITS + (
    "security_squad",
    "saboteur_agent",
)

ROUND_DEPLOY_LIMITS: dict[str, int] = {
    "campaign_volunteer": 6,
    "propagandist": 4,
    "bodyguard": 3,
    "rally_convoy": 2,
}

FORBIDDEN_SECRET_FIELDS: frozenset[str] = frozenset(
    {
        "opponent_budget",
        "opponent_overall",
        "hidden_support_rates",
        "opponent_private_plan",
        "opponent_bribes",
        "opponent_secret_plan",
        "enemy_budget",
        "enemy_overall",
        "secret_vote_rates",
        "opponent_exact_votes",
    }
)

IDENTITY_SPOOF_ARG_KEYS: frozenset[str] = frozenset(
    {
        "player_id",
        "target_player_id",
        "opponent_id",
        "user_id",
        "session_player_id",
        "match_id",
        "reveal_hidden",
        "bypass_fog_of_war",
        "include_opponent_secret",
    }
)


class SecurityViolationError(PermissionError):
    """Raised when a caller attempts unauthorized identity override or fog-of-war bypass."""


def _ascii_fold_lower(value: str) -> str:
    tr_map = str.maketrans("çğıİöşüÇĞÖŞÜ", "cgiiosuCGOSU")
    folded = value.translate(tr_map)
    normalized = unicodedata.normalize("NFKD", folded)
    stripped = "".join(ch for ch in normalized if not unicodedata.combining(ch))
    return " ".join(stripped.strip().lower().split())


_CITY_LOOKUP: dict[str, str] = {
    "baskent": "Baskent",
    "limankent": "Limankent",
    "demirkent": "Demirkent",
    "yesilova": "Yesilova",
    "universitekent": "Universitekent",
    "dagsehir": "Dagsehir",
}


def normalize_city_filter(city_name: str, *, allow_all: bool = True) -> str:
    """Validate and normalize a city argument against canonical 6 cities (or ``ALL``)."""
    if not isinstance(city_name, str) or not city_name.strip():
        raise ValueError("city_name must be a non-empty string.")
    cleaned = city_name.strip()
    if allow_all and cleaned.upper() == "ALL":
        return "ALL"
    folded = _ascii_fold_lower(cleaned)
    canonical = _CITY_LOOKUP.get(folded)
    if canonical is None:
        allowed = (["ALL"] if allow_all else []) + list(CANONICAL_CITIES)
        raise ValueError(f"Unknown city_name {city_name!r}. Allowed values: {allowed}.")
    return canonical


def build_default_demo_snapshot(
    match_id: str = "demo-match-001",
    player_id: str = "player_a",
) -> dict[str, Any]:
    """Return a rich, schema-valid Fog-of-War safe Round 4/6 match snapshot."""
    return {
        "match_id": match_id,
        "player_id": player_id,
        "round_number": 4,
        "phase": "PREPARATION",
        "current_role": "SABOTEUR",
        "party_budget": 14500.0,
        "player_overall": {
            "nationalist": 62.0,
            "liberal": 48.0,
            "democrat": 55.0,
            "communist": 22.0,
            "imperialist": 35.0,
        },
        "cities": [
            {
                "city_name": "Baskent",
                "population": 120000,
                "ideology_distribution": {
                    "nationalist": 34.0,
                    "liberal": 20.0,
                    "democrat": 22.0,
                    "communist": 8.0,
                    "imperialist": 16.0,
                },
                "rally_done": True,
                "visible_campaign_presence": {
                    "own_unit_count": 3,
                    "visible_opponent_unit_count": 1,
                    "contested": True,
                },
                "winner": "NONE",
            },
            {
                "city_name": "Demirkent",
                "population": 110000,
                "ideology_distribution": {
                    "nationalist": 26.0,
                    "liberal": 14.0,
                    "democrat": 20.0,
                    "communist": 28.0,
                    "imperialist": 12.0,
                },
                "rally_done": True,
                "visible_campaign_presence": {
                    "own_unit_count": 3,
                    "visible_opponent_unit_count": 3,
                    "contested": True,
                },
                "winner": "NONE",
            },
            {
                "city_name": "Limankent",
                "population": 90000,
                "ideology_distribution": {
                    "nationalist": 18.0,
                    "liberal": 38.0,
                    "democrat": 24.0,
                    "communist": 10.0,
                    "imperialist": 10.0,
                },
                "rally_done": True,
                "visible_campaign_presence": {
                    "own_unit_count": 2,
                    "visible_opponent_unit_count": 2,
                    "contested": True,
                },
                "winner": "NONE",
            },
            {
                "city_name": "Yesilova",
                "population": 80000,
                "ideology_distribution": {
                    "nationalist": 24.0,
                    "liberal": 20.0,
                    "democrat": 34.0,
                    "communist": 12.0,
                    "imperialist": 10.0,
                },
                "rally_done": False,
                "visible_campaign_presence": {
                    "own_unit_count": 7,
                    "visible_opponent_unit_count": 2,
                    "contested": True,
                },
                "winner": "NONE",
            },
            {
                "city_name": "Universitekent",
                "population": 70000,
                "ideology_distribution": {
                    "nationalist": 12.0,
                    "liberal": 32.0,
                    "democrat": 34.0,
                    "communist": 16.0,
                    "imperialist": 6.0,
                },
                "rally_done": False,
                "visible_campaign_presence": {
                    "own_unit_count": 2,
                    "visible_opponent_unit_count": 0,
                    "contested": False,
                },
                "winner": "NONE",
            },
            {
                "city_name": "Dagsehir",
                "population": 60000,
                "ideology_distribution": {
                    "nationalist": 40.0,
                    "liberal": 12.0,
                    "democrat": 20.0,
                    "communist": 8.0,
                    "imperialist": 20.0,
                },
                "rally_done": False,
                "visible_campaign_presence": {
                    "own_unit_count": 1,
                    "visible_opponent_unit_count": 0,
                    "contested": False,
                },
                "winner": "NONE",
            },
        ],
        "own_units": [
            {
                "unit_type": "campaign_volunteer",
                "count": 4,
                "city_name": "Yesilova",
                "zone": "TownSquare",
                "avg_health": 80.0,
            },
            {
                "unit_type": "propagandist",
                "count": 2,
                "city_name": "Yesilova",
                "zone": "MarketBoulevard",
                "avg_health": 70.0,
            },
            {
                "unit_type": "bodyguard",
                "count": 1,
                "city_name": "Yesilova",
                "zone": "TownSquare",
                "avg_health": 150.0,
            },
            {
                "unit_type": "campaign_volunteer",
                "count": 2,
                "city_name": "Demirkent",
                "zone": "FoundryGate",
                "avg_health": 74.0,
            },
            {
                "unit_type": "bodyguard",
                "count": 1,
                "city_name": "Demirkent",
                "zone": "UnionHall",
                "avg_health": 142.0,
            },
            {
                "unit_type": "propagandist",
                "count": 2,
                "city_name": "Baskent",
                "zone": "CentralPlaza",
                "avg_health": 70.0,
            },
            {
                "unit_type": "bodyguard",
                "count": 1,
                "city_name": "Baskent",
                "zone": "CentralPlaza",
                "avg_health": 150.0,
            },
            {
                "unit_type": "propagandist",
                "count": 2,
                "city_name": "Limankent",
                "zone": "HarborSquare",
                "avg_health": 68.0,
            },
            {
                "unit_type": "campaign_volunteer",
                "count": 2,
                "city_name": "Universitekent",
                "zone": "CampusPlaza",
                "avg_health": 80.0,
            },
            {
                "unit_type": "rally_convoy",
                "count": 1,
                "city_name": "Dagsehir",
                "zone": "HighlandPass",
                "avg_health": 180.0,
            },
        ],
        "visible_opponent_activity": [
            {
                "activity_type": "UNIT_SPOTTED",
                "city_name": "Demirkent",
                "unit_type": "propagandist",
                "count": 2,
                "zone": "IndustrialSquare",
                "seconds_ago": 14,
            },
            {
                "activity_type": "UNIT_SPOTTED",
                "city_name": "Demirkent",
                "unit_type": "campaign_volunteer",
                "count": 1,
                "zone": "FoundryGate",
                "seconds_ago": 22,
            },
            {
                "activity_type": "SPEECH_EXCERPT",
                "city_name": "Limankent",
                "zone": "HarborSquare",
                "speech_excerpt": "Lower port tariffs and open trade will bring rapid prosperity to Limankent.",
                "seconds_ago": 55,
            },
            {
                "activity_type": "UNIT_SPOTTED",
                "city_name": "Limankent",
                "unit_type": "bodyguard",
                "count": 2,
                "zone": "HarborGate",
                "seconds_ago": 42,
            },
            {
                "activity_type": "UNIT_SPOTTED",
                "city_name": "Yesilova",
                "unit_type": "campaign_volunteer",
                "count": 2,
                "zone": "MarketBoulevard",
                "seconds_ago": 9,
            },
        ],
        "recent_events": [
            {
                "event_type": "UNIT_SPOTTED",
                "summary": "Opponent campaign volunteers spotted advancing near Yesilova MarketBoulevard.",
                "city_name": "Yesilova",
                "seconds_ago": 9,
            },
            {
                "event_type": "UNIT_SPOTTED",
                "summary": "Unescorted opponent propagandists observed at Demirkent IndustrialSquare.",
                "city_name": "Demirkent",
                "seconds_ago": 14,
            },
            {
                "event_type": "DONATION_RECEIVED",
                "summary": "Earned +3200 party_budget in rally donations following Round 3 speech.",
                "city_name": "Limankent",
                "seconds_ago": 48,
            },
            {
                "event_type": "RALLY_COMPLETED",
                "summary": "Round 3 rally in Limankent concluded; moving into Round 4 preparation.",
                "city_name": "Limankent",
                "seconds_ago": 65,
            },
            {
                "event_type": "RALLY_COMPLETED",
                "summary": "Round 2 rally in Demirkent concluded without physical sabotage arrests.",
                "city_name": "Demirkent",
                "seconds_ago": 210,
            },
        ],
        "unlocked_bonuses": [
            "tech_nationalist_fervor_v1",
            "tech_liberal_media_network_v1",
            "tech_democrat_grassroots_v1",
        ],
    }


class VisibilityService:
    """Enforces Fog of War (Filter Before the Model) and Server-Side Identity Binding."""

    def __init__(self, schema_path: Path | None = None) -> None:
        target_schema = schema_path or SAFE_SNAPSHOT_SCHEMA_PATH
        self._schema: dict[str, Any] = json.loads(target_schema.read_text(encoding="utf-8"))
        self._validator = Draft202012Validator(self._schema)

    @classmethod
    def strip_forbidden_fields(cls, payload: Any, prefix: str = "") -> tuple[Any, list[str]]:
        """Recursively remove any forbidden hidden opponent fields before model/storage exposure."""
        stripped: list[str] = []
        if isinstance(payload, dict):
            cleaned: dict[str, Any] = {}
            for key, val in payload.items():
                key_str = str(key)
                path = f"{prefix}.{key_str}" if prefix else key_str
                if key_str.lower() in FORBIDDEN_SECRET_FIELDS:
                    stripped.append(path)
                    continue
                cleaned_val, child_stripped = cls.strip_forbidden_fields(val, prefix=path)
                cleaned[key_str] = cleaned_val
                stripped.extend(child_stripped)
            return cleaned, stripped
        if isinstance(payload, list):
            cleaned_list = []
            for idx, item in enumerate(payload):
                cleaned_item, child_stripped = cls.strip_forbidden_fields(item, prefix=f"{prefix}[{idx}]")
                cleaned_list.append(cleaned_item)
                stripped.extend(child_stripped)
            return cleaned_list, stripped
        return copy.deepcopy(payload), stripped

    @classmethod
    def contains_forbidden_fields(cls, payload: Any) -> list[str]:
        """Return any forbidden hidden-opponent keys found anywhere in ``payload``."""
        found: list[str] = []
        if isinstance(payload, dict):
            for key, val in payload.items():
                if str(key).lower() in FORBIDDEN_SECRET_FIELDS:
                    found.append(str(key))
                found.extend(cls.contains_forbidden_fields(val))
        elif isinstance(payload, list):
            for item in payload:
                found.extend(cls.contains_forbidden_fields(item))
        return found

    def sanitize_and_validate_snapshot(
        self,
        raw_snapshot: dict[str, Any],
        *,
        security_log_path: Path | None = None,
    ) -> tuple[dict[str, Any], list[str]]:
        """Strip any leaked secret fields (Security Rule 1), then validate against ``safe_snapshot.schema.json``."""
        if not isinstance(raw_snapshot, dict):
            raise ValueError("Snapshot payload must be a JSON object.")

        sanitized, stripped = self.strip_forbidden_fields(raw_snapshot)
        if stripped:
            match_id = str(sanitized.get("match_id") or raw_snapshot.get("match_id") or "unknown")
            player_id = str(sanitized.get("player_id") or raw_snapshot.get("player_id") or "unknown")
            log_security_event(
                event_type="FORBIDDEN_SNAPSHOT_FIELDS_STRIPPED",
                severity="HIGH",
                detail=f"Stripped forbidden hidden opponent fields before storage: {stripped}",
                match_session=f"{match_id}:{player_id}",
                player_id=player_id,
                metadata={"stripped_fields": stripped},
                log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
            )

        try:
            self._validator.validate(sanitized)
        except ValidationError as exc:
            raise ValueError(f"Invalid AiSafeSnapshot: {exc.message}") from exc

        return sanitized, stripped

    @staticmethod
    def verify_session_identity(
        *,
        authenticated_player_id: str,
        requested_player_id: str | None = None,
        session_match_id: str | None = None,
        requested_match_id: str | None = None,
        security_log_path: Path | None = None,
    ) -> None:
        """Reject attempts to override server-bound ``player_id`` or ``match_id`` (Security Rule 2)."""
        session_tag = f"{session_match_id or 'unknown'}:{authenticated_player_id}"
        if requested_player_id is not None and requested_player_id != authenticated_player_id:
            log_security_event(
                event_type="UNAUTHORIZED_IDENTITY_OVERRIDE",
                severity="CRITICAL",
                detail=(
                    f"Blocked attempt to override authenticated player_id "
                    f"{authenticated_player_id!r} with {requested_player_id!r}."
                ),
                match_session=session_tag,
                player_id=authenticated_player_id,
                metadata={"requested_player_id": requested_player_id},
                log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
            )
            raise SecurityViolationError(
                f"Unauthorized identity override: server-bound player_id is {authenticated_player_id!r}."
            )

        if (
            requested_match_id is not None
            and session_match_id is not None
            and requested_match_id != session_match_id
        ):
            log_security_event(
                event_type="UNAUTHORIZED_MATCH_OVERRIDE",
                severity="HIGH",
                detail=(
                    f"Blocked attempt to access match_id {requested_match_id!r} "
                    f"from active session {session_match_id!r}."
                ),
                match_session=session_tag,
                player_id=authenticated_player_id,
                metadata={"requested_match_id": requested_match_id},
                log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
            )
            raise SecurityViolationError(
                f"Unauthorized match_id override: active match_id is {session_match_id!r}."
            )

    @classmethod
    def verify_tool_call_security(
        cls,
        *,
        tool_name: str,
        arguments: dict[str, Any],
        authenticated_player_id: str,
        session_match_id: str,
        security_log_path: Path | None = None,
    ) -> None:
        """Inspect tool arguments for identity spoofing or unauthorized secret extraction attempts."""
        session_tag = f"{session_match_id}:{authenticated_player_id}"
        for key, value in arguments.items():
            key_lower = str(key).lower()
            if key_lower in IDENTITY_SPOOF_ARG_KEYS or key_lower in FORBIDDEN_SECRET_FIELDS:
                log_security_event(
                    event_type="UNAUTHORIZED_TOOL_ARGUMENT",
                    severity="CRITICAL",
                    detail=(
                        f"Blocked tool call {tool_name!r} passing disallowed identity/secret "
                        f"argument {key!r}={value!r}."
                    ),
                    match_session=session_tag,
                    player_id=authenticated_player_id,
                    metadata={"tool": tool_name, "rejected_arg": key},
                    log_path=security_log_path or DEFAULT_SECURITY_LOG_PATH,
                )
                raise SecurityViolationError(
                    f"Unauthorized tool argument {key!r}: identity and visibility are bound server-side."
                )


class SafeSessionStore:
    """Thread-safe store of Fog-of-War filtered snapshots indexed by ``(match_id, player_id)``."""

    def __init__(
        self,
        *,
        visibility_service: VisibilityService | None = None,
        security_log_path: Path | None = None,
        seed_demo: bool = True,
    ) -> None:
        self._lock = threading.RLock()
        self.visibility = visibility_service or VisibilityService()
        self.security_log_path = security_log_path or DEFAULT_SECURITY_LOG_PATH
        self._snapshots: dict[tuple[str, str], dict[str, Any]] = {}
        self._last_stripped_fields: dict[tuple[str, str], list[str]] = {}
        self._active_key: tuple[str, str] = ("demo-match-001", "player_a")

        if seed_demo:
            demo = build_default_demo_snapshot(*self._active_key)
            sanitized, _ = self.visibility.sanitize_and_validate_snapshot(
                demo, security_log_path=self.security_log_path
            )
            self._snapshots[self._active_key] = sanitized
            self._last_stripped_fields[self._active_key] = []

    def get_active_session_ids(self) -> tuple[str, str]:
        with self._lock:
            return self._active_key

    def set_active_session(self, match_id: str, player_id: str) -> None:
        with self._lock:
            key = (match_id, player_id)
            if key not in self._snapshots:
                demo = build_default_demo_snapshot(match_id=match_id, player_id=player_id)
                sanitized, _ = self.visibility.sanitize_and_validate_snapshot(
                    demo, security_log_path=self.security_log_path
                )
                self._snapshots[key] = sanitized
                self._last_stripped_fields[key] = []
            self._active_key = key

    def upsert_snapshot(
        self,
        raw_snapshot: dict[str, Any],
        *,
        authenticated_player_id: str | None = None,
        authenticated_match_id: str | None = None,
        activate: bool = True,
    ) -> tuple[dict[str, Any], list[str]]:
        """Validate, strip secret fields, and store snapshot under ``(match_id, player_id)``."""
        if not isinstance(raw_snapshot, dict):
            raise ValueError("Snapshot must be a JSON object.")

        if authenticated_player_id is not None:
            self.visibility.verify_session_identity(
                authenticated_player_id=authenticated_player_id,
                requested_player_id=raw_snapshot.get("player_id"),
                session_match_id=authenticated_match_id,
                requested_match_id=raw_snapshot.get("match_id"),
                security_log_path=self.security_log_path,
            )

        sanitized, stripped = self.visibility.sanitize_and_validate_snapshot(
            raw_snapshot, security_log_path=self.security_log_path
        )
        key = (sanitized["match_id"], sanitized["player_id"])
        with self._lock:
            self._snapshots[key] = sanitized
            self._last_stripped_fields[key] = stripped
            if activate:
                self._active_key = key
        return copy.deepcopy(sanitized), stripped

    def get_snapshot(
        self,
        match_id: str | None = None,
        authenticated_player_id: str | None = None,
    ) -> dict[str, Any]:
        """Return a deep copy of the authorized snapshot for ``(match_id, player_id)``."""
        with self._lock:
            active_match, active_player = self._active_key
            target_match = match_id or active_match
            target_player = authenticated_player_id or active_player
            key = (target_match, target_player)
            if key not in self._snapshots:
                raise KeyError(f"No snapshot stored for match_id={target_match!r}, player_id={target_player!r}.")
            return copy.deepcopy(self._snapshots[key])

    @staticmethod
    def get_action_limits(snapshot: dict[str, Any]) -> dict[str, Any]:
        """Compute authoritative per-round caps, valid unvisited cities, owned totals, and budget."""
        unlocked = set(snapshot.get("unlocked_bonuses", []))
        deploy_limits = dict(ROUND_DEPLOY_LIMITS)
        if "tech_democrat_grassroots_v1" in unlocked:
            deploy_limits["campaign_volunteer"] += 1

        owned_by_type: dict[str, int] = {}
        owned_by_city_and_type: dict[tuple[str, str], int] = {}
        for unit in snapshot.get("own_units", []):
            utype = unit["unit_type"]
            cnt = int(unit["count"])
            city = unit["city_name"]
            owned_by_type[utype] = owned_by_type.get(utype, 0) + cnt
            owned_by_city_and_type[(city, utype)] = owned_by_city_and_type.get((city, utype), 0) + cnt

        rallied_cities = [c["city_name"] for c in snapshot.get("cities", []) if c.get("rally_done")]
        remaining_cities = [c["city_name"] for c in snapshot.get("cities", []) if not c.get("rally_done")]

        return {
            "party_budget": float(snapshot.get("party_budget", 0.0)),
            "phase": snapshot.get("phase", "PREPARATION"),
            "round_number": int(snapshot.get("round_number", 1)),
            "deploy_limits": deploy_limits,
            "owned_by_type": owned_by_type,
            "owned_by_city_and_type": owned_by_city_and_type,
            "rallied_cities": rallied_cities,
            "remaining_cities": remaining_cities,
            "unlocked_bonuses": sorted(unlocked),
        }
