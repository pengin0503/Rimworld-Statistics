# 追加機能（分野1〜8）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 統計ウィンドウの表示改善、お気に入り、コロニー群の集計と比較、取得状況の一覧、歴代ランキングTOP10、CSV出力、プロフィールのバックアップ・復元、他MOD統計の拡張を、既存の統計IDと保存形式を変えずに段階的に追加する。

**Architecture:** 判定・計算・ファイル形式は `RimworldStatistics.Core` の純粋な関数として先にテストし、描画とゲームAPIは `RimworldStatistics`（ゲーム側）に置く。共通基盤（F1 集計範囲、F2 参照、F3 順位表、F4 表示部品、F5 分類、F6 表示設定、F7 出力）を小さく作り、各機能はその組合せにする。他MOD統計は本体の取得単位・ゲート・遅延パッチ・保存時点の読み取りに統合する。

**Tech Stack:** RimWorld 1.7（監査済み索引 1.7.4985 rev1269）、C# / .NET 10、Harmony、xUnit v3（Microsoft.Testing.Platform）、GitHub Actions（`numeric-verification.yml`）。

**Spec:** [追加機能の設計](../specs/2026-11-12-feature-expansion-design.md)、[別紙：他MOD統計](../specs/2026-11-12-feature-expansion-mod-statistics.md)。実装者は両方を読む。

## Global Constraints

設計 §2 の要約。すべてのタスクに暗黙に含まれる。

- 統計ID・`StoredKey`・改訂番号・`SaveReader.Version`（17）・`Profile.CurrentSchemaVersion`（2）・チェックポイント形式（1）を変えない。既存テストの期待値を書き換えない。
- `Unknown` を0にしない。`Partial`／`Experimental`／`UnknownLegacy` を昇格させない。CSVの欠測は空欄。
- 単一セーブの最新状態だけを示す表示を追加しない。過去の出来事を復元しない。
- `profile.json` にお気に入り・グループを保存しない。`profile.json` の既存データを削除しない。
- 公開SDK `RimworldStatistics.Api` を変えない。翻訳キーは日英同時（`bash tools/check_keys.sh`）。`GameFont` を変えない。
- 実験的取得は既定OFF。OFF・対象MODなし・旧アドオン併用でパッチ0件。毎tickの処理を足さない。
- 範囲外のコロニーをコロニー一覧から取り除かない（観測の帰属を変えないため）。
- R2 は読み取りのみ。リリース・Workshop 更新はしない。
- クラス名・メソッド名は推奨（仮称）。より良い形にしてよいが、Interfaces に書いた**意味**（入力・出力・不変条件）は保ち、変えたら PR 本文に書く。

## Review Focus

どのタスクのテストにも自然には現れないが、利用者が実際に踏みやすい入力。各行のテストは担当タスクに追加済み。

1. **空のプロフィール（初回起動、コロニー0）**：ランキング・取得状況・お気に入り・CSV・バックアップが「データなし」を表示し、例外を出さない（A4.1、A5.1、A6.1、A7.2、A2.1 のテスト）。
2. **システムのカルチャが小数点にカンマを使う環境（例：de-DE）**：CSV の数値が `1234.5` のまま出る（A7.1 のテスト）。
3. **翻訳のない SDK 提供元の統計（`LabelKey == Key`）や削除済みのキー**：お気に入り・ランキング・CSV で表示名がキーにフォールバックし、例外を出さない（A4.1、A7.2 のテスト）。
4. **手動統合（manual merge）で別名キーになったコロニー**：グループと除外が別名でも一致する（A3.2 のテスト）。
5. **非常に長い・全角のコロニー名や、`=` で始まる名前**：表で省略＋ツールチップ、CSVで引用と数式対策（A7.1 のテスト。表示は A1 の手動確認）。

## 作業パッケージと順序

1パッケージ＝1 Issue＝原則1 PR。Track A（画面・データ）と Track M（他MOD）は独立に進めてよい。親 Issue は #154。

| 順 | パッケージ | Issue | 分野 | 依存 | 規模の目安 |
|---|---|---|---|---|---|
| A1 | 表示基盤と表示改善 | #155 | 1 | なし | 中（2 PR に分けてもよい） |
| A2 | プロフィールのバックアップ・復元 | #156 | 7 | A1（表部品は任意） | 中 |
| A3 | 集計範囲とコロニーグループ | #157 | 3（段階1） | なし（A1 の後が望ましい） | 中 |
| A4 | お気に入り・カスタム概要 | #158 | 2 | A3（F6・範囲） | 小〜中 |
| A5 | 歴代ランキングTOP10 | #159 | 5 | A1、A3、A4.1 | 中 |
| A6 | 取得状況の一覧 | #160 | 4 | A1、A3、A4.1 | 中 |
| A7 | CSV出力 | #161 | 6 | A5、A6 | 中 |
| A8 | コロニー・グループの比較表示 | #162 | 3（段階2） | A3、A4.1 | 小〜中 |
| M1 | Vanilla Psycasts Expanded | #163 | 8 | なし | 小〜中 |
| M2 | VEF Outposts（VOE ほか） | #164 | 8 | なし | 中 |
| M3 | Hospitality | #165 | 8 | なし | 小〜中 |
| M4 | Dubs Bad Hygiene | #166 | 8 | なし | 小〜中 |
| M5 | Vehicle Framework | #167 | 8 | 所有者による 1.7 DLL の用意 | 小（保存時点のみ） |
| M6 | Smart Apparel Upgrade | #168 | 8 | なし | 小 |
| M7 | その他の候補（調査のみ） | #169 | 8 | なし | 調査 |

## 準備（各パッケージの最初に1回）

- [ ] **最新の `main` からブランチを作る**（例：`feat/<パッケージ>-<短い説明>`）。
- [ ] **参照DLLを取得する**（R2 の環境変数がある場合）

