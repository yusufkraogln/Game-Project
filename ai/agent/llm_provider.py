"""Dual-provider LLM abstraction supporting Local Ollama and Cloud APIs (Gemini, OpenAI, Groq)."""

from __future__ import annotations

import copy
import json
import os
import threading
import time
from dataclasses import dataclass
from typing import Any

import httpx
from dotenv import load_dotenv

load_dotenv()

VALID_PROVIDER_MODES = ("auto", "ollama", "api", "gemini", "openai", "groq")
CLOUD_PRIORITY = ("gemini", "groq", "openai")


class ProviderError(RuntimeError):
    """Base error for LLM provider failures."""


class ProviderUnavailableError(ProviderError):
    """Raised when the requested LLM provider is unconfigured or unreachable."""


@dataclass
class LLMResponse:
    """Normalized provider-independent LLM response."""

    content: str
    tool_calls: list[dict[str, Any]]
    provider_used: str
    model_used: str
    raw_message: dict[str, Any]
    latency_ms: float


def _clean_env(key: str, default: str = "") -> str:
    return os.getenv(key, default).strip()


def _parse_tool_arguments(raw_args: Any) -> dict[str, Any]:
    if isinstance(raw_args, dict):
        return raw_args
    if isinstance(raw_args, str):
        text = raw_args.strip()
        if not text:
            return {}
        try:
            parsed = json.loads(text)
            return parsed if isinstance(parsed, dict) else {}
        except json.JSONDecodeError:
            return {}
    return {}


