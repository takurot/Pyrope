# タスク実行プロンプト

このリポジトリでは `prompt/PLAN.md` と `prompt/SPEC.md` を参照しながら、AIキャッシュ制御型ベクターDB「Pyrope」を実装します。

## 実装フロー

### 1. ブランチ作成
- `main` から `feature/<トピック>` 形式で作成（例: `feature/garnet-frontend`）。
- 既存の作業ブランチがある場合はそれを継続して使う。

### 2. TDD（テスト駆動開発）
- **Red**: 失敗するテストを書く
- **Green**: テストを最小限の実装で通す
- **Refactor**: 可読性・再利用性を高める

### 3. ローカル品質チェック (自動化)
以下のスクリプトを実行し、全テストがパスすることを確認してください。
```bash
./scripts/check_quality.sh
```

### 4. PLAN.md の更新
- 対応したタスクの進捗を更新し、`Current:` や `Tests:` セクションに要約を追記。
- チェックリストの更新（[ ] -> [/] -> [x]）。

### 5. コミット & プッシュ
- 粒度はタスク単位で、短く命令形のメッセージを推奨（例: `Implement VEC.SEARCH command draft`）。
- 作業ブランチへプッシュし、必要に応じてPRを作成。

### 6. Pull Request 作成
```bash
gh pr create --title "<タスク名>" --body "<概要とテスト結果>"
```
- PRテンプレートがある場合は従う。関連Issue/PRをリンク。

### 7. CI結果の確認と対応
```bash
gh pr checks   # PRのCI状況
gh run list    # ワークフロー一覧
gh run view <run-id> --log   # 失敗時の詳細
```
- CIが赤の場合は原因を特定し、修正して再実行する。

## AI Agent Guidelines (AIアシスタント向け)
あなたがコードを変更・実装する場合は、**必ず**以下の手順を遵守してください。

1. **Automation**: コード変更後は必ず `./scripts/check_quality.sh` を実行して回帰テストを行うこと。
2. **Context**: `prompt/PLAN.md` を常に最新状態に保つこと。完了したタスクは `[x]` に更新する。
3. **Commit**: コミットメッセージは英語で、命令形("Add feature", "Fix bug")で記述すること。
4. **Self-Correction**: スクリプトが失敗した場合は、ユーザーに報告する前に自己修正を試みること。

## チェックリスト

- [ ] 作業ブランチが `main` から切られている（または既存の feature ブランチを継続）
- [ ] 先にテストを書いた（TDDを意識）
- [ ] `./scripts/check_quality.sh` がパスした
- [ ] `prompt/PLAN.md` を更新した (進捗・Currentセクション)
- [ ] コミットメッセージが簡潔
- [ ] PRを作成した（必要な場合）
- [ ] CIがすべてパスした

## 注意事項

- 既存テストを壊さないこと。必要に応じてモックやフィクスチャを更新する。
- `coverage/` などの生成物はコミットしない（CIがアーティファクト化）。
- macOS/Linuxを前提に手順を書く。
- 仕様変更が必要な場合は `prompt/SPEC.md` も合わせて更新提案すること。
