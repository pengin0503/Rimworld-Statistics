# Mod-specific Statistics Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **状態:** 将来の実装候補の計画（未承認・未実装）。最初に実施するのは前段 `2026-11-08-experimental-statistics.md` のP0のみ。**P1〜P3を一括実装しない。**

**Goal:** Rimworld-Statisticsで、他MOD独自の出来事や保存時状態を互換アダプターから数値記録できる拡張基盤を構築する。互換MOD未導入でも本体が動き、他MOD更新や削除で記録済み統計を失わない。

**Architecture:** 先にCoreへ動的・検証可能なprovider定義レジストリを作り、ゲーム側に条件付きロード・失敗隔離レイヤーを追加する。P2で1つの実MODを対象に取得方法を実証してから、P3でのみ外部開発者向けAPIを設計・公開する。共通の`MetricObservation`, `CheckpointPayload.Families`, `MetricCalculator`を再利用する。

**Tech Stack:** C# / .NET10, RimWorld 1.7, Harmony, System.Text.Json, xUnit, GitHub Actions, optional mod API (対象調査後に決定)。

**Spec:** `docs/superpowers/specs/2026-11-08-experimental-statistics-and-mod-adapters-design.md` §§4–5。

## Global Constraints

- **依存順:** 実験的取得P0が安定してから本計画P1に着手。P1/2/3はそれぞれ別PR・別判定であり、先の段階を飛ばさない。
- 「他MODがインストールされている」だけでは自動取得しない。provider自体が存在して適合・ユーザーが明示選択した場合のみ記録。未確認の取得を `Verified` と偽らない。
- 既存ユーザーの `Profile`、コロニーの一意照合、除外、セーブ枝、分岐/巻き戻し、部分/不明/対象外、古いJSONの保全を変更しない。
- 新統計IDは `mod.<normalized-package-id>.<family>.<metric>` で名前空間を分ける。既存キーとの衝突不可、リビジョンを固定し意味を変更したらrevisionを上げる。
- MOD未導入や対象MODの更新により今のAPIが欠けているときは、そのprovider/単位のみ停止。**存在しない計測を0で埋めない**。
- CoreでRimWorld.dllや他MOD.dllへの直接参照を追加しない。ゲーム側の任意依存は別モジュール/遅延検出・適合確認を用いる。
- 基本UIは数字の一覧のみ。レコードは集計済みの最小情報、フルイベントログ/名前/個人データを保存しない。無期限マップ走査/毎tickの全Def解析はしない。
- 実MOD/外部SDK固有の公開APIや互換性は、対象リポジトリのコードを監査するまで存在を断定しない。

## File Structure / Responsibilities (planned)

| File | Responsibility |
|---|---|
| Create: `Source/RimworldStatistics.Core/Stats/ModMetricProviderRegistry.cs` | 安定ID・provider/metricを重複なく登録し動的カタログを構成 |
| Create: `Source/RimworldStatistics.Core/Stats/ModMetricDescriptor.cs` | providerId、必須Package ID/版、修正番号、入力ソース、出力の契約 |
| Modify: `Source/RimworldStatistics.Core/Stats/MetricRegistry.cs` | 既存静的カタログとの無損失合成、キールックアップ |
| Modify: `Source/RimworldStatistics.Core/Stats/MetricCalculator.cs` | 動的カタログの既存代表/全体集計経路を使用 |
| Create: `Source/RimworldStatistics/Metrics/ModProviderDiscovery.cs` | 起動時のMOD存在/バージョン検出と適合判定 |
| Create: `Source/RimworldStatistics/Metrics/ModProviderAdapters.cs` | 接続時のHarmony/SDKイベント接続・例外隔離 |
| Modify: `Source/RimworldStatistics/Metrics/MetricGates.cs`, `MetricAdapters.cs` | 既存の選択ルールを外部providerにも共通適用 |
| Modify: `Source/RimworldStatistics.Core/Model/MetricObservation.cs`, `MetricCheckpoint.cs` | providerId・対象MOD情報の追跡に必要な、互換的な任意フィールド |
| Modify: `Source/RimworldStatistics/RimworldStatisticsMod.cs`, `UI/DetailsTab.cs` | 他MOD単位の有効/無効・利用不可・来歴・再起動表示 |
| Create: `Tests/RimworldStatistics.Core.Tests/ModMetricProviderRegistryTests.cs`, `ModMetricPersistenceTests.cs` | 未導入、衝突、欠測、再導入、同一イベント重複・データ保護 |
| Create after P2: `Source/RimworldStatistics/Metrics/Providers/<ChosenMod>*.cs` | 調査済みの具体的MODの任意アダプター |
| Create after P3 approval: `Source/RimworldStatistics.Api/` and `examples/ModMetricProvider/` | 別アセンブリからの安定APIとサンプル（未確定） |

