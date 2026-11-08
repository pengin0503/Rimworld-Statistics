# Experimental Statistics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **状態:** 実装前の設計計画（承認待ち）。**この文書の保存だけでは実装を開始しない。**

**Goal:** 30件の未検証取得単位のうちイベント/スナップショット29単位を一般のMOD設定から選択して実験取得でき、データの正確性ラベルを偽らず詳細統計へ表示する。

**Architecture:** `AcquisitionStatus`（検証証拠）とセッションの実行ポリシー（取得ON/OFF）、画面の表示ポリシーを分離する。既存`MetricAdapters`/`MetricGates`/`MetricCalculator`/`ProfileSession`を拡張し、保存/復元・コロニーの代表選択・旧A/D1表示の実装は流用する。

**Tech Stack:** RimWorld 1.7, .NET 10, C#, Harmony, Verse `ModSettings`, System.Text.Json, xUnit, GitHub Actions、JA/EN Keyed翻訳。

**Spec:** `docs/superpowers/specs/2026-11-08-experimental-statistics-and-mod-adapters-design.md` §§1–3,5。

## Global Constraints

- 未検証単位の `AcquisitionStatus.Unverified` は**変更しない**。既存 `NumericCatalogTests.NoUnitIsVerifiedWithoutAnInGameRun` も削除/改変してはならない。
- 初期状態の収集は完全OFF。明示選択された正規取得IDだけを収集。 `ending_party` は派生単位であり取得パッチを持たない。
- 取得設定の変更は次回のゲーム再起動で反映。実行中にHarmony Unpatch/Repatchしない。
- `RimworldStatisticsVerifyHarness` は別MOD、製品ZIPへ混入させない。普通の製品設定は `-savedatafolder` を必要としない。
- 途中ON/OFF・DLC欠落・後日導入における未観測期間の補完をしない。0は本当に観測した既知の0だけ。
- データ追加はオプショナルな保存フィールド・未知JSON保護を原則とする。既存v1の指標、コロニー同定、終了回数、保存失敗処理、観測の圧縮結果を維持。
- UIは数字の一覧・既存5タブ、グラフなし。翻訳は日英両方。
- P0だけで独立してPR・レビュー・リリース候補にできること。他MODアダプターP1以降を必須にしない。

## File Structure / Responsibilities

| File | Responsibility |
|---|---|
| Create: `Source/RimworldStatistics.Core/Stats/ExperimentalAcquisitionPolicy.cs` | 純Coreの選択設定・検証状態との統合（UI/Harmonyに依存しない） |
| Create: `Source/RimworldStatistics.Core/Stats/MetricDisplayPolicy.cs` | 公開/実験/過去記録/派生の行の選択 |
| Create: `Source/RimworldStatistics.Core/Stats/MetricEvidence.cs` | 取得時の確証と混在伝播、Coverageとは別 |
| Modify: `Source/RimworldStatistics/RimworldStatisticsSettings.cs` | Verse `ModSettings` に選択済みIDとトグルを保存 |
| Modify: `Source/RimworldStatistics/RimworldStatisticsMod.cs` | 設定UI・警告・未適用の変更表示 |
| Modify: `Source/RimworldStatistics/Metrics/MetricGates.cs` | 起動時に凍結したユーザー取得ポリシーとの連携 |
| Modify: `Source/RimworldStatistics/Metrics/MetricAdapters.cs` | 選択済みの取得アダプターだけインストール |
| Modify: `Source/RimworldStatistics.Core/Model/MetricObservation.cs`, `MetricCheckpoint.cs` | 実験的来歴の保存 |
| Modify: `Source/RimworldStatistics.Core/Stats/LiveMetricAccumulator.cs`, `MetricCalculator.cs` | 来歴の維持・集計後の保守的な品質伝播 |
| Modify: `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs` | 表示ポリシーを含むキャッシュキー |
| Modify: `Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`, `DetailsTab.cs` | ユーザー選択に従う定義と「実験的」表示 |
| Modify: `Languages/Japanese/Keyed/RimworldStatistics.xml`, English counterpart | 翻訳 |
| Create: `Tests/RimworldStatistics.Core.Tests/ExperimentalAcquisitionPolicyTests.cs`, `ExperimentalDisplayTests.cs`, `ExperimentalEvidenceTests.cs` | 新規の独立回帰テスト |
| Modify: `tools/VerifyHarness/README.md`, `docs/manual-test-checklist.md` | 実験モードと開発ハーネスの責任分離 |

