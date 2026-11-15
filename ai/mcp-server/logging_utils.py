"""Structured JSONL logging and secret redaction for Political Mayhem MCP & Agent."""

from __future__ import annotations

import json
import re
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

MCP_SERVER_DIR = Path(__file__).resolve().parent
DEFAULT_TOOL_LOG_PATH = MCP_SERVER_DIR / "tool_logs.jsonl"
DEFAULT_SECURITY_LOG_PATH = MCP_SERVER_DIR / "security_events.jsonl"

_WRITE_LOCK = threading.Lock()

_SENSITIVE_KEY_FRAGMENTS = (
    "api_key",
    "apikey",
    "token",
    "secret",
    "password",
    "authorization",
    "credential",
    "opponent_budget",
    "opponent_overall",
    "hidden_support_rates",
    "opponent_private_plan",
    "opponent_bribes",
    "secret_vote_rates",
)

_SECRET_VALUE_PATTERNS = (
    re.compile(r"AIza[0-9A-Za-z_-]{20,}"),
    re.compile(r"sk-[0-9A-Za-z_-]{16,}"),
    re.compile(r"gsk_[0-9A-Za-z_-]{16,}"),
    re.compile(r"(Bearer\s+)[A-Za-z0-9._-]{8,}", re.IGNORECASE),
)


def utc_timestamp_iso() -> str:
    """Return an ISO-8601 UTC timestamp string."""
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def redact_text(value: str) -> str:
    """Scrub credential tokens or API keys from free-form text."""
    cleaned = value
    for pattern in _SECRET_VALUE_PATTERNS:
        if pattern.groups:
            cleaned = pattern.sub(r"\1[REDACTED]", cleaned)
        else:
            cleaned = pattern.sub("[REDACTED]", cleaned)
    return cleaned


def redact_sensitive(data: Any) -> Any:
    """Recursively redact sensitive keys and secret-like strings before logging."""
    if isinstance(data, dict):
        sanitized: dict[str, Any] = {}
        for key, val in data.items():
            key_lower = str(key).lower()
            if any(frag in key_lower for frag in _SENSITIVE_KEY_FRAGMENTS):
                sanitized[str(key)] = "[REDACTED]"
            else:
                sanitized[str(key)] = redact_sensitive(val)
        return sanitized
    if isinstance(data, list):
        return [redact_sensitive(item) for item in data]
    if isinstance(data, tuple):
        return [redact_sensitive(item) for item in data]
    if isinstance(data, str):
        return redact_text(data)
    return data


def append_jsonl(path: Path, record: dict[str, Any]) -> None:
    """Thread-safely append a single JSON record as a line to ``path``."""
    safe_record = redact_sensitive(record)
    line = json.dumps(safe_record, ensure_ascii=False)
    with _WRITE_LOCK:
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("a", encoding="utf-8") as handle:
            handle.write(line + "\n")


def log_tool_call(
    *,
    tool: str,
    duration_ms: float,
    status: str,
    result_count: int = 0,
    error: str | None = None,
    match_session: str = "demo-match-001:player_a",
    log_path: Path | None = None,
) -> dict[str, Any]:
    """Append a structured MCP tool invocation record to ``tool_logs.jsonl``."""
    record: dict[str, Any] = {
        "timestamp": utc_timestamp_iso(),
        "match_session": match_session,
        "tool": tool,
        "duration_ms": round(float(duration_ms), 3),
        "status": status,
        "result_count": int(result_count),
        "error": redact_text(error) if error else None,
    }
    append_jsonl(log_path or DEFAULT_TOOL_LOG_PATH, record)
    return record


def log_security_event(
    *,
    event_type: str,
    severity: str,
    detail: str,
    match_session: str = "demo-match-001:player_a",
    player_id: str | None = None,
    query_excerpt: str | None = None,
    metadata: dict[str, Any] | None = None,
    log_path: Path | None = None,
) -> dict[str, Any]:
    """Append a security/prompt-injection/authorization event to ``security_events.jsonl``."""
    excerpt = redact_text(query_excerpt[:240]) if query_excerpt else None
    record: dict[str, Any] = {
        "timestamp": utc_timestamp_iso(),
        "event_type": event_type,
        "severity": severity,
        "match_session": match_session,
        "player_id": player_id,
        "detail": redact_text(detail),
        "query_excerpt": excerpt,
        "metadata": redact_sensitive(metadata) if metadata else {},
    }
    append_jsonl(log_path or DEFAULT_SECURITY_LOG_PATH, record)
    return record
