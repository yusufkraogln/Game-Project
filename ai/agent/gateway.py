"""Local HTTP AI Gateway bridging Unity 6 / Photon clients with AgentRunner, Speech Evaluator, and MCP Store."""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import os
import sys
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import httpx
from dotenv import load_dotenv

load_dotenv()

_AGENT_DIR = Path(__file__).resolve().parent
_ROOT_DIR = _AGENT_DIR.parent.parent
_MCP_DIR = _ROOT_DIR / "ai" / "mcp-server"
for _p in (str(_MCP_DIR), str(_ROOT_DIR)):
    if _p not in sys.path:
        sys.path.insert(0, _p)

from ai.agent.agent_runner import AgentRunner  # noqa: E402
from ai.agent.llm_provider import (  # noqa: E402
    CLOUD_PRIORITY,
    DEFAULT_PROVIDER_MANAGER,
    ProviderConfigManager,
)
from ai.agent.speech_evaluator import evaluate_speech_with_meta  # noqa: E402
from server import DEFAULT_SESSION_STORE  # noqa: E402
from state_store import SafeSessionStore, SecurityViolationError  # noqa: E402


class GatewayApp:
    """Stateful controller handling AI Gateway HTTP operations."""

    def __init__(
        self,
        *,
        session_store: SafeSessionStore | None = None,
        provider_manager: ProviderConfigManager | None = None,
        agent_runner: AgentRunner | None = None,
    ) -> None:
        self.session_store = session_store or DEFAULT_SESSION_STORE
        self.provider_manager = provider_manager or DEFAULT_PROVIDER_MANAGER
        self.agent_runner = agent_runner or AgentRunner(
            session_store=self.session_store,
            provider_manager=self.provider_manager,
        )

    def _normalize_provider_arg(self, raw_provider: Any) -> str | None:
        if raw_provider is None:
            return None
        text = str(raw_provider).strip()
        if not text:
            return None
        lowered = text.lower()
        if lowered == "cloud":
            for candidate in CLOUD_PRIORITY:
                if self.provider_manager.get_api_key(candidate):
                    return candidate
            return "api"
        return lowered

    def handle_health(self) -> tuple[int, dict[str, Any]]:
        match_id, player_id = self.session_store.get_active_session_ids()
        snap = self.session_store.get_snapshot(match_id, player_id)
        health = self.provider_manager.get_health_summary()
        avail = health.get("available_providers", {})
        cloud_available = any(
            bool(avail.get(c, {}).get("configured")) for c in CLOUD_PRIORITY
        )
        return HTTPStatus.OK, {
            "status": "ok",
            "provider": health["configured_provider"],
            "active_provider": health["resolved_provider"],
            "ollama_available": bool(avail.get("ollama", {}).get("reachable")),
            "cloud_available": cloud_available,
            **health,
            "active_session": {
                "match_id": match_id,
                "player_id": player_id,
                "round_number": snap.get("round_number"),
                "phase": snap.get("phase"),
                "current_role": snap.get("current_role", "NONE"),
                "party_budget": snap.get("party_budget"),
            },
        }

    def handle_provider_config(self, body: dict[str, Any]) -> tuple[int, dict[str, Any]]:
        if not isinstance(body, dict):
            return HTTPStatus.BAD_REQUEST, {"error": "Request body must be a JSON object."}
        raw_provider = body.get("provider") if body.get("provider") is not None else body.get("provider_mode")
        model = body.get("model")
        if raw_provider is None and model is None:
            return HTTPStatus.BAD_REQUEST, {"error": "Provide at least one of 'provider' ('provider_mode') or 'model'."}
        provider = self._normalize_provider_arg(raw_provider)
        try:
            updated = self.provider_manager.update_config(provider=provider, model=model)
            health = self.provider_manager.get_health_summary()
            resolved_provider = health["resolved_provider"]
            resolved_model = health["resolved_model"]
            provider_mode_val = (
                str(raw_provider).strip().lower()
                if isinstance(raw_provider, str) and raw_provider.strip()
                else updated["default_provider"]
            )
            return HTTPStatus.OK, {
                "status": "ok",
                "ok": True,
                "config": updated,
                "provider_mode": provider_mode_val,
                "active_provider": resolved_provider,
                "model_name": resolved_model,
                "resolved_provider": resolved_provider,
                "resolved_model": resolved_model,
                "message": f"Provider configured: {resolved_provider} ({resolved_model})",
            }
        except ValueError as exc:
            return HTTPStatus.BAD_REQUEST, {"error": str(exc)}

    def handle_snapshot(
        self,
        body: dict[str, Any],
        *,
        header_player_id: str | None = None,
        header_match_id: str | None = None,
    ) -> tuple[int, dict[str, Any]]:
        if not isinstance(body, dict):
            return HTTPStatus.BAD_REQUEST, {"error": "Snapshot payload must be a JSON object."}

        raw_snapshot = body.get("snapshot") if "snapshot" in body and isinstance(body["snapshot"], dict) else body
        auth_player = header_player_id or body.get("authenticated_player_id")
        auth_match = header_match_id or body.get("authenticated_match_id")

        try:
            stored, stripped = self.session_store.upsert_snapshot(
                raw_snapshot,
                authenticated_player_id=auth_player,
                authenticated_match_id=auth_match,
                activate=True,
            )
            return HTTPStatus.OK, {
                "status": "ok",
                "match_id": stored["match_id"],
                "player_id": stored["player_id"],
                "round_number": stored["round_number"],
                "phase": stored["phase"],
                "stripped_forbidden_fields": stripped,
            }
        except SecurityViolationError as exc:
            return HTTPStatus.FORBIDDEN, {"error": str(exc), "error_type": "security_error"}
        except ValueError as exc:
            return HTTPStatus.BAD_REQUEST, {"error": str(exc), "error_type": "validation_error"}

    def handle_commander_ask(
        self,
        body: dict[str, Any],
        *,
        header_player_id: str | None = None,
        header_match_id: str | None = None,
    ) -> tuple[int, dict[str, Any]]:
        if not isinstance(body, dict):
            return HTTPStatus.BAD_REQUEST, {"error": "Request body must be a JSON object."}

        question = body.get("player_question") or body.get("question") or body.get("query")
        if not isinstance(question, str) or not question.strip():
            return HTTPStatus.BAD_REQUEST, {"error": "'player_question' must be a non-empty string."}

        raw_provider = body.get("provider") if body.get("provider") is not None else body.get("provider_mode")
        provider = self._normalize_provider_arg(raw_provider)
        model = body.get("model")
        inline_snapshot = body.get("snapshot") if isinstance(body.get("snapshot"), dict) else None
        req_player_id = header_player_id or body.get("player_id")
        req_match_id = header_match_id or body.get("match_id")

        try:
            result = self.agent_runner.run(
                question.strip(),
                match_id=req_match_id,
                player_id=req_player_id,
                provider=provider,
                model=model,
                snapshot=inline_snapshot,
            )
            plan = result.get("plan") if isinstance(result.get("plan"), dict) else {}
            security_blocked = bool(result.get("security_blocked", False))
            provider_used = str(result.get("provider_used") or "heuristic_fallback")
            model_used = str(result.get("model_used") or "deterministic-fallback")
            fallback_used = provider_used == "heuristic_fallback"
            explanation = str(plan.get("summary") or "")
            security_reason = (
                explanation or "Blocked under Fog of War policy."
                if security_blocked
                else ""
            )
            latency_ms = int(round(float(result.get("latency_ms", 0))))

            formatted_tool_calls: list[dict[str, Any]] = []
            for tc in result.get("tool_calls") or []:
                if not isinstance(tc, dict):
                    continue
                args = tc.get("arguments") if isinstance(tc.get("arguments"), dict) else {}
                tc_ok = bool(tc.get("ok"))
                err = tc.get("error")
                status_str = (
                    "OK_FOG_FILTERED"
                    if (security_blocked and tc_ok)
                    else ("OK" if tc_ok else "ERROR")
                )
                formatted_tool_calls.append(
                    {
                        "tool": str(tc.get("tool") or ""),
                        "arguments": args,
                        "ok": tc_ok,
                        "error": err,
                        "duration_ms": int(round(float(tc.get("duration_ms", 1)))),
                        "status": str(tc.get("status") or status_str),
                        "arguments_summary": str(
                            tc.get("arguments_summary")
                            or json.dumps(args, ensure_ascii=False)[:160]
                        ),
                        "result_summary": str(
                            tc.get("result_summary")
                            or (f"Error: {err}" if err else "OK")[:160]
                        ),
                    }
                )

            response_payload: dict[str, Any] = {
                "ok": True,
                "plan": plan,
                "action_plan": plan,
                "validation": result.get("validation", {}),
                "tool_calls": formatted_tool_calls,
                "rag_sources": result.get("rag_sources", []),
                "latency_ms": latency_ms,
                "provider_used": provider_used,
                "model_used": model_used,
                "model_name": model_used,
                "fallback_used": fallback_used,
                "is_fallback": fallback_used,
                "security_blocked": security_blocked,
                "security_reason": security_reason,
                "security_note": security_reason,
                "explanation": explanation,
            }
            return HTTPStatus.OK, response_payload
        except SecurityViolationError as exc:
            return HTTPStatus.FORBIDDEN, {"error": str(exc), "error_type": "security_error"}
        except ValueError as exc:
            return HTTPStatus.BAD_REQUEST, {"error": str(exc), "error_type": "validation_error"}

    def handle_speech_evaluate(self, body: dict[str, Any]) -> tuple[int, dict[str, Any]]:
        if not isinstance(body, dict):
            return HTTPStatus.BAD_REQUEST, {"error": "Request body must be a JSON object."}

        speech_text = body.get("speech_text") or body.get("speaker_transcript")
        if not isinstance(speech_text, str) or not speech_text.strip():
            return HTTPStatus.BAD_REQUEST, {
                "error": "'speech_text' ('speaker_transcript') must be a non-empty string."
            }

        sabotage_text = str(
            body.get("sabotage_text")
            or body.get("verbal_sabotage_text")
            or body.get("saboteur_transcript")
            or ""
        )
        city_name = str(body.get("city_name") or "Yesilova")
        speech_mode = str(body.get("speech_mode") or body.get("speech_type") or "RALLY_SPEECH")
        raw_provider = body.get("provider") if body.get("provider") is not None else body.get("provider_mode")
        provider = self._normalize_provider_arg(raw_provider)
        model = body.get("model")

        city_dist = (
            body.get("city_ideology_distribution")
            if body.get("city_ideology_distribution") is not None
            else body.get("city_ideologies")
        )
        speaker_overall = body.get("speaker_overall")

        if city_dist is None or speaker_overall is None:
            try:
                snap = self.session_store.get_snapshot()
                if speaker_overall is None:
                    speaker_overall = snap.get("player_overall")
                if city_dist is None:
                    match_city = next(
                        (c for c in snap.get("cities", []) if c["city_name"].lower() == city_name.lower()),
                        None,
                    )
                    if match_city is not None:
                        city_dist = match_city["ideology_distribution"]
            except KeyError:
                pass

        try:
            result, meta = evaluate_speech_with_meta(
                speech_text,
                sabotage_text=sabotage_text,
                city_name=city_name,
                city_ideology_distribution=city_dist,
                speaker_overall=speaker_overall,
                speech_mode=speech_mode,
                provider=provider,
                model=model,
                provider_manager=self.provider_manager,
            )
            provider_used = str(meta.get("provider_used") or "heuristic_fallback")
            model_used = str(meta.get("model_used") or "deterministic-fallback")
            fallback_used = bool(meta.get("fallback_used", provider_used == "heuristic_fallback"))
            latency_ms = int(round(float(meta.get("latency_ms", 0))))
            return HTTPStatus.OK, {
                **result,
                "ok": True,
                "provider_used": provider_used,
                "model_name": model_used,
                "fallback_used": fallback_used,
                "latency_ms": latency_ms,
                "evaluation": result,
            }
        except ValueError as exc:
            return HTTPStatus.BAD_REQUEST, {"error": str(exc)}

    def handle_speech_transcribe(self, body: dict[str, Any]) -> tuple[int, dict[str, Any]]:
        if not isinstance(body, dict):
            return HTTPStatus.BAD_REQUEST, {"error": "Request body must be a JSON object."}

        t0 = time.perf_counter()
        city_name = str(body.get("city_name") or "Baskent")
        top_ideology = str(body.get("top_ideology") or "").strip().lower()
        language = str(body.get("language") or "tr").strip() or "tr"

        try:
            duration_seconds = float(body.get("duration_seconds") or 1.0)
        except (TypeError, ValueError):
            duration_seconds = 1.0

        audio_b64 = str(body.get("audio_wav_base64") or body.get("audio_base64") or "").strip()
        wav_bytes: bytes | None = None
        if audio_b64:
            try:
                wav_bytes = base64.b64decode(audio_b64, validate=True)
            except (binascii.Error, ValueError) as exc:
                return HTTPStatus.BAD_REQUEST, {"error": f"Invalid base64 WAV audio: {exc}"}

        if not top_ideology:
            try:
                snap = self.session_store.get_snapshot()
                match_city = next(
                    (c for c in snap.get("cities", []) if c["city_name"].lower() == city_name.lower()),
                    None,
                )
                if match_city and isinstance(match_city.get("ideology_distribution"), dict):
                    dist = match_city["ideology_distribution"]
                    top_ideology = max(dist, key=lambda k: float(dist[k]))
            except KeyError:
                pass
        if top_ideology not in ("nationalist", "liberal", "democrat", "communist", "imperialist"):
            top_ideology = "democrat"

        # Try cloud Whisper STT (Groq or OpenAI) when audio bytes and API key are available
        if wav_bytes:
            whisper_targets = (
                ("groq", "https://api.groq.com/openai/v1/audio/transcriptions", "whisper-large-v3-turbo"),
                ("openai", "https://api.openai.com/v1/audio/transcriptions", "whisper-1"),
            )
            for prov_key, endpoint_url, whisper_model in whisper_targets:
                api_key = self.provider_manager.get_api_key(prov_key)
                if not api_key:
                    continue
                try:
                    resp = httpx.post(
                        endpoint_url,
                        headers={"Authorization": f"Bearer {api_key}"},
                        files={"file": ("speech.wav", wav_bytes, "audio/wav")},
                        data={"model": whisper_model, "language": language},
                        timeout=20.0,
                    )
                    resp.raise_for_status()
                    text = str((resp.json() or {}).get("text") or "").strip()
                    if text:
                        return HTTPStatus.OK, {
                            "ok": True,
                            "transcript": text,
                            "provider_used": f"{prov_key}-whisper",
                            "fallback_used": False,
                            "latency_ms": int(round((time.perf_counter() - t0) * 1000.0)),
                        }
                except (httpx.HTTPError, ValueError):
                    continue

        # Context-aware Turkish rally speech fallback aligned to city's top ideology & duration
        detailed = duration_seconds >= 3.5
        fallback_lines = {
            "nationalist": (
                f"{city_name} meydanından sesleniyoruz: yerli sanayi, güçlü sınır güvenliği ve ulusal egemenlik ile yarınlarımızı koruyacağız!"
                if detailed
                else f"{city_name} için yerli sanayi, vatan ve güçlü sınır güvenliği!"
            ),
            "liberal": (
                f"{city_name} esnafı ve girişimcileriyle serbest piyasa, açık ticaret ve düşük vergilerle refahı büyüteceğiz!"
                if detailed
                else f"{city_name} için serbest piyasa, açık ticaret ve girişim özgürlüğü!"
            ),
            "democrat": (
                f"{city_name} halkıyla katılımcı demokrasi, hukuk devleti ve şeffaf yönetim temelinde ortak bir gelecek kuracağız!"
                if detailed
                else f"{city_name} için şeffaf demokrasi, adalet ve toplumsal uzlaşı!"
            ),
            "communist": (
                f"{city_name} işçileri ve emekçileri için sendikal haklar, güçlü kamu yatırımları ve eşit paylaşım kuracağız!"
                if detailed
                else f"{city_name} işçileri için emek, dayanışma ve kamu yatırımı!"
            ),
            "imperialist": (
                f"{city_name} merkezli büyük altyapı hamleleriyle bölgesel gücümüzü ve stratejik otoritemizi zirveye taşıyacağız!"
                if detailed
                else f"{city_name} için büyük güç vizyonu ve dev projeler!"
            ),
        }
        transcript = fallback_lines[top_ideology]
        return HTTPStatus.OK, {
            "ok": True,
            "transcript": transcript,
            "provider_used": "offline_ideology_stt_fallback",
            "fallback_used": True,
            "latency_ms": int(round((time.perf_counter() - t0) * 1000.0)),
        }