## Review Focus

1. 古いセーブに新規取得単位をON: 過去未観測の件数0・完全値を生成せず、開始時点と `Partial` を保つ → Task 4。
2. ON→OFF→ONにセーブ分岐・再ロード: チェックポイントの重複加算をせず、空白期間を完全値に合算しない → Task 4。
3. 同じHarmonyクラスが2単位へ対応: 一度だけパッチし、選択されていない単位へイベントを流さない → Task 3。
4. ユーザーが設定を変更して再起動前に詳細タブを開く: 表示/取得の適用状態を誤表示しない → Task 2,5。
5. 記録時 `Unverified`→将来 `Verified`、または対象DLCを削除: 旧観測の信頼性を勝手に昇格させず、昔の記録を保持 → Task 4,5。

---

### Task 1: Pure Core acquisition-policy resolution

**Files:** Create `Source/RimworldStatistics.Core/Stats/ExperimentalAcquisitionPolicy.cs`; test `Tests/RimworldStatistics.Core.Tests/ExperimentalAcquisitionPolicyTests.cs`.

**Interfaces:** `ExperimentalSelection(bool Enabled, IReadOnlyCollection<string> ExplicitUnitIds)`; `ExperimentalAcquisitionPolicy.Resolve(ExperimentalSelection selection, IReadOnlyCollection<AcquisitionUnit> catalog): IReadOnlySet<string>`。認証情報やVerse型は使用しない。

- [ ] **Step 1: RED test** — `DefaultSelection_OpensNoUnverifiedUnits`; `ExplicitSelections_OpenOnlyKnownUnits`; `UnknownAndDerivedIds_NeverBecomePatchTargets`; `VerifiedUnits_DoNotDependOnExperimentalOptIn`; `NewlyAddedUnit_IsNotAutoEnabled` を作成する。
- [ ] **Step 2: Confirm RED** — `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --filter FullyQualifiedName~ExperimentalAcquisitionPolicyTests` が当初失敗することを確認。
- [ ] **Step 3: Implement** — 正規IDの比較（Ordinal）、除外: 未知ID/`Derived`、検証済み標準取得との和集合、同一IDの重複排除。選択スナップショットはimmutableであること。
- [ ] **Step 4: GREEN** — 同フィルタと既存 `NumericCatalogTests` を実行し成功を確認。
- [ ] **Step 5: Commit** — `test/feat(experimental): define safe acquisition selection`。

### Task 2: Persistent settings and restart boundary

**Files:** Modify `RimworldStatisticsSettings.cs`, `RimworldStatisticsMod.cs`; test Task 1 のCore方針 + 開発ハーネスにUI受入手順。

**Interfaces:** `RimworldStatisticsSettings` は `experimentalEnabled=false`、選択済みIDの永続コレクション、`showRecordedExperimental=false` を提供。 `ActiveAcquisitionSelection`（起動時のスナップショット）と `DesiredAcquisitionSelection`（UIで保存した値）は別。最終的なC#名の選択はこのTaskのテストに先に固定し、後続Taskも同じ名を使う。

- [ ] **Step 1: RED** — 既定OFF / 旧`ModSettings`読み込み / 20 ID選択の保存再開 / 重複・未知ID / 途中切替後にActiveが不変 / 一括選択しても新単位が後で勝手にONにならない、をCoreテストとUIテスト手順に固定。
- [ ] **Step 2: Confirm RED** — Coreフィルタの失敗、ならびに既存MODで設定欄がないことを確認。
- [ ] **Step 3: Implement** — Scribeで安全な正規ID群の永続化、チェックリスト＋一括操作＋警告＋必要DLC状態、再起動要求表示。ゲーム起動時のみActiveを固定。
- [ ] **Step 4: GREEN** — Coreテスト、日英翻訳、実際のゲーム再起動時の設定保持。既存2設定は引き続き同値。
- [ ] **Step 5: Commit** — `feat(experimental): add opt-in settings and restart semantics`。

