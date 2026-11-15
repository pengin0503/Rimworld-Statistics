# 共通統計契約テスト基盤（Task C, 2026-11-15）

## 1. テストの目的

`Tests/RimworldStatistics.Core.Tests/Common/` は、組み込み統計および追加MOD統計（統合アダプター・外部SDKプロバイダー）が共通して満たすべきライフサイクル契約を、特定のMODファイルやゲーム本体に依存せずに検証する共通自動テスト基盤である。

従来、追加MODを1件実装するたびに「登録・MOD依存判定・イベント記録・セーブ/ロード・再インポート・コロニー別/全体集計・性能劣化」を個別の手書きテストで検証しようとすると重複が大きくなり、逆にルール単体テストだけでは **Core パイプライン全体を通した不具合（例外隔離漏れ、リビジョン不一致による非表示など）** を取りこぼすおそれがあった。

本基盤は以下の役割分担でこれを解決する。

- **共通契約テスト（`Common/`）**: 登録、例外隔離、リビジョン整合、チェックポイント/スナップショットの保存・復元・系統分岐・再インポート冪等性、全体集計、メモリ割当・計算量を一括で保証する。
- **実DLL契約テスト（`IntegratedAdapterContractTests`）**: ビルド済み `RimworldStatistics.dll` とゲーム参照DLL（`Assembly-CSharp.dll` / `UnityStubs.dll` / `0Harmony.dll`）を用い、実際に登録されている統合アダプター全件の記述子・ファミリー・空状態ビルド・未登録漏れを自動検査する（任意実行）。
- **MOD固有ルールテスト（`*RulesTests` 等）**: 各MODの「成功判定」「非プレイヤー除外」「実数量算出」「固有の境界条件」を `ConditionMatrix` 等で検証する。

## 2. 構成と各テストの役割

共通基盤は `Tests/RimworldStatistics.Core.Tests/Common/` 配下の1つのテストキットと6つの契約テストクラスで構成される。

| ファイル | 対象カテゴリ (`Trait`) | 主な検証内容 |
| --- | --- | --- |
| `StatisticsTestKit.cs` | — | 本番API（`ModMetricDescriptor` / `LiveFamily` / `LiveMetricAccumulator` / `CheckpointMetrics` / `ProfileMutations` / `CampaignMatcher` / `MetricRegistry` / `MetricCalculator`）をラップする `TestShapes`・`TestColony`・`GameSession`・`TestProfile`・`ProfileView`・`ConditionMatrix`・`Allocations` を提供。 |
| `StatisticRegistrationContractTests.cs` | `Contract` | 全 `MetricObservationSource × ColonySelection × HasBreakdown` 形状の登録と可視性制御、MOD削除後の記録保持、`RequiredPackages`（大小文字無視・semver範囲・バージョン欠損のフェイルクローズ）、`RequiredDefNames`、キー衝突・組み込みキー保護、ファミリー名重複拒否、`ModProviderExecutionGuard` / `DeferredHookRegistry` / `CheckpointMetrics.ToObservation` における例外隔離。 |
| `EventRecordingContractTests.cs` | `Contract` | 0/負数非計上・計測済み0の `Complete` 扱い、任意リビジョン（v1/v2）での表示、`OptionalTransitionRecording` の独立成功イベント非統合、`ExternalSdkMetricPolicy` による実IDベース重複排除と一方向ダイジェスト化、`Track`/`Untrack` による未完了ジョブ非計上とギャップ後破棄、セーブ/ロード/再インポート時の非加算、`ConditionMatrix` の誤り検出。 |
| `SaveLineageContractTests.cs` | `Contract` | 親子・兄弟分岐・巻き戻しで最新セーブのみを表示し合算しないこと、親欠損でも保持済みチェックポイント値を失わないこと、同一セーブの別名コピーの重複排除、MOD一時削除〜再導入時の値保持と `Partial` 化、DLC無効コロニーの `NotApplicable` 除外、未知ファミリー/未知トップレベルJSONの往復保存、旧プロフィール（`schemaVersion: 1`、数値記録なし）の互換性。 |
| `AggregationContractTests.cs` | `Contract` | `OverallAggregation`（`Sum` / `Max` / `Min`）× `Checkpoint` / `SaveSnapshot` の全体集計、`Unknown` コロニーを0扱いしないこと、除外コロニーの母数除外、1コロニーでも `Partial` なら全体が `Partial` になること、`Representative` / `ObservationMax` / `ObservationMin` の未計測セーブ時の扱い。 |
| `PerformanceContractTests.cs` | `Contract`, `Performance` | 既存カウンターへの10万回記録でスレッド割当0バイト、100万回記録してもチェックポイントJSONサイズが増えないこと、統計数（50→200）およびセーブ数（250→1000）に対する線形スケーリング（スレッド単位メモリ割当比）、400セーブ反復インポート時のプロフィール肥大化上限と再インポート時バイト一致。 |
| `IntegratedAdapterContractTests.cs` | `Contract`, `OptionalGameDll` | ゲームDLL存在時にのみ実行する任意テスト。`ModProviderAdapters.BuildAllAdapters` と `IntegratedTransitionAdapters.Build` からIL走査で到達可能な統合アダプターを列挙し、記述子妥当性・重複なし・組み込みファミリー非衝突・MOD未導入時フェイルクローズ・空状態 `Build` でのリビジョン一致と例外なし・未登録の静的アダプターメソッド検出を検査。 |

