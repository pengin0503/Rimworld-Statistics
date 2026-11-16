# UI・ツールチップ・文章改善 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking. **本依頼では計画の保存まで。実装はしない。**

**Goal:** 通算統計の14件の画面改善候補と、12言語の文章短縮を、保存データ・集計ルールを変えずに段階的に実装する。

**Architecture:** 既存UI（DetailsTab、CoverageTab、ComparisonPanel、RankingsTab、Dialog_MetricChart、Dialog_ColonyMetricHistory）を局所的に改善し、Coreには実際に単体テストが必要な表示用の純粋関数のみ追加する。表示内容と内部識別は分離し、翻訳は既存の Keyed XML を維持する。大規模なUI作り直しはしない。

**Tech Stack:** RimWorld 1.7、C#/.NET 10、Verse/Unity IMGUI、Core xUnit、GitHub Actions numeric-verification.yml、12言語 Keyed XML。

**Spec:** [2026-11-16 UI・文章改善設計](../specs/2026-11-16-ui-and-copy-refinement-design.md)（**実装前に全文を読むこと**）。

## Global Constraints

- 最新 main と進行中PRを確認し、同じ改善が完了済みなら繰り返さない。基準コミットは 2c6e880a（PR #285）。
- 変更はUI・翻訳・UI表示専用の純粋関数・テストのみ。profile.json、ModSettings、チェックポイント、CSV、統計ID、取得フック、公開SDK、保存形式の意味を変更しない。
- 値0、欠測、対象外、部分記録、未検証を混同しない。データの誤集計を視覚的に隠さない。
- グラフはセーブ分岐の親子関係を証明できた点のみ接続。保存時スナップショットとイベントの通算値を混同しない。
- 取得状況では内部IDを通常表示から隠すが、内部の照合キーや取得可否は維持する。SDK容量超過／取得失敗などの重要警告は削除しない。
- 12言語でキーの同一性・引数の順序・HTML/XMLエスケープを維持。長文の削除は統計定義を壊さない場合だけ行う。
- リリースとWorkshopは更新しない。R2は読み取り専用。ゲーム内検証は可能な範囲で rimworld-for-mod-test を利用し、実施できなければ未検証と記録。
- 作業パッケージ単位でレビュー可能なPRを作り、CI成功後にマージ。途中で今のタスクと無関係な統計機能の追加をしない。

## Review Focus（主要5ケース）

1. **0件・1件・35件のコロニー**：同一数値の派生統計をまとめても欠測1/35を0/35や完全記録に見せない（Task 3）。
2. **長い名前・翻訳**：ドイツ語・ロシア語・中国語などの長い名前やRTLに依存しない表示（既存12言語）、狭い画面でもラベルの全文を確認できる（Task 4/5/9）。
3. **0・負数・極大/極小・Unknown**：目盛りが有限で0基準を維持し、欠測を棒・点の0にしない（Task 7/8）。
4. **同じtickの別セーブ分岐**：追加・削除した比較系列によって既存の系統グラフの接続規則が変わらない（Task 8/9）。
5. **UI操作の独立**：星を押してもグラフが開かず、展開を押しても行クリックが発動しない。条件リセット・折りたたみでスクロールが破綻しない（Task 3/4/6）。

## 準備・共通検証

1. 最新 main からブランチを作成。該当する Issue / PR、spec、既存 UI、既存テストを読んで優先する作業を決める。
2. 変更前の基準テストを確認する。環境にR2参照DLLがなくても UseNuGetRefs=true を使える。

~~~bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release --nologo -p:UseNuGetRefs=true
bash tools/check_keys.sh
~~~

3. 各タスクの手動確認を docs/manual-test-checklist.md に追記する。必要ならそのタスクのPRで変更する。
4. PRごとに「変更した画面／変更しない集計／テスト結果／ゲーム内未確認」を明記する。RimWorld 1.7実ゲーム・CIを混同しない。

---

## Track P1 — 文面・取得状況（最優先、先行して実施）

### Task 1：ツールチップ・グラフ短文の第2段（U03・U13）