## Review Focus

1. 対象MOD削除後も過去観測は `Quarantined` で消去せず、再インストール時には同じrevisionで再開・空白期間はPartial → P1 Task 4。
2. 異なるproviderのキー衝突・同じproviderの登録順序が逆: 既存定義を上書きしない・安定して拒否 → P1 Task 1。
3. あるMODのフックのみ例外: バニラ統計・別provider・保存成功を損なわない → P1 Task 3。
4. バニラと互換MODの両方が同じ出来事を通知: 1件だけ計上し、曖昧な場合はPartial/Unknown → P2 Task 2。
5. 外部providerの新しいAPIバージョン・未翻訳Def・未知JSON: クラッシュせずIDのフォールバック、読み書き後もデータ保全 → P1 Task 2,4 / P3 Task 1。

---

## P1 — 内部拡張レジストリ（単独PR）

### Task 1: Validate a deterministic provider catalog

**Files:** Create `Stats/ModMetricDescriptor.cs`, `Stats/ModMetricProviderRegistry.cs`, `Tests/ModMetricProviderRegistryTests.cs`。

**Interfaces:** `ModMetricDescriptor(ProviderId, OwnerPackageId, FamilyId, MetricKey, Revision, Kind, RequiredPackageIds, RequiredDlcIds, Definition)`。 `ModMetricProviderRegistry.Register(descriptor): RegistrationResult`、`BuildCatalog(): IReadOnlyList<MetricDefinition>`。曖昧さのない最終の型定義はテストより前に確定。

- [ ] **Step 1: RED** — 安定IDの正規化、余分なスペース/不正文字、同じキー同じrevisionの二重登録、異なる定義で同一キー、Caseの違い、既存バニラキーとの衝突を拒否。登録順序が変わっても同じ並び。
- [ ] **Step 2: Confirm RED** — `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --filter FullyQualifiedName~ModMetricProviderRegistryTests` が失敗することを確認。
- [ ] **Step 3: Implement** — 重複防止・安定順、nullableな依存MOD情報、動的カタログ生成。未登録・未知の指標のJSONは保持のみ。
- [ ] **Step 4: GREEN** — RegistryTestsを成功させ、旧`NumericCatalogTests` を維持。
- [ ] **Step 5: Commit** — `feat(providers): validate and register namespaced metric definitions`。

### Task 2: Compose dynamic definitions into existing UI/aggregation

**Files:** Modify `Stats/MetricRegistry.cs`, `Stats/MetricCalculator.cs`, `Profiles/ProfileSession.cs`, `UI/DetailsTab.cs`; Test `ModMetricProviderRegistryTests.cs` / `ExperimentalDisplayTests.cs`。

**Interfaces:** `MetricRegistry` の静的な既存公開リストを壊さず、`BuildEffectiveCatalog(registered, visible)` 相当で生成する読み取り専用リスト。定義集合のfingerprintを `ProfileSession` キャッシュキーに追加。

- [ ] **Step 1: RED** — 旧統計の順序/数値が同値、1 providerの独自指標だけ追加、同名でも別familyで衝突しない、Provider消失時は記録を消さずUIは利用不可を表示、翻訳が無い場合は安定キーを表示。
- [ ] **Step 2: Confirm RED** — 新規テストが失敗することを確認。
- [ ] **Step 3: Implement** — `MetricCalculator.BuildView(...,definitions)` を共通利用。カタログは登録時に確定、毎フレーム新規構築しない。
- [ ] **Step 4: GREEN** — 既存全Metric/Rankingテスト、キャッシュ切替テスト、日英キー検査を成功させる。
- [ ] **Step 5: Commit** — `feat(providers): extend shared numeric UI without a second aggregator`。

