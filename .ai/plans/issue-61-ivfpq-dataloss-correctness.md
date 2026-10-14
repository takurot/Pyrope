# Implementation Plan - Issue #61: IvfPqVectorIndex: Multiple Silent Data-Loss and Correctness Defects

## 1. Overview & Problem Statement
Issue #61 identifies 8 critical correctness and data-loss defects in `IvfPqVectorIndex`, `ProductQuantizer`, and `KMeansUtils`:
1. **`Delete()` is a no-op after `Build()`**: Vectors reside in `_invertedLists` after `Build()`. `Delete()` only removes from `_buffer`, returning `false` and leaving the deleted vector queryable forever in `_invertedLists`.
2. **`Rebuild` drops previously indexed vectors**: When `Build()` is called again after initial indexing, `_buffer.Values` is used exclusively. All previously indexed vectors in `_invertedLists` are discarded, resulting in total data loss of historical vectors.
3. **ADC ranking hardcoded to negative L2-squared**: `Search()` computes `score = -distSq` regardless of whether `Metric` is configured as `L2`, `InnerProduct`, or `Cosine`. For `InnerProduct` and `Cosine`, this produces completely inverted or nonsensical rankings, and results from `_buffer` (which use exact cosine/IP scores) cannot be meaningfully ranked against `_invertedLists` results.
4. **`GetStats()` returns hardcoded 0**: `GetStats()` always returns `new IndexStats(0, Dimension, Metric.ToString())`, failing to report actual indexed vectors and bypassing thread synchronization.
5. **Undisposed locks in `IvfPqVectorIndex`**: `_lock` (`ReaderWriterLockSlim`) is never disposed, risking resource leaks when indexes are recreated or disposed.
6. **Missing parameter validation across components**:
   - `IvfPqVectorIndex`: constructor does not validate `dimension > 0`, `m > 0`, `k > 0`, `nList > 0`. `Add`, `Upsert`, `Delete`, `Search` lack null/empty/dimension validation.
   - `ProductQuantizer`: constructor does not check `dim <= 0`, `m <= 0`, `k <= 0`. `Train`, `Encode`, `Decode`, `ComputeDistanceTable` lack null and dimension checks.
   - `KMeansUtils`: `Train` and `FindNearestCentroid` lack null and dimension checks.
7. **Codebook under-allocation when `data.Length < K`**: In `ProductQuantizer.Train()`, when training data has fewer samples than $K$, `_centroids[mIdx]` has length $< K$. Subsequent lookups (`table[m][entry.Code[m]]`) can throw `IndexOutOfRangeException`.
8. **`KMeansUtils.FindNearestCentroid` `float.MinValue` sentinel**: Initializing `bestScore = float.MinValue` fails if all distances/scores evaluate to `float.MinValue` or `float.NegativeInfinity`. Empty centroids are also not guarded.

## 2. Technical Design