def _make_handler(app: GatewayApp, *, quiet: bool = True) -> type[BaseHTTPRequestHandler]:
    class GatewayHTTPHandler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, format: str, *args: Any) -> None:
            if not quiet:
                super().log_message(format, *args)

        def _send_json(self, status: int, payload: dict[str, Any]) -> None:
            encoded = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(encoded)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "Content-Type, X-Player-Id, X-Match-Id")
            self.end_headers()
            self.wfile.write(encoded)

        def _read_json_body(self) -> dict[str, Any]:
            length_raw = self.headers.get("Content-Length", "0")
            try:
                length = int(length_raw)
            except ValueError as exc:
                raise ValueError("Invalid Content-Length header.") from exc
            if length <= 0:
                return {}
            if length > 2_000_000:
                raise ValueError("Request payload exceeds 2MB limit.")
            raw = self.rfile.read(length).decode("utf-8")
            parsed = json.loads(raw)
            if not isinstance(parsed, dict):
                raise ValueError("JSON body must be an object.")
            return parsed

        def do_OPTIONS(self) -> None:
            self._send_json(HTTPStatus.OK, {"status": "ok"})

        def do_GET(self) -> None:
            path = urlparse(self.path).path.rstrip("/")
            if path == "/api/health":
                status, payload = app.handle_health()
                self._send_json(status, payload)
                return
            self._send_json(HTTPStatus.NOT_FOUND, {"error": f"Unknown GET endpoint: {path}"})

        def do_POST(self) -> None:
            path = urlparse(self.path).path.rstrip("/")
            try:
                body = self._read_json_body()
            except (ValueError, json.JSONDecodeError) as exc:
                self._send_json(HTTPStatus.BAD_REQUEST, {"error": f"Invalid JSON request: {exc}"})
                return

            header_player = self.headers.get("X-Player-Id")
            header_match = self.headers.get("X-Match-Id")

            if path == "/api/provider/config":
                status, payload = app.handle_provider_config(body)
            elif path == "/api/snapshot":
                status, payload = app.handle_snapshot(
                    body, header_player_id=header_player, header_match_id=header_match
                )
            elif path == "/api/commander/ask":
                status, payload = app.handle_commander_ask(
                    body, header_player_id=header_player, header_match_id=header_match
                )
            elif path == "/api/speech/evaluate":
                status, payload = app.handle_speech_evaluate(body)
            elif path == "/api/speech/transcribe":
                status, payload = app.handle_speech_transcribe(body)
            else:
                status, payload = HTTPStatus.NOT_FOUND, {"error": f"Unknown POST endpoint: {path}"}

            self._send_json(status, payload)

    return GatewayHTTPHandler


