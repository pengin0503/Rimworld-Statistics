# リポジトリ全体コードレビュー記録（2026-11-17）

- レビュー日：2026-11-17
- 対象：`pengin0503/Rimworld-Statistics`（`main` コミット `919d2b7fdf233c13bab5a1ed4686683cc417ce84`、PR `#355`〜`#361` マージ後）
- 対象範囲：
  - コア演算・永続化・CSV/HTML出力・外部SDK（`Source/RimworldStatistics.Core/**`、`Source/RimworldStatistics.Api/**`）
  - Harmonyパッチ・実行時統計収集・MOD連携（`Source/RimworldStatistics/Patches/**`、`Source/RimworldStatistics/Metrics/**`、`Source/RimworldStatistics/IntegratedMods/**`、`GameComponent_RimworldStatistics.cs`、`ProfileService.cs`、`LiveStateReader.cs`、`SavedStateMetricReader.cs`）
  - ゲーム内IMGUI UI・設定画面・V2-B/V2-Cダイアログ（`Source/RimworldStatistics/UI/**`、`RimworldStatisticsMod.cs`、`RimworldStatisticsSettings.cs`）
  - 12言語ローカライズ・テスト・パッケージ検証・CI（`Languages/**`、`Tests/**`、`tools/**`、`.github/workflows/**`、`.gitignore`）
- 状態：**読み取り専用の全体コードレビュー記録のみ。プロダクションコードの修正、実ゲーム検証の本実行、GitHub Release / Steam Workshop の更新は行っていない。**

---

## 1. エグゼクティブサマリー・未解決Issue対応状況

`main`（`919d2b7`）時点でオープンとなっているGitHub Issue（`#304`、`#357`、`#358`）および、直近マージされたV2-B（`#355` セーブ系統可視化）・12言語翻訳監査（`#359`）・V2-C（`#360` コロニー総括レポート・静的HTML出力）・CI軽量化（`#361`）を含む全レイヤーを精査した。

### オープンIssueとの対応関係

