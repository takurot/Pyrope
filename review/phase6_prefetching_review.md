# Code Review Request: Advanced Cache & Prefetching Implementation

Please review the implementation of **Phase 6: Advanced Differentiation Features** for the Pyrope Vector Search Server.

## Key Features Implemented

1.  **Delta Indexing (LSM Strategy)**
    - **Goal**: Optimize write throughput while maintaining read performance.
    - **Implementation**: `DeltaVectorIndex.cs` separates data into a mutable `Head` (write-heavy) and immutable `Tail` (read-optimized).
    - **Files**: `src/Pyrope.GarnetServer/Vector/DeltaVectorIndex.cs`

2.  **Semantic Caching (L2: Cluster-Based)**
    - **Goal**: Improve cache hit rates for semantically similar queries.
    - **Implementation**: Uses `SemanticClusterRegistry` to map queries to K-Means centroids. Queries falling into the same cluster share a cache entry.
    - **Files**: `src/Pyrope.GarnetServer/Services/SemanticClusterRegistry.cs`, `src/Pyrope.GarnetServer/Extensions/VectorCommandSet.cs`

3.  **Cost-Aware Caching**
    - **Goal**: Dynamically relax cache strictness for expensive queries.
    - **Implementation**: `CostCalculator.cs` estimates query cost. `VectorCommandSet` relaxes the clustering threshold (`IsClusterCloseEnough`) based on this cost.
    - **Files**: `src/Pyrope.GarnetServer/Services/CostCalculator.cs`

4.  **Predictive Prefetching (Full Stack)**
    - **Goal**: Predict next user actions and pre-populate the cache.
    - **Components**:
        - **AI Sidecar (Python)**: `PredictionEngine` class implements a Markov Chain model to predict `NextClusterId` from `CurrentClusterId` based on session history. Exposed via gRPC (`ReportClusterAccess`, `GetPrefetchRules`).
        - **Garnet Service (C#)**: `PredictivePrefetcher` background service records interactions and fetches rules from Sidecar.
        - **Execution**: In `VectorCommandSet`, predicted clusters trigger a background search using the cluster's centroid, populating the L2 cache for subsequent hits.
    - **Files**:
        - `src/Pyrope.AISidecar/prediction_engine.py` (Logic)
        - `src/Pyrope.AISidecar/server.py` (RPC)
        - `src/Pyrope.GarnetServer/Services/PredictivePrefetcher.cs` (Service)
        - `src/Pyrope.GarnetServer/Extensions/VectorCommandSet.cs` (Integration)

## Focus Areas for Review

- **Concurrency & Safety**: Are the background prefetch tasks in `VectorCommandSet` safe? Are locks in `PredictivePrefetcher` sufficient?
- **Architecture**: Is the coupling between `VectorCommandSet` and `PredictivePrefetcher` appropriate?
- **Error Handling**: Are gRPC failures or Sidecar downtime handled gracefully without impacting the hot path?
- **Correctness**: Does the logic for `QueryKey` matching (specifically using `ClusterId` for prefetch hits) hold up?

Thank you!