class ProviderConfigManager:
    """Thread-safe runtime LLM provider & model selector with health discovery."""

    def __init__(self) -> None:
        self._lock = threading.RLock()
        initial = _clean_env("LLM_PROVIDER", "auto").lower()
        self.default_provider: str = initial if initial in VALID_PROVIDER_MODES else "auto"
        self.default_model: str | None = None

        self.ollama_base_url: str = _clean_env("OLLAMA_BASE_URL", "http://localhost:11434").rstrip("/")
        self.default_models: dict[str, str] = {
            "ollama": _clean_env("OLLAMA_MODEL", "qwen3:8b") or "qwen3:8b",
            "gemini": _clean_env("GEMINI_MODEL", "gemini-2.5-flash") or "gemini-2.5-flash",
            "openai": _clean_env("OPENAI_MODEL", "gpt-4o-mini") or "gpt-4o-mini",
            "groq": _clean_env("GROQ_MODEL", "llama-3.3-70b-versatile") or "llama-3.3-70b-versatile",
        }

    def get_api_key(self, provider: str) -> str:
        env_map = {
            "gemini": "GEMINI_API_KEY",
            "openai": "OPENAI_API_KEY",
            "groq": "GROQ_API_KEY",
        }
        env_var = env_map.get(provider)
        return _clean_env(env_var) if env_var else ""

    def check_ollama(self, timeout: float = 1.5) -> tuple[bool, list[str]]:
        """Check whether local Ollama daemon is reachable and return installed model names."""
        try:
            resp = httpx.get(f"{self.ollama_base_url}/api/tags", timeout=timeout)
            if resp.status_code != 200:
                return False, []
            data = resp.json()
            models = [m.get("name", "") for m in data.get("models", []) if m.get("name")]
            return True, models
        except (httpx.HTTPError, ValueError):
            return False, []

    def update_config(self, *, provider: str | None = None, model: str | None = None) -> dict[str, Any]:
        """Update active runtime provider and/or model override."""
        with self._lock:
            if provider is not None:
                norm = provider.strip().lower()
                if norm not in VALID_PROVIDER_MODES:
                    raise ValueError(
                        f"Unsupported provider {provider!r}. Allowed: {list(VALID_PROVIDER_MODES)}."
                    )
                self.default_provider = norm

            if model is not None:
                cleaned_model = model.strip()
                self.default_model = cleaned_model or None
                if cleaned_model and self.default_provider in self.default_models:
                    self.default_models[self.default_provider] = cleaned_model

            return self.get_config()

    def get_config(self) -> dict[str, Any]:
        with self._lock:
            return {
                "default_provider": self.default_provider,
                "default_model": self.default_model,
                "provider_models": dict(self.default_models),
                "ollama_base_url": self.ollama_base_url,
            }

    def resolve_provider(
        self,
        override_provider: str | None = None,
        override_model: str | None = None,
    ) -> tuple[str, str]:
        """Resolve concrete ``(provider_name, model_name)`` honoring runtime override and ``auto`` rules."""
        with self._lock:
            mode = (override_provider or self.default_provider or "auto").strip().lower()
            if mode not in VALID_PROVIDER_MODES:
                raise ValueError(
                    f"Unsupported provider {mode!r}. Allowed: {list(VALID_PROVIDER_MODES)}."
                )

            explicit_model = (override_model or self.default_model or "").strip() or None

            if mode == "auto":
                for cloud in CLOUD_PRIORITY:
                    if self.get_api_key(cloud):
                        return cloud, explicit_model or self.default_models[cloud]
                ollama_ok, installed = self.check_ollama()
                if ollama_ok:
                    chosen_model = explicit_model or self.default_models["ollama"]
                    if chosen_model not in installed and installed:
                        # Prefer qwen3 or llama3.1 chat models over embedding models
                        chat_models = [m for m in installed if "embed" not in m.lower()]
                        if chat_models and not any(m.startswith(chosen_model.split(":")[0]) for m in chat_models):
                            chosen_model = chat_models[0]
                    return "ollama", chosen_model
                raise ProviderUnavailableError(
                    "No Cloud API key configured and local Ollama daemon is unreachable."
                )

            if mode == "api":
                for cloud in CLOUD_PRIORITY:
                    if self.get_api_key(cloud):
                        return cloud, explicit_model or self.default_models[cloud]
                raise ProviderUnavailableError(
                    "Cloud API mode requested, but none of GEMINI_API_KEY, GROQ_API_KEY, or OPENAI_API_KEY is set."
                )

            if mode == "ollama":
                ollama_ok, _ = self.check_ollama()
                if not ollama_ok:
                    raise ProviderUnavailableError(
                        f"Ollama requested, but daemon is not reachable at {self.ollama_base_url}."
                    )
                return "ollama", explicit_model or self.default_models["ollama"]

            # Specific cloud provider: gemini, openai, groq
            if not self.get_api_key(mode):
                raise ProviderUnavailableError(f"{mode.upper()} requested, but its API key is not configured.")
            return mode, explicit_model or self.default_models[mode]

    def get_health_summary(self) -> dict[str, Any]:
        """Return safe provider availability details without exposing any secret keys."""
        ollama_ok, ollama_models = self.check_ollama()
        resolved_provider = None
        resolved_model = None
        try:
            resolved_provider, resolved_model = self.resolve_provider()
        except ProviderUnavailableError:
            pass

        cfg = self.get_config()
        return {
            "configured_provider": cfg["default_provider"],
            "configured_model_override": cfg["default_model"],
            "resolved_provider": resolved_provider or "heuristic_fallback",
            "resolved_model": resolved_model or "deterministic-fallback",
            "available_providers": {
                "ollama": {
                    "reachable": ollama_ok,
                    "base_url": self.ollama_base_url,
                    "default_model": cfg["provider_models"]["ollama"],
                    "installed_models": ollama_models,
                },
                "gemini": {
                    "configured": bool(self.get_api_key("gemini")),
                    "default_model": cfg["provider_models"]["gemini"],
                },
                "groq": {
                    "configured": bool(self.get_api_key("groq")),
                    "default_model": cfg["provider_models"]["groq"],
                },
                "openai": {
                    "configured": bool(self.get_api_key("openai")),
                    "default_model": cfg["provider_models"]["openai"],
                },
            },
        }

    def chat(
        self,
        messages: list[dict[str, Any]],
        *,
        tools: list[dict[str, Any]] | None = None,
        provider: str | None = None,
        model: str | None = None,
        temperature: float = 0.1,
        json_mode: bool = False,
        timeout: float = 45.0,
    ) -> LLMResponse:
        """Send a chat completion request (with optional tools) to the resolved LLM backend."""
        resolved_provider, resolved_model = self.resolve_provider(provider, model)

        if resolved_provider == "ollama":
            return self._call_ollama(
                messages=messages,
                model=resolved_model,
                tools=tools,
                temperature=temperature,
                json_mode=json_mode,
                timeout=timeout,
            )
        if resolved_provider in ("openai", "groq"):
            return self._call_openai_compat(
                provider=resolved_provider,
                messages=messages,
                model=resolved_model,
                tools=tools,
                temperature=temperature,
                json_mode=json_mode,
                timeout=timeout,
            )
        if resolved_provider == "gemini":
            return self._call_gemini(
                messages=messages,
                model=resolved_model,
                tools=tools,
                temperature=temperature,
                json_mode=json_mode,
                timeout=timeout,
            )
        raise ProviderError(f"Unsupported resolved provider: {resolved_provider}")

    def _call_ollama(
        self,
        *,
        messages: list[dict[str, Any]],
        model: str,
        tools: list[dict[str, Any]] | None,
        temperature: float,
        json_mode: bool,
        timeout: float,
    ) -> LLMResponse:
        t0 = time.perf_counter()
        ollama_messages = self._convert_messages_for_ollama(messages)
        payload: dict[str, Any] = {
            "model": model,
            "messages": ollama_messages,
            "stream": False,
            "options": {
                "temperature": temperature,
                "num_ctx": 4096,
            },
        }
        if "qwen3" in model.lower():
            payload["think"] = False
        if tools:
            payload["tools"] = tools
        elif json_mode:
            payload["format"] = "json"

        try:
            resp = httpx.post(f"{self.ollama_base_url}/api/chat", json=payload, timeout=timeout)
            resp.raise_for_status()
            data = resp.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise ProviderError(f"Ollama request failed ({model}): {exc}") from exc

        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        msg = data.get("message") or {}
        content = str(msg.get("content") or "")
        normalized_calls: list[dict[str, Any]] = []
        for idx, tc in enumerate(msg.get("tool_calls") or []):
            fn = tc.get("function") or {}
            normalized_calls.append(
                {
                    "id": tc.get("id") or f"call_ollama_{idx}",
                    "name": str(fn.get("name") or ""),
                    "arguments": _parse_tool_arguments(fn.get("arguments")),
                }
            )
        return LLMResponse(
            content=content,
            tool_calls=normalized_calls,
            provider_used="ollama",
            model_used=model,
            raw_message=msg,
            latency_ms=elapsed_ms,
        )

    @staticmethod
    def _convert_messages_for_ollama(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
        converted: list[dict[str, Any]] = []
        for msg in messages:
            role = msg.get("role", "user")
            if role == "tool":
                entry: dict[str, Any] = {
                    "role": "tool",
                    "content": str(msg.get("content") or ""),
                }
                if msg.get("name"):
                    entry["tool_name"] = msg["name"]
                converted.append(entry)
            elif role == "assistant" and msg.get("tool_calls"):
                normalized_ollama_calls = []
                for tc in msg["tool_calls"]:
                    tc_copy = copy.deepcopy(tc)
                    fn = tc_copy.setdefault("function", {})
                    fn["arguments"] = _parse_tool_arguments(fn.get("arguments"))
                    normalized_ollama_calls.append(tc_copy)
                entry = {
                    "role": "assistant",
                    "content": str(msg.get("content") or ""),
                    "tool_calls": normalized_ollama_calls,
                }
                converted.append(entry)
            else:
                converted.append({"role": role, "content": str(msg.get("content") or "")})
        return converted

    @staticmethod
    def _convert_messages_for_openai(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
        converted: list[dict[str, Any]] = []
        for msg in messages:
            role = msg.get("role", "user")
            if role == "assistant" and msg.get("tool_calls"):
                openai_calls = []
                for tc in msg["tool_calls"]:
                    fn = tc.get("function") or {}
                    raw_args = fn.get("arguments", {})
                    if isinstance(raw_args, str):
                        args_str = raw_args
                    else:
                        args_str = json.dumps(raw_args if raw_args is not None else {}, ensure_ascii=False)
                    openai_calls.append(
                        {
                            "id": tc.get("id", ""),
                            "type": tc.get("type", "function"),
                            "function": {
                                "name": str(fn.get("name") or ""),
                                "arguments": args_str,
                            },
                        }
                    )
                converted.append(
                    {
                        "role": "assistant",
                        "content": str(msg.get("content") or ""),
                        "tool_calls": openai_calls,
                    }
                )
            elif role == "tool":
                entry: dict[str, Any] = {
                    "role": "tool",
                    "content": str(msg.get("content") or ""),
                }
                if msg.get("tool_call_id") is not None:
                    entry["tool_call_id"] = str(msg["tool_call_id"])
                if msg.get("name") is not None:
                    entry["name"] = str(msg["name"])
                converted.append(entry)
            else:
                converted.append({"role": role, "content": str(msg.get("content") or "")})
        return converted

    def _call_openai_compat(
        self,
        *,
        provider: str,
        messages: list[dict[str, Any]],
        model: str,
        tools: list[dict[str, Any]] | None,
        temperature: float,
        json_mode: bool,
        timeout: float,
    ) -> LLMResponse:
        t0 = time.perf_counter()
        url = (
            "https://api.openai.com/v1/chat/completions"
            if provider == "openai"
            else "https://api.groq.com/openai/v1/chat/completions"
        )
        api_key = self.get_api_key(provider)
        headers = {
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
        }
        payload: dict[str, Any] = {
            "model": model,
            "messages": self._convert_messages_for_openai(messages),
            "temperature": temperature,
        }
        if tools:
            payload["tools"] = tools
            payload["tool_choice"] = "auto"
        elif json_mode:
            payload["response_format"] = {"type": "json_object"}

        try:
            resp = httpx.post(url, headers=headers, json=payload, timeout=timeout)
            resp.raise_for_status()
            data = resp.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise ProviderError(f"{provider.upper()} request failed ({model}): {exc}") from exc

        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        choices = data.get("choices") or []
        msg = choices[0].get("message", {}) if choices else {}
        content = str(msg.get("content") or "")
        normalized_calls: list[dict[str, Any]] = []
        for idx, tc in enumerate(msg.get("tool_calls") or []):
            fn = tc.get("function") or {}
            normalized_calls.append(
                {
                    "id": tc.get("id") or f"call_{provider}_{idx}",
                    "name": str(fn.get("name") or ""),
                    "arguments": _parse_tool_arguments(fn.get("arguments")),
                }
            )
        return LLMResponse(
            content=content,
            tool_calls=normalized_calls,
            provider_used=provider,
            model_used=model,
            raw_message=msg,
            latency_ms=elapsed_ms,
        )

    @staticmethod
    def _convert_messages_for_gemini(
        messages: list[dict[str, Any]],
    ) -> tuple[list[dict[str, str]], list[dict[str, Any]]]:
        system_parts: list[dict[str, str]] = []
        contents: list[dict[str, Any]] = []
        for msg in messages:
            role = msg.get("role", "user")
            if role == "system":
                system_parts.append({"text": str(msg.get("content") or "")})
            elif role == "user":
                contents.append({"role": "user", "parts": [{"text": str(msg.get("content") or "")}]})
            elif role == "assistant":
                parts: list[dict[str, Any]] = []
                if msg.get("content"):
                    parts.append({"text": str(msg["content"])})
                for tc in msg.get("tool_calls") or []:
                    fn = tc.get("function") or {}
                    parts.append(
                        {
                            "functionCall": {
                                "name": fn.get("name", ""),
                                "args": _parse_tool_arguments(fn.get("arguments")),
                            }
                        }
                    )
                if parts:
                    contents.append({"role": "model", "parts": parts})
            elif role == "tool":
                tool_name = str(msg.get("name") or "tool")
                raw_content = str(msg.get("content") or "{}")
                try:
                    parsed_resp = json.loads(raw_content)
                    if not isinstance(parsed_resp, dict):
                        parsed_resp = {"result": parsed_resp}
                except json.JSONDecodeError:
                    parsed_resp = {"result": raw_content}
                func_resp_part = {
                    "functionResponse": {
                        "name": tool_name,
                        "response": parsed_resp,
                    }
                }
                # Group consecutive tool responses into one user turn as required by Gemini generateContent
                if (
                    contents
                    and contents[-1].get("role") == "user"
                    and contents[-1].get("parts")
                    and "functionResponse" in contents[-1]["parts"][0]
                ):
                    contents[-1]["parts"].append(func_resp_part)
                else:
                    contents.append({"role": "user", "parts": [func_resp_part]})
        return system_parts, contents

    def _call_gemini(
        self,
        *,
        messages: list[dict[str, Any]],
        model: str,
        tools: list[dict[str, Any]] | None,
        temperature: float,
        json_mode: bool,
        timeout: float,
    ) -> LLMResponse:
        t0 = time.perf_counter()
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
        headers = {
            "x-goog-api-key": self.get_api_key("gemini"),
            "Content-Type": "application/json",
        }

        system_parts, contents = self._convert_messages_for_gemini(messages)

        payload: dict[str, Any] = {
            "contents": contents,
            "generationConfig": {"temperature": temperature},
        }
        if system_parts:
            payload["systemInstruction"] = {"parts": system_parts}
        if tools:
            declarations = []
            for item in tools:
                fn = item.get("function") or {}
                params = copy.deepcopy(fn.get("parameters") or {"type": "object", "properties": {}})
                params.pop("additionalProperties", None)
                declarations.append(
                    {
                        "name": fn.get("name"),
                        "description": fn.get("description", ""),
                        "parameters": params,
                    }
                )
            payload["tools"] = [{"functionDeclarations": declarations}]
        elif json_mode:
            payload["generationConfig"]["responseMimeType"] = "application/json"

        try:
            resp = httpx.post(url, headers=headers, json=payload, timeout=timeout)
            resp.raise_for_status()
            data = resp.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise ProviderError(f"Gemini request failed ({model}): {exc}") from exc

        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        candidates = data.get("candidates") or []
        parts = candidates[0].get("content", {}).get("parts", []) if candidates else []
        text_chunks: list[str] = []
        normalized_calls: list[dict[str, Any]] = []
        for idx, part in enumerate(parts):
            if "text" in part:
                text_chunks.append(str(part["text"]))
            if "functionCall" in part:
                fc = part["functionCall"]
                normalized_calls.append(
                    {
                        "id": f"call_gemini_{idx}",
                        "name": str(fc.get("name") or ""),
                        "arguments": _parse_tool_arguments(fc.get("args")),
                    }
                )

        content = "\n".join(text_chunks).strip()
        raw_msg = {
            "role": "assistant",
            "content": content,
            "tool_calls": [
                {
                    "id": tc["id"],
                    "type": "function",
                    "function": {"name": tc["name"], "arguments": tc["arguments"]},
                }
                for tc in normalized_calls
            ],
        }
        return LLMResponse(
            content=content,
            tool_calls=normalized_calls,
            provider_used="gemini",
            model_used=model,
            raw_message=raw_msg,
            latency_ms=elapsed_ms,
        )


def format_assistant_tool_message(resp: LLMResponse) -> dict[str, Any]:
    """Build an OpenAI/Ollama-compatible assistant message containing tool_calls."""
    formatted_calls = []
    for tc in resp.tool_calls:
        formatted_calls.append(
            {
                "id": tc["id"],
                "type": "function",
                "function": {
                    "name": tc["name"],
                    "arguments": tc["arguments"],
                },
            }
        )
    return {
        "role": "assistant",
        "content": resp.content or "",
        "tool_calls": formatted_calls,
    }


def format_tool_result_message(tool_call_id: str, tool_name: str, result: dict[str, Any]) -> dict[str, Any]:
    """Build a provider-compatible tool output message."""
    return {
        "role": "tool",
        "tool_call_id": tool_call_id,
        "name": tool_name,
        "content": json.dumps(result, ensure_ascii=False),
    }


DEFAULT_PROVIDER_MANAGER = ProviderConfigManager()
