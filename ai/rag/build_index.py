"""Build a reproducible hybrid RAG index from Political Mayhem knowledge files."""

from __future__ import annotations

import argparse
import json
import math
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from dotenv import load_dotenv

try:
    from ai.rag.text_utils import (
        fetch_ollama_embedding,
        l2_normalize_sparse,
        term_frequencies,
        tokenize,
    )
except ImportError:
    from text_utils import (
        fetch_ollama_embedding,
        l2_normalize_sparse,
        term_frequencies,
        tokenize,
    )

RULE_HEADER_RE = re.compile(r"^##\s*\[([^\]]+)\]\s*(.*)$")


def _format_unit_chunk(unit: dict[str, Any]) -> dict[str, Any]:
    title = f"{unit['name']} ({unit.get('display_name_tr', unit['name'])})"
    strengths = ", ".join(unit.get("strengths", []))
    weaknesses = ", ".join(unit.get("weaknesses", []))
    cost_note = unit.get("cost", {}).get("note", "")
    text = (
        f"Unit ID: {unit['id']} | Name: {unit['name']} | Turkish Name: {unit.get('display_name_tr', '')}\n"
        f"Deployment Limit Per Round: {unit.get('deployment_limit_per_round')} | "
        f"Stats: HP={unit.get('health')}, Damage={unit.get('damage')}, Armor={unit.get('armor')}, Speed={unit.get('speed')}\n"
        f"Strengths / Counters Against: {strengths}\n"
        f"Weaknesses / Countered By: {weaknesses}\n"
        f"Tactical Role: {unit.get('tactical_role', '')}\n"
        f"Cost Rule: {cost_note}\n"
        f"Description: {unit.get('description', '')}"
    ).strip()
    return {
        "id": unit["id"],
        "type": "unit",
        "title": title,
        "text": text,
    }


def _format_tech_chunk(tech: dict[str, Any]) -> dict[str, Any]:
    title = f"{tech['name']} ({tech.get('display_name_tr', tech['name'])})"
    prereq = ", ".join(f"{k}>={v}" for k, v in tech.get("prerequisite_overall", {}).items())
    text = (
        f"Technology ID: {tech['id']} | Name: {tech['name']} | Turkish Name: {tech.get('display_name_tr', '')}\n"
        f"Ideology: {tech.get('ideology')} | Prerequisite Overall: {prereq}\n"
        f"Unlock Condition: {tech.get('unlock_condition', '')}\n"
        f"Effect: {tech.get('effect', '')}\n"
        f"Donation Bonus: {tech.get('donation_bonus', '')}\n"
        f"Combat or Rally Bonus: {tech.get('combat_or_rally_bonus', '')}\n"
        f"Description: {tech.get('description', '')}"
    ).strip()
    return {
        "id": tech["id"],
        "type": "technology",
        "title": title,
        "text": text,
    }


def _parse_rules_markdown(rules_path: Path) -> list[dict[str, Any]]:
    chunks: list[dict[str, Any]] = []
    current_id: str | None = None
    current_title: str = ""
    current_lines: list[str] = []

    def flush() -> None:
        if current_id is None:
            return
        body = "\n".join(current_lines).strip()
        text = f"{current_title}\n{body}".strip() if current_title else body
        chunks.append(
            {
                "id": current_id,
                "type": "rule",
                "title": current_title or current_id,
                "text": text,
            }
        )

    for line in rules_path.read_text(encoding="utf-8").splitlines():
        match = RULE_HEADER_RE.match(line.strip())
        if match:
            flush()
            current_id = match.group(1).strip()
            current_title = match.group(2).strip() or current_id
            current_lines = []
        elif current_id is not None:
            current_lines.append(line)

    flush()
    return chunks


def load_knowledge_chunks(knowledge_dir: Path) -> list[dict[str, Any]]:
    """Load and normalize unit, technology, and rule chunks from disk."""
    chunks: list[dict[str, Any]] = []

    units_path = knowledge_dir / "units.json"
    if units_path.exists():
        units = json.loads(units_path.read_text(encoding="utf-8"))
        chunks.extend(_format_unit_chunk(u) for u in units)

    techs_path = knowledge_dir / "technologies.json"
    if techs_path.exists():
        techs = json.loads(techs_path.read_text(encoding="utf-8"))
        chunks.extend(_format_tech_chunk(t) for t in techs)

    rules_path = knowledge_dir / "rules.md"
    if rules_path.exists():
        chunks.extend(_parse_rules_markdown(rules_path))

    return chunks