### Task 3: Optional-mod detection and isolated adapter lifecycle

**Files:** Create `Metrics/ModProviderDiscovery.cs`, `Metrics/ModProviderAdapters.cs`; Modify `MetricGates.cs`, `MetricAdapters.cs`; Test `ModMetricProviderRegistryTests.cs` + `tools/VerifyHarness` の静的検証。

**Interfaces:** `ModProviderDiscovery.Resolve(activeModPackages, providerDescriptors): ProviderAvailability`（Core互換の入力形にする）、Game side `ModProviderAdapters.InstallEligible(...)`。Harmonyの実行は起動時1回のみ。

- [ ] **Step 1: RED** — MOD不在、DLCなし、名前の大文字小文字、版不適合、必須Defなし、パッチ適用失敗、2 providers独立、再起動前設定変更では実行中アダプターが不変。
- [ ] **Step 2: Confirm RED** — 新テストに失敗がある状態を記録。
- [ ] **Step 3: Implement** — `LoadedModManager` からPackage ID等を取得し、静的記述と照合。対象MODの型へ無条件にリンクしない。定義/メソッドの存在を検査してからのみパッチ/イベント登録。該当単位が失敗した場合はfail-closedで隔離。
- [ ] **Step 4: GREEN** — Core全件・`bash tools/refcheck.sh && bash tools/check_patches.sh`。本体は対象MOD不在でビルド・起動できる。
- [ ] **Step 5: Commit** — `feat(providers): conditionally activate optional-mod adapters`。

### Task 4: Preserve provider provenance through save, load and removal

**Files:** Modify `Model/MetricObservation.cs`, `Model/MetricCheckpoint.cs`, `Stats/LiveMetricAccumulator.cs`, `Profiles/ProfileMutations.cs`; Test `ModMetricPersistenceTests.cs`, `NumericSnapshotRetentionTests.cs`, `ModelJsonTests.cs`。

**Interfaces:** `ModMetricDescriptor.ProviderId`、`OwnerPackageId`、計測revisionおよびP0の `MetricEvidence` を保存上で追跡可能にする。未知のproviderが存在しても読み書きは非破壊。

- [ ] **Step 1: RED** — セーブ1→MOD削除→セーブ2→MOD復帰→セーブ3、再インポート同一チェックポイントの冪等、親P→分岐A/B、revision変更、同定不能・未知フィールド保全、DLC不在で0を追加しない、別コロニー合算なし。
- [ ] **Step 2: Confirm RED** — 新規PersistenceTestsが失敗することを確認。
- [ ] **Step 3: Implement** — 既存のFamilyStateと観測の任意メタデータを拡張し、欠落期間の`Partial`判定とProvider不在の履歴保持。古いバージョンのJSONを読み書きするとき未知フィールドを落とさない。
- [ ] **Step 4: GREEN** — 全Coreテスト成功、SaveSnapshot圧縮や観測件数に差異がないことを確認。
- [ ] **Step 5: Commit** — `feat(providers): persist provider metadata without losing old observations`。

### Task 5: P1 integration and performance gate

**Files:** Modify `docs/manual-test-checklist.md`、`tools/VerifyHarness/README.md`。ダミーproviderをテストfixture内に用意（製品ZIPには含めない）。

- [ ] **Step 1: RED/acceptance** — provider無し/複数/更新/重複の統合手順、DLCと設定の分離、1イベントだけの確定記録、実験的ラベル、保存・再取込を検証する期待値を明文化。
- [ ] **Step 2: Implement minimal test fixture** — ダミーproviderでGame型不要のCore契約のみをテストし、実MOD参照を追加しない。
- [ ] **Step 3: GREEN** — `dotnet test ... -c Release`、`bash tools/refcheck.sh && bash tools/check_patches.sh && bash tools/check_keys.sh`、配布ZIPに不要DLLがないことを確認。
- [ ] **Step 4: Review / Commit** — 実データを破壊しないかレビュー、性能受入（2% TPS目標・保存追加時間50msまたは5%）を計測。P1単独PRで停止し、P2へ進む前にレビューを受ける。

---

## P2 — 実際の他MOD1件で実証（別PR）

### Task 1: Select and audit one real target MOD