### 本基盤の整備で修正されたCore不具合

共通契約テストの作成・実行により発見され、合わせて修正したCore不具合は以下の2件である。

1. **`Source/RimworldStatistics.Core/Stats/LiveFamilies.cs` (`CheckpointMetrics.ToObservation`)**
   - 従来は `ArithmeticException` 等5種類の例外のみを捕捉していたため、`IndexOutOfRangeException`・`InvalidCastException`・`FormatException`・`NotSupportedException` 等を投げたファミリーがあるとチェックポイント観測全体が失われていた。
   - `catch (Exception e) when (e is not OutOfMemoryException)` に改め、壊れたファミリーの出力のみを落として他のファミリーと元のチェックポイントJSONを保護するようになった。
2. **`Source/RimworldStatistics.Core/Stats/OptionalTransitionRecording.cs` (`OptionalTransitionRecording.PutCount`)**
   - 従来は `MetricKeys.Revision(metricKey)`（組み込みキー辞書にないMODキーには常に `1` を返す）が使われていたため、`Revision >= 2` の追加MODファミリーが記録した値が `MetricCalculator` で不一致となり一切表示されなかった。
   - `output.Into[metricKey].Revision = output.State.Revision;` を明示設定し、改訂済みMODファミリーの値が正しく表示されるようになった（既存の組み込み呼び出し元 `HealthDeathFamily` / `MembershipFamily` は `Revision == 1` のため既存プロフィール・組み込み統計と完全互換）。

## 3. 追加MOD開発時の使い方

新しい統合MOD（`Source/RimworldStatistics/IntegratedMods/<ModName>/`）や外部SDK連携を追加する際は、次の手順で検証する。

### 3.1 共通契約テスト（ゲームDLL不要・CI自動実行）

追加MODの統計定義やパイプライン自体は `Common/` の契約テストで共通仕様が守られている前提とし、通常の Core テストをそのまま実行する。

```bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
```

### 3.2 各MOD固有のルールテスト（必須）

共通テストは **「渡された値が成功・プレイヤー所有・実数量であるか」** までは判定できない。追加MODごとにゲーム非依存の純粋ロジック（`IntegratedMods/<ModName>/Logic/*.cs`）を切り出し、固有テストで以下を検証する。

1. **条件組合せ網羅（`ConditionMatrix` の活用）**
   - 例: 「処理が成功した」「プレイヤー陣営である」「対象状態が実際に変化した」「重複処理ではない」「数量が正である」など、判定に使う真偽条件の組を `ConditionMatrix.AssertOnlyWhenAll` / `ConditionMatrix.AssertAgrees` で全組合せ検証し、非プレイヤー混入や失敗時誤計上を防ぐ。
2. **一意性と実数量の境界**
   - 一意IDで重複排除してよいイベントか、独立した成功を別々に数えるべきかを明示的にテストする。
   - スタック数や消費量など「1回のイベントで複数増減する数量」や、NaN/無限大/0以下の無効値拒否を検証する。
3. **既存アダプターの規約遵守**
   - `IntegratedTransitionAdapters.Build()`（またはスナップショットなら `ModProviderAdapters.BuildAllAdapters()`）へ必ず登録する。
   - `ModMetricDescriptor` の `OwnerPackageId` / `RequiredPackages` / `RequiredDefNames` / `RequiredDlc` / `LabelKey` / `DescriptionKey` を設定し、`bash tools/check_keys.sh` を通す。

## 4. 既存テストとの対応

