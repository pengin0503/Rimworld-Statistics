# 最新 main（17e4934）包括監査・改善点・追加機能・追加MOD対応ロードマップ（2026-11-18）

- 調査日：2026-11-18（日本時間）
- 対象リポジトリ：`pengin0503/Rimworld-Statistics`
- 基準コミット：`main` `17e49346c23b6449aa91e63144aec2c59d412f5e`（PR `#355`〜`#370` マージ完了時点）
- 参照した外部リファレンス（読み取り専用）：
  - R2 バケット `rimworld-17` `Game-Windows`（RimWorld `1.7.5011 rev24` 本体 DLL・Core / Royalty / Ideology / Biotech / Anomaly / Odyssey 各 Def・解析済みメタデータ・実 `.rws` セーブスキーマ）
  - GitHub オープン Issue（`#304`、`#357`、`#358`）、直近マージ PR（`#355`〜`#370`）、最新 GitHub Actions 実行結果（Run `44252915035`）
- 位置付け：**読み取り専用の網羅調査・設計・優先度整理ドキュメント**。本書自体は機能の実装完了や対応MODの正式追加を宣言するものではなく、秘密情報・個人セーブ識別子・ハッシュ値は一切含まない。

---

## 1. エグゼクティブサマリー・現状到達点

### 1.1 直近の開発到達点（PR `#355`〜`#370`）
2026-11-17 の全体コードレビュー記録（[2026-11-17-full-codebase-review-ja.md](2026-11-17-full-codebase-review-ja.md)、コミット `919d2b7` 時点）以降、以下の 8 PR が `main` にマージされた。

- `#363`：コロニー年表（V2-A）の追加
- `#364`：コロニー横断 2指標散布図（V2-D）の追加
- `#365`：テストにおけるメモリアロケーション計測の最適化
- `#366`：セーブ解析・A/B 性能比較・回帰レポート用 Google Colab ノートブック 3本の追加
- `#367`：コロニー別履歴折れ線グラフの期間拡大（ズーム）表示の追加
- `#368`：プレイ中のメニュー（Esc メニュー）から現在のコロニー総括レポートを直接開く導線の追加
- `#369`：コロニー年表の文字検索・ゲーム内日数範囲フィルタの追加
- `#370`：コロニーのカスタム表示名・検索付きピッカー・コロニー別履歴グラフのお気に入りの追加

これにより V2 系計画の主要プレイヤー向け機能が一通り揃った一方、**`#357`・`#358` の未解決課題が残存しているほか、PR `#363`〜`#370` で新規導入された画面遷移・状態管理・フィルタ・ノートブックに起因する新たな不具合 8件と既存バグの新機能への波及**が確認された。

### 1.2 ローカル検証スクリプト・CI の現状（`17e4934` 時点）

| 検証項目 | 結果 | 備考 |
| --- | --- | --- |
| GitHub Actions `numeric-verification.yml`（Run `44252915035`） | PASS | `.NET 10` ビルド・単体テスト・パッケージ検証成功 |
| `bash tools/check_keys.sh` | PASS | 使用キー 1,765 件が全 12 言語に存在 |
| `python3 tools/check_translation_formats.py` | PASS | 1,765 キー × 12 言語のプレースホルダ検証通過 |
| `python3 tools/check_mod_provider_names.py` | PASS | 104 オーナーパッケージの整合確認 |
| `python3 tools/check_player_faction_access.py` | PASS | 安全でない `Faction.OfPlayer` 直接参照なし |
| `python3 tools/check_mod_source_labels.py` | PASS（**検査漏れあり**） | `RimworldStatistics.xml` の 804 キーのみ検査しており、分割 XML 群を走査していない（後述 §2.2.10） |
| `python3 -m unittest discover -s tools/tests -p "test_*.py"` | PASS | 10 テスト通過 |

### 1.3 検出課題の全体集計

| 区分 | 件数 | 内訳 |
| --- | --- | --- |
| **PR `#363`〜`#370` 起因の新規不具合** | **8件** | High 1件（プレイ中の全セーブ再スキャン実行可能化）、Medium 7件 |
| **既存未解決不具合（`#357` / `#358` / `#362` 残件＋波及）** | **19件** | High 3件、Medium 11件（うち 2件は V2-D 散布図へ新規波及）、Low 5件 |
| **コード・UX・アーキテクチャ・CI・資料の改善点** | **6領域** | 公開資料追従、ピッカー統一、CSV 元派閥名保持、MOD 統合 3系統整理・IL 監査拡大、囚人作業帰属、CI 調整 |
| **追加できる機能（バニラ 1.7 / DLC / `.rws` / UI / SDK）** | **15施策** | バニラ・DLC・過去 `.rws` 活用 6領域 ＋ UI・分析・出力・診断・SDK 拡張 9施策 |
| **追加で対応できる MOD** | **計78件＋既存拡張** | 既存対応 105件の第2段階指標、現行候補 58件の Tier 1〜3 分類、新規第7波候補 12件、1.7 復活監視 8件 |

---

## 2. 修正すべき不具合・不整合

### 2.1 PR `#363`〜`#370`（`919d2b7..17e4934`）で新たに発生・顕在化した不具合（8件）

