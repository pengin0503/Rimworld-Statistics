# 生涯累計統計の拡張と既存表示の整理 — 設計（2026-11-10）

**状態（2026-11-10更新）：ユーザーがP0〜P2の実装を承認済み。ドラフト実装PR #88が進行中。以前の設計段階の実装禁止は現在の承認により失効。ただし、検証完了までmainへのマージおよび正式リリースは禁止。**

**対象：** pengin0503/Rimworld-Statistics、調査時 main `dbb374bacf4a0da3fe396cf060384d8a5cee8f41`（v1.3.0）。起点は [Issue #82](https://github.com/pengin0503/Rimworld-Statistics/issues/82)。実装開始前に main の差分を再確認する。

## 1. 目的と変更しないもの

ユーザーが最も見たいのは、ゲーム全編で実際に蓄積した「累計回数・個数・資源量・シルバー額」と「通算プレイ日数・脱出回数」である。「直近15日間」「1年あたり」「最大/平均/最小」「保存時点の在庫」などは主表示から分離し、ゲーム全編の累計を発見しやすくする。グラフは導入しない。

- 既存の `MetricKeys`、`MetricRegistry.Catalog`、`ProfileJson`、チェックポイント、データを消去・改変しない。過去の統計は全件「すべての統計」で引き続き閲覧可能。
- RimWorld 1.7、DLCのない環境、異なるMOD構成のセーブで動作する原則を保つ。Anomaly向け機能は必要条件にしない。
- 同じコロニーの複数セーブを合算しない。セーブの直系が証明できない分岐から、過去イベントを継ぎ足した累計を捏造しない。独立コロニー間の合計と同一コロニー内の継続値を区別する。
- 取得できなかった期間、導入前、無効化中、上限超過、MOD未導入、ゲーム版の相違は `Partial` / `Unknown` / `NotApplicable` に従い表示する。欠測を0で埋めない。
- `AcquisitionStatus.Unverified` は維持。新規取得ゲートも既定OFF。取得ゲートと表示可否は別の状態である。
- 毎Tickの全Pawn/全Thing走査、新規の高頻度ダメージ・射撃パッチはこの段階の対象外。

## 2. 既存コードと取捨選択

調査時の定義数は321：`MetricRegistry.Shipped` 57、`LiveCatalog.Definitions` 214、`SnapshotCatalog.Definitions` 41、`RankingDependencyCatalog.Definitions` 9。実ゲームで321個が検証済みという意味ではない。`LiveCatalog` 214件のうち`OverallAggregation.Sum`は154件だが、**Sumを生涯累計の判定に流用しない**（スナップショットの値を合算する別種のSumも存在）。

### 比較した3案

- **A：全項目をそのまま一覧表示し、新しいキーを増やす。** 最も単純だが同じ合計と内訳の二重表示が増え、主目的に反する。
- **B：実測済みの主累計を前面に置き、種類別の数値は既存合計の展開行に入れる。平均・期間・ピークは詳細に残す。** データの後方互換性が高く、重複行が少ない。**採用。**
- **C：従来の保存・取得処理を削除して累計だけに絞る。** 既存ユーザーの記録を失わせる。**不採用。**

## 3. 通算累計の意味・判定規則

`LifetimeCumulative` は単なる単位`Count`でも`OverallAggregation.Sum`でもない。**「成立済みイベントの蓄積件数または量であり、ある確定済みセーブの直系に沿って値が増える/変わらない記録」**を明示的な登録一覧で認定する。通算ゲーム日数は例外的に経過総量として認める。

- 採用：累計交易回数・売買品目数・販売/購入総量・売買総額・製作/採掘/植付/研究完了/撃破/治療の件数や量・入植者の加入/出生/死亡回数、確定エンディング回数、独立コロニーの代表プレイ日数など。欠測値の存在を認める。
- 原則除外：平均/比率/順位、最高/最低、○日間・○年あたり、日時・達成までの日数、最終在庫、同時人数、セーブ時点のDLC状態。最高記録は単調非減少でも**累積の総量ではない**。
- 登録一覧は明示的ホワイトリストとし、未分類の既存/外部MODの`MetricDefinition`を勝手に「累計」と認定しない。外部SDK統計はその提供元の明示的な意味付けを検証できるまで「すべて」に表示する。
- 1本の連続した計測系列ならイベント累計は減らない。ただしロードによる巻き戻し・分岐で表示対象となる代表セーブが変われば表示値が小さくなる可能性がある。これを隠すために異なる分岐の最大値や差分を合成しない。完全な全プレイ経路通算は別の新規設計である。
- 正式な画面ラベルは「通算（記録範囲内）」とし、`Complete`と`Partial`の違いを明示する。既存の`ThroughTick`/ `SinceTick` と `MetricEvidence`を維持する。

## 4. P0：取得処理を増やさず、既存の保存済み内訳を表示

**新しいHarmony Patch・新しいFamily・新規永続カウンターなし。** 既存の親メトリクスを展開可能にする。後方互換の観点から新しい別名出力を増殖させない。

| 親の既存キー | 新しく見える内訳 | 正式な取得元 | 注意 |
|---|---|---|---|
| `trade.sales.units` | 品目Def別の累計売却個数 | `TradeFamily.Traded`: `s.CountBy(Sold, l.Def, l.Count)` | `Sold.Sum`は**売却額**であり個数ではない。親値は従来の`SoldUnits.N`を使用 |
| `trade.buys.units` | 品目Def別の累計購入個数 | `s.CountBy(Bought, l.Def, l.Count)` | `Bought.Sum`は購入額。親値は従来の`BoughtUnits.N` |
| `work.crops.sown` | 作物Def別の累計植付回数 | `WorkFamily.Sown`: `s.CountBy(Sow, cropDef)` | 植付した**株の数**。栽培済みの収穫量・種類数とは別 |
| `medicine.surgery.count` | 成功回数・失敗回数 | `MedicineFamily.Surgery`: `Surgery_.N` と `SurgeryOk.N` | 失敗は`総数-成功`。結果確定済み手術のみ。既存成功率は詳細に維持 |
| `trade.count` | 交易形態別の成立回数 | `s.CountBy(Deal, traderKind)` | 既存 `trade.orbital.count` 等の値を変更せず、通常画面では内訳に折りたたむ |

親値の保存キー/スカラー/意味を変更せず、`LiveCatalog`の既存定義で展開可否だけを指定し、`FamilyOutputContext.PutBreakdown`に親スカラーと子辞書を渡す設計。既存出力の`MetricKeys.Revision`を安易に上げず、まず実際の保存と再読込み経路で互換性を証明すること。

上限は `FamilyOps.KeyCap=4096`。辞書は上限時に新しいキーを落とすが、独立した親総量は継続しうる。したがって、**内訳が欠けた場合だけ内訳がPartial、親は実際の計測範囲に応じたcoverage**とし、`sum(内訳) != 親`を無理に調整しない。未知Defの表示は「不明」とし推測しない。旧セーブでfamilyそのものが無ければ欠測として表示し、0を作らない。

### 4.1. 既存プロファイルの再読込み契約（重要）

チェックポイントのファミリー原データは .rws 内の `numericStatistics` にあり、`CheckpointMetrics.ToObservation` がそこから`MetricObservation.Metrics`を生成する。一方、`Profile.MetricObservations`には**既に計算した出力**が保存され、`ImportEngine`は`ProfileMutations.NeedsRead(..., SaveReader.Version)`に従って未変更ファイルをスキップする。従って**P0でfamily.Buildを変更しただけでは既存プロファイルの内訳が自動補完されると保証できない**。

実装時には、`SaveReader.Version`（現行9）を新しい出力形式に対応させて更新するか、同等の安全な再評価機構を用意する。推奨は一度だけのReaderVersion更新で、重複インポートやファイル改変をしないこと。既存の読込失敗時は良好な古い観測値を維持し、スキャン済みセーブでもチェックポイントを再構築できることをテストで証明する。読み取り元の.rwsがなく、旧プロファイルに内訳がない場合は回復不能なUnknownとし、親のカウントは維持する。なお`WorkFamily`・`TradeFamily`・`MedicineFamily`の**ファミリーRevisionは上げない**。

## 5. 表示整理（データを削らない）

現行 `DetailsTab` の全コロニー/個別コロニー・カテゴリ・検索・並べ替え・内訳展開を維持したうえで、一覧表示モードを追加する。新規タブの複製は不要。

- **「通算」（初期表示）**：第3節で認定された累計のみ。主行は合計、種類別件数は展開する。既存の`OverviewTab`の機能・エンディング表示は削除しない。
- **「その他」**：累計以外の記録（期間・平均・率・ピーク・ランキング・スナップショット・到達日数・MOD数）。
- **「すべて」**：従来と同じ全定義を検索・閲覧可能。取得ゲート、対象DLC、`Partial`/ `Unknown`表示を変えない。旧キーや設定を削除しない。

モード変更時にはキャッシュ済み行を再構築するのみとし、新しいゲーム内スキャンや保存は発生させない。既存UIで折りたたみ表示するときは親・子の同一数字を表示上で二重合計しない。外部MODの統計は勝手に既知の通算へ混ぜず「すべて」の中に保持。

明示的な通常画面除外例：`wealth.total.max_gain_15d`, `wealth.total.max_loss_15d`, `wealth.total.max_gain_half_day`, `wealth.total.max_loss_half_day`, `wealth.total.max_loss_rate_half_day`, `wealth.total.max_growth_rate_year`, `wealth.total.first_100k`, `wealth.total.first_1m`, `defense.raids_per_year`, `population.deaths_per_year`、平均心情、保存時点の在庫。いずれも**キーは残す**。

## 6. P1：既存の低頻度イベントを使うが新規保存が必要

P0が受け入れられた後に別変更として実装を検討する。

- 製作物Def別の累計製作個数：`WorkFamily.Crafted(workerId, products, tick)`の`CraftedThing.Def/Count`から取得。
- 採掘資源Def別の累計採掘個数：`WorkFamily.Mined(workerId, def, units)`の引数から取得。
- Bill完了回数（過去候補`N3-W-012`は原則除外済み）：別集計の実用性が確認できた場合のみ再審査。既定では採用しない。

`WorkFamily`のリビジョンを単純に増やすと、`LiveMetricAccumulator.Resume`は古い記録を**継続せず新規開始**する（`NumericCheckpointTests.Resume_AnotherRevision_StartsOverAndNeverMixesDefinitions`で確認済み）。旧workの累計を消さないため、候補：独立した小さな新Family（仮`work_details`、revision=1、取得ゲートはUnverified）、既存の成功時イベントだけを共有し、単独の起点をPartialとして明示する。既存`work`の取得処理と独立したON/OFF時でもHarmonyを二重適用しないよう実装前に調査する。別案（既存Familyへの互換フィールド追加）は移行と連続性が証明できるまで不採用。

## 7. P2：新規フックや高頻度処理は調査・適格化が先

候補：清掃完了件数／除去した汚れ数、消火成功件数、完了した食事摂取件数、伐採獲得木材数、運搬完了件数と量。以下を満たした項目だけ採用する。

- RimWorld 1.7.4985の実ゲームソース/逆コンパイルと`RimWorld-for-mod-test`で、**確定イベント・コール回数・所属マップ・MOD共存**を確認。自然消滅・雨・重複通知・仕事途中停止・NPCの行動が入植者実績に混ざらない。
- 既存の`B-006`/`B-007`（ダメージ累計）、`B-009`〜`B-012`（発射/命中）は過去候補で保留。高頻度パッチ・互換性・性能の根拠が揃うまで計画から外す。
- TPSと保存時サイズ/時間の基準測定を含む実証で許容可能な場合に限る。RimWorld-for-mod-testのA/Bは初期化・Harmony登録を確認するだけで、UI操作、ゲーム内ロード、実際のイベント計測やTPSを保証しない。実セーブ1.7.5000は1.7.4985ホストに逆方向ロードしない。

## 8. 受け入れ基準と非目標

1. P0の各内訳の正の件数・ゼロ件・欠測・4096キー超過・再ロード・分岐で、親の保存済み総数とスカラーの意味が変わらない。内訳の誤合算なし。
2. 「通算」に平均、15日間変動、スナップショット、ランキングは出ない。「すべて」で旧321定義とアドオンの既存出力を失わない。検索・並べ替えとコロニー切替が動作する。
3. 旧セーブ、MODの有効/無効切替、保存失敗、巻き戻し、複数セーブ・分岐、欠測、DLC無しで誤った完全値を出さない。ユーザーの原本セーブは読み取り専用。
4. 性能：新規毎Tick走査0、P0は新規Harmony0。P1/P2は各段階でメモリ・TPS・セーブ性能を別に判定。
5. 正式リリースv1.3.0は変更しない。設計PRの内容は資料だけとし、実装とマージ済みの区別を明確にする。

## 根拠

- 現行ソース：`Source/RimworldStatistics.Core/Stats/{MetricRegistry,LiveCatalog,SnapshotCatalog,MetricDisplayPolicy,LiveFamilies,LiveMetricAccumulator}.cs`
- 取得元：`Source/RimworldStatistics.Core/Stats/Families/{TradeFamily,WorkFamily,MedicineFamily}.cs`
- 表示：`Source/RimworldStatistics/UI/DetailsTab.cs`
- 調査：`docs/superpowers/specs/2026-11-07-statistics-expansion-{candidate-catalog,triage}.md`
- 既存検証：`Tests/RimworldStatistics.Core.Tests/{NumericCatalogTests,NumericCheckpointTests,NumericRemainingMetricsTests}.cs`

## 9. 2026-11-10 実装進捗と現行の承認条件

本仕様に残る「P0のみ」「P1/P2実装禁止」の記述は作成時の履歴であり、2026-11-10のユーザー承認で更新された。ただし、検証未完了・重大欠陥のまま正式リリースしてはならない。

- **P0**: 通算・その他・すべての切替、明示的累計ホワイトリスト、交易/作物/手術の5種内訳、Reader v10での旧チェックポイント再評価、日英UI。元のメトリクスとfamily Revision不変。通算主画面で旧取引形態カウンターを折りたたみ、Allには残す。
- **P1**: 独立family `work_details` (revision 1, Unverified, default OFF) で製作完了品Def別・実際に配置された採掘資源Def別の累計。旧workと独立して保存・計測。Bill完了回数は重複して付加価値が低く採用しない。
- **P2実装済・未実ゲーム検証**: 独立family `chore` (revision 1, Unverified, default OFF) で汚れの完全除去数、消火成功数、食事摂取数、保管セル配置による運搬完了件数と個数。コンテナ搬入等を含まない運搬2種は常にPartialとする。
- **P2追加実装・未実ゲーム検証**: 清掃ジョブ成功はジョブIDと実際に完全除去した汚れにより判定。伐採木材は主要産物の配置成功コールバックで実個数を取得。屠殺後の解体は屠殺時の死体IDを保存し、後続の標準食肉加工Billで同一死体を消費した場合だけ加算。木材と解体はMOD独自経路を除くためPartial。成立イベントの実ゲーム検証、MOD互換・TPSは未完了であり、検証済みと断言・正式リリースすることはできない。詳細は `docs/research/2026-11-10-lifetime-p2-source-evidence.md`。
- **Release条件**: CI成功だけでは不十分。旧セーブ再読込、セーブ分岐、実ゲームの正負イベント、DLC/MOD構成、TPS・allocations・保存時間の実測、レビュー、配布ZIP/SHA256/Workshop準備CIが通るまではmainへマージせずv1.4.0を作成しない。


## 10. 2026-11-10 追加方針の明確化（全コロニー横断性が必須）

新規に提案する「最新値／現在値／セーブ時点の状態値」は、単一セーブを読むだけで意味が完結するため**採用しない**。独立コロニーの最終状態を `Sum` で足しても、それだけではプレイ全編の累積記録とはならない。資産・自由入植者・囚人・心情・脅威ポイント等の最新読み取り値を新規追加する先の案は撤回する。追加の判断は「独立した複数コロニーを比較・集計することで初めて価値があるか」を基準とし、生涯累計、横断的な歴代記録（ピーク・最長・達成）、複数コロニーの比較・分布を優先する。

既存の各コロニー専用値、既存スナップショット、最終資産合計、平均・期間統計は**調査・表示整理の候補**とするだけで、現時点では削除・非表示・数値変更を行わない。現行331定義の具体的な分類と追加禁止候補は [全コロニー横断性の監査記録](../../research/2026-11-10-cross-colony-metric-scope-audit.md) を参照。取得可能性と採用可否は別に評価する。