`Tests/RimworldStatistics.Core.Tests/` の既存テストは特定の機能・回帰・実セーブ検証を担っており、`Common/` の契約テストはそれらを置き換えるものではなく、**追加MOD共通仕様の横断的な回帰防止層** として補完関係にある（既存テストは削除・無効化しない）。

| 共通契約テスト (`Common/`) | 対応・補完関係にある既存テスト | 役割の違い・補完点 |
| --- | --- | --- |
| `StatisticRegistrationContractTests` | `ModMetricProviderRegistryTests`、`ModMetricPersistenceTests`、`ExternalSdkMetricTests` | 既存テストはレジストリ単体・SDK登録・永続化を個別検証。契約テストは `2 × 4 × 2` 全形状を通しで流し、MOD削除後の記憶保持・組み込み `"mod.*"` キー予約保護・全種例外隔離を一括保証。 |
| `EventRecordingContractTests` | `IntegratedOptionalTransitionTests`、`ExternalSdkMetricTests`、`NumericCheckpointTests` | 既存テストは `OptionalTransitionRecording` の4,097件上限回避やSDK重複排除を個別に確認。契約テストは `GameSession` → `TestProfile` を通し、`Revision=2` の表示や再インポート冪等性、`Track`/`Untrack` と `MarkGap` の連携を共通契約化。 |
| `SaveLineageContractTests` | `MetricChartLineageTests`、`NumericCheckpointTests`、`ModMetricPersistenceTests`、`PostV14DlcCompatibilityTests` | 既存テストは組み込み指標で系統グラフやDLC非該当、圧縮・未知JSONを検証。契約テストは同じ不変条件を「追加MOD統計のライフサイクル」として通し検証。 |
| `AggregationContractTests` | `NumericAggregationTests`、`CrossColonyAggregationTests`、`IndexedBreakdownAggregationTests` | 既存テストは組み込み指標や派生順位表のクロスコロニー集計を検証。契約テストは `ModMetricDescriptor` 定義から合成された追加MOD指標での `Sum`/`Max`/`Min`・`Representative`/`ObservationMax`/`ObservationMin`・`Partial`/`Unknown` 伝播を網羅。 |
| `PerformanceContractTests` | `ProfileWriteCostTests`、各種 `tools/*Benchmark` | 既存テスト・ベンチマークはプロフィール書込バッファ再利用や実フォルダ速度を計測。契約テストはイベント記録の0バイト割当、チェックポイントJSONサイズ定数性、統計数・セーブ数に対するスレッド割当の線形性をCI内で自動監視。 |
| `IntegratedAdapterContractTests` | `IntegratedMods/*Tests`、`NewModOutcomeTests`、`PopularOutcomeTests`、`RaceXenotypeModStatisticsTests` 等の個別MODテスト | 個別MODテストはルール判定（`Logic/*.cs`）の正しさを検証。`IntegratedAdapterContractTests` は実ゲームDLLとビルド済み `RimworldStatistics.dll` のIL走査により、**全統合アダプターの登録漏れ・記述子不整合・空ビルド破損** を横断検査。 |

## 5. 実DLLテスト（`IntegratedAdapterContractTests`）の実行方法

`IntegratedAdapterContractTests` はゲーム本体DLL（`Assembly-CSharp.dll`, `UnityStubs.dll`）と Harmony（`0Harmony.dll`）を必要とするため、これらを持たない通常のCI（`.github/workflows/numeric-verification.yml`）では `Assert.Skip` により自動スキップされる。ローカルや検証環境で実行する手順は以下の通り。

### 5.1 ゲーム参照DLLの取得と本体ビルド

Private R2 バケットの認証情報（環境変数）が設定された環境で参照DLLを取得し、ゲーム側DLLをビルドする（`.refs/` および `1.7/Assemblies/` は `.gitignore` 対象）。

```bash
python3 tools/fetch_dev_inputs.py refs .refs
dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release --nologo
```

※ `.refs` の代わりに、ローカルにインストール済みの RimWorld 1.7 `Managed` フォルダと Harmony MOD の `0Harmony.dll` を直接指定することもできる。

### 5.2 実DLLテストの実行

環境変数 `RWS_GAME_MANAGED_DIR` と `RWS_HARMONY_DLL` を指定して実行する（相対パスはカレントディレクトリまたはリポジトリルート基準で解決される）。別パスのDLLを検査したい場合は `RWS_STATISTICS_DLL` で上書き可能。

