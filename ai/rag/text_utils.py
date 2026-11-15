"""Shared text normalization, bilingual tokenization, and similarity helpers for RAG."""

from __future__ import annotations

import math
import re
import unicodedata
from typing import Iterable

import httpx

TURKISH_CHAR_MAP = str.maketrans(
    {
        "İ": "i",
        "I": "i",
        "ı": "i",
        "Ş": "s",
        "ş": "s",
        "Ğ": "g",
        "ğ": "g",
        "Ü": "u",
        "ü": "u",
        "Ö": "o",
        "ö": "o",
        "Ç": "c",
        "ç": "c",
    }
)

STOPWORDS = frozenset(
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "by",
        "can",
        "do",
        "does",
        "for",
        "from",
        "how",
        "in",
        "into",
        "is",
        "it",
        "its",
        "of",
        "on",
        "or",
        "that",
        "the",
        "their",
        "this",
        "to",
        "what",
        "when",
        "where",
        "which",
        "who",
        "why",
        "will",
        "with",
        "bir",
        "bu",
        "cok",
        "da",
        "daha",
        "de",
        "en",
        "gibi",
        "hangisi",
        "her",
        "icin",
        "ile",
        "ise",
        "kadar",
        "ki",
        "mi",
        "mu",
        "nasil",
        "ne",
        "nedir",
        "neye",
        "olan",
        "olarak",
        "su",
        "ve",
        "veya",
        "ya",
    }
)

TOKEN_RE = re.compile(r"[a-z0-9_]+")


def normalize_text(text: str) -> str:
    """Fold Turkish diacritics and lowercase text deterministically."""
    if not text:
        return ""
    mapped = text.translate(TURKISH_CHAR_MAP)
    decomposed = unicodedata.normalize("NFKD", mapped)
    ascii_folded = "".join(ch for ch in decomposed if not unicodedata.combining(ch))
    return ascii_folded.lower()


def _stem_english_plural(token: str) -> str | None:
    if len(token) >= 5 and token.endswith("ies"):
        return token[:-3] + "y"
    if len(token) >= 5 and token.endswith("s") and not token.endswith(("ss", "us", "is")):
        return token[:-1]
    return None


def tokenize(text: str) -> list[str]:
    """Tokenize English/Turkish text with snake_case splitting and prefix stems."""
    normalized = normalize_text(text)
    raw_tokens = TOKEN_RE.findall(normalized)
    tokens: list[str] = []

    for tok in raw_tokens:
        if len(tok) < 2 or tok in STOPWORDS:
            continue

        tokens.append(tok)

        # Split snake_case identifiers while preserving the full identifier token
        parts = [p for p in tok.split("_") if len(p) >= 2 and p not in STOPWORDS] if "_" in tok else [tok]
        if "_" in tok:
            tokens.extend(parts)

        for part in parts:
            plural_stem = _stem_english_plural(part)
            if plural_stem and len(plural_stem) >= 2 and plural_stem not in STOPWORDS:
                tokens.append(plural_stem)
                if len(plural_stem) >= 5:
                    tokens.append(f"s5:{plural_stem[:5]}")
            if len(part) >= 5:
                tokens.append(f"s5:{part[:5]}")
            if len(part) >= 7:
                tokens.append(f"s7:{part[:7]}")

    return tokens


def term_frequencies(tokens: Iterable[str]) -> dict[str, int]:
    """Count token frequencies into a dictionary."""
    counts: dict[str, int] = {}
    for tok in tokens:
        counts[tok] = counts.get(tok, 0) + 1
    return counts


def l2_normalize_sparse(vec: dict[str, float]) -> dict[str, float]:
    """L2-normalize a sparse dictionary vector."""
    norm = math.sqrt(sum(v * v for v in vec.values()))
    if norm <= 0.0:
        return {}
    return {k: v / norm for k, v in vec.items()}


def cosine_similarity_sparse(vec_a: dict[str, float], vec_b: dict[str, float]) -> float:
    """Compute cosine similarity between two L2-normalized sparse vectors."""
    if not vec_a or not vec_b:
        return 0.0
    if len(vec_a) > len(vec_b):
        vec_a, vec_b = vec_b, vec_a
    return sum(val * vec_b.get(key, 0.0) for key, val in vec_a.items())


def cosine_similarity_dense(vec_a: list[float], vec_b: list[float]) -> float:
    """Compute cosine similarity between two dense float vectors."""
    if not vec_a or not vec_b or len(vec_a) != len(vec_b):
        return 0.0
    dot = sum(a * b for a, b in zip(vec_a, vec_b))
    norm_a = math.sqrt(sum(a * a for a in vec_a))
    norm_b = math.sqrt(sum(b * b for b in vec_b))
    if norm_a <= 0.0 or norm_b <= 0.0:
        return 0.0
    return dot / (norm_a * norm_b)


def fetch_ollama_embedding(
    text: str,
    base_url: str = "http://localhost:11434",
    model: str = "nomic-embed-text",
    timeout: float = 5.0,
) -> list[float] | None:
    """Request a dense embedding vector from a local Ollama server, returning None on failure."""
    base = base_url.rstrip("/")
    try:
        resp = httpx.post(
            f"{base}/api/embed",
            json={"model": model, "input": text},
            timeout=timeout,
        )
        if resp.status_code == 200:
            data = resp.json()
            embeddings = data.get("embeddings")
            if isinstance(embeddings, list) and embeddings and isinstance(embeddings[0], list):
                return [float(x) for x in embeddings[0]]
        # Legacy Ollama fallback endpoint
        resp_legacy = httpx.post(
            f"{base}/api/embeddings",
            json={"model": model, "prompt": text},
            timeout=timeout,
        )
        if resp_legacy.status_code == 200:
            emb = resp_legacy.json().get("embedding")
            if isinstance(emb, list) and emb:
                return [float(x) for x in emb]
    except (httpx.HTTPError, ValueError, TypeError):
        return None
    return None