```bash
python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install -q boto3
```

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py refs .refs
```

R2 がない環境では、以降のビルドに `-p:UseNuGetRefs=true` を付ける（CI と同じ）。

- [ ] **基準の確認**：次がすべて成功することを確かめる。失敗したら変更せずに報告する。

```bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
```

```bash
dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release --nologo -p:UseNuGetRefs=true
```

```bash
bash tools/check_keys.sh
```

## 共通の完了確認（各 PR の最後）

- [ ] Core テストが全件成功（失敗0。スキップは私有データ由来のものだけ）。
- [ ] ゲーム側のビルド成功、`bash tools/check_keys.sh` 成功。Harmony の対象を変えた場合は `bash tools/check_patches.sh`（参照DLLがある場合）。
- [ ] `bash tools/package.sh artifacts/RimworldStatistics.zip` が成功し、ZIP に私有データ・外部MODのDLLが入っていない。
- [ ] `docs/manual-test-checklist.md` に、そのパッケージの手動確認項目を追記した。
- [ ] PR 本文に「自動で確かめたこと」「手動確認が必要なこと」「設計から変えた点」を分けて書いた。
- [ ] CI（`numeric-verification.yml`）が成功した。

---

## A1 表示基盤と表示改善（分野1）

### Task A1.1：列幅の計算（Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Layout/ColumnLayout.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ColumnLayoutTests.cs`

**Interfaces:**
- Produces: `public readonly record struct ColumnSpec(float MinWidth, float Weight);` と `public static class ColumnLayout { public static ColumnPlan Resolve(IReadOnlyList<ColumnSpec> columns, float available); }`、`public sealed record ColumnPlan(float[] Widths, float TotalWidth) { public bool NeedsHorizontalScroll(float available); }`。合計最小幅 ≤ available なら余りを重みで配分し合計を available にする。超えるなら各列は最小幅で、TotalWidth は合計最小幅。

- [ ] **Step 1: 失敗するテストを書く** — `ExtraSpaceIsSharedByWeight`（最小 100/100、重み 1/3、幅 400 → 150/250）、`OverflowKeepsMinimumsAndScrolls`（最小 300/300、幅 400 → 300/300、Total 600、NeedsHorizontalScroll true）、`ZeroWeightsShareEqually`、`NonPositiveAvailableReturnsMinimums`、`SingleColumnFillsWidth`。
- [ ] **Step 2: 失敗を確認** — `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true -- --filter-class "*ColumnLayoutTests"` が未定義で失敗。
- [ ] **Step 3: `ColumnLayout.Resolve` を実装**。
- [ ] **Step 4: 成功を確認**（同じコマンド）。
- [ ] **Step 5: コミット**。

### Task A1.2：表と操作行の部品、エンディング・取り込み状況への適用

**Files:**
- Create: `Source/RimworldStatistics/UI/Widgets/StatsTable.cs`（見出し・並べ替えの印・可視行描画・省略セルだけのツールチップ・幅のキャッシュ）
- Create: `Source/RimworldStatistics/UI/Widgets/ControlRow.cs`（文字幅に応じたボタン幅、折り返し）
- Modify: `Source/RimworldStatistics/UI/EndingsTab.cs`、`Source/RimworldStatistics/UI/ImportTab.cs`

**Interfaces:**
- Consumes: A1.1 の `ColumnLayout`。
- Produces（推奨）：`StatsTable` は「列定義（見出しキー、`ColumnSpec`、セル文字列の取り出し）」「行数」「データの版（`object` の参照またはカウンタ）」を受け取り、版と言語（`LanguageDatabase.activeLanguage`）が変わったときだけセル文字列と幅を作り直す。`ControlRow` は `Rect Next(string label, float min, float max)` と行の高さの合計を返す。

- [ ] **Step 1:** エンディングを `StatsTable` へ移す。行は `StatsView` が変わったときだけ作り、描画は可視行だけ。日付の降順は従来どおり。
- [ ] **Step 2:** 取り込み状況の問題一覧と各行の文字列を、セッションの変更（`ProfileSession` の変更番号を読む内部APIを足すか、`ImportRunner.LastResult` と `ImportState.LastScanUtc` の組）でキャッシュする。進捗文字列は棒の幅で省略し、全文をツールチップに出す。
- [ ] **Step 3:** ビルドが通ることを確認（準備のビルドコマンド）。
- [ ] **Step 4:** コミット。

### Task A1.3：概要の可変格子と設定画面の高さ

**Files:**
- Modify: `Source/RimworldStatistics/UI/OverviewTab.cs`、`Source/RimworldStatistics/RimworldStatisticsMod.cs`
- Create（任意）: `Source/RimworldStatistics.Core/Layout/GridLayout.cs`＋`Tests/.../GridLayoutTests.cs`（列数＝`max(1, floor((幅+間隔)/(最小セル幅+間隔)))` を純粋関数にする場合）

- [ ] **Step 1:** 概要のセル列数を幅から決め、内容が高さを超えたら縦スクロールにする。ラベルは1行で省略し全文をツールチップ。値は中サイズのまま、入らないときは省略＋ツールチップ。セルの文字列は `StatsView` と言語が同じ間はキャッシュする。
- [ ] **Step 2:** 設定画面のスクロール領域の高さを、前回描画時の `Listing_Standard.CurHeight` で決める（初回は従来の式）。
- [ ] **Step 3（任意）:** 全タブが可変幅に対応した後、統計ウィンドウの `resizeable` を有効にするか判断する。有効にする場合は最小サイズを標準（1000×720、画面の90%以内）とし、PR に理由を書く。
- [ ] **Step 4:** ビルド確認、コミット。

### Task A1.4：詳細統計とコロニー一覧

