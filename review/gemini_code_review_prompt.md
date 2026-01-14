# Code Review Request: Phase 6 - Advanced Differentiation Features

あなたは経験豊富なソフトウェアアーキテクトです。以下のコードをレビューし、改善点を提案してください。

## レビュー対象

**Phase 6: Advanced Differentiation Features** の実装

Pyrope Vector Search Serverに以下の高度な機能を追加しました：

1. **Delta Indexing (LSM Strategy)** - 書き込み最適化
2. **Semantic Caching (L2: Cluster-Based)** - セマンティックキャッシュ
3. **Cost-Aware Caching** - コストベースのキャッシュ緩和
4. **Predictive Prefetching** - Markov Chainベースの予測プリフェッチ

---

## 対象ファイル

### C# (Garnet Server)

**Core Implementation:**
- `src/Pyrope.GarnetServer/Vector/DeltaVectorIndex.cs` - Delta Indexing (LSM)
- `src/Pyrope.GarnetServer/Vector/CostCalculator.cs` - クエリコスト計算
- `src/Pyrope.GarnetServer/Services/SemanticClusterRegistry.cs` - セマンティッククラスタ管理
- `src/Pyrope.GarnetServer/Services/PredictivePrefetcher.cs` - 予測プリフェッチサービス
- `src/Pyrope.GarnetServer/Services/IPredictivePrefetcher.cs` - インターフェース定義

**Integration:**
- `src/Pyrope.GarnetServer/Extensions/VectorCommandSet.cs` - VEC.SEARCH統合
- `src/Pyrope.GarnetServer/Controllers/IndexController.cs` - Centroid API追加
- `src/Pyrope.GarnetServer/Model/ResultCache.cs` - キャッシュ拡張
- `src/Pyrope.GarnetServer/Model/QueryKey.cs` - クエリキー拡張
- `src/Pyrope.GarnetServer/Program.cs` - DI登録

**Tests:**
- `tests/Pyrope.GarnetServer.Tests/Vector/DeltaVectorIndexTests.cs`
- `tests/Pyrope.GarnetServer.Tests/Vector/CostCalculatorTests.cs`
- `tests/Pyrope.GarnetServer.Tests/Services/PredictivePrefetcherTests.cs`
- `tests/Pyrope.GarnetServer.Tests/Extensions/PrefetchExecutionTests.cs`
- `tests/Pyrope.GarnetServer.Tests/Extensions/CacheHintForceTests.cs`

### Python (AI Sidecar)

- `src/Pyrope.AISidecar/prediction_engine.py` - Markov Chain予測エンジン
- `src/Pyrope.AISidecar/semantic_model.py` - KMeansクラスタリング
- `src/Pyrope.AISidecar/server.py` - gRPCサービス拡張
- `src/Pyrope.AISidecar/tests/test_prediction_engine.py` - ユニットテスト

### Proto

- `src/Protos/policy_service.proto` - gRPCスキーマ拡張

---

## 機能詳細

### 1. Delta Indexing (LSM Strategy)

**目的**: 書き込みスループットの最適化

**実装**:
- `Head` (mutable, 新規データ) と `Tail` (immutable, 圧縮済み) の2層構造
- バックグラウンドでのCompaction処理
- 読み取り時は両層をマージ

**懸念事項**:
- Compaction中のロック戦略
- メモリ使用量の制御

### 2. Semantic Caching (L2)

**目的**: 類似クエリのキャッシュヒット率向上

**実装**:
- KMeansで学習したCentroidsをGarnet Serverに登録
- クエリベクトルを最近傍Centroidにマッピング
- 同一クラスタのクエリはキャッシュ共有

**懸念事項**:
- Centroid更新時のキャッシュ無効化
- クラスタ境界付近のクエリ精度

### 3. Cost-Aware Caching

**目的**: 高コストクエリのキャッシュ優先度向上

**実装**:
- `CostCalculator`でクエリコストを推定
- 高コストクエリはクラスタ許容距離を緩和

### 4. Predictive Prefetching

**目的**: ユーザー行動予測による先読みキャッシュ

**実装**:
- AI SidecarがMarkov Chainで遷移確率を学習
- `PredictivePrefetcher`が定期的にルールを取得
- `VectorCommandSet`がプリフェッチをバックグラウンド実行

**懸念事項**:
- ホットパスへの影響最小化
- gRPC障害時のフォールバック

---

## レビュー観点

### 1. 設計・アーキテクチャ

- [ ] `VectorCommandSet`の責務が膨らみすぎていないか
- [ ] `PredictivePrefetcher`とSidecar間の結合度は適切か
- [ ] DIコンテナの登録は正しいか

### 2. パフォーマンス

- [ ] ホットパス (`VEC.SEARCH`) での不要な処理はないか
- [ ] `Task.Run`によるプリフェッチがスレッドプールを圧迫しないか
- [ ] ロック競合: `DeltaVectorIndex`のRead/Write Lock

### 3. 信頼性・エラーハンドリング

- [ ] gRPC通信失敗時のフォールバック
- [ ] Centroid未登録時の動作
- [ ] Compaction失敗時のリカバリ

### 4. 並行性・スレッドセーフティ

- [ ] `ConcurrentDictionary`の使用箇所
- [ ] `ReaderWriterLockSlim`の適切な使用
- [ ] async/awaitのデッドロック回避

### 5. テストカバレッジ

- [ ] エッジケース (空のインデックス、単一要素等)
- [ ] 並行アクセスのテスト
- [ ] 障害シナリオのテスト

---

## 出力形式

以下の形式でフィードバックを提供してください：

```markdown
## サマリー

[全体的な評価と主要な所見]

## 重要な問題 (Critical)

1. **[問題タイトル]**
   - 場所: `ファイル名:行番号`
   - 説明: [問題の詳細]
   - 推奨: [修正方法]

## 改善提案 (Improvement)

1. **[提案タイトル]**
   - 場所: `ファイル名:行番号`
   - 説明: [現状と改善案]
   - コード例: (任意)

## 良い点 (Positive)

- [良い実装パターンや設計の評価]

## 質問・確認事項

- [レビュアーからの質問]
```

---

## 追加コンテキスト

- **PR**: #34 (feat/phase6-semantic-caching)
- **ベンチマーク結果**: `docs/benchmarks/20260111_phase6_delta_indexing.md`
- **Phase 6計画**: `prompt/PLAN.md` の P6-1 〜 P6-4 セクション

### 設計判断

1. **Markov Chain採用理由**: LSTMより軽量でリアルタイム学習が可能。スタンドアロン環境でも動作。
2. **gRPC採用理由**: 既存のpolicy_service.protoを拡張。mTLS対応済み。
3. **バックグラウンドプリフェッチ**: ホットパスへの影響を0にするため`Task.Run`で分離。

### 既知の制約

- Centroidsは外部から明示的にPUSHする必要がある (自動学習は未実装)
- PredictionEngineのルール閾値 (3回以上の遷移) はハードコード