**Files:** Modify: Languages/*/Keyed/RimworldStatistics.xml、Source/RimworldStatistics/UI/DetailsTab.cs、UI/Dialog_MetricChart.cs、UI/Dialog_ColonyMetricHistory.cs（必要な変更だけ）。Tests: tools/check_keys.sh、手動UI一覧。

**Interfaces:** 現行の翻訳キーと引数契約を保持する。新しい表示キーを増やす場合は全12言語で同時追加。

- [x] Step 1：現在の訳語を点検し、設計§4の8候補の旧文・新文・適用条件・参照箇所の一覧を確定する。RWS_Chart_LastCapture は「最も進んだセーブが同じ分岐とは限らない」を維持。
- [x] Step 2：キー・プレースホルダーの静的検証を先に書く（8キーの存在、{0}/{1}対応、12言語同数。適用できない項目は理由を記録）。旧文への依存があるテストがあれば先に失敗確認。
- [x] Step 3：全12言語を変更し、表とツールチップの重複（例：観測時点と記録コロニー数）を排除。説明が必要な定義は短文化しても削除しない。
- [x] Step 4：check_keys.sh、Coreテスト、ゲーム側ビルドを通し、翻訳幅の長い3言語以上を目視確認する。変更をコミット。

### Task 2：取得状況の表示名と初期展開（U04・U05）

**Files:** Modify: Source/RimworldStatistics/UI/CoverageTab.cs、Languages/*/Keyed/RimworldStatistics.xml。Optional Create: Source/RimworldStatistics.Core/Stats/MetricAcquisitionLabels.cs（純粋な分類が必要な場合のみ）。Test: Tests/RimworldStatistics.Core.Tests/MetricStatusTests.cs（純粋関数部分）、手動UIチェック。

**Interfaces:** データのGroupId（内部ID）と表示用のGroupLabel（翻訳済みテキスト）を別々に扱う。内部IDは展開キー・グループ識別に保持。

- [x] Step 1：GroupId、Acquisition、Provenance、SDK、DLC の各種類と取得状態の現行入力を一覧にし、ユーザーに見せる表示名称を決める。トレード等は分類名にし、内部キーは見出しに出さない。
- [x] Step 2：グループ順・重複表示名・未翻訳SDK提供元・空データ・検索時の展開状態の失敗ケースをテストで先に固定する。分類の同名表示でも別の取得単位を統合しない。
- [x] Step 3：Expanded.UnionWith(...) による全初期展開をやめ、初期折りたたみへ変更する。見出しに取得済み件数／対象項目数を出し、ユーザーが選んだ展開状態は維持する。
- [x] Step 4：ツールチップの技術ID列挙を整理するが、重要な失敗理由・MissingColonies・SDKイベント容量超過の警告は残す。
- [x] Step 5：12言語・狭いUIで確認、テスト・ビルド・check_keys.sh成功後コミット。

## Track P2 — 中心となる数値一覧

### Task 3：派生統計の任意グループ化（U02）

**Files:** Modify: Source/RimworldStatistics/UI/DetailsTab.cs。Consult: Source/RimworldStatistics.Core/Stats/CrossColonyCatalog.cs。Optional Create: Source/RimworldStatistics.Core/Stats/DerivedMetricGroups.cs。Test: Tests/RimworldStatistics.Core.Tests/DerivedMetricGroupTests.cs（新設する場合）。

**Interfaces:** 既存 MetricDefinition.Key、StoredKey、CrossColonyCatalog に基づいて基礎行と派生行を関連付ける。表示だけを並び替え、数値や元の NumericRow は作り変えない。

- [ ] Step 1：代表的な関連統計（購入額／売却額／収穫量／製作数／在庫など）を列挙し、max/min/sumの対応をキーで検証する失敗テストを書く。似た名前・同じ数値でも無関係な指標はまとまらない。
- [ ] Step 2：グループ化可能な場合の親行と折りたたみ子行を描画。子行は明示的に展開可能で、同一値の別指標も削除しない。
- [ ] Step 3：名前順の通常表示だけ初期グループ化。検索語がある、値順、コロニー別表示などで予測しづらい並び替えになる場合はフラットのまま表示（または同等の一貫性を持つ方式）。
- [ ] Step 4：星・展開・クリックで開くグラフのヒット領域が独立することをテスト／手動確認。0/1/35コロニー、部分／不明、異なる内訳あり、ソート切替、検索、再展開、スクロールを回帰確認。
- [ ] Step 5：Coreテスト・ビルド後コミット。