**Files:**
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`、`Source/RimworldStatistics/UI/ColoniesTab.cs`

- [ ] **Step 1:** 詳細統計の操作行を `ControlRow` にする（固定の 125/150/220/180 を文字幅ベースに。検索欄の最小幅 120 を保ち、入らなければ次の行）。表の列を `ColumnSpec`（名前・値・状態・件数）にし、状態列は「実験的 · 部分（欠けあり）」が入る最小幅を持つ。省略したセルだけ全文ツールチップ（行全体のツールチップは従来どおり）。
- [ ] **Step 2:** コロニー一覧の列の最小幅に見出しの文字幅を含める（「シナリオ / ストーリーテラー」が重ならない）。#144 のキャッシュ・並べ替え・選択・除外・横スクロールの挙動は変えない。下段のセーブ一覧は選択コロニーと `StatsView` とファイル索引が同じ間はキャッシュする。
- [ ] **Step 3:** `docs/manual-test-checklist.md` に「表示（分野1）」節を追加：日本語・英語 × 4サイズ × UIスケール100%・150% で5タブと設定画面に重なりがない／省略セルの全文ツールチップ／並べ替え・除外・選択・縦横スクロールが従来どおり／文字サイズが変わっていない／開いたまま放置してもフレーム時間が増え続けない。
- [ ] **Step 4:** 共通の完了確認、PR。PR 本文で #123 の残り（日英ソート・スクロール・除外・選択の実ゲーム確認、描画の割り当て計測）を手動確認項目として挙げる。

---

## A2 プロフィールのバックアップ・復元（分野7）

### Task A2.1：バックアップの作成と検証（Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Backup/BackupManifest.cs`、`Source/RimworldStatistics.Core/Backup/ProfileBackup.cs`、`Source/RimworldStatistics.Core/Files/AtomicFile.cs`（F7）
- Test: `Tests/RimworldStatistics.Core.Tests/ProfileBackupTests.cs`

**Interfaces:**
- Produces（推奨）：
  - `public sealed class BackupManifest { public int Format { get; init; } = 1; DateTime CreatedUtc; string ModVersion; int ProfileSchemaVersion; string Label; int Colonies; int Observations; List<BackupFileEntry> Files; }`、`public sealed record BackupFileEntry(string Path, long Size, string Sha256);`（Path は `/` 区切りの相対パス）。
  - `ProfileBackup.Create(BackupSource source, string backupDirectory, BackupMetadata meta) -> BackupInfo`：`BackupSource` は「`profile.json` のバイト列」「`checkpoint-archive/` のディレクトリ」「`preferences.json` の文字列（null可）」。`.zip.tmp` に書き、開き直して全ハッシュを照合してから `.zip` へ改名。既存の名前は上書きしない。
  - `ProfileBackup.Validate(string zipPath, int currentSchemaVersion) -> BackupValidation`：`Ok`、`Reason`（`Unreadable`／`NoManifest`／`UnknownFormat`／`HashMismatch`／`ProfileUnreadable`／`NewerSchema`）、`Manifest`、`Summary`。`profile.json` は `ProfileJson.Deserialize` で読む。
  - `ProfileBackup.List(string backupDirectory) -> IReadOnlyList<BackupInfo>`（壊れたzipも「検証失敗」として一覧に出す）。

- [ ] **Step 1: 失敗するテストを書く** — `CreateWritesManifestWithHashesOfEveryFile`、`CreatedZipValidatesOk`、`TamperedEntryIsHashMismatch`、`MissingManifestIsRejected`、`TruncatedZipIsUnreadable`、`NewerSchemaIsRejected`（schemaVersion 3 の profile）、`CorruptProfileJsonIsRejected`、`ExistingFileIsNeverOverwritten`（同時刻で2回作成 → 別名）、`FailedWriteLeavesNoZip`（書き込み先を読み取り専用にする等で失敗させ、`.zip` が残らない）、`EmptyProfileCanBeBackedUp`（Review Focus 1）。`TempDir` を使う。
- [ ] **Step 2: 失敗を確認** — `-- --filter-class "*ProfileBackupTests"`。
- [ ] **Step 3: 実装**。zip は `System.IO.Compression.ZipArchive`。ハッシュは `SHA256`。
- [ ] **Step 4: 成功を確認**。
- [ ] **Step 5: 計測** — 1,000件規模の退避ファイル行を含む合成データで、作成と検証の時間・zipサイズを PR に記録する（目安を超えても失敗にはしない。数値を残す）。
- [ ] **Step 6: コミット**。

### Task A2.2：復元（入れ替えと巻き戻し、Core）

**Files:**
- Modify: `Source/RimworldStatistics.Core/Backup/ProfileBackup.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ProfileRestoreTests.cs`

**Interfaces:**
- Produces（推奨）：`ProfileBackup.Restore(string zipPath, string dataDirectory, RestoreOptions options) -> RestoreResult`。手順は設計 §6.7 の (3)(4)：`profile.json.lock` を `FileShare.None` で開いたまま、現行の `profile.json`・`.bak1〜3`・`.previous`・`checkpoint-archive/` を同じボリュームの一時フォルダへ移し、検証済みの内容を展開して所定の場所へ移す。失敗したら元へ戻す。`RestoreOptions.FailAfterStep`（テスト専用の失敗注入）を持たせてよい。`preferences.json` の中身は `RestoreResult.Preferences` で返し、適用は呼び出し側が決める。

- [ ] **Step 1: 失敗するテストを書く** — `RestoreBringsBackExactBytes`（バックアップ → `profile.json` と退避ファイルを変更 → 復元 → バイト列一致）、`RestoreRemovesNewerArchiveFiles`（バックアップ後に増えた退避ファイルは消える）、`FailureDuringMoveRollsBack`（失敗注入後、元のファイルがすべて元の場所・元のバイト列）、`InvalidBackupIsNeverApplied`（ハッシュ不一致で現行ファイル無変更）、`RestoredProfileLoadsAsPrimary`（復元後に `new ProfileStore(dir).Load(...)` の `Source == Primary`）、`LockHeldByOtherWriterFailsSafely`（lock を別に開いた状態で復元 → 失敗・無変更）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A2.3：セッションの一時停止・終了・読み直し