### Task 3: Apply only explicitly opened Harmony acquisition units

**Files:** Modify `MetricGates.cs`, `MetricAdapters.cs`; Test `ExperimentalAcquisitionPolicyTests.cs`、`tools/VerifyHarness/` の既存診断。

**Interfaces:** `MetricGates.ConfigureProductionSelection(IReadOnlySet<string> unitIds)`（初期化時の一回のみ）、既存 `IsOpen` / `IsRecording` を維持。VerifyHarnessの `OpenForVerification` は独立した開発経路。

- [ ] **Step 1: RED** — OFFで `Unverified` パッチ0、選択researchでresearchのみ記録、共同フックの適用1回、必須DLCなしではパッチOFF、失敗した1単位だけ停止、開発Harnessの既存動作維持。
- [ ] **Step 2: Confirm RED** — 追加の純Core/静的Harmony参照テストを失敗させる。実機結果が必要なケースは開発Harnessの手順に期待値を先に記載。
- [ ] **Step 3: Implement** — ModSettingsから凍結した選択を登録し、従来の `MetricAdapters.ApplyOpen` のゲートへ統合。二重 `Patch()` は従来の `Done` / `Broken` を維持。
- [ ] **Step 4: GREEN** — `bash tools/refcheck.sh && bash tools/check_patches.sh`、Core全件、Harnessに対する静的検査を成功させる。
- [ ] **Step 5: Commit** — `feat(experimental): gate production metric hooks by opt-in`。

### Task 4: Provenance, save and resume invariants

**Files:** Create `Stats/MetricEvidence.cs`; Modify `Model/MetricObservation.cs`, `Model/MetricCheckpoint.cs`, `Stats/LiveMetricAccumulator.cs`, `Stats/MetricCalculator.cs`; Test `ExperimentalEvidenceTests.cs`, `NumericCheckpointTests.cs`, `ModelJsonTests.cs`。

**Interfaces:** `MetricEvidence`: `UnknownLegacy`, `Experimental`, `Verified`, `Mixed`。保存粒度はスナップショットの単位別か、Checkpointのファミリー別。最終集計時は `MetricEvidence.Combine(...)` で保守的に品質を伝播する。`MetricCoverage` は変更しない。

- [ ] **Step 1: RED** — 実験的値の往復、初期値欠測、OFF期間、P→A/B分岐、将来の検証済み移行後も昔の実験来歴維持、旧v2/未知JSONの無損失往復、出所が混在すると`Mixed` を固定。
- [ ] **Step 2: Confirm RED** — 新規ファイルのテストが失敗することを確認。失敗が単なるfixture破損でないことを調べる。
- [ ] **Step 3: Implement** — オプショナルな来歴フィールド、保存時の時点情報の設定、CのResumeで欠測期間をPartial扱い、Bの保存時情報の維持、集計での品質伝播。将来版のJSON/defを消さない。
- [ ] **Step 4: GREEN** — `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release`。保存レコード・圧縮・取り込み・分岐回帰も全件成功。
- [ ] **Step 5: Commit** — `feat(experimental): preserve capture evidence through checkpoints`。

### Task 5: Published + experimental view selection without stale cache

**Files:** Create `Stats/MetricDisplayPolicy.cs`; Modify `Stats/MetricCalculator.cs`, `Profiles/ProfileSession.cs`, `UI/Dialog_RimworldStatistics.cs`; Test `ExperimentalDisplayTests.cs`。

**Interfaces:** `MetricDisplayPolicy.SelectDefinitions(IReadOnlyCollection<MetricDefinition> catalog, IReadOnlySet<string> visibleUnitIds): IReadOnlyList<MetricDefinition>`。 `ProfileSession` キャッシュキーは `IncludeDevMode` と安定ソートされた `visibleUnitIds` を含む。取得選択と表示選択は別のパラメータ。