### 2.1 `IvfPqVectorIndex` (`src/Pyrope.GarnetServer/Vector/IvfPqVectorIndex.cs`)
- **State Management & Rebuild Support**:
  - Maintain `private readonly Dictionary<string, float[]> _rawVectors = new();` to store canonical raw vectors for all active IDs.
  - In `Add(id, vector)` / `Upsert(id, vector)`:
    - Validate `id` (not null or whitespace) and `vector` (not null, length == `Dimension`, no NaN/Infinity).
    - Under write lock: `_buffer[id] = vector; _rawVectors[id] = vector;`
  - In `Delete(id)`:
    - Validate `id`.
    - Under write lock:
      - Remove from `_buffer` and `_rawVectors`.
      - If `_isBuilt`: iterate `_invertedLists.Values` and call `list.RemoveAll(entry => entry.Id == id)`.
      - Return `true` if removed from `_buffer`, `_rawVectors`, or `_invertedLists`.
  - In `Build()`:
    - Under write lock:
      - If `_rawVectors.Count == 0`:
        - Clear `_invertedLists`, `_centroids`, `_centroidNorms`, `_buffer`.
        - Set `_isBuilt = false;` return.
      - Snapshot entries as a stable list: `var items = _rawVectors.ToList();` (guaranteeing exact 1-to-1 ordering between keys, vectors, residuals, and cluster assignments).
      - Extract all vectors: `var allVectors = items.Select(x => x.Value).ToList();`.
      - Determine coarse cluster count: `int nClusters = Math.Min(NList, allVectors.Count);`.
      - Train coarse centroids using `KMeansUtils.Train(allVectors, nClusters, Dimension, Metric, seed: 123)`.
      - Compute `_centroidNorms`.
      - Compute residuals: `residuals[i] = items[i].Value - centroid`.
      - Train PQ: `_pq.Train(residuals)`.
      - Encode all vectors from `items` into `_invertedLists`:
        - `PqEntry(items[i].Key, _pq.Encode(residuals[i]), VectorMath.ComputeNorm(items[i].Value))`.
      - Clear `_buffer`.
      - Set `_isBuilt = true`.
  - In `GetStats()`:
    - Under read lock: return `new IndexStats(_rawVectors.Count, Dimension, Metric.ToString())`.
  - In `Search(query, topK, options)`:
    - Validate `query` (not null, length == `Dimension`, no NaN/Infinity), `topK > 0`.
    - Under read lock:
      - 1. Search `_buffer`: `float score = ComputeScore(query, kvp.Value, queryNorm)`.
      - 2. If `_isBuilt`:
        - Rank centroids against query using `ComputeScore(query, _centroids[i], queryNorm, _centroidNorms[i])`.
        - Optimization (Hoisting): If `Metric == VectorMetric.InnerProduct || Metric == VectorMetric.Cosine`, precompute `var ipTable = _pq.ComputeInnerProductTable(query);` **once outside the probes loop** (as it depends only on query $q$ and PQ codebook).
        - For top `nProbe` centroids:
          - If `Metric == VectorMetric.L2`:
            - `resQuery = query - centroid`
            - `table = _pq.ComputeDistanceTable(resQuery)`
            - For entry in cluster: `distSq = sum(table[m][entry.Code[m]])`, `score = -distSq`
          - If `Metric == VectorMetric.InnerProduct`:
            - Optimization (Hoisting): Precompute `float coarseDot = VectorMath.DotProduct(query, centroid);` **once per cluster outside the entries loop**.
            - For entry in cluster: `dotRes = sum(ipTable[m][entry.Code[m]])`, `score = coarseDot + dotRes`
          - If `Metric == VectorMetric.Cosine`:
            - Optimization (Hoisting): Precompute `float coarseDot = VectorMath.DotProduct(query, centroid);` **once per cluster outside the entries loop**.
            - For entry in cluster:
              - `dotRes = sum(ipTable[m][entry.Code[m]])`
              - `float dot = coarseDot + dotRes`
              - Zero division guard matching `VectorMath.Cosine`: `if (queryNorm < 1e-6f || entry.Norm < 1e-6f) score = 0f; else score = Math.Clamp(dot / (queryNorm * entry.Norm), -1.0f, 1.0f);`
      - PriorityQueue dequeue and sort descending.
  - Implement `IDisposable`:
    - Dispose `_lock`.
    - Check `_disposed` in methods.

### 2.2 `ProductQuantizer` (`src/Pyrope.GarnetServer/Vector/ProductQuantizer.cs`)
- Constructor validation:
  - `dim <= 0`: throw `ArgumentOutOfRangeException`
  - `m <= 0`: throw `ArgumentOutOfRangeException`
  - `k <= 0` or `k > 256`: throw `ArgumentOutOfRangeException`
  - `dim % m != 0`: throw `ArgumentException`