def create_gateway_server(
    host: str | None = None,
    port: int | None = None,
    *,
    app: GatewayApp | None = None,
    quiet: bool = True,
) -> ThreadingHTTPServer:
    """Create a configured ``ThreadingHTTPServer`` bound to ``AI_GATEWAY_HOST:AI_GATEWAY_PORT``."""
    bind_host = host or os.getenv("AI_GATEWAY_HOST", "127.0.0.1")
    bind_port = port if port is not None else int(os.getenv("AI_GATEWAY_PORT", "8765"))
    gateway_app = app or GatewayApp()
    handler_cls = _make_handler(gateway_app, quiet=quiet)
    server = ThreadingHTTPServer((bind_host, bind_port), handler_cls)
    server.daemon_threads = True
    return server


def main() -> None:
    parser = argparse.ArgumentParser(description="Political Mayhem AI HTTP Gateway")
    parser.add_argument("--host", default=os.getenv("AI_GATEWAY_HOST", "127.0.0.1"))
    parser.add_argument("--port", type=int, default=int(os.getenv("AI_GATEWAY_PORT", "8765")))
    parser.add_argument("--verbose", action="store_true", help="Print HTTP access logs.")
    args = parser.parse_args()

    httpd = create_gateway_server(host=args.host, port=args.port, quiet=not args.verbose)
    print(f"Political Mayhem AI Gateway listening on http://{args.host}:{args.port}", flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.shutdown()
        httpd.server_close()


if __name__ == "__main__":
    main()