**Files:**
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`、`Source/RimworldStatistics/ProfileService.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ProfileSessionTests.cs`

**Interfaces:**
- Produces（推奨）：
  - `ProfileSession.WithWritesPaused<T>(Func<T> action)`：未保存分を書き出し、書き込みの排他（`_ioLock`）を保持したまま `action` を実行する（バックアップの一貫性用）。
  - `ProfileSession.Close()`：書き出し後、以後の書き込みをすべて無効にする（古いセッションが復元後のファイルを上書きしないため）。
  - `ProfileService.Generation`（int）と `ProfileService.Reload()`：タイトル画面で、取り込みが動いていないときだけ現在のセッションを `Close` し、新しいセッションを読み込む。

- [ ] **Step 1: 失敗するテストを書く** — `ClosedSessionNeverWritesAgain`（Close 後の Mutate＋Flush でファイル無変更）、`WritesPausedSeesFlushedProfile`（未保存の変更が action 内で読むファイルに反映済み）、`BackgroundWriteWaitsForPause`。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。`ProfileService.Reload` はゲーム側のためビルド確認のみ。
- [ ] **Step 5:** コミット。

### Task A2.4：データ管理タブ（取り込み状況＋バックアップ）

**Files:**
- Modify: `Source/RimworldStatistics/UI/ImportTab.cs`（タブ名を「データ管理」へ。既存の内容は上段に残す）、`Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`（`ProfileService.Generation` の変化でビュー・タブのキャッシュを捨てる）、`Languages/*/Keyed/RimworldStatistics.xml`

- [ ] **Step 1:** 「今すぐバックアップ」（取り込み中は無効＋理由）、一覧（作成時刻・ラベル・MOD版・サイズ・検証結果）、「復元…」「削除…」（`Dialog_MessageBox.CreateConfirmation`）、「フォルダーを開く」「パスをコピー」（F7）。処理はバックグラウンドで行い、進捗と結果を出す。
- [ ] **Step 2:** 復元の確認画面に検証結果（作成時刻・MOD版・コロニー数・観測数・新しいスキーマなら復元不可の理由）と「表示設定も戻す」（A3 以前は非表示でよい）を出す。実行時は「復元前」バックアップを先に作って検証し、`ProfileService.Reload` を呼ぶ。
- [ ] **Step 3:** 手動確認項目を追記：作成・一覧・削除／復元後に統計が戻る／復元前バックアップから元に戻せる／取り込み中に無効／壊したzip（1バイト改変）が復元不可と表示される／`Application.OpenURL` でフォルダが開くか（開かない場合はパスのコピーで代替できるか）。
- [ ] **Step 4:** 共通の完了確認、PR。

---

## A3 集計範囲とコロニーグループ（分野3 段階1）

### Task A3.1：表示設定の形式（F6、Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Preferences/ViewPreferences.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ViewPreferencesTests.cs`

**Interfaces:**
- Produces（推奨）：`public sealed class ViewPreferences { public const int CurrentVersion = 1, MaxFavorites = 64, MaxGroups = 32, MaxGroupNameLength = 64; public List<string> Favorites; public List<ColonyGroup> Groups; public static ViewPreferences Parse(string? json, out bool recovered); public string Serialize(); }`、`public sealed class ColonyGroup { public string Id; public string Name; public List<string> ColonyKeys; }`。`Parse` は例外を出さない（壊れていれば空＋`recovered = true`）。重複・空文字のキー、上限超過は読み込み時に正規化する。

- [ ] **Step 1: 失敗するテストを書く** — `RoundTripsFavoritesAndGroups`、`UnknownFieldsAreIgnored`、`CorruptJsonYieldsEmptyAndRecovered`、`NullOrEmptyYieldsEmptyNotRecovered`、`DuplicatesAndBlankKeysAreRemoved`、`LimitsAreEnforced`（65件 → 64件、長い名前 → 64文字）、`NewerVersionIsReadBestEffort`（version 2 の既知フィールドは読む）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A3.2：範囲の集計（F1、Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/ViewScope.cs`
- Modify: `Source/RimworldStatistics.Core/Matching/Colony.cs`、`Source/RimworldStatistics.Core/Stats/StatsCalculator.cs`、`Source/RimworldStatistics.Core/Stats/MetricCalculator.cs`、`Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ViewScopeTests.cs`

**Interfaces:**
- Produces（推奨）：
  - `public sealed record ViewScope { public static ViewScope All { get; } ; public static ViewScope ForGroup(ColonyGroup group); public string CacheKey { get; } public bool Contains(Colony colony); }`（`Contains` は `Key` か `AliasKeys` の一致）。
  - `Colony.OutOfScope`（`bool`）と `Colony.Counted => !Excluded && !OutOfScope`。`ColonyStats.InScope`、`StatsResult.OutOfScopeColonyCount`、`NumericColony.InScope`。
  - `ProfileSession.GetView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null, ViewScope? scope = null)` と同じ引数の `TryGetCachedView`。範囲別のキャッシュは全体＋最大3件。
  - 集計（合計・最大・最小・比率・`MetricRankings`・`CrossColonyAggregator`）は `Counted` のコロニーだけを使う。`Attach` と `CampaignMatcher` は全コロニー。

- [ ] **Step 1: 失敗するテストを書く** —
  - `AllScopeIsIdenticalToCurrentView`（同じプロフィールで、既存の `GetView(options, units)` と `scope: ViewScope.All` の全体の行・`StatsResult` が一致）。
  - `GroupEqualsExcludingEveryOtherColony`（3コロニー、グループ {A,B} の値 ＝ C を除外したときの値。合計・最大・最小・比率・`ranking.*`・`cross.*` を比較）。
  - `ScopeDoesNotChangeAttribution`（`NumericView.Unattributed` と各コロニーの `Observations` が範囲によらず同じ）。
  - `ExcludedColonyInGroupIsNotCounted`。
  - `AliasKeyMatchesGroupMember`（手動統合後の別名キーでも一致。Review Focus 4）。
  - `UnknownGroupKeysAreIgnored`（存在しないキーのみのグループ → 対象0、例外なし）。
  - `ScopedViewsAreCachedAndBounded`。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。既存テストがすべて変更なしで通ること。
- [ ] **Step 5:** ProfileBenchmark を実行して、全体ビューの `build_view_ms` が基準から悪化していないことを PR に記録する。

```bash
dotnet run -c Release --project tools/ProfileBenchmark/ProfileBenchmark.csproj -- 100 500 1500 3000
```

- [ ] **Step 6:** コミット。

### Task A3.3：範囲の切替とグループ編集（ゲーム側）

**Files:**
- Modify: `Source/RimworldStatistics/RimworldStatisticsSettings.cs`（`viewPreferencesJson` 文字列を1件追加）、`Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`、`Source/RimworldStatistics/UI/OverviewTab.cs`、`Source/RimworldStatistics/UI/DetailsTab.cs`、`Languages/*`
- Create: `Source/RimworldStatistics/UI/Dialog_ColonyGroup.cs`、`Source/RimworldStatistics/UI/ViewPreferencesStore.cs`（`ViewPreferences` の読み書きと `Mod.WriteSettings()`）

- [ ] **Step 1:** タイトル行の右に範囲の切替（全コロニー／グループ／作成・編集）。選択はセッション中だけ覚える。
- [ ] **Step 2:** グループ編集画面：名前、検索、全選択・全解除、コロニー一覧（除外の印付き。除外は数えない旨を表示）、見つからないキーの件数と削除。
- [ ] **Step 3:** 概要のツールチップと詳細統計の注記に「範囲名・対象・除外・範囲外」の件数を出す。A2 の復元で「表示設定も戻す」を有効にする。
- [ ] **Step 4:** 手動確認項目を追記：グループの作成・名前変更・削除が再起動後も残る／全コロニーの値が導入前と同じ／グループの値が他を除外したときと同じ。
- [ ] **Step 5:** 共通の完了確認、PR。

---

## A4 お気に入り・カスタム概要（分野2）

### Task A4.1：統計の参照と基本記録の仮キー（F2、Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/MetricLookup.cs`、`Source/RimworldStatistics.Core/Stats/BasicMetrics.cs`
- Modify: `Source/RimworldStatistics.Core/Stats/StatsCalculator.cs`（`ColonyStats.HasObservedPoint` を追加）
- Test: `Tests/RimworldStatistics.Core.Tests/MetricLookupTests.cs`

**Interfaces:**
- Produces（推奨）：
  - `BasicMetrics`：`Prefix = "basic."`、キー `basic.days`／`basic.real_play_hours`／`basic.peak_wealth`／`basic.last_wealth`／`basic.raids`／`basic.threats`／`basic.killed`／`basic.departed`／`basic.max_population`／`basic.endings`／`basic.saves`、`IReadOnlyList<string> Keys`、`double? Value(ColonyStats colony, string key)`（観測点がなければ null。資産は `PeakWealthKnown`／`LastWealthKnown` が false なら null）。
  - `MetricLookup`：`MetricDefinition? Definition(StatsView view, string key)`、`NumericRow? Overall(StatsView view, string key)`、`NumericRow? ForColony(StatsView view, string colonyKey, string key)`、`bool IsKnownKey(StatsView view, string key)`。

- [ ] **Step 1: 失敗するテストを書く** — `FindsOverallRowByKey`、`FindsPerColonyRowByKey`、`IndividualOnlyHasNoOverallRow`、`ProviderDefinitionWithoutTranslationIsFound`（`LabelKey == Key` の SDK 定義。Review Focus 3）、`DeletedKeyIsUnknownNotThrowing`、`BasicKeysNeverCollideWithRegistry`（`MetricRegistry.Catalog` に `basic.` で始まるキーがない）、`BasicValueIsNullWithoutObservedPoint`、`EmptyProfileHasNoRows`（Review Focus 1）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A4.2：表示名・書式・ツールチップの共通化

**Files:**
- Create: `Source/RimworldStatistics/UI/MetricText.cs`
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`

**Interfaces:**
- Produces（推奨）：`MetricText.Label(MetricDefinition)`、`MetricText.Format(MetricDefinition, NumericValue)`、`MetricText.CoverageLabel(...)`、`MetricText.EvidenceLabel(...)`、`MetricText.Tip(NumericRow row, string label, TipContext context)`。中身は現在の `DetailsTab.DefinitionLabel`・`Format`・`CoverageLabel`・`EvidenceLabel`・`BuildTip` を**挙動を変えずに**移したもの。基本記録の仮キー用の表示名もここに置く。

- [ ] **Step 1:** 移動のみ行い、詳細統計の表示が変わらないことをビルドと手動の目視で確かめる（翻訳キーの追加・削除なし）。
- [ ] **Step 2:** コミット。

### Task A4.3：★とお気に入り欄

**Files:**
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`、`Source/RimworldStatistics/UI/OverviewTab.cs`、`Languages/*`

- [ ] **Step 1:** 詳細統計の最上位の行の左端に★の切り替え（内訳行には出さない）。押下で `ViewPreferences.Favorites` に追加・削除し保存する。
- [ ] **Step 2:** 概要の基本の数値の下に「お気に入り」欄（カード：表示名、範囲の値、状態の印、ランキング系なら保持コロニー。ツールチップは `MetricText.Tip`）。編集モードで上へ・下へ・解除。参照先がなければ「不明な統計」と解除ボタンだけを出す。
- [ ] **Step 3:** 手動確認項目を追記：登録・並べ替え・解除が再起動後も残る／値が詳細統計と一致／MODを外しても提供元定義の統計が出る／設定を壊しても開ける。
- [ ] **Step 4:** 共通の完了確認、PR。

---

## A5 歴代ランキングTOP10（分野5）

### Task A5.1：順位表（F3、Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/Leaderboard.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/LeaderboardTests.cs`

**Interfaces:**
- Consumes: A3.2 の `Counted`、A4.1 の `MetricLookup`・`BasicMetrics`。
- Produces（推奨）：`public sealed record LeaderboardEntry(int Rank, string ColonyKey, double Value, MetricCoverage Coverage, MetricEvidence Evidence, int? ThroughTick);`、`public sealed record Leaderboard(string MetricKey, bool Descending, IReadOnlyList<LeaderboardEntry> Entries, int Measured, int Targets, bool HasPartial);`、`public static class LeaderboardBuilder { public static Leaderboard Build(StatsView view, string metricKey, bool descending); public static IReadOnlyList<string> RankableKeys(StatsView view); }`。有効値の判定は `CrossColonyAggregator.Extreme` と同じ（`Known`、有限、`Unknown`/`NotApplicable` 以外）。同値は同順位、同順位内はコロニーキーの序数順。`RankableKeys` は基本記録と、派生（`OverallAggregation.Ranking`）以外のすべての定義。

- [ ] **Step 1: 失敗するテストを書く** —
  - `TiesShareRankAndSkipNext`（値 5,5,3 → 順位 1,1,3。同順位内はキー順）。
  - `UnknownAndNotApplicableAreNotRanked`（`Measured`/`Targets` も検証）。
  - `PartialIsRankedAndFlagged`（`HasPartial == true`）。
  - `AscendingReversesOrder`。
  - `OnlyCountedColoniesAreRanked`（除外・範囲外を含まない）。
  - `TopMatchesCrossMaxHolderForEveryComparableKey`（`CrossColonyCatalog` の max 定義ごとに、合成の3コロニーで1位のキーと同率が `cross.max.*` の `ColonyKey`／`HolderKeys` と一致）。
  - `BasicDaysMatchesColonyTableOrder`（観測点のないコロニーは出ない）。
  - `EmptyProfileYieldsEmptyBoard`（Review Focus 1）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A5.2：ランキングタブ

**Files:**
- Create: `Source/RimworldStatistics/UI/RankingsTab.cs`
- Modify: `Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`、`Languages/*`

- [ ] **Step 1:** 左：順位を付けられる統計の一覧（検索・カテゴリ・★、可視行描画）。右：順位表（順位・コロニー名・値・状態・記録時点の日）。既定10件、「すべて表示」、大きい順・小さい順、下部に「測定済み n／対象 m。未取得は順位に含めない」。順位表は統計・方向・ビューが変わったときだけ作る。
- [ ] **Step 2:** 英語で7タブが標準幅に入るか確かめ、入らなければ設計 §7 の代替案から選んで PR に記録する。
- [ ] **Step 3:** 手動確認項目を追記、共通の完了確認、PR。

---

## A6 取得状況の一覧（分野4）

### Task A6.1：分類（F5、Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/MetricStatus.cs`
- Modify（必要なら）: `Source/RimworldStatistics.Core/Stats/MetricAvailabilityPolicy.cs`（コロニー別の行から判定する補助を追加。既存の `Classify(NumericRow)` は変えない）
- Test: `Tests/RimworldStatistics.Core.Tests/MetricStatusTests.cs`

**Interfaces:**
- Produces（推奨）：`public enum MetricVerification { Standard, Experimental, Legacy }`、`public enum UnitRecordingState { Recording, NotSelected, DlcMissing, TargetModMissing, Failed, NotRequired }`、`public sealed record MetricStatus(MetricDefinition Definition, MetricAvailability Availability, bool PeriodIncomplete, MetricVerification Verification, UnitRecordingState Recording, int Measured, int Targets, IReadOnlyList<string> MissingColonyKeys)`、`public static class MetricStatusClassifier { public static IReadOnlyList<MetricStatus> Classify(StatsView view, Func<string, UnitRecordingState> unitState); }`。画面の4段階（取得済み・部分記録・未取得・対象外）は `Availability` と `PeriodIncomplete` から導く（設計 §5 F5）。

- [ ] **Step 1: 失敗するテストを書く** — `NoTargetsIsNotApplicable`、`NoMeasuredIsNotMeasured`、`SomeMeasuredIsPartial`、`PartialCoverageIsPartial`、`LateStartIsPartial`（`SinceTick > 0`）、`AllMeasuredIsMeasured`、`UnverifiedUnitIsExperimental`、`ProviderMetricIsExperimental`、`LegacyEvidenceIsLegacy`、`DerivedUsesOverallCounts`、`SaveFileMetricRecordingIsNotRequired`、`MissingColonyKeysListsUnmeasuredCountedColonies`、`IndividualOnlyIsClassifiedFromColonyRows`、`CountsSumToDefinitionCount`、`EmptyProfileIsAllNotApplicable`（Review Focus 1）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A6.2：取得状況タブと詳細統計の状態絞り込み

**Files:**
- Create: `Source/RimworldStatistics/UI/CoverageTab.cs`、`Source/RimworldStatistics/Metrics/UnitRecordingStates.cs`（`MetricGates`・`ModsConfig`・提供元の要否から `UnitRecordingState` を返す）
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`、`Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`、`Languages/*`

- [ ] **Step 1:** 上部に4段階＋実験的の件数。一覧は取得単位（なければ `Provenance`）ごとにまとめ、展開で統計の行。単位の行に今回の記録の状態・DLC/対象MODの要否・統計数・取得済み数。統計の行のツールチップに欠けているコロニー名（先頭10件＋件数）。絞り込み：程度・検証・カテゴリ・検索。「取得済みは数値の正しさを意味しない」の注記。
- [ ] **Step 2:** 詳細統計の「数値のある項目のみ」を状態の絞り込み（すべて／数値あり／部分記録／未取得／実験的）に置き換える。「数値あり」は従来の `MetricDisplayPolicy.HasMeasuredNumber` と同じ判定。既定は「すべて」。
- [ ] **Step 3:** 手動確認項目を追記：OFF／ON＋再起動／DLCなし／対象MODなしで「今回の記録」が一致。
- [ ] **Step 4:** 共通の完了確認、PR。

---

## A7 CSV出力（分野6）

### Task A7.1：CSVの書き出し（Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Export/CsvWriter.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/CsvWriterTests.cs`

**Interfaces:**
- Produces（推奨）：`public sealed class CsvWriter : IDisposable { public CsvWriter(Stream output); public void Row(params CsvField[] fields); }`、`public readonly record struct CsvField(string? Text, bool Numeric) { public static CsvField Of(string? text); public static CsvField Number(double? value); public static CsvField Integer(long? value); }`。UTF-8 BOM、CRLF、`, " CR LF` を含む文字列は二重引用符で囲み `"` を `""` に。数値は `CultureInfo.InvariantCulture`、実数は `"R"`。null は空欄。文字列で先頭が `= + - @` のものは `'` を前置する（数値列には適用しない）。

- [ ] **Step 1: 失敗するテストを書く** — `StartsWithUtf8Bom`、`UsesCrlf`、`QuotesCommaQuoteAndNewline`、`NullIsEmptyField`、`NumbersAreInvariantUnderGermanCulture`（`CultureInfo.CurrentCulture = de-DE` で `1234.5` → `1234.5`。Review Focus 2）、`FormulaPrefixedTextIsEscaped`（`=SUM(A1)` → `'=SUM(A1)`、`-Alpha` → `'-Alpha`）、`NegativeNumberIsNotEscaped`（`Number(-3)` → `-3`）、`LongCjkTextRoundTrips`（Review Focus 5）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A7.2：出力内容の組み立て（Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Export/StatisticsExport.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/StatisticsExportTests.cs`（期待ファイルは `Tests/RimworldStatistics.Core.Tests/Fixtures/Export/` に置く）

**Interfaces:**
- Consumes: A5.1 `LeaderboardBuilder`、A6.1 `MetricStatusClassifier`、A4.1 `BasicMetrics`、A3.2 範囲。
- Produces（推奨）：`public sealed record ExportText(IReadOnlyDictionary<string, string> MetricLabels, IReadOnlyDictionary<string, string> ColonyNames, Func<MetricDefinition, NumericValue, string> DisplayValue, string Language, string ScopeName);`（翻訳はメインスレッドで済ませた辞書と書式関数。書式関数は翻訳を呼ばない）、`public static class StatisticsExport { public static ExportSummary Write(StatsView view, ExportText text, Func<string, UnitRecordingState> unitState, string directory, CancellationToken cancel); }`。設計 §6.6 の6ファイル・列名をそのまま使う。書き込みは一時フォルダに行い、完了後に `exports/<UTC時刻>/` へ改名する。

- [ ] **Step 1: 失敗するテストを書く** — `GoldenExportOfSmallProfile`（2コロニー・数統計の合成プロフィールの6ファイルが期待ファイルと一致）、`SummaryMatchesOverallRows`、`RankingsMatchLeaderboard`、`UnknownValueIsBlankWithReason`、`ScopeIsRespectedAndColoniesListsAll`（`colonies.csv` は全コロニーを `in_scope` 付きで出す）、`MissingLabelFallsBackToKey`（Review Focus 3）、`CancellationLeavesNoFinishedFolder`、`EmptyProfileWritesHeadersOnly`（Review Focus 1）。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** 3,000件の合成セーブ規模で出力時間・割り当てを測る短い計測を `tools/ProfileBenchmark` に追加するか、PR に手元の計測値を記録する。
- [ ] **Step 6:** コミット。

### Task A7.3：データ管理タブへの追加

**Files:**
- Modify: `Source/RimworldStatistics/UI/ImportTab.cs`、`Languages/*`

- [ ] **Step 1:** 「CSV出力」ボタン（現在の範囲名を表示）。メインスレッドで `ExportText` を作り、書き込みはバックグラウンド。進捗・完了（保存先、フォルダーを開く、パスをコピー）・失敗を表示。
- [ ] **Step 2:** 手動確認項目を追記：日本語版 Excel（または LibreOffice）で文字化けせずに開ける／欠測が空欄／値が詳細統計・ランキングと一致。
- [ ] **Step 3:** 共通の完了確認、PR。

---

## A8 コロニー・グループの比較表示（分野3 段階2）

### Task A8.1：比較の行（Core）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/Comparison.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ComparisonTests.cs`

**Interfaces:**
- Consumes: A3.2（範囲別ビュー）、A4.1（`MetricLookup`）。
- Produces（推奨）：`public abstract record ComparisonTarget` の2種（`ColonyTarget(string ColonyKey)`、`ScopeTarget(ViewScope Scope)`）、`public static IReadOnlyList<ComparisonRow> Comparison.Build(IReadOnlyList<(ComparisonTarget Target, StatsView View)> columns, IEnumerable<string> metricKeys)`、`ComparisonRow(string MetricKey, IReadOnlyList<NumericValue?> Values)`。列は最大4。

- [ ] **Step 1: 失敗するテストを書く** — `ColonyColumnEqualsPerColonyRow`、`ScopeColumnEqualsScopedOverallRow`、`MissingIsNullNotZero`、`MoreThanFourColumnsIsRejected`。
- [ ] **Step 2〜4:** 失敗確認 → 実装 → 成功確認。
- [ ] **Step 5:** コミット。

### Task A8.2：詳細統計の比較表示

**Files:**
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs`、`Languages/*`

- [ ] **Step 1:** 表示切替に「比較」を追加。列の対象を最大4つ選ぶ。既存の絞り込み（表示区分・カテゴリ・検索・状態）に従う。列ごとの値で並べ替え、欠測は最後。セルのツールチップは表示時に作る。
- [ ] **Step 2:** 手動確認項目を追記、共通の完了確認、PR。

---

## M1〜M7 他MOD統計（分野8）

各パッケージは[別紙](../specs/2026-11-12-feature-expansion-mod-statistics.md)の該当節を仕様とする。共通の手順は別紙 §4。ここではタスクの形だけを決める。

### 各Mの共通タスク

**Files（典型）：**
- 本体の取得単位にする場合：`Source/RimworldStatistics.Core/Stats/Families/<Name>Family.cs` または `Stats/<Name>Snapshot.cs`、`Source/RimworldStatistics.Core/Stats/AcquisitionUnits.cs`、`Source/RimworldStatistics.Core/Stats/LiveCatalog.cs`／`MetricRegistry.cs`（定義の登録）、`Source/RimworldStatistics/Patches/Metrics/<Name>MetricPatches.cs`（`[MetricAdapter]` と `TargetMethod`）、`Source/RimworldStatistics/Metrics/MetricGates.cs`（対象の型があるときだけ開く）、`Source/RimworldStatistics/SavedStateMetricReader.cs`（保存時点の読み取り）。
- 既存の内蔵連携を拡張する場合：`Source/RimworldStatistics/IntegratedMods/<Mod>/Logic/`（テスト対象。テストプロジェクトが自動で取り込む）、`.../Source/`（フック・SDK登録）。
- 共通：`Languages/*`、`tools/IntegratedModAudit/check_<mod>_contract.py`（公開ソースがある場合）、`.github/workflows/numeric-verification.yml`（固定コミットの取得と契約確認の実行）、`Tests/RimworldStatistics.Core.Tests/...`。

- [ ] **Step 1: 対象の版を固定する** — 公開ソースのコミット SHA、または R2 の DLL のパスとサイズを PR 本文に記録する。別紙の「確認が必要」をすべて確かめ、結果を PR 本文に書く。確かめられない項目があれば、その統計は作らずに Issue に理由を残す。
- [ ] **Step 2: 失敗するテストを書く（Core）** — 少なくとも：確定時だけ数える（開始・失敗・中断は数えない）／同じイベントIDを二重に数えない／内訳の上限超過で `Partial`／プレイヤー勢力以外を数えない／対象の型がないとき取得単位が閉じる（`MetricGates` の判定を Core で表せる部分）／記録済みの値が対象MODなしでも残る（既存の `ModMetricPersistenceTests` の形）。M2 は「`Deliver` の引数を一度だけ列挙する」（列挙回数を数える偽の `IEnumerable` で検証）を追加する。
- [ ] **Step 3: 失敗を確認、実装、成功を確認。**
- [ ] **Step 4: 静的確認** — 契約確認スクリプトをCIに追加し、ローカルでも実行する。Harmony の対象がバニラの型なら `bash tools/check_patches.sh` を通す。
- [ ] **Step 5: パッチ数の確認** — 取得OFF・対象MODなし・旧アドオン併用で新しいパッチ0件、ONで想定件数（#117 と同じ観点）を、手動確認項目と RWMT の B 確認の対象に加える。
- [ ] **Step 6: 共通の完了確認、PR。** PR 本文に「実ゲームの数値・TPSは未確認」と明記し、取得単位は `Unverified` のまま。

### パッケージ別の追加事項

| パッケージ | 追加のテスト・確認 |
|---|---|
| M1 VPE | `royalty.psycasts` と同じ操作で両方が増えないこと（VPE導入時の手動確認）。`ChangeLevel(int)` の `levelOffset <= 0` を数えない。`InitializeFromPsylink` を数えない |
| M2 VEF Outposts | `Produce` の外で呼ばれた `Deliver`（保管品の引き出し・キャラバン化）を数えない。既存 `exploration.outposts.active` の値が変わらない |
| M3 Hospitality | 敵対化・逮捕・全滅で帰還を数えない。客との売買がバニラの交易統計と重複しない |
| M4 DBH | `JobCondition.Succeeded` 以外を数えない。対象外のジョブで集合の参照比較だけを行う（ジョブ終了ごとの割り当てなし） |
| M5 Vehicle Framework | 着手前に所有者が1.7のDLLを用意する。型名を DLL のメタデータで確定してから実装する |
| M6 SAU | 手動の着替えを数えない。同じ指示（ポーンID＋服ID＋発行tick）を二重に数えない |
| M7 その他 | 調査結果を `docs/research/` に記録し、実装するなら新しい Issue を作る |

## 実ゲーム検証の進め方

- **Core とCI**：上記のテストと `numeric-verification.yml` で、計算・形式・ファイル操作を確かめる。
- **RWMT（`pengin0503/RimWorld-for-mod-test`）**：Linux の検証ホストで、MODの読み込み（A）と Harmony パッチの登録（B）を確かめられる。描画（文字の重なり）とゲーム中の数値は対象外。RWMT のワークフローは検証する Statistics の SHA を固定して `main` で動くため、実行には RWMT 側の PR と、所有者の明示的な指示によるマージが必要（RWMT の `AGENTS.md`）。実装担当は PR の作成までを行う。他MOD（VPE・VEF・VOE・Hospitality・DBH は R2 にある）を同時に読み込む確認は、RWMT のワークフローの拡張として提案する。
- **所有者の手動確認**：`docs/manual-test-checklist.md` の追記項目。分野1の見た目、分野7の復元、分野8の数値はここで確かめる。確認できるまで、他MODの取得単位は `Unverified` のまま。
- Anomaly の実ゲーム検証は行わない。1.7 のマイナー版の差は、具体的な差が確認されない限り同一として扱う。

## 既存 Issue との関係

- #123：A1 で残りの手動確認項目を引き継ぐ。
- #117：M1〜M6 はすべて「OFF でパッチ0件」の方針に従う。
- #62／#63／#64：分野8の新しい取得単位も、それぞれの検証（実ゲームの観測、TPS、MOD構成の違い）の対象に加わる。新しい Issue は作らない。
