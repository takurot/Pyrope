# タスク実行プロンプト

このリポジトリでは `prompt/PLAN.md` と `prompt/SPEC.md` を参照しながら、AIキャッシュ制御型ベクターDB「Pyrope」を実装します。

---

## プロジェクト構造

```
Pyrope/
├── src/                    # ソースコード
│   ├── Pyrope.GarnetServer/       # C# Garnetサーバー（フロントエンド）
│   └── Pyrope.AISidecar/          # Python AIサイドカー（Warm Path）
├── tests/                  # テストコード
├── scripts/                # ユーティリティスクリプト
│   └── check_quality.sh           # 品質チェック（テスト・lint・format）
├── prompt/                 # AI向けドキュメント
│   ├── PROMPT.md                  # 本ファイル（実装ガイドライン）
│   ├── PLAN.md                    # 実装ロードマップ・進捗管理
│   └── SPEC.md                    # 完全機能仕様書
└── docker-compose.yml      # 開発環境
```

---

## 実装フロー

### 1. 探索フェーズ（Explore）
- **まず関連ファイルを読む**（コードを書く前に）
- `prompt/PLAN.md` で現在の進捗とタスクを確認
- `prompt/SPEC.md` で機能仕様を参照
- 関連する既存コードを読み、パターンを理解

### 2. 計画フェーズ（Plan）
- 複雑なタスクでは**計画を先に立てる**（コードを書かない）
- "think hard" を使って深く考え、設計を練る
- 必要に応じて計画をドキュメント化（GH Issue等）

### 3. ブランチ作成
- `main` から `feature/<トピック>` 形式で作成（例: `feature/garnet-frontend`）
- 既存の作業ブランチがある場合はそれを継続

### 4. TDD（テスト駆動開発）
- **Red**: 失敗するテストを書く
- **Green**: テストを最小限の実装で通す
- **Refactor**: 可読性・再利用性を高める

### 5. ローカル品質チェック (自動化)
```bash
./scripts/check_quality.sh
```

### 6. PLAN.md の更新
- 対応したタスクの進捗を更新し、`Current:` や `Tests:` セクションに要約を追記
- チェックリストの更新: `[ ]` → `[/]` → `[x]`

### 7. コミット & プッシュ
- 粒度はタスク単位で、短く命令形のメッセージを推奨（例: `Implement VEC.SEARCH command draft`）
- 作業ブランチへプッシュし、必要に応じてPRを作成

### 8. Pull Request 作成
```bash
gh pr create --title "<タスク名>" --body "<概要とテスト結果>"
```

### 9. CI結果の確認と対応
```bash
gh pr checks       # PRのCI状況
gh run list        # ワークフロー一覧
gh run view <run-id> --log   # 失敗時の詳細
```

---

## AI Agent Guidelines（AIアシスタント向け）

コードを変更・実装する場合は、**必ず**以下の手順を遵守してください。

### コンテキスト管理
1. **PLAN.md を常に参照**: 進捗・タスク状況を把握
2. **SPEC.md を必要に応じて参照**: 機能仕様の確認
3. **既存コードを読む**: 新規実装前にパターンを理解

### 複雑なタスクの対処
- **チェックリストを使う**: 大きなタスクは分割してMarkdownチェックリストで管理
- **段階的に実装**: 一度にすべてを変更せず、検証しながら進める
- **サブエージェントを活用**: 詳細の調査や検証にはサブエージェントを使う

### 自動化と自己修正
1. **Automation**: コード変更後は必ず `./scripts/check_quality.sh` を実行
2. **Self-Correction**: テスト/lint失敗時は、ユーザーに報告する前に**自己修正を試みる**
3. **Context Update**: `prompt/PLAN.md` を常に最新状態に保つ
4. **Commit Style**: コミットメッセージは英語で、命令形（"Add feature", "Fix bug"）で記述

### コード変更時の注意
- 既存テストを壊さない（必要に応じてモックやフィクスチャを更新）
- `coverage/` などの生成物はコミットしない（CIがアーティファクト化）
- 仕様変更が必要な場合は `prompt/SPEC.md` も合わせて更新提案

---

## よく使うコマンド

### ビルド & テスト
```bash
# 全体品質チェック（推奨）
./scripts/check_quality.sh

# C# テスト
dotnet test Pyrope.sln

# 個別プロジェクトのテスト
dotnet test tests/Pyrope.GarnetServer.Tests/Pyrope.GarnetServer.Tests.csproj

# Python サイドカーテスト
python3 -m unittest discover -s src/Pyrope.AISidecar/tests -p "test_*.py"
```

### Git & GitHub
```bash
# ブランチ作成
git checkout -b feature/<トピック>

# PRの作成
gh pr create --title "<タスク名>" --body "<概要>"

# CI状況確認
gh pr checks
gh run list
gh run view <run-id> --log
```

### Docker
```bash
docker-compose up -d      # 開発環境起動
docker-compose down       # 停止
docker-compose logs -f    # ログ確認
```

---

## トラブルシューティング

### CI失敗時
1. `gh run view <run-id> --log` でログを確認
2. ローカルで `./scripts/check_quality.sh` を実行して再現
3. 原因を特定し、修正してコミット
4. 再度CIを確認

### テスト失敗時
1. 失敗したテストを特定（エラーメッセージを読む）
2. 最小限の修正でテストを通す
3. 関連テストへの影響を確認
4. 品質チェックを再実行

### ビルドエラー時
1. エラーメッセージを確認
2. 依存関係の問題か、コードの問題かを切り分け
3. `dotnet restore` / `pip install -r requirements.txt` を試す
4. 必要に応じて既存コードのパターンを参照

---

## チェックリスト

- [ ] 作業ブランチが `main` から切られている（または既存の feature ブランチを継続）
- [ ] 先にテストを書いた（TDDを意識）
- [ ] `./scripts/check_quality.sh` がパスした
- [ ] `prompt/PLAN.md` を更新した (進捗・Currentセクション)
- [ ] コミットメッセージが簡潔
- [ ] PRを作成した（必要な場合）
- [ ] CIがすべてパスした
