# Code Review Request: P6-13 Gemini-based Cache Control

## Overview
Please review the implementation of **P6-13: Gemini-based Cache Control Integration**.
This PR introduces the `LLMPolicyEngine`, which uses Google Gemini to autonomously optimize cache policies (TTL, Admission, Eviction) based on real-time system metrics.

## Key Focus Areas

### 1. Autonomous Control Logic (`llm_policy_engine.py`)
- **Goal-Oriented Prompting**: Evaluates whether the shift from prescriptive rules to abstract goals (Stability, Efficiency, Resource Management) is effective and robust.
- **Decision Caching**: Verify the quantization and caching logic of metrics to prevent redundant API calls.
- **Structured Output**: Check the reliability of JSON parsing and the "reasoning" field extraction from Gemini.

### 2. Integration & Reliability (`server.py`, `llm_worker.py`)
- **Fallback Mechanism**: Review the robustness of the fallback to `HeuristicPolicyEngine` on LLM timeout (50ms) or API failures.
- **Feature Flagging**: Ensure the `LLM_POLICY_ENABLED` flag is correctly respected.
- **Async Metrics Flow**: Verify how system metrics are aggregated and passed to the LLM engine without blocking the gRPC server.

### 3. Model Configuration
- **Gemini 2.5 Flash Lite**: Verify if this remains the most optimal choice compared to 1.5 Pro or 3.0 Preview for this high-frequency, low-latency control loop.

## Verification Performed
- Unit tests for `LLMPolicyEngine` (prompt generation, parsing, fallback).
- E2E Benchmarks showing Gemini autonomously scaling TTL from 300s to 1800s under load.
- Simulated API failures to verify graceful fallback.

## Documents to Reference
- Implementation Details: [llm_policy_engine.py](file:///Volumes/Storage/src/Pyrope/src/Pyrope.AISidecar/llm_policy_engine.py)
- Benchmark Results: [20260116_p6_13_gemini_cache.md](file:///Volumes/Storage/src/Pyrope/docs/benchmarks/20260116_p6_13_gemini_cache.md)

---
**Instruction for Receiver**: Please provide a critical review focusing on system stability, API cost efficiency, and the "autonomy" of the AI decisions.