### Task 4：詳細表・色・フィルタ（U01・U06・U07・U08）

**Files:** Modify: Source/RimworldStatistics/UI/DetailsTab.cs、UI/OverviewTab.cs、UI/CoverageTab.cs（必要範囲）、UI/Widgets/ControlRow.cs。Optional Create: UI/Widgets/MetricVisualState.cs。Test: Tests/RimworldStatistics.Core.Tests/ColumnLayoutTests.cs、手動UI。

**Interfaces:** 画面の NumericRow / MetricCoverage / MetricEvidence に対する表示専用の短い状態ラベル・色を用いる（内部enumと記録は保持）。

- [ ] Step 1：右揃え数値・左揃え名称、状態別の色、チャートアイコン、フィルター解除、結果件数のテスト条件を定義し、ColumnLayout の幅・切れを検証。
- [ ] Step 2：同一スクロール座標内で行の列位置が一致するよう描画調整。長い値は省略して全文に到達可能とする。対応行にグラフアイコンを追加し、クリック領域を星と展開から分離。
- [ ] Step 3：状態は「完全／部分／未取得／実験的」を短い文字と彩度控えめな色で示す。強調色を装飾として増やさず、色なしでも意味が分かる。
- [ ] Step 4：検索・フィルターの条件、リセット動作、件数を実装。絞り込み変更時に旧行の描画・キャッシュ・スクロールが残らない。
- [ ] Step 5：小/標準/大ウィンドウ、長文訳、マウス操作を目視確認し、Coreテスト・ビルド・check_keys.sh後コミット。

## Track P3 — 比較・ランキング・全体レイアウト

### Task 5：比較表の固定名称列（U09）

**Files:** Modify: Source/RimworldStatistics/UI/ComparisonPanel.cs。Optional: Source/RimworldStatistics/UI/Widgets/StatsTable.cs。Test: UI手動回帰 + Core の ColumnLayoutTests。

**Interfaces:** 先頭列は左端に固定した描画領域、データ列は水平スクロール領域。両者は単一の縦スクロール位置と行索引を共有。

- [ ] Step 1：2列以上で横へスクロールした時も名前が見え、1列・狭幅・長名・スクロール位置変化で行対応がずれない受入条件を用意。
- [ ] Step 2：見出し／行の名前列を固定し、数値列だけ横移動。重なるツールチップ／クリック領域を作らない。
- [ ] Step 3：切れた名前に全文ツールチップを追加。ソート列変更・グループ選択・除外状態でも合計や値は元と同じ。
- [ ] Step 4：手動確認＋ビルド、コミット。

### Task 6：ランキングパネルと全体余白（U10・U14）

**Files:** Modify: UI/RankingsTab.cs、UI/Dialog_RimworldStatistics.cs、UI/Widgets/ControlRow.cs。Test: Tests/RimworldStatistics.Core.Tests/StatisticsWindowLayoutTests.cs、手動UI。

- [ ] Step 1：左パネル表示／非表示を切り替えても selectedKey、検索結果、順位表の縦スクロール・横幅が壊れないことを定義。
- [ ] Step 2：ランキングのパネル折りたたみと復帰ボタンを追加。値と順位の選択状態は変えず、最小幅で操作できる。
- [ ] Step 3：タイトル、タブ、フィルタの余白と選択状態のスタイルを整える。既存の「狭幅でタブをメニューにする」挙動を維持。
- [ ] Step 4：WindowLayoutの各プリセット、長い訳、0件、RimWorldのUIスケール変更で検証し、コミット。

## Track P4 — グラフ

### Task 7：棒グラフの目盛りとラベル（U11）

**Files:** Modify: Source/RimworldStatistics/UI/Dialog_MetricChart.cs。Optional Create: Source/RimworldStatistics.Core/Stats/MetricChartTicks.cs。Test: Tests/RimworldStatistics.Core.Tests/MetricChartTicksTests.cs。

**Interfaces（純粋なヘルパが必要な場合）:** MetricChartTicks.Generate(double min, double max, int targetSteps) : IReadOnlyList<double>。有効有限値と0基準の範囲を入力し、見やすい1/2/5×10^n刻みの有限な目盛りを返す（目盛りがデータを欠落させない）。