- [ ] **Step 1: RED** — 初期表示は現在の `MetricRegistry.All` と完全同値、研究だけ表示、ライブラリ内の未公開依存Rankを誤表示しない、DLC対象外は対象外、同セッション内で表示設定切替後に古いビューを返さない、旧指標を消さない。
- [ ] **Step 2: Confirm RED** — 新規テストを失敗させる。
- [ ] **Step 3: Implement** — `MetricRegistry.Catalog` の個別定義を選択、依存派生は入力条件を満たすときのみ算出、`MetricCalculator.BuildView(...,definitions)` を再利用、UIの選択・再計算キャッシュを適切に更新。
- [ ] **Step 4: GREEN** — `ExperimentalDisplayTests` と `NumericRankingDependencyTests` 全件成功、除外コロニー同値。
- [ ] **Step 5: Commit** — `feat(experimental): show opted-in metrics without stale views`。

### Task 6: Visible experimental status, diagnostics and translations

**Files:** Modify `UI/DetailsTab.cs`, `RimworldStatisticsMod.cs`, `Languages/Japanese/Keyed/RimworldStatistics.xml`, English counterpart, `docs/manual-test-checklist.md`。

**Interfaces:** 行には `MetricEvidence` をもとに「実験的」「混在」「来歴不明」を表示。`MetricCoverage`（Complete,Partial,SnapshotOnly等）は別欄/ツールチップで保持する。

- [ ] **Step 1: RED** — 表示OFFは実験行なし、表示ONでは実験バッジ必須、対象DLCなしは「不可/対象外」、今は取得していないが保存済みの値は表示できる、日英キー全件存在。
- [ ] **Step 2: Confirm RED** — 翻訳キー検査または表示用投影テストの失敗を確認。
- [ ] **Step 3: Implement** — カテゴリリストを実際の表示定義から生成し、説明・警告・再起動の文面を追加。表示名が見つからなくても安定IDを表示。
- [ ] **Step 4: GREEN** — `bash tools/check_keys.sh` とCoreの投影テストを成功させる。
- [ ] **Step 5: Commit** — `feat(experimental): label experimental data and explain coverage`。

### Task 7: End-to-end validation and release gate

**Files:** Modify `tools/VerifyHarness/README.md`, `docs/manual-test-checklist.md`; No production code unless an actual regression is found.

- [ ] **Step 1: RED/acceptance** — 旧版MOD設定移行、OFF/ON起動、Royalty/Biotech/DLC欠落、ロード順変更、セーブ失敗、同名別コロニー、RimSaves、別セーブ枝、数値表示の欠損、実験モードの負荷の受入手順と期待値を文書化。
- [ ] **Step 2: Verify executable tests** — `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release`、`bash tools/refcheck.sh && bash tools/check_patches.sh && bash tools/check_keys.sh`、`bash tools/package.sh /tmp/RimworldStatistics-experimental.zip`、`unzip -t /tmp/RimworldStatistics-experimental.zip`。
- [ ] **Step 3: Verify packaging** — 本体ZIPにHarness/ゲームDLLを含めない。既存の標準OFFは旧版と等価。実験OFF時のTPS比較を含む既存受入予算（2%、B保存50msまたは5%）を実機で評価し、超過は対象取得単位の警告/無効化を検討。
- [ ] **Step 4: Review and commit** — 設計差分・セーブ互換・来歴・失敗モードの全レビューを行い、必要な追加テストを実装して再検査。最後に手順書のコミット。
- [ ] **Step 5: Create separate PR** — P0のみでPRを作成。ユーザーが承認するまでマージ・リリースをしない。

## Notes for the eventual implementer

- 静的 `MetricRegistry.All` を書き換えるだけでは、`ProfileSession` のキャッシュ（現在は `IncludeDevMode` のboolのみ）と `DetailsTab.DrawControls` のカテゴリ表示が追従しない。Task 5が必須。
- `MetricAdapters` はゲーム起動時の `[StaticConstructorOnStartup]` 。設定UIのその場反映を契約にしない。
- 古い `metricObservations` の `Quarantined` データやチェックポイントの `ExtensionData` を保護し、変更に伴う読み直しで損失を起こさないこと。
- UIの実験モードを「検証に合格した証拠」として解釈しない。

**Handoff:** 本計画は設計承認後に実装できるよう作成した。現時点では製品コードに手を触れない。