**Files:** Create `docs/research/<DATE>-mod-provider-pilot-audit.md`。

- [ ] **Step 1: Candidate survey** — 少なくとも3候補について、実コードの入手性/ライセンス・Package ID/対象MOD版・独自性・利用価値・API安定性・重複の危険・取得頻度・負荷を比較。既存のユーザー製MODでも、公開APIと利用シナリオを同様に監査。
- [ ] **Step 2: Select** — 価値の高い1 MODと最初の取得2項目を決定（成功イベント1種、保存時スナップショット1種）。対象MODの実際のDef/コールバックの定義が確認できなければ取得実装を開始しない。
- [ ] **Step 3: Evidence** — 1.7対応リビジョン・メソッドと二重通知/キャンセル経路の証拠、失敗条件、対応範囲を調査文書に保存・コミット。

### Task 2: Implement one optional, low-overhead adapter

**Files:** Create `Source/RimworldStatistics/Metrics/Providers/<ChosenMod>MetricProvider.cs`（確定名はTask 1の調査時に決定）、`Tests/.../<ChosenMod>ProviderTests.cs`。

- [ ] **Step 1: RED** — MODあり/なし・版不適合・正常イベント1回・キャンセル0回・同事象二重通知1回のみ・セーブ失敗は未確定・DLC欠落、取得対象外のDefでは記録しない。
- [ ] **Step 2: Confirm RED** — Coreの模擬コールバックによる期待値が失敗することを確認。
- [ ] **Step 3: Implement** — 実証済みの最小API/パッチだけ使い、対象MOD未導入時は静的初期化が対象型をロードしない。オプトイン/ProviderGateも必要。EventのカウントとSaveSnapshotの解釈はP1共通基盤。
- [ ] **Step 4: GREEN** — CI、静的API照合、実MOD経路の正負試験と受入性能比較を行う。実機不可なら根拠を限定したままExperimentalとして公開するかユーザーに判断を委ねる。
- [ ] **Step 5: Commit/PR** — 対象MODの互換性・取得数・注意点をREADMEに明記し、P2単独PRへ。P1とは切り離して判断。

---

## P3 — 外部開発者向けSDK（P2成功後、必要性を再評価）

### Task 1: Stable, versioned registration contract

**Files:** Create `Source/RimworldStatistics.Api/`、`examples/ModMetricProvider/`、`docs/mod-provider-api.md`（予定パス）。

**Interfaces:** 実装後に `IRwsMetricProvider`、`IMetricRegistrationContext`、`IEventRecorder` 相当の最小APIを**P2の実証から設計**。Coreにある`MetricDescriptor`のゲーム非依存フィールドを使い、未検証APIの種類をむやみに増やさない。外部DLLの参照経路と互換版を明示する。

- [ ] **Step 1: RED** — サンプルMODを別プロジェクトでビルドし、合致したAPI版だけ登録・異なる版なら失敗隔離、互換エラーで古いJSON消失なしを試験。
- [ ] **Step 2: Confirm RED** — APIがない状態で独立サンプルのビルド/契約テストが失敗することを確認。
- [ ] **Step 3: Implement** — サンプルで必要となった最小APIとメジャー/マイナーの互換規則、登録タイミング、名前空間、例外/負荷の取り扱い、翻訳のフォールバックを固定。
- [ ] **Step 4: GREEN** — 本体とサンプルの異なるリリース組合せ、旧APIのデータ、MOD無し、後方互換をCIで確認。
- [ ] **Step 5: Review/PR** — API変更管理・互換性の維持コストをレビューし、P3単独PRとして提案。ユーザーがSDK公開を承認するまで製品の必須機能にしない。

## Scope cut / Done definition

- **P0完了:** 未検証の既存29取得単位をユーザーが選択でき、既存の正式な統計に影響しない。
- **P1完了:** ダミーproviderが既存統計エンジン・保存に統合され、MOD不在や衝突でデータを失わない。
- **P2完了:** 監査済みの実MODに対して2つの独自指標が取得でき、二重計上しない。
- **P3完了:** 別のサンプルMODが公開済みのSDKを使い、異なるバージョンの互換をCIで確認できる。

**Handoff:** 対象MODを選ぶのはP2実施時。今は他MOD固有の製品コードを追加しない。
