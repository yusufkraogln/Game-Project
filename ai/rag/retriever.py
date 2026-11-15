"""Hybrid BM25 + TF-IDF (+ optional Ollama embeddings) RAG retriever."""

from __future__ import annotations

import argparse
import json
import math
import os
import time
from pathlib import Path
from typing import Any

from dotenv import load_dotenv

try:
    from ai.rag.text_utils import (
        cosine_similarity_dense,
        cosine_similarity_sparse,
        fetch_ollama_embedding,
        l2_normalize_sparse,
        term_frequencies,
        tokenize,
    )
except ImportError:
    from text_utils import (
        cosine_similarity_dense,
        cosine_similarity_sparse,
        fetch_ollama_embedding,
        l2_normalize_sparse,
        term_frequencies,
        tokenize,
    )


class KnowledgeRetriever:
    """Fast local hybrid retriever over `knowledge_index.json`."""

    def __init__(
        self,
        index_path: str | Path | None = None,
        use_ollama_embeddings: bool | None = None,
        ollama_base_url: str | None = None,
        ollama_embed_model: str | None = None,
    ) -> None:
        default_index = Path(__file__).resolve().parent / "index" / "knowledge_index.json"
        self.index_path = Path(index_path) if index_path is not None else default_index
        if not self.index_path.exists():
            raise FileNotFoundError(
                f"Knowledge index not found at {self.index_path}. Run ai/rag/build_index.py first."
            )

        self._index: dict[str, Any] = json.loads(self.index_path.read_text(encoding="utf-8"))
        self.documents: list[dict[str, Any]] = self._index["documents"]
        self.bm25_idf: dict[str, float] = self._index["bm25_idf"]
        self.tfidf_idf: dict[str, float] = self._index["tfidf_idf"]
        self.avgdl: float = float(self._index.get("avgdl", 1.0))

        env_ollama = os.getenv("USE_OLLAMA_EMBEDDINGS", "false").lower() == "true"
        self.use_ollama_embeddings = (
            bool(use_ollama_embeddings) if use_ollama_embeddings is not None else env_ollama
        ) and bool(self._index.get("uses_ollama_embeddings", False))
        self.ollama_base_url = ollama_base_url or os.getenv("OLLAMA_BASE_URL", "http://localhost:11434")
        self.ollama_embed_model = ollama_embed_model or os.getenv(
            "OLLAMA_EMBED_MODEL", "nomic-embed-text"
        )

    def _compute_bm25(self, query_tokens: list[str], doc: dict[str, Any], k1: float = 1.5, b: float = 0.75) -> float:
        doc_tf: dict[str, int] = doc["tf"]
        doc_len = float(doc["doc_len"])
        score = 0.0
        for term in query_tokens:
            freq = doc_tf.get(term, 0)
            if freq <= 0:
                continue
            idf = self.bm25_idf.get(term, 0.0)
            denom = freq + k1 * (1.0 - b + b * (doc_len / max(1.0, self.avgdl)))
            score += idf * (freq * (k1 + 1.0)) / denom
        return score

    def _build_query_tfidf(self, query_tokens: list[str]) -> dict[str, float]:
        q_tf = term_frequencies(query_tokens)
        raw = {
            term: (1.0 + math.log(freq)) * self.tfidf_idf[term]
            for term, freq in q_tf.items()
            if term in self.tfidf_idf
        }
        return l2_normalize_sparse(raw)

    def search(self, query: str, top_k: int = 3) -> list[dict[str, Any]]:
        """Return top-k relevant knowledge chunks with source IDs and relevance scores."""
        if not query or not query.strip():
            return []

        top_k = max(1, min(int(top_k), len(self.documents)))
        query_tokens = tokenize(query)
        if not query_tokens:
            return []

        query_token_set = set(query_tokens)
        query_tfidf = self._build_query_tfidf(query_tokens)

        query_dense: list[float] | None = None
        if self.use_ollama_embeddings:
            query_dense = fetch_ollama_embedding(
                query,
                base_url=self.ollama_base_url,
                model=self.ollama_embed_model,
            )

        bm25_scores = [self._compute_bm25(query_tokens, doc) for doc in self.documents]
        max_bm25 = max(bm25_scores) if bm25_scores else 0.0

        scored: list[tuple[float, dict[str, Any]]] = []
        for doc, bm25_raw in zip(self.documents, bm25_scores):
            bm25_norm = (bm25_raw / max_bm25) if max_bm25 > 0.0 else 0.0
            tfidf_cos = cosine_similarity_sparse(query_tfidf, doc["tfidf"])

            title_tokens = set(doc.get("title_tokens", []))
            title_overlap = (
                len(query_token_set & title_tokens) / max(1, len(query_token_set))
                if title_tokens
                else 0.0
            )

            lexical_score = 0.50 * bm25_norm + 0.40 * tfidf_cos + 0.10 * title_overlap

            if query_dense is not None and "embedding" in doc:
                dense_cos = max(0.0, cosine_similarity_dense(query_dense, doc["embedding"]))
                final_score = 0.55 * lexical_score + 0.45 * dense_cos
            else:
                final_score = lexical_score

            if final_score > 0.0:
                scored.append((final_score, doc))

        scored.sort(key=lambda pair: pair[0], reverse=True)

        return [
            {
                "source_id": doc["id"],
                "score": round(score, 4),
                "title": doc["title"],
                "type": doc["type"],
                "text": doc["text"],
            }
            for score, doc in scored[:top_k]
        ]


def _run_verification_queries(retriever: KnowledgeRetriever, queries: list[str], top_k: int) -> None:
    for idx, q in enumerate(queries, start=1):
        t0 = time.perf_counter()
        results = retriever.search(q, top_k=top_k)
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        print(f"\n[Query {idx}] {q!r} ({elapsed_ms:.2f} ms)")
        for rank, item in enumerate(results, start=1):
            print(
                f"  #{rank} source_id={item['source_id']} | type={item['type']} | "
                f"score={item['score']:.4f} | title={item['title']}"
            )


def main() -> None:
    load_dotenv()
    parser = argparse.ArgumentParser(description="Query Political Mayhem RAG index.")
    parser.add_argument("--query", type=str, default=None, help="Single query to search.")
    parser.add_argument("--top-k", type=int, default=3, help="Number of results to return.")
    parser.add_argument(
        "--use-ollama-embeddings",
        action="store_true",
        default=None,
        help="Use Ollama dense embeddings if available in index.",
    )
    args = parser.parse_args()

    retriever = KnowledgeRetriever(use_ollama_embeddings=args.use_ollama_embeddings)
    if args.query:
        _run_verification_queries(retriever, [args.query], args.top_k)
    else:
        default_queries = [
            "What counters Propagandist units and how are Bodyguards defeated?",
            "Kampanya Gönüllüsü ve Koruma birimlerinin güçlü ve zayıf yönleri nelerdir?",
            "How does Election Night bribery work and what is the maximum vote swing cap?",
            "Parti bütçesi bağış formülü nasıl çalışır ve miting turlarında harcanabilir mi?",
            "What does the Liberal Media Network Overall threshold unlock?",
            "Sabotajcı yakalanınca revolver mekaniği nasıl işler?",
        ]
        _run_verification_queries(retriever, default_queries, args.top_k)


if __name__ == "__main__":
    main()