#### 2.1.1 [High] プレイ中（`ProgramState.Playing`）に通算統計画面へ遷移すると、全セーブの「再スキャン」「強制再検査」を実行できてしまう（`#368` 起因）
- **対象コード**：
  - [Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L268-L278)、[Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L416-L420)（`OpenAllStatisticsWindow`）
  - [ImportTab.cs](../../Source/RimworldStatistics/UI/ImportTab.cs#L109-L125)
  - [ImportRunner.cs](../../Source/RimworldStatistics/ImportRunner.cs#L35-L67)（`ImportRunner.Start`）
  - 比較参照：[DataManagementTab.cs](../../Source/RimworldStatistics/UI/DataManagementTab.cs#L51)、[ProfileService.cs](../../Source/RimworldStatistics/ProfileService.cs#L143)
- **問題点**：
  - `#368` 以前は [Dialog_RimworldStatistics.cs](../../Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs) がメインメニュー（`ProgramState.Entry`）からしか開けなかったため、`DataManagementTab` には `Current.ProgramState == ProgramState.Entry` ガードがある一方、`ImportTab` の「今すぐスキャン」「強制再検査」ボタンおよび `ImportRunner.Start` には `Entry` ガードが存在しない。
  - `#368` でプレイ中の Esc メニュー →「現在のコロニーの統計」→「すべての統計を開く」の導線が追加された結果、**プレイ中にバックグラウンド全セーブ走査・強制再検査を開始できてしまう**。
  - これは [InGameStatistics.xml](../../Languages/Japanese/Keyed/InGameStatistics.xml#L7)（「セーブフォルダの再スキャンは行いません」）の明示仕様に反するほか、`CancelForGameStart()` はゲーム開始遷移時にしか呼ばれないため、プレイ中のオートセーブ／手動セーブ書き込みとのファイル競合や TPS 低下を引き起こす。
- **修正方針**：
  - `ImportRunner.Start` の先頭で `Current.ProgramState == ProgramState.Entry` を検証して拒否するとともに、`ImportTab` のスキャン・強制再検査ボタンも `Entry` 以外では無効化し、タイトル画面専用操作である旨の注記を表示する。

#### 2.1.2 [Medium] 読取失敗セーブ（`Status != ObservationStatus.Ok`）が混在すると、プレイ中バナーの最終記録日数が `0.0日` に化ける（`#368` 起因）
- **対象コード**：
  - [InGameColonyAccess.cs](../../Source/RimworldStatistics.Core/Stats/InGameColonyAccess.cs#L72-L76)（`DescribeRecordedPoint`）
  - [ColoniesTab.cs](../../Source/RimworldStatistics/UI/ColoniesTab.cs#L509-L516)（`BuildDetail`）
- **問題点**：
  - `InGameColonyAccess.DescribeRecordedPoint` が `colony.Observations` を `obs.Status == ObservationStatus.Ok` でフィルタせずに走査している。最新の `.rws` が読取失敗（`Damaged` 等、`TicksGame == 0`）だった場合、その破損レコードが最新点として選ばれ、プレイ中バナーに `0.0日` と誤表示される。
  - また `LastWriteUtc != default` の判定も、[ColonyChronicle.cs](../../Source/RimworldStatistics.Core/Stats/ColonyChronicle.cs) の `ValidUtc`（`Year > 1970`）と不統一であり、`ColoniesTab.BuildDetail` のセーブ一覧でも読取失敗行の日数が `—`（`Dash`）ではなく `0.0日` と表示される。
- **修正方針**：
  - `DescribeRecordedPoint` で `obs.Status == ObservationStatus.Ok` のみを対象とし、UTC 判定を `ValidUtc` に統一し、`ColoniesTab.BuildDetail` でも `obs.Status != ObservationStatus.Ok` の日数セルを `Dash` にする。

#### 2.1.3 [Medium] Colab ノートブック（`#366`）のアサーション矛盾・標準出力汚染・自己削除リスク・型変換漏れ
- **対象コード**：
  - [01_Save_Analysis.ipynb](../../notebooks/01_Save_Analysis.ipynb)（Cell 6, Cell 8, Cell 10）
  - [02_Performance_Comparison.ipynb](../../notebooks/02_Performance_Comparison.ipynb)（Cell 2, Cell 4）
  - [03_Regression_Report.ipynb](../../notebooks/03_Regression_Report.ipynb)（Cell 2）
  - [RealFolder.cs](../../tools/SaveImportBenchmark/RealFolder.cs#L52-L63)
- **問題点**：
  1. `01_Save_Analysis.ipynb` の Cell 8（`RealSaveImport`）は識別不能セーブ（`unidentifiable > 0`）の混在を正常許容する一方、Cell 10（`SaveImportBenchmark --dir`）は `RealFolder.cs` 側で識別不能セーブも `cold_failed` / `warm_failed` に加算するため、`assert int(row["cold_failed"]) == 0` および `assert int(row["warm_skipped"]) == len(inputs)` が即座に `AssertionError` で失敗する。
  2. `01_Save_Analysis.ipynb` は事前 `dotnet build` ＋ `--no-build` を行わずに `dotnet run` の stdout を直接 `csv.DictReader` に渡しているため、初回ビルドメッセージやワークロード通知が stdout に混入すると CSV パースが壊れる。
  3. `01_Save_Analysis.ipynb` の Cell 6 は `SAVE_SOURCE` と `STAGE`（`/content/rws-colab-private/saves`）の同一パス判定を行わずに `shutil.rmtree(STAGE)` を実行するため、ユーザーが `STAGE` に直接ファイルを置いた場合に元セーブを消去してしまう。
  4. `02_Performance_Comparison.ipynb` は `--depth 100` でクローンするため 100 コミット超前のタグ／コミットを `BASE_REF` に指定すると解決失敗し、また `02`・`03` ともに `assert int(REPEATS) >= 1` のみで `REPEATS = int(REPEATS)` の再代入を行っていないため文字列渡しで失敗する余地がある。
- **修正方針**：
  - `SaveImportBenchmark` で `Damaged/Unexpected` と `Unidentifiable` を分離するかノートブック側のアサーションを整合させ、事前 `dotnet build` ＋ `--no-build`、`os.path.realpath` による同一パス保護、浅いクローン失敗時の `git fetch` フォールバック、`REPEATS = int(REPEATS)` 正規化を入れる。

#### 2.1.4 [Medium] 単一コロニー文脈の UI（`#364` 散布図、`#370` コロニー別お気に入り等）に「最高資産の合計」「通算ゲーム内日数」と全体合算ラベルが出る
- **対象コード**：
  - [Dialog_MetricScatterPlot.cs](../../Source/RimworldStatistics/UI/Dialog_MetricScatterPlot.cs#L238)
  - [OverviewTab.cs](../../Source/RimworldStatistics/UI/OverviewTab.cs#L405)（`BuildColonyFavorites`）
  - [ColonyPickerDialog.cs](../../Source/RimworldStatistics/UI/Widgets/ColonyPickerDialog.cs#L165)（`ColonyChartPicker`）
  - [RankingsTab.cs](../../Source/RimworldStatistics/UI/RankingsTab.cs#L52)、[Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L254)、[Dialog_MetricChart.cs](../../Source/RimworldStatistics/UI/Dialog_MetricChart.cs#L66)
  - [Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L1007-L1012)、[Dialog_ColonyChronicle.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyChronicle.cs#L967-L972)
- **問題点**：
  - `MetricText.BasicLabel` は通算集計用のキー（`RWS_Stat_PeakWealthSum`＝「最高資産の合計」、`RWS_Stat_LastWealthSum`＝「最終資産の合計」、`RWS_Stat_Days`＝「通算ゲーム内日数」）を返す。これを 1点＝1コロニーの散布図やコロニー別履歴ピッカー・お気に入りカードにそのまま使っているため、単一コロニーの値に対して「合計」「通算」と誤表示される。
- **修正方針**：
  - `MetricText.PerColonyBasicLabel` を新設し、既存の単一コロニー用翻訳キー（`RWS_Col_PeakWealth`、`RWS_Col_LastWealth`、`RWS_Col_Days` 等）へマッピングする。

#### 2.1.5 [Medium] `ViewPreferences` 参照保持による設定ロストアップデートと、コロニー表示名（`#370`）の未反映箇所
- **対象コード**：
  - [Dialog_ColonyGroups.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyGroups.cs#L24-L58)
  - [Dialog_RimworldStatistics.cs](../../Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs#L256-L278)（`UpdateActivePreferences` / `RefreshSavedPreferences`）
  - [Dialog_HistoricalCounterIntervals.cs](../../Source/RimworldStatistics/UI/Dialog_HistoricalCounterIntervals.cs#L63)
  - [Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L97-L103)、[Dialog_MetricChart.cs](../../Source/RimworldStatistics/UI/Dialog_MetricChart.cs#L345)、[Dialog_ColonyMetricHistory.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyMetricHistory.cs#L812)
- **問題点**：
  1. `Dialog_ColonyGroups` はコンストラクタ時点の `ViewPreferences` インスタンス参照を保持し続ける。一方で `UpdateActivePreferences` は設定更新のたびに新しい `ViewPreferences` インスタンスへ差し替えるため、グループ編集画面を開いたままコロニー表示名やお気に入りを変更すると、グループ保存時に古いインスタンスで上書きして変更が消失する。
  2. `Dialog_HistoricalCounterIntervals` は `ColonyPresentation` を通さず生の `row.ColonyName` をそのまま描画している。
  3. `Dialog_ColonySaveLineage`、`Dialog_MetricChart`、`Dialog_ColonyMetricHistory`（比較オーバーレイ名）は `ColonyPresentation.Signature` の変化を監視しておらず、開いたまま表示名を変更しても再構築されない。
- **修正方針**：
  - 最新 `ViewPreferences` を取得するデリゲート経由での差分更新に統一し、全ダイアログで `ColonyPresentation` と署名監視を通す。

#### 2.1.6 [Medium] 年表の日数フィルタ（`#369`）と履歴拡大（`#367`）の入力仕様不一致＆丸め表示との境界ズレ
- **対象コード**：
  - [ColonyChronicleFilter.cs](../../Source/RimworldStatistics.Core/Stats/ColonyChronicleFilter.cs#L100-L112)、[ColonyChronicleFilter.cs](../../Source/RimworldStatistics.Core/Stats/ColonyChronicleFilter.cs#L219-L225)
  - [HistoryPlotRange.cs](../../Source/RimworldStatistics.Core/Stats/HistoryPlotRange.cs#L55-L61)、[HistoryPlotRange.cs](../../Source/RimworldStatistics.Core/Stats/HistoryPlotRange.cs#L120-L135)
- **問題点**：
  - `HistoryPlotRange`（`#367`）は欧州言語圏の小数カンマ（`12,5` → `12.5`）を許容し、開始日＞終了日の入力を自動で入れ替えるが、`ColonyChronicleFilter`（`#369`）はカンマを不正入力として弾き、開始＞終了もエラー扱いする。
  - さらに年表の行表示は `ticks / 60000.0` を小数 1桁丸め（`N1`）で表示するため、たとえば `70,000 ticks`（`1.166日`）の出来事は画面上 `1.2日目` と表示されるにもかかわらず、開始日に `1.2`（`72,000 ticks`）を入力すると厳密ティック比較で除外され、目の前に `1.2日目` と書かれていた行が消える。
- **修正方針**：
  - 小数カンマ正規化・逆転範囲の扱いを両クラスで統一し、年表フィルタの日数境界も表示丸め単位または半開区間丸めと整合させる。

#### 2.1.7 [Medium] 除外コロニーのグラフ空表示、グループ選択時の `scopeName` 欠落、HTML 出力の切替レース、重複ウィンドウ積層
- **対象コード**：
  - [MetricCalculator.ChartHistory.cs](../../Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs#L76)、[MetricChartSeries.cs](../../Source/RimworldStatistics.Core/Stats/MetricChartSeries.cs#L16-L51)
  - [ColoniesTab.cs](../../Source/RimworldStatistics/UI/ColoniesTab.cs#L467)、[Dialog_ColonyChronicle.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyChronicle.cs#L189)
  - [Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L126-L133)、[Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L237-L246)、[Dialog_ColonySummaryReport.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs#L502-L516)
- **問題点**：
  1. 集計除外（`Excluded`）に設定したコロニーの総括レポートや年表から履歴グラフを開くと、`MetricCalculator.ChartHistory` 側の `c.Counted` 条件に阻まれて空グラフになる。
  2. `ColoniesTab` および `Dialog_ColonyChronicle` から `Dialog_ColonySummaryReport` を開く際に `scopeName` を渡していないため、コロニーグループで絞り込んで順位が `#1/3` になっていても、ヘッダーや出力 HTML の対象範囲が「すべてのコロニー」と誤記される。
  3. 総括レポートで HTML 出力のバックグラウンドタスク実行中に別コロニーへ切り替えると、完了通知に世代／`colonyKey` 照合がないため別コロニーの出力結果として表示される。
  4. 総括 ⇄ 年表 ⇄ セーブ系統の相互ボタンが既存ウィンドウのフォーカス再利用を行わず毎回 `Find.WindowStack.Add` するため、同種ダイアログが何重にも積層する。

#### 2.1.8 [Medium] 新規ダイアログ（`#363` / `#364`）の IMGUI 例外安全性・エラー画面表示の不統一
- **対象コード**：
  - [MetricPickerDialog.cs](../../Source/RimworldStatistics/UI/Widgets/MetricPickerDialog.cs#L74-L98)
  - [Dialog_MetricScatterPlot.cs](../../Source/RimworldStatistics/UI/Dialog_MetricScatterPlot.cs#L78-L90)
  - [Dialog_ColonyChronicle.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyChronicle.cs#L100-L113)
- **問題点**：
  - `MetricPickerDialog` は `Widgets.BeginScrollView` 内で外部コールバック `onPick` を呼び、`Text.Anchor` を変更しているが `try/finally` で保護していない。
  - `Dialog_MetricScatterPlot` は `try/finally` のみで `catch` を持たず、`Dialog_ColonyChronicle` は `catch` 後に `"RWS_WindowError"` を描画しないため例外時に無言の空白ウィンドウになる。

---

### 2.2 オープン Issue（`#357` / `#358`）および既存レビューの未解決不具合（新機能への波及含む）

各不具合の詳細な背景は [2026-11-17-full-codebase-review-ja.md](2026-11-17-full-codebase-review-ja.md) も参照。`17e4934` 時点でも全件未修正であり、一部は `#364`（散布図）へ新たに波及している。

#### 優先度：High（3件）
1. **分岐親チェックポイントの毎セッション反復アーカイブ・不要復元ループ**（`#357`）
   - [CheckpointArchivePlanner.cs](../../Source/RimworldStatistics.Core/Profiles/CheckpointArchivePlanner.cs#L15-L58)、[ProfileSession.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileSession.cs#L192-L329)、[ProfileMutations.Checkpoints.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileMutations.Checkpoints.cs#L76-L99)。非競合子と `Conflicted` 子の両方を持つ親が毎セッション `.jsonl` 追記と復元を繰り返し、`MetricsArchived == false` でも `.jsonl` 全走査が発生する。
2. **RimWorld 1.7 非同期 `LongEvent`（別スレッドでのマップ生成・NPC ポーン生成）による統計ファミリーの恒久的 `Partial` 劣化・競合**（Issue 未起票）
   - [LiveMetrics.cs](../../Source/RimworldStatistics/Metrics/LiveMetrics.cs#L95-L109)、[GameFacts.cs](../../Source/RimworldStatistics/Patches/Metrics/GameFacts.cs#L67-L88)、[CaravanMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/CaravanMetricPatches.cs#L257-L310)（`ShrineProbe.Watched` のスレッド競合）、[OdysseyMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/OdysseyMetricPatches.cs#L98-L113) ほか。別スレッドまたはロード中のパッチ発火により `LiveMetrics.Resolve` が失敗扱いとなり、無関係な統計まで恒久的に `Partial` に劣化する。
3. **既存タブ・ダイアログ全般の IMGUI `BeginScrollView` / `BeginGroup` / グローバル描画状態の `try/finally` 欠落**（`#358`）
   - [DetailsTab.cs](../../Source/RimworldStatistics/UI/DetailsTab.cs)、[ComparisonPanel.cs](../../Source/RimworldStatistics/UI/ComparisonPanel.cs#L131-L216)、[RankingsTab.cs](../../Source/RimworldStatistics/UI/RankingsTab.cs#L287-L318)、[StatsTable.cs](../../Source/RimworldStatistics/UI/Widgets/StatsTable.cs)、[Dialog_MetricChart.cs](../../Source/RimworldStatistics/UI/Dialog_MetricChart.cs#L108-L241)、[Dialog_ColonyMetricHistory.cs](../../Source/RimworldStatistics/UI/Dialog_ColonyMetricHistory.cs#L163-L253)、[Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L410-L498) 等。

#### 優先度：Medium（11件）
1. **セーブ XML 防御パースの不備**（`#357`）：[SaveReader.ChartHistory.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.ChartHistory.cs#L63-L75) の `OverflowException` 未捕捉、[ImportProblems.cs](../../Source/RimworldStatistics.Core/Import/ImportProblems.cs#L7-L16) の `FormatException`/`OverflowException` 分類（`Damaged` でなく `Unexpected`）、[SaveReader.History.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.History.cs#L152-L185) の `<def>Wealth_Total</def>` 長さ未制限・重複カウント漏れ。
2. **外部 SDK `ModMetricDescriptor.IsValid` の契約検証 3条件欠落**（`#357`）：[ModMetricDescriptor.cs](../../Source/RimworldStatistics.Core/Stats/ModMetricDescriptor.cs#L32-L44)。
3. **`MetricRankings.InputPeak` が `NotApplicable` コロニーを母数 `Targets` に含めて `Partial` 降格させる問題**（`#357`）：[MetricRankings.cs](../../Source/RimworldStatistics.Core/Stats/MetricRankings.cs#L244-L273)。
4. **エンディング専用コロニー（`!HasObservedPoint`）に対する `BasicMetrics.Value` の一括 `null` 返却と V2-D への波及**（`#357`）：[BasicMetrics.cs](../../Source/RimworldStatistics.Core/Stats/BasicMetrics.cs#L55-L71) に加え、**`#364` で新設された [MetricScatterPlot.cs](../../Source/RimworldStatistics.Core/Stats/MetricScatterPlot.cs#L259-L268)（`Read`）にもそのまま波及**しており、エンディング達成回数等を軸にしても対象から脱落する。
5. **最小値指標（`MoodMin`・最短到達日数）の `descending: true` 順位逆転・単位誤分類と V2-D への波及**：[ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L160-L282) で「最低心情が最も高いコロニー」が 1位になり `MoodMin` が `%` ではなく整数表示になる問題が、**`#364` の [MetricScatterPlot.cs](../../Source/RimworldStatistics.Core/Stats/MetricScatterPlot.cs#L252-L255) の軸単位分類にも波及**している。
6. **エンディング代表選定の `.ThenBy(e => e.Source)` 欠落・DevMode `SaveTrace` 二重出力**（`#357`）：[StatisticsExport.cs](../../Source/RimworldStatistics.Core/Export/StatisticsExport.cs#L170-L193)、[EndingsTab.cs](../../Source/RimworldStatistics/UI/EndingsTab.cs#L56-L88)、[ColonySummaryReport.cs](../../Source/RimworldStatistics.Core/Stats/ColonySummaryReport.cs#L117-L133)。
7. **`[MetricAdapter]` Harmony パッチ前処理（`MarketValue` 取得・デリゲート生成等）の `try/catch` 隔離漏れ**：[WorkMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs#L155-L210) ほか。
8. **小画面レイアウト潰れ・`ColoniesTab` のメインスレッド同期 `Mutate`・`SaveFileIndex.Read` の例外捕捉不足**（`#358`）：[ColoniesTab.cs](../../Source/RimworldStatistics/UI/ColoniesTab.cs#L358)、[Dialog_ColonySaveLineage.cs](../../Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs#L70-L78) ほか。
9. **比較パネル・詳細タブ・総括レポート間の値フォーマット／内訳ラベル乖離と `RankingsTab` 検索 `.Trim()` 欠落**（`#358`）：[ComparisonPanel.cs](../../Source/RimworldStatistics/UI/ComparisonPanel.cs)、[RankingsTab.cs](../../Source/RimworldStatistics/UI/RankingsTab.cs#L238-L241)。
10. **`check_mod_source_labels.py` の検査対象 XML 漏れと分割 XML 内の 12言語 MOD ブランド名重複**（`#358`）：[check_mod_source_labels.py](../../tools/check_mod_source_labels.py#L50) が `RimworldStatistics.xml` のみを検査しており、[PopularModStatistics.xml](../../Languages/Japanese/Keyed/PopularModStatistics.xml)、[Batch2PopularModStatistics.xml](../../Languages/Japanese/Keyed/Batch2PopularModStatistics.xml)、[Batch4ModStatistics.xml](../../Languages/Japanese/Keyed/Batch4ModStatistics.xml)、[Batch5ModStatistics.xml](../../Languages/Japanese/Keyed/Batch5ModStatistics.xml) 等の分割 XML に含まれる `RWS_VFECatches`、`RWS_AlphaBiomes_HomeMaps`、`RWS_VleLandmarks_HomeMaps`、`RWS_RimTalk_Conversations`、`RWS_AlphaMechs_MechConversions`、`RWS_AnomaliesExpected_StudiedEntities`、`RWS_NewMod_RealRuins` 等の MOD 名重複を検出できていない。
11. **`.gitignore` への `.env` / `*.env` / `r2_credentials.env` 未登録、および `check_sdk_package.py` の配布 ZIP ホワイトリスト未実装**（`#358`）：[.gitignore](../../.gitignore)、[check_sdk_package.py](../../tools/check_sdk_package.py)。

#### 優先度：Low（5件）
- [OptionalTransitionRecording.cs](../../Source/RimworldStatistics.Core/Stats/OptionalTransitionRecording.cs#L15-L38) の同値書き込み時リビジョン更新、[FishingFamily.cs](../../Source/RimworldStatistics.Core/Stats/Families/FishingFamily.cs#L48-L75) の飽和加算時の内訳伝播、[SnapshotChartSeries.cs](../../Source/RimworldStatistics.Core/Model/SnapshotChartSeries.cs#L47-L64) の `Enum.IsDefined` 未検証、[ProfileMutations.SnapshotCompaction.cs](../../Source/RimworldStatistics.Core/Profiles/ProfileMutations.SnapshotCompaction.cs#L65-L115) の一時割当、[WorkMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs#L14-L25) `HarvestProbe` のセッション終了時クリア漏れ。

---

## 3. コード・UX・アーキテクチャ・CI・公開資料の改善点

1. **公開ドキュメントの最新機能（PR `#355`〜`#370`）への更新**
   - [README.md](../../README.md)、[steam-workshop.md](../steam-workshop.md)、[About.xml](../../About/About.xml) が「タイトル画面の統計ボタンから開く 7タブ構成」のまま止まっており、**プレイ中メニューからの起動（`#368`）、年表（`#363`/`#369`）、セーブ系統（`#355`）、総括レポート＆HTML 出力（`#360`）、散布図（`#364`）、履歴拡大（`#367`）、コロニー表示名・お気に入り（`#370`）** が未反映である。利用者向けの説明を現行仕様に合わせる価値が高い。
2. **コロニー切替 UI の検索付きピッカーへの統一**
   - `#370` で [ColonyPickerDialog.cs](../../Source/RimworldStatistics/UI/Widgets/ColonyPickerDialog.cs) が追加されたが、`Dialog_ColonySummaryReport`、`Dialog_ColonyChronicle`、`Dialog_ColonySaveLineage`、`ComparisonPanel`、`Dialog_MetricChart` のコロニー選択は素の `FloatMenu` のままであり、数十コロニー蓄積時に画面外へ溢れる。これらを `ColonyPickerDialog` に統一し、`Dialog_ColonyGroups` にも検索欄を追加することで操作性を揃えられる。
3. **CSV / HTML 出力における「元の派閥名（`FactionName`）」の保持**
   - [ExportPanel.cs](../../Source/RimworldStatistics/UI/ExportPanel.cs#L68) がカスタム表示名を `colony_name` に渡すため、表示名変更後の `colonies.csv` からゲーム内の元派閥名が失われる。既存列順を壊さず末尾に `faction_name` 列を追加（総括 HTML にも元派閥名を併記）することで、外部集計と可読性を両立できる。
4. **MOD 連携アーキテクチャ（3方式混在）の整理と自動 IL 監査・R2 Windows 最新版参照の拡充**
   - 現在 ① `BuiltInModMetricCatalog`、② `ExternalSdkHost`（4,096 ダイジェスト上限あり）、③ `IntegratedTransitionAdapters` / `ModProviderAdapters`（約 95 MOD）の 3経路が混在している。
   - また [IntegratedModAudit](../../tools/IntegratedModAudit) は初期 11 パッケージのみを対象としており、[fetch_dev_inputs.py](../../tools/fetch_dev_inputs.py) も R2 の `Game-Linux`（`1.7.4985`）固定となっている。R2 `Game-Windows`（`1.7.5011 rev24`）の最新メタデータ対応と、アダプタ型・メソッド署名のスモーク検証拡大が望ましい。
5. **汎用作業統計における「コロニー囚人労働・奴隷」の帰属判定整理**
   - [WorkMetricPatches.cs](../../Source/RimworldStatistics/Patches/Metrics/WorkMetricPatches.cs) 等の `pawn.Faction == Faction.OfPlayerSilentFail` 条件では、コロニー管理下の囚人（`pawn.IsPrisonerOfColony`、`Prison Labor` 等）による生産・採掘・収穫が自コロニー成果から漏れる。コロニー労働主体の共通判定ヘルパーへ整理する余地がある。
6. **GitHub Actions・テストスクリプトの微調整**
   - [numeric-verification.yml](../../.github/workflows/numeric-verification.yml#L225) が定期実行限定の `rws-profile-benchmark.csv` を通常 PR でも `upload-artifact` 対象にして未検出警告を出している点の条件分岐、および [test_colab_notebooks.py](../../tools/tests/test_colab_notebooks.py#L9-L13) のファイル名ハードコードの `glob` 化。

---

## 4. 追加できる機能（既存機能との重複なし・統計原則準拠）

> **前提確認**：V2-A（年表）、V2-B（セーブ系統）、V2-C（総括レポート・HTML 出力）、V2-D（2指標散布図）、履歴ズーム、プレイ中起動、コロニー表示名・検索・コロニー別お気に入り、HAR/Biotech の加入・死亡種族内訳はすべて実装済みのため除外。また過去に不採用確定となった V2-E（通算自己ベスト更新履歴の擬似復元）も除外している。

### 4.1 軸1：新規バニラ 1.7・公式 DLC 統計 ＆ 過去 `.rws` セーブデータ活用

#### 4.1.1 【最有力・過去 `.rws` 対応】通常セーブ内に常時保存されている `ThreatPoints`（襲撃脅威ポイント）＆ `Adaptation`（適応係数）の時系列グラフ復元
- **R2 実証根拠**：RimWorld 1.7 の `HistoryAutoRecorderGroupDefs` にある `Debug` グループ（`ThreatPoints`：0.5日刻み・内部 1/10 スケール保存、`Adaptation`：1日刻み）は `devModeOnly: true` のためバニラの履歴タブには表示されないが、**通常プレイの `.rws` セーブ内の `<history><autoRecorderGroups>` にも常時記録・保存されている**ことを実セーブで確認済み。
- **価値**：MOD 導入前の過去セーブを含め、[SaveReader.ChartHistory.cs](../../Source/RimworldStatistics.Core/Reading/SaveReader.ChartHistory.cs) を拡張するだけで「資産・人口推移に対してストーリーテラーの襲撃脅威ポイントや適応係数がどう推移していたか」を可視化できる。

#### 4.1.2 HAR / Biotech 種族・ゼノタイプ統計のギャップ補完（3点）
1. **ゼノタイプ別在籍ピーク**（自由入植者・囚人・奴隷）：[RosterSnapshot.cs](../../Source/RimworldStatistics.Core/Stats/RosterSnapshot.cs) には種族別ピーク（`FreeSpeciesPeak` 等）があるがゼノタイプ別ピークが未実装。`.rws` の `<genes><xenotype>` から過去セーブも取得可能。
2. **出産（`membership.human_birth`）の種族別・ゼノタイプ別内訳**：現在はスカラー件数のみで、コロニー誕生児の種族・ゼノタイプ内訳が空白となっている。
3. **カスタム／ハイブリッドゼノタイプ（`CustomXenotype`）の予約バケット集約**：現在 [GameFacts.cs](../../Source/RimworldStatistics/Patches/Metrics/GameFacts.cs#L39-L45)（`GameFacts.Xenotype`）等で `xenotypeName` を持つカスタムゼノタイプが `null` を返すため、**カスタムゼノタイプが 1人でも加入・死亡するとゼノタイプ内訳全体が `Partial` に降格する**。予約キー（例：`RimWorld/CustomXenotype`）へ集約することで完全性を維持できる。

#### 4.1.3 【Anomaly DLC】専用統計ファミリーの新設（現在公式 DLC で唯一 0件の空白領域）
- **過去 `.rws` 対応スナップショット**：
  - `<entityCodex><discoveredEntities>` による発見済みアノマリー実体種類数（全 `EntityCodexEntryDef` に対する進捗）
  - `GameComponent_Anomaly.highestLevelReached` によるモノリス最大覚醒段階
  - 拘束プラットフォーム上の収容中エンティティ数ピーク
- **ライブ確定イベント**：
  - `PsychicRitual` 完了数 ＋ `PsychicRitualDef` 別内訳（※バニラ Anomaly だけでなく Anomaly 系 MOD の追加儀式も共通基盤で一括捕捉可能）
  - `CompHoldingPlatformTarget.Escape`（収容実体の脱走回数）・新規拘束完了数、`PitGate` 攻略崩壊数

#### 4.1.4 【Odyssey DLC】グラヴシップ規模・燃料消費・軌道（Orbit）到達・ランドマーク訪問・ユニーク武器
- 現在の [OdysseyFamily.cs](../../Source/RimworldStatistics.Core/Stats/Families/OdysseyFamily.cs)（発進・着陸・漁獲の 3指標）に加え、R2 `1.7.5011` DLL で確認できた以下の確定指標を追加可能：
  - `Building_GravEngine` によるグラヴシップ床面積（`validSubstructure` セル数）ピーク・推進器数ピーク
  - 発進時のケムフューエル消費累計（`ConsumeFuel(float)`）
  - `PlanetTile.LayerDef` による軌道層（`Orbit`）到達回数
  - `WorldLandmarks`（全 51種 `LandmarkDef`）別の訪問タイル内訳
  - `CompUniqueWeapon` 保有数ピークおよび `WeaponTraitDef` 内訳

#### 4.1.5 【Biotech DLC】3大メカニターボス召喚・撃破実績（過去 `.rws` 対応）＆培養槽・遺伝子工学イベント
- 実 `.rws` の `GameComponent_Bossgroup`（`<killedBosses>`、`<timesCalledBossgroups>`：`Diabolus` / `Warqueen` / `Apocriton`）から過去セーブを含めて取得でき、コロニー年表（V2-A）のマイルストーンにも統合可能。
- ライブ側では `Building_GrowthVat` 育成完了数、`Building_GeneExtractor.Finish`（遺伝子抽出完了数）、`Building_GeneAssembler.Finish`（ゼノジェルム合成完了数）、遺伝子移植完了数を追加可能。

#### 4.1.6 【Ideology / Royalty / バニラ】未収集の主要確定イベント・内訳・環境情報
- **Ideology**：`GenGuest.EnslavePrisoner`（囚人奴隷化回数 — 既存「奴隷解放数」の対になる入口）、流動イデオロギー改革回数（`IdeoDevelopmentTracker.reformCount`、過去 `.rws` 対応）、`CompBiosculpterPod` サイクル別完了数（治療・若返り・快楽）、ガウランレン木接続・聖遺物収集数。
- **Royalty**：バニラ超能力（`Psycasts`）の `AbilityDef` 別内訳、帝国許可証（`PermitsUsed`）の `RoyalTitlePermitDef` 別内訳、アニマの木リンク儀式完了数（`CompPsylinkable.FinishLinkingRitual`）。
- **バニラ戦闘・動物・環境**：襲撃撃退数・撃破数の敵派閥 `FactionDef` 別内訳、動物（調教・誕生・屠殺・狩猟）の動物種別内訳、自勢力メカノイドの `ThingDef` 別ピーク、`.rws` の `<world><info>`（気温・降水量・汚染度・マップサイズ）抽出、打ち切りなしの永続 `Tale` 3種（`TileSettled` / `Marriage` / `MajorThreat`）の年表（V2-A）補助表示（※数値合算には非使用）。

---

### 4.2 軸2：新規 UI・分析・エクスポート・診断・外部 SDK 機能

1. **キーボードショートカット（`KeyBindingDef`）＆通算統計から「現在のコロニー」への逆ジャンプ導線**
2. **属性条件による「スマートグループ（動的 `ViewScope`）」**（ストーリーテラー別、パーマデスのみ、クリア済みのみ、生存 N 日以上、バイオーム別等の自動フィルタ）
3. **散布図（V2-D）の属性別カラーリング切替**（データ状態／エンディング到達有無／パーマデス／難易度／ストーリーテラー ※回帰直線・相関係数は出さない原則を維持）
4. **`1日あたり変化率（/day）` の比較パネル・ランキング・散布図への展開**（既存の厳密な観測区間計算 [MetricRates.cs](../../Source/RimworldStatistics.Core/Stats/MetricRates.cs) を詳細タブのツールチップ以外にも展開）
5. **長大履歴グラフ（`Dialog_ColonyMetricHistory`）のピーク保持 Min-Max Envelope デシメーション**（1px 幅あたり最初・最小・最大・最後の最大 4点保持により、真のピーク値・谷値・欠測ギャップを 100% 維持したまま数千点描画を軽量化）
6. **コロニー年表（V2-A）の単体 Markdown / HTML 出力 ＆ 総括 HTML レポート（V2-C）への統合**
7. **時系列履歴 CSV（`colony_metric_history.csv`）の追加出力**（既存 5 CSV を非破壊維持したまま 6ファイル目として追加し、Colab 等での時系列曲線分析に対応）
8. **PII（パス・コロニー名・秘密情報）ゼロの「匿名診断サマリ出力」**（ゲーム版・MOD 版・有効連携アダプタ数・セーブ状態件数・既知インポート失敗種別のみを出力し、不具合報告・競合切り分けを支援）
9. **外部連携 SDK v1.2 の後方互換拡張**（プロバイダ MOD 表示名のプロファイル永続化による MOD 削除後の `"External mod"` 表示解消 ＋ バケット上限付き内訳イベント記録 API）

---

## 5. 追加で対応できる MOD の全体整理と推奨実装ロードマップ

MOD 個別の詳細な評価表・Workshop リンク・技術ゲートは **[第7波候補・既存候補ティア分類台帳（2026-11-18）](../planned-mod-statistics/2026-11-18-seventhwave-and-tier-roadmap-ja.md)** および **[active-candidates.md](../planned-mod-statistics/active-candidates.md)** に保存している。要点は以下の通り。

1. **既存対応済み MOD（実質 105件：[supported-mods.md](../supported-mods.md) 103件 ＋ 内蔵作者 MOD 2件）の第2段階メトリクス候補**：
   - `Hospital`（完治退院患者数・実受領治療報酬銀）、`A RimWorld of Magic`（全クラス呪文/スキル AbilityDef 別発動数・クラス覚醒数）、`Dubs Rimatomics`（SACS 迎撃/砲撃完了数・スクラム/メルトダウン回数）、`Altered Carbon 2`（スリーブ育成・ニードルキャスト完了数）、`Alpha Mechs` / `Alpha Genes`（追加メカボス最終討伐数・遺伝子吸収/変異成功数）、`Empire Refactored`（実到着税収）、`Vanilla Gravship Expanded`（新規獲得 Gravdata 量）、`World Domination 2.0` / `Rim War`（プレイヤー部隊の戦闘勝利・拠点制圧数）。
2. **既存候補 58件の優先度ティア分類**：
   - **Tier 1（即戦力・高価値 12件）**：`S02` Gravship Voyage、`F06` SRTS Expanded、`R01` NewRatkinPlus（ハムスター発電輪）、`S31` Quarry、`S29` Mines 2.0、`S37` Biotech Cloning、`S41` Integrated Implants、`S42` Gene Remover、`S43` Gene Ripper、`S15` Rimesis、`S51` Psychology 1.7（2系統）、`S52` ISEKAI RPG LEVELING。
   - **Tier 2（1.7 配布 DLL の IL 監査対象 27件）**：Odyssey/多階層探索 7件、戦闘/ボス/メカ/戦略 8件、自動化/キャンペーン/種族派閥 3件、Anomaly/帝国/心理/教育/超能力 9件。
   - **Tier 3（汎用統計・`ModdedIncidentFamily` で網羅濃厚な整理対象 11件）**：`S05`、`S12`、`S17`、`S22`、`S23`、`S25`（高頻度ゾンビ死亡フック非推奨）、`S36`、`S40`、`S45`、`S57`、`S60`。
   - **保留 8件（`HOLD-01`〜`08`）**：`HOLD-08` WVC - Xenotypes and Genes、`HOLD-01` Medieval Overhaul、`HOLD-07` VAE Insanity が昇格有力。`HOLD-02`、`HOLD-04`、`HOLD-06` は対象外確定推奨。
3. **第7波・新規開拓 MOD 候補（`W01`〜`W12`、12件）＆ 1.7 復活ウォッチリスト（`RW01`〜`RW08`、8件）**：
   - `W01` Prison Labor、`W02` MedPod、`W03` Alpha Implants、`W04` Alpha Vehicles (Neolithic / Early Cars)、`W05` Biomes! Islands、`W06` Biomes! Prehistoric、`W07` RimNauts 2、`W08` Universum、`W09` RimQuest (Continued)、`W10` Custom Base Framework、`W11` Faction Traits: Quests & Visitors、`W12` Cash Register。

### 推奨実装フェーズ順序
- **フェーズ 1（品質・安全性修正）**：プレイ中スキャン抑止（§2.1.1）、非同期 `LongEvent` スレッドガード（§2.2 High-2）、分岐親アーカイブ無限ループ解消（`#357`）、単一コロニーラベル・最小値順位・IMGUI `try/finally`・分割 XML MOD ラベル修正（`#358` ＋ §2.1 各項）、Colab ノートブック修正（§2.1.3）、公開ドキュメント更新（§3.1）。
- **フェーズ 2（バニラ 1.7・DLC・分析機能拡張）**：過去 `.rws` の `ThreatPoints` / `Adaptation` 履歴復元、ゼノタイプ在籍ピーク・出産内訳・`CustomXenotype` 対応、Anomaly DLC 統計ファミリー新設、Biotech ボス実績、`/day` 展開・Min-Max Envelope 描画間引き・時系列 CSV。
- **フェーズ 3（MOD 対応拡大）**：既存大型 MOD の第2段階メトリクス追加、および Tier 1（12件）＋ 第7波上位（`Prison Labor` / `MedPod` / `Alpha Implants` 等）の実 DLL 監査・順次実装。