- In `Train(float[][] data)`:
  - Validate `data != null`. If `data.Length == 0`, throw `ArgumentException("Training data cannot be empty")` or return if pre-validated.
  - Validate each element of `data` has length `Dim`.
  - In parallel subspace training:
    - Call `KMeansUtils.Train(subData, K, SubDim, VectorMetric.L2, maxIter: 10, seed: 42 + mIdx)`.
    - Ensure exactly `K` centroids in `_centroids[mIdx]`:
      - If `trained.Count < K`: pad `_centroids[mIdx]` to length `K` by cycling existing trained centroids or zero-padding.
  - Set `_isTrained = true;`
- In `ComputeDistanceTable(float[] query)`:
  - Validate `_isTrained`.
  - Validate `query != null` and `query.Length == Dim`.
- Add `ComputeInnerProductTable(float[] query)`:
  - Validate `_isTrained`.
  - Validate `query != null` and `query.Length == Dim`.
  - For each subspace `mIdx`:
    - `table[mIdx][k] = VectorMath.DotProduct(subQuery, centroids[k])`.
- In `Encode(float[] vector)` and `Decode(byte[] code)`:
  - Add null and length checks.

### 2.3 `KMeansUtils` (`src/Pyrope.GarnetServer/Vector/KMeansUtils.cs`)
- In `Train(data, k, dimension, metric, maxIter, seed)`:
  - Validate `data != null`, `k > 0`, `dimension > 0`.
  - If `data.Count == 0`, return empty list.
  - In `FindNearestCentroid`:
    - Validate `vec != null`, `centroids != null`, `centroids.Count > 0`.
    - Initialize `float bestScore = float.NegativeInfinity;`.
    - Return nearest centroid index.

## 3. Testing Strategy (TDD)
Write unit tests in `tests/Pyrope.GarnetServer.Tests/Vector/IvfPqVectorIndexTests.cs` and new `tests/Pyrope.GarnetServer.Tests/Vector/KMeansUtilsTests.cs`:
1. **Red Test 1: Post-build deletion**: Add vectors, call `Build()`, call `Delete()`, verify `Delete()` returns `true`, and subsequent `Search()` does not return the deleted ID.
2. **Red Test 2: Rebuild preserves vectors**: Add vectors A & B, `Build()`, add vector C, `Build()`, verify `Search()` can retrieve vectors A, B, and C.
3. **Red Test 3: Metric correctness in ADC ranking**:
   - Test `VectorMetric.InnerProduct`: verify highest inner product vector ranks first and score is positive dot product.
   - Test `VectorMetric.Cosine`: verify highest cosine similarity vector ranks first, score is in $[-1, 1]$ (within `Math.Clamp`), and zero-norm inputs are handled safely.
   - Test buffer and index coexistence: buffer exact score and inverted list ADC score are on identical scales.
4. **Red Test 4: `GetStats()` accuracy**: Verify `GetStats().Count` tracks counts accurately across `Add`, `Build`, `Delete`, and `Rebuild`.
5. **Red Test 5: Subspace codebook under-allocation**: `ProductQuantizer.Train` with `data.Length < K` produces codebooks of length `K` without throwing `IndexOutOfRangeException`.
6. **Red Test 6: Input validation & `IDisposable`**: Verify constructor and method validations throw expected argument exceptions, and index disposal disposes lock.
7. **Red Test 7: `KMeansUtilsTests`**:
   - `Train` throws on null data, invalid dimension, non-positive $k$.
   - `FindNearestCentroid` throws on null or empty centroids list.
   - Sentinel robustness: correctly finds nearest centroid even when distances/scores are large negative values or evaluate to `float.NegativeInfinity`.

## 4. Implementation Steps
1. Create branch `feature/issue-61-ivfpq-dataloss-correctness`.
2. Write plan and submit to Stage 2 Plan Review subagent.
3. Once approved, write failing tests (Stage 3 Red).
4. Implement fixes in `KMeansUtils.cs`, `ProductQuantizer.cs`, `IvfPqVectorIndex.cs` (Stage 3 Green).
5. Run `dotnet format` and `./scripts/check_quality.sh` (Stage 4).
6. Invoke Stage 5 Code Review subagent.
7. Create PR, watch CI, squash merge to `main`, update `prompt/PLAN.md` (Stage 6).