def build_knowledge_index(
    knowledge_dir: Path,
    output_path: Path,
    use_ollama_embeddings: bool = False,
    ollama_base_url: str = "http://localhost:11434",
    ollama_embed_model: str = "nomic-embed-text",
) -> dict[str, Any]:
    """Build hybrid BM25 + TF-IDF (+ optional Ollama dense embeddings) index."""
    chunks = load_knowledge_chunks(knowledge_dir)
    if not chunks:
        raise ValueError(f"No knowledge chunks found in {knowledge_dir}")

    tokenized_docs: list[list[str]] = []
    doc_tf_list: list[dict[str, int]] = []
    df_counts: dict[str, int] = {}

    for chunk in chunks:
        tokens = tokenize(f"{chunk['id']} {chunk['title']} {chunk['text']}")
        tf = term_frequencies(tokens)
        tokenized_docs.append(tokens)
        doc_tf_list.append(tf)
        for term in tf:
            df_counts[term] = df_counts.get(term, 0) + 1

    num_docs = len(chunks)
    avgdl = sum(len(toks) for toks in tokenized_docs) / max(1, num_docs)

    bm25_idf = {
        term: math.log((num_docs - df + 0.5) / (df + 0.5) + 1.0)
        for term, df in df_counts.items()
    }
    tfidf_idf = {
        term: math.log((num_docs + 1.0) / (df + 1.0)) + 1.0
        for term, df in df_counts.items()
    }

    ollama_embeddings_attached = 0
    indexed_docs: list[dict[str, Any]] = []

    for chunk, tokens, tf in zip(chunks, tokenized_docs, doc_tf_list):
        raw_tfidf = {
            term: (1.0 + math.log(freq)) * tfidf_idf[term]
            for term, freq in tf.items()
        }
        norm_tfidf = l2_normalize_sparse(raw_tfidf)
        title_tokens = sorted(set(tokenize(f"{chunk['id']} {chunk['title']}")))

        doc_entry: dict[str, Any] = {
            "id": chunk["id"],
            "type": chunk["type"],
            "title": chunk["title"],
            "text": chunk["text"],
            "doc_len": len(tokens),
            "tf": tf,
            "tfidf": {k: round(v, 6) for k, v in norm_tfidf.items()},
            "title_tokens": title_tokens,
        }

        if use_ollama_embeddings:
            embedding = fetch_ollama_embedding(
                f"{chunk['title']}\n{chunk['text']}",
                base_url=ollama_base_url,
                model=ollama_embed_model,
            )
            if embedding is not None:
                doc_entry["embedding"] = [round(x, 6) for x in embedding]
                ollama_embeddings_attached += 1

        indexed_docs.append(doc_entry)

    index_payload: dict[str, Any] = {
        "schema_version": "1.0",
        "game": "POLITICAL MAYHEM: Battle for the Cities",
        "built_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "chunk_count": num_docs,
        "avgdl": round(avgdl, 4),
        "uses_ollama_embeddings": ollama_embeddings_attached == num_docs,
        "ollama_embed_model": ollama_embed_model if ollama_embeddings_attached == num_docs else None,
        "bm25_idf": {k: round(v, 6) for k, v in sorted(bm25_idf.items())},
        "tfidf_idf": {k: round(v, 6) for k, v in sorted(tfidf_idf.items())},
        "documents": indexed_docs,
    }

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(index_payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return index_payload


def main() -> None:
    load_dotenv()
    rag_dir = Path(__file__).resolve().parent

    parser = argparse.ArgumentParser(description="Build Political Mayhem hybrid RAG index.")
    parser.add_argument(
        "--knowledge-dir",
        type=Path,
        default=rag_dir / "knowledge",
        help="Directory containing units.json, technologies.json, and rules.md.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=rag_dir / "index" / "knowledge_index.json",
        help="Destination path for the serialized knowledge index JSON.",
    )
    parser.add_argument(
        "--use-ollama-embeddings",
        action="store_true",
        default=os.getenv("USE_OLLAMA_EMBEDDINGS", "false").lower() == "true",
        help="Attach local Ollama dense embeddings (nomic-embed-text) if Ollama is running.",
    )
    args = parser.parse_args()

    payload = build_knowledge_index(
        knowledge_dir=args.knowledge_dir,
        output_path=args.output,
        use_ollama_embeddings=args.use_ollama_embeddings,
        ollama_base_url=os.getenv("OLLAMA_BASE_URL", "http://localhost:11434"),
        ollama_embed_model=os.getenv("OLLAMA_EMBED_MODEL", "nomic-embed-text"),
    )
    print(
        f"Built RAG index at {args.output} "
        f"({payload['chunk_count']} chunks, ollama_embeddings={payload['uses_ollama_embeddings']})"
    )


if __name__ == "__main__":
    main()