- [ ] Step 1：ゼロのみ、正のみ、負のみ、正負混在、極小・極大、単一点の系列で軸・数値が妥当な失敗テストを先に書く。
- [ ] Step 2：目盛り・補助線と数値ラベルを導入。0の基準線をより明瞭にし、正負それぞれの棒の向きを維持。
- [ ] Step 3：欠測・対象外を棒にしない。Partialは元の識別を維持。長い品目名・大きい値でも棒と値欄が重ならない。
- [ ] Step 4：CoreテストとUI目視、ビルド後コミット。

### Task 8：折れ線グラフの凡例・比較系列削除（U12）

**Files:** Modify: Source/RimworldStatistics/UI/Dialog_ColonyMetricHistory.cs。Test: Tests/RimworldStatistics.Core.Tests/MetricChartHistoryTests.cs、MetricChartLineageTests.cs、手動UI。

- [ ] Step 1：凡例が1行に収まらない場合、系列2件を追加／片方削除／全削除、追加読込途中で削除した場合の受入条件を定義。
- [ ] Step 2：凡例を折り返すかスクロール可能な表示域へ移す。追加コロニーに個別削除ボタンを設置し、色・選択セット・表示領域・レンジ・凡例キャッシュを更新。
- [ ] Step 3：削除した非同期読込結果があとから線を復活させないこと、観測点・欠測点・分岐の連結規則が変わらないことを回帰検証。
- [ ] Step 4：0点／1点／大点数、狭い画面と複数言語を確認し、Coreテスト・ビルド後コミット。

## Track P5 — 全言語統合確認

### Task 9：12言語、UI負荷、統合レビュー（U03・U07・U13・U14横断）

**Files:** Modify: Languages/*/Keyed/RimworldStatistics.xml、docs/manual-test-checklist.md（必要な部分のみ）。Tests: tools/check_keys.sh、GitHub Actions numeric-verification.yml。

- [ ] Step 1：全12言語の同一キー・プレースホルダーを自動照合。旧キーを削除するのはコード参照0件を確認してから。
- [ ] Step 2：日本語・英語・ドイツ語・ロシア語を優先して長文ツールチップ、比較列、凡例折返し、数値の右揃え、グラフアイコンを実画面で確認。
- [ ] Step 3：0／1／35コロニー、Unknown／0／Partial、内訳・除外・グループ、プロファイル大規模、空ランキング、保存時点／通算の表示差を確認。fps/tpsへの新たな負荷がないことを確認。
- [ ] Step 4：既存Coreテスト、ゲームビルド、check_keys.sh、CI全成功。ゲーム内未検証の箇所は正確に記載。
- [ ] Step 5：仕様・マニュアルとPRの完了条件を更新し、実装PRをマージ（Release / Workshop は触らない）。

## マイルストーン・依存関係

| 順 | タスク | 主な候補 | 依存 | 推奨PR |
|---|---|---|---|---|
| 1 | Task 1 | U03, U13, 文言8件 | なし | copy |
| 2 | Task 2 | U04, U05 | なし | coverage |
| 3 | Task 3 | U02 | なし（Task 4と同じファイルなので並列修正非推奨） | derived |
| 4 | Task 4 | U01, U06, U07, U08 | Task 3の後 | details |
| 5 | Task 5 | U09 | なし | compare |
| 6 | Task 6 | U10, U14 | なし | layout |
| 7 | Task 7 | U11 | なし | bar-chart |
| 8 | Task 8 | U12 | なし | history-chart |
| 9 | Task 9 | 全体 | Task 1〜8 | integration |

同じファイルを触る Track 間は直列マージか、最新 main へのリベースをする。全候補を一括PRにする必要はない。UIと文章をまとめて確認したい場合でも、レビュー可能な単位で完了させる。

## Done（実装時のみ）

- [ ] 選択した候補について画面操作と受入条件が成立し、各PRに差分・テスト根拠がある。
- [ ] 機能面の回帰なし（統計数値・意味、取得状況、保存データ、エクスポート、セーブ分岐の解釈）。
- [ ] 全12言語のキー整合とゲームビルド成功。手動確認の実施／未実施を分けて報告。
- [ ] GitHub Release / Steam Workshop / Cloudflare R2 の変更なし。

**本計画の作成タスクはこの文書をリポジトリへ保存した時点で完了し、以上の実装チェックボックスは未着手のまま残す。**
