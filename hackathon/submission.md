# Gemini-Driven Garnet Vector DB: Project Submission

## Project Name
**Gemini-Driven Garnet Vector DB** (Pyrope Autonomous)

## Elevator Pitch
Pyrope: A self-driving vector database. Using Gemini as an autonomous brain to real-time optimize cache policies and search parameters, achieving extreme stability without manual tuning.

---

## Inspiration
In the current AI landscape, Large Language Models (LLMs) are primarily viewed as "content generators" or "knowledge retrievers" (RAG). However, we saw a critical gap: as vector databases grow, the **manual tuning** of their internal parameters becomes an impossible bottleneck.

Developers are forced to manually adjust Cache TTL, admission thresholds, and indexing parameters to balance the "Impossible Trinity" of performance:
1. **Latency ($L$)**: Meeting sub-50ms SLAs.
2. **Efficiency ($\eta$)**: Maximizing cache hit rates to save expensive computations.
3. **Resource Consumption ($R$)**: Preventing system saturation.

Our inspiration was to move Gemini from the "Application Layer" to the "Systems Layer," creating a **Self-Driving Database** where the AI doesn't just provide data—it drives the engine.

## What it does
**Gemini-Driven Garnet Vector DB** is an autonomous vector database that uses Google Gemini as its "brain" to optimize its own performance in real-time. 

Instead of relying on rigid, hardcoded rules, the database periodically sends its system telemetry—QPS, Cache Miss Rate, P99 Latency, and CPU/GPU utilization—to a Gemini-powered "AI Sidecar." Gemini analyzes these metrics against high-level goals (e.g., "Keep latency < 50ms while maximizing efficiency") and autonomously reconfigures the database's cache and search policies. It can proactively extend Cache TTL by 30x during search storms or tighten admission gates during CPU spikes to ensure system stability.

## How we built it
We fused the "Muscle" of high-performance systems with the "Brain" of generative AI:
- **Core Engine**: Developed in **C#** based on **Microsoft Garnet**, extended with a custom vector search layer and an internal metrics aggregator.
- **AI Sidecar**: A **Python** service using **gRPC** to communicate with the core engine. It features a "Decision Cache" to ensure the AI doesn't add latency to the database's hot path.
- **The Brain**: **Google Gemini (2.5 Flash Lite)**. We implemented a **Goal-Oriented Prompting** architecture, where Gemini solves a dynamic optimization problem:
  $$\text{Maximize } f(\text{Policy}) = w_1 \cdot \text{HitRate} - w_2 \cdot \text{ResourceCost}$$
  $$\text{Subject to } L_{P99} < 50ms$$
- **Reliability**: A multi-layered fallback system that reverts to a Heuristic Engine if the LLM call exceeds a 50ms deadline or if the API is unavailable.

## Challenges we ran into
1. **The Latency Trap**: Running an LLM in the middle of a database request path is impossible. We solved this by decoupling the decision-making into an asynchronous sidecar with a quantized metrics cache.
2. **Beyond Prescriptive Rules**: Our early prompts were too specific (e.g., "If $X$, do $Y$"). This was just a glorified rule engine. We had to pivot to "Objective-Based Prompting," giving Gemini the **intent** and letting it explore the parameter space.
3. **Structured Reliability**: Ensuring a reasoning model always returns a valid JSON configuration for a low-level C# parser required intense prompt engineering and rigorous schema validation layers.

## Accomplishments that we're proud of
- **Autonomous Strategy Discovery**: We were amazed to see Gemini decide that a **1800-second TTL** was the optimal way to survive a specific high-load pattern—a value we had never hardcoded or even considered.
- **Extreme Efficiency**: By integrating an AI-managed **IVF-Flat Tail Index**, we achieved a **12x improvement in P99 latency** compared to our baseline brute-force implementation.
- **Stability**: The system correctly identified "Search Storms" and automatically tightened admission criteria, protecting the core server from CPU saturation without any human intervention.

## What we learned
- **LLMs as System Controllers**: We learned that LLMs can deeply understand hardware constraints and SLAs. Gemini 2.5 Flash Lite is the "sweet spot" for this, offering incredible reasoning speed at a cost that makes per-minute system tuning viable.
- **Context is King**: Providing Gemini with not just current metrics, but its *previous* decisions, allowed it to "calm down" system oscillations, essentially acting as a sophisticated AI-based PID controller.

## What's next for Gemini-Driven Garnet Vector DB
- **Predictive Prefetching**: Using Gemini to analyze query patterns and "warm up" the cache with predicted results *before* the user even searches.
- **Cross-Region Autonomous DR**: Allowing Gemini to manage disaster recovery and data replication based on global latency and regional cost variations.
- **Self-Healing Indices**: Training Gemini to detect "data drift" and autonomously trigger index rebuilds or compaction to maintain search accuracy over time.