```bash
RWS_GAME_MANAGED_DIR=.refs/game \
RWS_HARMONY_DLL=.refs/harmony/0Harmony.dll \
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj \
  -c Release -p:UseNuGetRefs=true --no-build \
  --filter-class RimworldStatistics.Core.Tests.Common.IntegratedAdapterContractTests
```

実行時に検査される内容:

- `ModProviderAdapters.BuildAllAdapters` および `IntegratedTransitionAdapters.Build` からIL走査（補助メソッドやコンパイラ生成ステートマシンを含む）で到達可能な全モジュールを実行し、記述子・ユニット・リビジョン・DLC一致・空状態 `Family.Build` を確認する。
- アセンブリ内の `RimworldStatistics.*` 名前空間にある静的・引数なしのアダプター宣言メソッドが、どこからも呼ばれないまま放置されていないか（未登録漏れ）を検出する。
- 安全のため、`Allowed` に列挙されたアセンブリ（`Assembly-CSharp`, `UnityStubs`, `Waylith`, `0Harmony`, `RimworldStatistics*`）以外の読み込みは拒否し、ゲームのスタートアップ処理や設定フォルダアクセスは実行しない。

### 5.3 CIでの扱い

`.github/workflows/numeric-verification.yml` の `Core unit tests (no game launch)` ステップは `Tests/RimworldStatistics.Core.Tests` 全体を実行するため、`IntegratedAdapterContractTests` を除くすべての共通契約テスト（`StatisticRegistrationContractTests`・`EventRecordingContractTests`・`SaveLineageContractTests`・`AggregationContractTests`・`PerformanceContractTests`）が PR・push ごとに自動実行される。`IntegratedAdapterContractTests` はゲームDLL未配置のためCI上ではスキップされる。

## 6. 変異テスト（Mutation Testing）による検出力の確認

本契約テストが実際の不具合を検出できることを確認するため、以下の3種類の意図的な不具合（ミュータント）を注入してテスト失敗を確認した（確認後はすべて元に戻している）。

1. **`LiveFamilies.cs` 例外隔離の狭小化**: `when (e is not OutOfMemoryException)` を元の5例外限定に戻すと、`StatisticRegistrationContractTests.AFamilyThatFailsToBuildItsValuesDropsOnlyItsOwnValues` が `IndexOutOfRangeException` / `InvalidCastException` / `FormatException` / `NotSupportedException` の4ケースで失敗し検出した。
2. **`OptionalTransitionRecording.cs` リビジョン設定の欠落**: `output.Into[metricKey].Revision = output.State.Revision;` を除去すると、`EventRecordingContractTests.TheStatisticOfEveryRevisionIsShownFromItsOwnFamily` が `revision = 2` の2ケースで失敗し検出した。
3. **非 `Build` 名アダプターメソッドの登録漏れ**: `IntegratedTransitionAdapters.Build()` から `WorldCampaignOutcomes.RimWarVictory()` の呼び出しを削除して実DLLテストを実行すると、`IntegratedAdapterContractTests.EveryDeclaredOptionalStatisticFollowsTheRegistrationContract` が未登録モジュール `RimworldStatistics.WorldCampaigns.WorldCampaignOutcomes.RimWarVictory` を検出して失敗した。

## 7. 既知の制限・未検証境界

- **MOD固有ロジックの正しさは非対象**: 「イベントが本当に成功したか」「非プレイヤーを除外しているか」「取得した数量が正しいか」は各MODのフック実装と `Logic/*.cs` に依存し、共通契約テストだけでは保証されない。必ず各MOD側の固有テストで検証すること。
- **`PopularOutcomes.Build` はゲーム外で未検証**: `PopularOutcomes.Build()` は列挙中に `ModsConfig.OdysseyActive` を参照するため、ゲーム外の単体テストプロセスでは型初期化例外（`TypeInitializationException`）で中断する。`IntegratedAdapterContractTests` はこれを `notChecked (needs the running game)` として診断メッセージに明示し、検証済みとは扱わない（1件目の `LandformOutcomes.Build` より後の項目は実ゲーム環境での確認が必要）。
- **実際のパッチ適用・実行時動作は別枠**: `IntegratedAdapterContractTests` は宣言メタデータと空状態の `Family.Build` のみを検査し、Harmony detour の適用可否、対象MOD実DLLとの型・署名一致、実ゲームプレイ時のセーブ往復・UI表示・TPS負荷は検証しない。これらは各MODのIL契約監査ツール（`tools/IntegratedModAudit/`）および実ゲーム検証環境（`RimWorld-for-mod-test` / `tools/VerifyHarness`）で別途確認する。