| Issue | 現在の状態 | レビュー結果の要点 |
| --- | --- | --- |
| [Issue #304](https://github.com/pengin0503/Rimworld-Statistics/issues/304) | 未実施（指示により本実行をスキップ） | 実ゲーム起動による受け入れ検証タスク。ヘッドレス環境ではGUI描画目視確認（P1〜P5）・実フレーム時間/FPS体感計測・Windowsネイティブ実機確認を完遂できないため、ローカルWindows実機での実施が必要（詳細は第5節）。 |
| [Issue #357](https://github.com/pengin0503/Rimworld-Statistics/issues/357) | 一部解決（`#361`）・残件あり | `PerformanceContractTests` のウォームアップ不足による揺らぎは `#361`（`919d2b7`）で解消済み。一方、分岐親チェックポイントの毎セッション反復退避・復元、XML防御パース分類、外部SDK記述子検証、`MetricRankings.InputPeak` の `NotApplicable` 扱い、`BasicMetrics` のエンディング専用コロニー欠落、エンディング出力の重複・タイブレーク不整合（V2-Cの `ColonySummaryReport` にも波及）は未解決。 |
| [Issue #358](https://github.com/pengin0503/Rimworld-Statistics/issues/358) | 一部解決（`#361`）・残件あり | `.gitignore` への `.devoutputs/`・`__pycache__/` 追加および `check_sdk_package.py --verify-contents` の基本検証は `#361` で導入済み。一方、IMGUI `BeginGroup`/`BeginScrollView`/描画状態の `try/finally` 欠落（V2-B/V2-Cの新規ダイアログにも波及）、小画面レイアウト潰れ、比較列フォーマット乖離、12言語MODラベルのブランド名重複、`.env` 系 `.gitignore` 漏れ、ZIPアーカイブの厳密ホワイトリスト未実装は未解決。 |
| Harmonyパッチ・実行時取得層（新規検出） | 未起票（本レビューで検出） | RimWorld 1.7の非同期 `LongEvent`（別スレッドでの `MapGenerator.GenerateMap` やNPC `PawnGenerator.GeneratePawn`）中に発火するパッチが `LiveMetrics.Resolve` のスレッド検査を踏み、無関係な統計ファミリーを恒久的に `Partial`（欠測あり）へ劣化させる重大バグ等を検出（詳細は第3節）。 |

### 重大度別の検出件数

| 重大度 | 件数 | 主な対象領域 |
| --- | --- | --- |
| **High** | 3件 | ① 分岐親チェックポイントの反復アーカイブ・不要復元、② 非同期 `LongEvent` 別スレッド発火による統計ファミリーの誤 `Partial` 劣化とマップ生成イベント取りこぼし、③ IMGUI `BeginGroup` / `BeginScrollView` / グローバル描画状態の `try/finally` 欠落 |
| **Medium** | 12件 | 防御的XMLパース分類、外部SDKバリデーション、`MetricRankings.InputPeak`、`BasicMetrics`（`#332` 境界）、エンディング集計/出力重複、V2-Cレポートの `OverallAggregation.Min` 順位逆転、Harmonyパッチ前処理の例外隔離漏れ、UIレイアウト・比較表示、12言語MODラベル分離、`.gitignore`/パッケージ検証 |
| **Low** | 6件 | `OptionalTransitionRecording` のリビジョン上書き、`FishingFamily` オーバーフロー伝播、`SnapshotChartKey.Fits` enum検証、コンパクション時の割り当て、静的プローブのコロニー間持ち越し、`LiveMetrics` のScribeモード検査 |

---

## 2. Core / API / 出力層のレビュー結果（`#357`・V2-C `#360` 関連）

### 2.1 [High] 分岐親チェックポイントの毎セッション反復アーカイブと不要な `.jsonl` 復元
- **対象コード**：
  - [CheckpointArchivePlanner.cs](../../Source/RimworldStatistics.Core/Profiles/CheckpointArchivePlanner.cs#L15-L58)（`CheckpointArchivePlanner.Plan`）
  - [ProfileSession.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileSession.cs#L62-L67)、[ProfileSession.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileSession.cs#L194-L214)（`RestoreBranchFallbackParents` / `RestoreCheckpointMetrics`）
  - [ProfileMutations.Checkpoints.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileMutations.Checkpoints.cs#L76-L99)（`QueueParentRestorationIfBranchOmitsLastCapture`）
- **問題点**：
  1. 親チェックポイント `P` に有効な子 `C1`（`Conflicted == false`）と競合した子 `C2`（`Conflicted == true`）の両方が存在する場合、`CheckpointArchivePlanner.Plan` は「少なくとも1つの非競合子が存在する」ため `P` をアーカイブ対象（`MetricsArchived = true`、`.jsonl` 追記、`P.Metrics.Clear()`）に選ぶ。しかし次回ロード時の `RestoreBranchFallbackParents` は「`Conflicted` な子の親」である `P` を再びインライン復元（`MetricsArchived = false`）してしまう。結果として、**セッションを開くたびに同一の親 `P` が `.jsonl` へ重複追記→クリア→復元を繰り返す無限ループ**になり、アーカイブ肥大化とI/O負荷を引き起こす。
  2. `QueueParentRestorationIfBranchOmitsLastCapture` は、子チェックポイントが `lastCaptureKeys` を省略した際に `parent.MetricsArchived` を確認せず無条件に `PendingCheckpointMetricRestorations` へ積む。さらに `RestoreCheckpointMetrics` も `point.Metrics.Count > 0 && !point.MetricsArchived` のチェックを行わないため、既にインライン保持されている親に対しても不要な `.jsonl` 全走査が発生する。
- **推奨対応**：
  - `CheckpointArchivePlanner.Plan` で `RestoreBranchFallbackParents` の対象となる親（競合子を持つ親など、ロード時にインライン保持が必要な親）を最初からアーカイブ対象外とする。
  - `QueueParentRestorationIfBranchOmitsLastCapture` および `RestoreCheckpointMetrics` で `MetricsArchived` 状態を確認し、インライン保持済みのチェックポイントに対する `.jsonl` 走査を抑止する。

### 2.2 [Medium] セーブXML防御的パースの例外捕捉漏れ・分類不整合・未境界文字列読み取り
- **対象コード**：
  - [SaveReader.ChartHistory.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.ChartHistory.cs#L63-L75)（`ReadRecorderChartVerified`）
  - [ImportProblems.cs](../../Source/RimworldStatistics.Core/Import/ImportProblems.cs#L7-L16)（`ImportProblems.Classify`）
  - [SaveReader.History.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.History.cs#L152-L185)（`ReadHistoryRecorders`）
  - [SaveReader.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.cs#L247-L254)（`Int` / `Double` / `Bool`）
- **問題点**：
  1. `ReadRecorderChartVerified` は `catch (FormatException)` のみを捕捉して `OverflowException` を捕捉しておらず、破損した整数長フィールド等でチャート単体の検証失敗にとどまらずセーブ全体の読み取り失敗へ波及する。
  2. `SaveReader.cs` の `Int` / `Double` / `Bool` がタグ存在時の不正文字列で例外を送出する設計自体は、`TicksGame` や `Raids` に偽の `0` を捏造しないための意図的な防御である。しかし `ImportProblems.Classify` が `XmlException` / `InvalidDataException` のみを `ImportProblemKind.Damaged` とし、`FormatException` / `OverflowException` を `ImportProblemKind.Unexpected` に分類しているため、破損セーブが「想定外エラー」として扱われる。
  3. `SaveReader.History.cs` で `<def>Wealth_Total</def>` が `<records>` より先に現れた場合、`ReadElementContentAsString()` が長さ制限なしで呼ばれる。また2つ目の `Wealth_Total` レコーダーに `<records>` 要素が無い場合、`WealthTotalCount++` が実行されず重複レコーダー検知（無効化）をすり抜ける。
- **推奨対応**：
  - `ReadRecorderChartVerified` で `FormatException` に加え `OverflowException` も捕捉する。
  - `ImportProblems.Classify` で `FormatException` / `OverflowException` を `ImportProblemKind.Damaged` に分類する。
  - `SaveReader.History.cs` の `<def>` 読み取りに文字数上限付きヘルパーを用い、`<def>Wealth_Total</def>` 出現時点で確実に重複カウントを進める。

### 2.3 [Medium] 外部SDKメトリクス記述子（`ModMetricDescriptor`）の契約バリデーション漏れ
- **対象コード**：
  - [ModMetricDescriptor.cs](../../Source/RimworldStatistics.Core/Stats/ModMetricDescriptor.cs#L32-L44)（`ModMetricDescriptor.IsValid`）
  - [ExternalSdkMetricPolicy.cs](../../Source/RimworldStatistics.Core/Stats/ExternalSdkMetricPolicy.cs#L58-L78)（`ExternalSdkMetricRegistry.Register`）
- **問題点**：
  - `ModMetricDescriptor.IsValid()` に以下の3条件が欠落している：
    1. `Source != ModMetricSource.Checkpoint || Selection == ModColonySelection.Representative`（チェックポイント由来指標は代表点選択のみ有効という契約）
    2. `HasBreakdown || Selection != ModColonySelection.ObservationMaxByBreakdown`（内訳なし指標での内訳最大選択の禁止）
    3. `RequiredDlc is null || !string.IsNullOrWhiteSpace(RequiredDlc)`（空文字 `""` や空白のみのDLC指定の拒否）
  - 不正な組み合わせが `IsValid()` を通過すると、後段の `ModMetricDefinitions.ToDefinition` 等で実行時 `ArgumentOutOfRangeException` を誘発する恐れがある。
- **推奨対応**：上記3条件を `ModMetricDescriptor.IsValid()` に追加し、`ExternalSdkMetricRegistry.Register` で安全に拒否（`InvalidDescriptor`）する。

### 2.4 [Medium] `MetricRankings.InputPeak` における `NotApplicable` コロニーの母数算入バグ
- **対象コード**：
  - [MetricRankings.cs](../../Source/RimworldStatistics.Core/Stats/MetricRankings.cs#L244-L273)（`MetricRankings.InputPeak`）
- **問題点**：
  - `InputPeak` は対象コロニー数を `Targets = colonies.Count` に固定している。一方、入植者死亡0回のコロニーにおける無死亡期間指標や、取引0回のコロニーにおける最大売却額指標など、入力行が `MetricCoverage.NotApplicable` となるコロニーは `measured` に含まれない。
  - その結果、`measured.Count < colonies.Count` となり、`LongestDeathFree`（`ranking.death_free.peak`）や `LargestSale`（`ranking.trade.peak_sale`）の全体ランキングが、欠測が一切ないにもかかわらず誤って `MetricCoverage.Partial` に降格する（なお [Leaderboard.cs](../../Source/RimworldStatistics.Core/Stats/Leaderboard.cs#L52-L54) 側では `NotApplicable` を正しく `targets` から除外している）。
- **推奨対応**：`LeaderboardBuilder.Build` と同様に、`MetricCoverage.NotApplicable` のコロニーを `InputPeak` の `Targets` 母数から除外する。

### 2.5 [Medium] エンディング記録のみのコロニー（`#332`）に対する `BasicMetrics.Value` の過剰な `null` 返却
- **対象コード**：
  - [BasicMetrics.cs](../../Source/RimworldStatistics.Core/Stats/BasicMetrics.cs#L55-L71)（`BasicMetrics.Value`）
  - [ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L289-L325)（`BuildBasicItem`）
- **問題点**：
  - `#332` 対応により `BasicMetrics.Value` は冒頭で `if (!colony.HasObservedPoint) return null;` を返している。生存日数・襲撃数・資産・最大人口・プレイ時間が未知（`null`）である点は正しいが、**エンディング記録のみから確定している `Endings`（クリア回数）、`Saves`（セーブ数 `0`）、およびライブ記録済みエンディングから確定する `Departed`（旅立った入植者数）まで一律 `null` になっている**。
  - これにより、`LeaderboardBuilder`・棒グラフ・`colonies.csv` 等でエンディング専用コロニーのクリア回数や旅立ち人数が集計・順位付けから脱落する。V2-Cの `ColonySummaryReport.BuildBasicItem` では `Endings` と `Saves` のみ個別フォールバックを入れているが、`Departed` は依然欠落しており、`GetBoard(basicKey)` が返す順位も `Unmeasured` のまま不整合となっている。
- **推奨対応**：`BasicMetrics.Value` 側でキーごとに観測要否を判定し、`Endings`・`Saves`・確定済み `Departed` は `!HasObservedPoint` でも値を返すよう統一する。

### 2.6 [Medium] エンディング一覧・CSV・総括レポートにおける重複出力と代表レコード選定タイブレーク不備
- **対象コード**：
  - [StatisticsExport.cs](../../Source/RimworldStatistics.Core/Export/StatisticsExport.cs#L170-L193)
  - [EndingsTab.cs](../../Source/RimworldStatistics/UI/EndingsTab.cs#L53-L85)
  - [ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L117-L133)
  - [Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L612-L645)
- **問題点**：
  1. **代表レコード `best` のタイブレーク欠落**：同一 `(EndingType, Ordinal)` グループ内で代表レコードを選ぶ `.OrderByDescending(e => e.Departed)` に `.ThenBy(e => e.Source)` が無い（`StatisticsExport`、`EndingsTab`、`Dialog_ColonySummaryReport` の3箇所すべて）。`Departed == 0` で `Live` 記録と `SeededTrace` が並んだ場合、`SeededTrace` が選ばれて確定しているはずの離脱者数 `0` やゲーム内日数が `"—"`（不明）として表示・CSV出力される。
  2. **`RecoveredEndings` および DevMode 除外時の `SaveTrace` 重複**：`UnexplainedTraces`（または `StatisticsExport` の `SaveTrace` 出力）を作る際、`CountedEndings` に含まれる型しか除外しておらず、`ColonyRecord.RecoveredEndings` で既に説明済みのエンディング型や、`IncludeDevMode == false` で非表示にした開発者モード達成分のセーブ痕跡が「未説明の痕跡」として二重に出力・表示される（`ColonySummaryReport` L125-127 にも同じパターンが複製されている）。
  3. **初回達成日時の算出漏れ**：`StatisticsExport.cs` の `first` 算出で `o.ExplainedTraces` の考慮が漏れている箇所がある。
- **推奨対応**：エンディングのグループ化・代表選定・未説明痕跡抽出ヘルパーを `Core` に一元化し、`StatisticsExport`・`EndingsTab`・`ColonySummaryReport`・`Dialog_ColonySummaryReport` から共通利用する。

### 2.7 [Medium] V2-C `ColonySummaryReport` における `OverallAggregation.Min` 指標の順位逆転
- **対象コード**：
  - [ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L160-L165)、[ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L212-L218)、[ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L260-L282)
- **問題点**：
  - `ColonySummaryReport.Build` 内の `GetBoard(string key)` がすべての指標に対して `LeaderboardBuilder.Build(view, key, descending: true)` をハードコードしている。
  - しかし、`WealthTotalFirst100K`（資産10万到達日数）、`WealthTotalFirst1M`、`FreeColonistsFirst10`（入植者10人到達日数）、`QuestSuccessDurationMin`（最短クエスト達成日数）などの「小さいほど速い／極値である」指標や、`OutdoorTempMin`・`FoodMin`・`MoodMin`・`FreeColonistsMin` といった `OverallAggregation.Min` の指標では、降順ソートにより**最も遅い／最も大きい値のコロニーが1位（`#1`）と判定され、`TopRankedHighlights`（上位3位ハイライト）に逆転した順位で選出される**。
  - また `ClassifyValueKind`（L265）で `definition.Unit is MetricUnit.Percent or MetricUnit.Mood` を `OverallAggregation.Min` 判定より先に評価しているため、`MoodMin`（最低心情）が `PeakOrExtreme` ではなく `MeanOrRate` に分類される。
- **推奨対応**：指標定義に応じて昇順・降順（少なくとも最短到達日数系や `OverallAggregation.Min` の扱い）を `RankingsTab` の既定ソート方針と揃える。また `ClassifyValueKind` では極値判定（`.min` / `_min` / `OverallAggregation.Min`）を単位ベースの `MeanOrRate` 判定より優先する。

### 2.8 [Low] Core層の軽微な改善項目（4件）
1. **[OptionalTransitionRecording.cs](../../Source/RimworldStatistics.Core/Stats/OptionalTransitionRecording.cs#L15-L38)**：`PutCount` が `mod.` プレフィックス以外のキーに対しても `MetricKeys.Revision` を上書きし得るため、キー名前空間に応じたリビジョン管理を明確化する。
2. **[FishingFamily.cs](../../Source/RimworldStatistics.Core/Stats/Families/FishingFamily.cs#L48-L75)**：総釣果数 `Units` が `long` オーバーフロー（`Overflow`）した際、魚種別内訳 `AcceptedUnitsByDef` 側へ `Partial` 降格が伝播しない。
3. **[SnapshotChartSeries.cs](../../Source/RimworldStatistics.Core/Model/SnapshotChartSeries.cs#L47-L64)**：`SnapshotChartKey.Fits` でデシリアライズされた `Coverage` 配列要素に対する `Enum.IsDefined` 検証を行うと破損データ耐性が向上する。
4. **[ProfileMutations.SnapshotCompaction.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileMutations.SnapshotCompaction.cs#L65-L115)**：`LeastShapeChange` ループ内で毎回一時リスト・辞書を割り当てており、チェックポイント数が多い場合のGC負荷削減余地がある。

---

## 3. Harmonyパッチ・実行時統計収集層のレビュー結果

### 3.1 [High] 非同期 `LongEvent`（別スレッドでのマップ生成・NPC生成）による無関係ファミリーの恒久的 `Partial` 劣化とイベント取りこぼし
- **対象コード**：
  - [LiveMetrics.cs](../../Source/RimworldStatistics/Metrics/LiveMetrics.cs#L95-L109)（`LiveMetrics.Resolve`）
  - [GameFacts.cs](../../Source/RimworldStatistics/Patches/Metrics/GameFacts.cs#L67-L88)（`Record` / `AnyRecording` / `Missed`）
  - [GameComponent_RimworldStatistics.cs](../../Source/RimworldStatistics/GameComponent_RimworldStatistics.cs#L204-L216)（`FlushMissedFamilies`）
  - [CaravanMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/CaravanMetricPatches.cs#L174)、[CaravanMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/CaravanMetricPatches.cs#L257-L310)（`ShrineMapGeneratedMetricPatch` / `ShrineUnfogMetricPatch` / `ShrineProbe.Watched`）
  - [OdysseyMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/OdysseyMetricPatches.cs#L98-L113)（`OdysseyNewLandingMapMetricPatch`）
  - [IdeologyMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/IdeologyMetricPatches.cs#L16-L30)（`PawnSetIdeoMetricPatch`）
  - [RoyaltyMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/RoyaltyMetricPatches.cs#L17-L29)（`RoyaltySetTitleMetricPatch`）
  - [BiotechGrowthRepairMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/BiotechGrowthRepairMetricPatches.cs#L18-L26)（`BiotechChildStageMetricPatch`）
- **問題点**：
  - RimWorld 1.7では、プレイ中（`Current.Game == game`）のキャラバン到着・クエスト現場生成・Odysseyグラヴシップ着陸などで `LongEventHandler.QueueLongEvent(..., doAsynchronously: true)` が使われ、**ワーカースレッド上で `MapGenerator.GenerateMap`、霧解除（`FloodFillerFog`）、先住民・敵NPCの `PawnGenerator.GeneratePawn`（思想設定・称号付与・成長段階初期化を含む）が実行される**。
  - `LiveMetrics.Resolve` は `Environment.CurrentManagedThreadId != MainThreadId` のとき `Current.Game == game` であれば `facts.Missed[family] = true` を記録して `null` を返す。そして次フレームの `FlushMissedFamilies` が該当ファミリーに `MarkGap` を書き込み、**そのコロニーの統計カバレッジを恒久的に `Partial`（欠測あり）へ降格させる**。
  - 多くのパッチ（`PawnSetIdeoMetricPatch`、`RoyaltySetTitleMetricPatch`、`BiotechChildStageMetricPatch` など）が、対象ポーンが自派閥入植者かどうかのフィルタを行う**前**に `LiveMetrics.CanCaptureEvent` や `GameFacts.AnyRecording` を呼んでいる（あるいは別スレッドで生成中の入植者/NPCに対して `GameFacts.Record` を呼んでいる）。そのため、非同期マップ生成で敵NPCが1人生成されるだけで、`Ideology`・`Royalty`・`Biotech` などの統計ファミリーが誤って `Partial` に汚染される。
  - さらに `ShrineMapGeneratedMetricPatch` や `OdysseyNewLandingMapMetricPatch` は `MapGenerator.GenerateMap` の Postfix であり、まさに非同期ワーカースレッド上で発火するケースがあるため、太古の脅威（Ancient Shrine）の監視登録やグラヴシップ新規着陸マップの計測自体がスキップされ、かつ `Caravan` / `Odyssey` ファミリーが `Partial` になる。加えて `ShrineProbe.Watched`（非同期コレクション）への別スレッドからの読み書きも競合し得る。
- **推奨対応**：
  1. 各パッチでスレッドローカルに完結する高速な事前フィルタ（対象派閥・マップ種別など）を `CanCaptureEvent` / `Record` より先に評価し、統計対象外のNPCイベントで `Resolve` を呼ばない。
  2. `MapGenerator.GenerateMap` 系など正当にワーカースレッド上で発生するイベントについては、スレッドセーフなキュー（`ConcurrentQueue` 等）に積んでメインスレッドの `GameComponentTick` / `GameComponentUpdate` 冒頭（または `LongEvent` 完了直後）でドレインして `GameFacts` に反映し、`Missed` 扱いにしない。

### 3.2 [Medium] `[MetricAdapter]` パッチの `Prefix` / `Postfix` 前処理における例外隔離漏れ
- **対象コード**：
  - [WorkMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs#L155-L210)（`MiningPlaceMetricPatch` の `placedAction` ラッパー、`BillDoneMetricPatch` の `p.MarketValue` 算出等）
  - [TradeMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/TradeMetricPatches.cs#L18-L65)（`TradeExecuteMetricPatch` の `CurTotalCurrencyCostForDestination` 呼び出し等）
  - [SocialMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/SocialMetricPatches.cs#L18-L95)
  - [MembershipMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/MembershipMetricPatches.cs#L18-L110)
  - [RaidMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/RaidMetricPatches.cs#L18-L85)
- **問題点**：
  - `GameFacts.Record(family, callback)` の `callback` 内部で起きた例外は `LiveMetrics.Fail` によって安全に捕捉・隔離されるが、**パッチメソッド本体の `GameFacts.Record` 呼び出し前の前処理（バニラ・他MODのプロパティ参照、`StatDefOf.MarketValue` 計算、デリゲートラップ内など）は `try/catch` で囲まれていない**。
  - 他MODの不完全な `Thing` や特殊な取引アイテム等で `MarketValue` 取得などが例外を投げた場合、統計の隔離機構（`LiveMetrics.Fail`）を通らずにRimWorld本体の呼び出し元（生産完了処理や取引確定処理）を中断させてしまう。
- **推奨対応**：各パッチのエントリポイント全体を `try { ... } catch (Exception ex) { LiveMetrics.Fail(family, ex); }` で保護するか、状態読み取り自体を `Record` / 安全ラッパーの内側へ移動する。

### 3.3 [Low] 実行時収集層の軽微な改善項目（2件）
1. **[WorkMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs#L14-L25)**：`HarvestProbe` の静的フィールド（`LastPlant` / `LastTick`）が `LiveMetrics.Install`（別セーブのロード時）でリセットされない。同一セッション内で別セーブをロードした直後に同一Tick番号と偶然一致する参照を保持し続けないよう、`ShrineProbe` 等と同様に初期化時にクリアするのが望ましい（加えて `Plant` 参照のGC保持も防げる）。
2. **[LiveMetrics.cs](../../Source/RimworldStatistics/Metrics/LiveMetrics.cs#L95-L109)**：[OptionalTransitionCallSite.cs](../../Source/RimworldStatistics/Metrics/OptionalTransitionCallSite.cs#L9-L11) には `Scribe.mode != LoadSaveMode.Inactive` のガードがある一方、`LiveMetrics.Resolve` には無い。Real Ruins等のMODがプレイ中に別マップ断片やポーンをScribeデシリアライズした際、ロード中の副作用呼び出しを実イベントと誤認しないよう `Scribe.mode == LoadSaveMode.Inactive` を条件に加えるとより堅牢になる。

---

## 4. UI・12言語ローカライズ・パッケージ検証層のレビュー結果（`#358`・V2-B `#355`・V2-C `#360` 関連）

### 4.1 [High] IMGUIの `BeginGroup` / `BeginScrollView` およびグローバル描画状態変更に `try/finally` が無い
- **対象コード**：
  - 既存タブ・共通部品・ダイアログ：[DetailsTab.cs](../../Source/RimworldStatistics/UI/DetailsTab.cs)、[ComparisonPanel.cs](../../Source/RimworldStatistics/UI/ComparisonPanel.cs)、[OverviewTab.cs](../../Source/RimworldStatistics/UI/OverviewTab.cs)、[ColoniesTab.cs](../../Source/RimworldStatistics/UI/ColoniesTab.cs)、[CoverageTab.cs](../../Source/RimworldStatistics/UI/CoverageTab.cs)、[RankingsTab.cs](../../Source/RimworldStatistics/UI/RankingsTab.cs)、[DataManagementTab.cs](../../Source/RimworldStatistics/UI/DataManagementTab.cs)、[ImportTab.cs](../../Source/RimworldStatistics/UI/ImportTab.cs)、[StatsTable.cs](../../Source/RimworldStatistics/UI/Widgets/StatsTable.cs)、[Dialog_MetricChart.cs](../../Source/RimworldStatistics/UI/Dialog_MetricChart.cs)、[Dialog_ColonyGroups.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyGroups.cs)、[Dialog_ColonyMetricHistory.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyMetricHistory.cs)、[Dialog_HistoricalCounterIntervals.cs](../../Source/RimworldStatistics/UI/Dialog_HistoricalCounterIntervals.cs)
  - V2-B / V2-C 新規ダイアログ：[Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L387-L476)、[Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L322-L411)
- **問題点**：
  - Unity IMGUIでは `Widgets.BeginScrollView` / `GUI.BeginGroup` に対応する `EndScrollView` / `EndGroup` が例外でスキップされると、GUIクリップスタックが破損し、以降のフレームでRimWorld全体のUI描画が崩壊する。また `Text.Font`・`Text.Anchor`・`Text.WordWrap`・`GUI.color` が復元されないと他ウィンドウの文字サイズや配色が壊れる。
  - 現在、`Dialog_RimworldStatistics.DoWindowContents` の最上位には例外ハンドラがあるものの、各タブ・ダイアログ内部の `BeginGroup` / `BeginScrollView` や描画状態変更に `try/finally`（または `using` スコープ構造体）が使われていない。特に `Dialog_ColonySaveLineage` や `Dialog_ColonySummaryReport` のような別ウィンドウは `Dialog_RimworldStatistics` の外側で直接 `WindowStack` から呼ばれるため、内部例外時のIMGUIスタック保護が不可欠である。
- **推奨対応**：軽量な `readonly struct` の `IDisposable` スコープ（`ScrollScope`、`GroupScope`、`TextStyleScope`）を導入し、全タブ・全ダイアログで `using` による確実な復元を行う。

### 4.2 [Medium] 小画面・高UIスケール時のレイアウト潰れ、メインスレッド同期ブロック、ファイル列挙例外捕捉
- **対象コード**：
  - [DataManagementTab.cs](../../Source/RimworldStatistics/UI/DataManagementTab.cs#L36-L95)
  - [ImportTab.cs](../../Source/RimworldStatistics/UI/ImportTab.cs#L30-L85)
  - [ColoniesTab.cs](../../Source/RimworldStatistics/UI/ColoniesTab.cs#L65-L125)
  - [Dialog_RimworldStatistics.cs](../../Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs#L155-L175)（`RefreshPresentFiles`）
  - [Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L70-L84)（`TryReadPresentFiles`）
- **問題点**：
  1. `DataManagementTab`（および `ImportTab`）は外側スクロールを持たず固定高さを差し引いて残り高さを `ProblemsTable` 等に割り当てているため、720p相当の解像度や高UIスケール環境、あるいは多言語の折り返し増加時に `ProblemsTable` の高さが極小・負値になり、下部コントロールと重なる。
  2. `ImportTab` の3ボタン横並び行が `ControlRow` や `.Truncate` を通しておらず、ドイツ語・フランス語・ロシア語等の長い翻訳ラベルではみ出す。
  3. `ColoniesTab` の「集計から除外」チェックボックス切り替えが `MutateWithoutBlocking` ではなく同期ロックを伴う `Mutate` を呼んでおり、バックグラウンドスキャン中にUIスレッドをブロックし得る。またテーブル高さ計算に `Mathf.Max(0f, ...)` の下限クランプが無い。
  4. `Dialog_RimworldStatistics.RefreshPresentFiles` および V2-B `Dialog_ColonySaveLineage.TryReadPresentFiles` は `SaveFileIndex.Read` に対して `IOException` / `UnauthorizedAccessException` のみを捕捉しているが、不正パス文字やセキュリティ例外等でも統計ウィンドウ全体が落ちないよう基底例外保護を揃えるべきである。
- **推奨対応**：`DataManagementTab` / `ImportTab` に最小高さ保証または外側スクロールを導入し、`ColoniesTab` の除外トグルを `MutateWithoutBlocking` に変更する。

### 4.3 [Medium] `ComparisonPanel`・`DetailsTab`・`Dialog_MetricChart`・`Dialog_ColonySummaryReport` 間の値フォーマット・ツールチップ乖離
- **対象コード**：
  - [ComparisonPanel.cs](../../Source/RimworldStatistics/UI/ComparisonPanel.cs#L140-L225)
  - [DetailsTab.cs](../../Source/RimworldStatistics/UI/DetailsTab.cs#L240-L330)
  - [Dialog_MetricChart.cs](../../Source/RimworldStatistics/UI/Dialog_MetricChart.cs#L190-L260)
  - [Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L650-L660)
  - [RankingsTab.cs](../../Source/RimworldStatistics/UI/RankingsTab.cs#L80-L115)、[OverviewTab.cs](../../Source/RimworldStatistics/UI/OverviewTab.cs#L150-L195)、[CoverageTab.cs](../../Source/RimworldStatistics/UI/CoverageTab.cs#L55-L95)
- **問題点**：
  1. `ComparisonPanel` が `DetailsTab.FormatRowValue` / `ColonyTip` を共有していないため、内訳のみを持つ指標（`Number == null` かつ `Breakdowns.Count > 0`）で `"— · Snapshot"` のように不自然な表記になり、記録保持者サブラベルも欠落する。また単一コロニー列のホバー時に全体集計用のツールチップが出る箇所がある。
  2. 非修飾の `ThingDef` 内訳キーの表示名解決ロジックが `DetailsTab` と `Dialog_MetricChart` で微妙に異なっており、`Dialog_ColonySummaryReport`（L653）も `DetailsTab` の整形ヘルパーを通さず `MetricText.Format` を直接呼んでいる。
  3. `RankingsTab` の検索文字列に `.Trim()` が無く、末尾スペース入力でヒットしなくなる。また `OverviewTab` の `RWS_Fav_AddBasic` ボタンや `CoverageTab` のフィルタボタン群に動的幅計算・`.Truncate`＋ツールチップが無い。
- **推奨対応**：指標値・内訳ラベル・コロニー別ツールチップの整形ロジックをUI共通ヘルパーへ集約し、全画面で同一表記にする。

### 4.4 [Medium] 12言語MOD指標タイトルのブランド名重複と検証スクリプトの検査範囲漏れ
- **対象コード**：
  - [tools/check_mod_source_labels.py](../../tools/check_mod_source_labels.py#L45-L60)
  - `Languages/*/Keyed/RimworldStatistics_ModMetrics.xml`（全12言語）
- **問題点**：
  - UI設計方針では、MOD指標の所属MOD名はセカンダリバッジ（出典ラベル）側に表示し、指標タイトル（`LabelKey`）側にはMODブランド名を重複させないルールとなっている。
  - しかし `tools/check_mod_source_labels.py` が `RimworldStatistics.xml` のみを走査し、実際にMOD指標キーが定義されている `RimworldStatistics_ModMetrics.xml` を検査していない。
  - その結果、`#359` の12言語翻訳監査後も、少なくとも以下の7キーで全12言語にわたりMODブランド名が指標タイトル内に残存している：
    - `RWS_VFECatches`、`RWS_AlphaBiomes_HomeMaps`、`RWS_VleLandmarks_HomeMaps`、`RWS_RimTalk_Conversations`、`RWS_AlphaMechs_MechConversions`、`RWS_AnomaliesExpected_StudiedEntities`、`RWS_NewMod_RealRuins`
    - （関連して `RWS_RimWarVictory`、`RWS_VnpeGrinder_SlurryStored`、`RWS_VreAndroid_Awakenings`、`RWS_Vsie_AspirationsCompleted` の略称・固有名の扱いもあわせて整理が望ましい）
- **推奨対応**：`tools/check_mod_source_labels.py` の検査対象を `Languages/*/Keyed/*.xml` 全体に拡大した上で、12言語の該当 `LabelKey` から重複MOD名を除去する。

### 4.5 [Medium] `.gitignore` の `.env` 保護不足とリリースZIP検証の厳密ホワイトリスト未実装
- **対象コード**：
  - [.gitignore](../../.gitignore)
  - [tools/check_sdk_package.py](../../tools/check_sdk_package.py#L39-L64)
- **問題点**：
  1. `#361` で `.gitignore` に `.devoutputs/` や `__pycache__/` は追加されたが、開発ルールでリポジトリ直下に配置され得る認証情報ファイル（`.env`、`*.env`、`r2_credentials.env`）が `.gitignore` に登録されていない。誤って `git add .` した際の秘密情報混入リスクを防ぐため明示的に除外すべきである。
  2. `#361` で `tools/check_sdk_package.py --verify-contents` に `About/` 一致確認、`PublishedFileId.txt` 数値検証、`Languages/*/Keyed/*.xml` 存在確認、および特定除外語（`Recruitment` / `VerifyHarness` / `.devinputs`）の拒否が追加された。しかし「ブラックリスト方式」であるため、想定外のファイル（`.pdb`、`.json`、`.env`、一時ファイル、未許可ディレクトリ等）がリリースZIPに紛れ込んでも検出できない。
- **推奨対応**：
  - `.gitignore` に `.env`、`*.env` を追加する。
  - `tools/check_sdk_package.py --verify-contents` に、許可されたエントリパス（`About/About.xml`、`About/Preview.png`、`About/PublishedFileId.txt`、`LoadFolders.xml`、`1.7/Assemblies/RimworldStatistics*.dll`、`Languages/<Lang>/Keyed/*.xml`、`LICENSE`、`README.md` 等）以外をすべてエラーとする厳密なホワイトリスト検証を追加する。

---

## 5. Issue `#304`（RimWorld 1.7 実ゲーム受け入れ検証）の現状整理

[Issue #304](https://github.com/pengin0503/Rimworld-Statistics/issues/304) で求められている受け入れ基準のうち、ヘッドレス環境・自動テストで検証可能な範囲と、実機GUI環境が必須な範囲は以下のとおり整理される。

1. **自動テスト・ハーネスでカバー済みの範囲**：
   - `.NET` 単体テスト・契約テスト（`RimworldStatistics.Core.Tests`、メモリ割り当て契約、SDKパッケージ整合性、ローカライズキー整合性）
   - セーブXMLスキャン・チェックポイント親子系統（V2-B）・総括レポートおよび静的HTMLエスケープ（V2-C）のロジック検証
2. **ローカルWindows実機（GUI付きRimWorld 1.7）での実施が必要な範囲**：
   - P1〜P5の各UI画面（特に新規追加されたセーブ系統グラフ `Dialog_ColonySaveLineage`、総括レポート `Dialog_ColonySummaryReport`、12言語フォント折り返し、720p/高UIスケール表示）の目視レンダリング確認
   - 実プレイ中（非同期マップ生成・キャラバン・襲撃・取引・エンディング到達・セーブ/ロード反復）のFPS/フレーム時間計測と、第3.1節で指摘した非同期 `LongEvent` パッチ挙動の改修後確認
   - Windows環境固有のファイルロック・パス区切り・ブラウザでの出力HTML閲覧確認

> **推奨される進め方**：第2節〜第4節のコードレビュー指摘事項（特に **High** 3件：`#357` の分岐親アーカイブ反復、第3.1節の非同期 `LongEvent` パッチ誤 `Partial` 化、`#358` のIMGUI `try/finally` 保護）を先に修正・テスト追加した上で、`#304` の実ゲーム受け入れ検証をまとめて実施するのが手戻りが最も少ない。

---

## 6. 参照

- [Issue #304: RimWorld 1.7実ゲームで統計取得・UI・性能の受け入れ検証を行う](https://github.com/pengin0503/Rimworld-Statistics/issues/304)
- [Issue #357: Core/APIの分岐親アーカイブ反復・防御的XMLパース・統計集計・CSV出力の不整合とCIテスト揺らぎを修正する](https://github.com/pengin0503/Rimworld-Statistics/issues/357)
- [Issue #358: UIレイアウト・IMGUI例外安全性・比較表示・多言語タイトル分離・パッケージ検証の不整合を修正する](https://github.com/pengin0503/Rimworld-Statistics/issues/358)
- [v2.0.0候補の既存RimWorld MODとの重複・独自性調査](2026-11-17-v2-0-originality-comparison-ja.md)
- [v2.0.0実装前設計](../superpowers/specs/2026-11-17-v2-0-distinctive-features-design.md)
