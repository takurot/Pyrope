from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from math import sqrt
from typing import Mapping, Optional, Sequence
import time


FILTER_TYPE_ENCODING = {
    "none": 0.0,
    "tag": 1.0,
    "numeric": 2.0,
    "hybrid": 3.0,
    "unknown": -1.0,
}


def _now_ms() -> int:
    return int(time.time() * 1000)


def infer_filter_type(tags: Optional[Sequence[str]], numeric_filters: Optional[Mapping[str, float]]) -> str:
    has_tags = bool(tags)
    has_numeric = bool(numeric_filters)
    if has_tags and has_numeric:
        return "hybrid"
    if has_tags:
        return "tag"
    if has_numeric:
        return "numeric"
    return "none"


@dataclass(frozen=True)
class QueryFeatures:
    norm: float
    top_k: float
    filter_type: float


@dataclass(frozen=True)
class SystemFeatures:
    qps: float
    queue_depth: float


@dataclass(frozen=True)
class HistoryFeatures:
    hit_rate: float
    revisit_interval_ms: float


@dataclass
class QueryHistoryEntry:
    hits: int
    total: int
    last_seen_ms: int


class QueryHistory:
    def __init__(self, max_entries: int = 10000) -> None:
        self._entries: "OrderedDict[str, QueryHistoryEntry]" = OrderedDict()
        self._max_entries = max_entries

    def record(self, query_id: str, hit: bool, timestamp_ms: Optional[int] = None) -> None:
        now_ms = _now_ms() if timestamp_ms is None else timestamp_ms
        entry = self._entries.get(query_id)
        if entry is None:
            entry = QueryHistoryEntry(hits=0, total=0, last_seen_ms=now_ms)
            self._entries[query_id] = entry
        entry.total += 1
        if hit:
            entry.hits += 1
        entry.last_seen_ms = now_ms
        self._entries.move_to_end(query_id)
        if len(self._entries) > self._max_entries:
            self._entries.popitem(last=False)

    def features(self, query_id: str, timestamp_ms: Optional[int] = None) -> HistoryFeatures:
        now_ms = _now_ms() if timestamp_ms is None else timestamp_ms
        entry = self._entries.get(query_id)
        if entry is None or entry.total == 0:
            return HistoryFeatures(hit_rate=0.0, revisit_interval_ms=-1.0)
        revisit_interval = max(0.0, float(now_ms - entry.last_seen_ms))
        hit_rate = entry.hits / entry.total
        return HistoryFeatures(hit_rate=hit_rate, revisit_interval_ms=revisit_interval)


class FeatureEngineer:
    def __init__(self, history: Optional[QueryHistory] = None) -> None:
        self._history = history or QueryHistory()

    def extract_query_features(
        self,
        vector: Optional[Sequence[float]],
        top_k: int,
        filter_type: str,
    ) -> QueryFeatures:
        norm = 0.0
        if vector:
            norm = sqrt(sum(value * value for value in vector))
        encoded_filter = FILTER_TYPE_ENCODING.get(filter_type, FILTER_TYPE_ENCODING["unknown"])
        return QueryFeatures(norm=norm, top_k=float(top_k), filter_type=encoded_filter)

    def extract_system_features(self, qps: float, queue_depth: Optional[float] = None) -> SystemFeatures:
        if queue_depth is None:
            queue_depth = 0.0
        return SystemFeatures(qps=float(qps), queue_depth=float(queue_depth))

    def extract_history_features(self, query_id: str, timestamp_ms: Optional[int] = None) -> HistoryFeatures:
        return self._history.features(query_id, timestamp_ms=timestamp_ms)

    def record_query(self, query_id: str, hit: bool, timestamp_ms: Optional[int] = None) -> None:
        self._history.record(query_id, hit, timestamp_ms=timestamp_ms)
