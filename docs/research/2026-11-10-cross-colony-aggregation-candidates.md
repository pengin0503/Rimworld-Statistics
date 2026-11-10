# 全コロニー横断の合計・最多/最少・品目ランキング：追加方針と候補（2026-11-10）

**位置付け:** 既存の331統計定義を変更しない調査・仕様判断。2026-11-10の[横断性監査](2026-11-10-cross-colony-metric-scope-audit.md)における「スナップショットの合計を新規候補から原則除外」という解釈を更新する。対象mainの起点: `8f747ad6ab685b33556352685789d4e5fdb543ce`。**実装、削除、取得ゲート変更、タグ/リリース操作はしない。**

## 1. 採否の原則

Rimworld-Statisticsに適するのは**独立コロニーを横断して統合、比較、ランキング化した記録**であり、**イベントの全期間累計に限定しない**。各コロニーの保存状態を1件ずつ読むとしても、独立コロニー間で加算・最大/最小・種類別ランキング・記録保持コロニーを求めるなら有効な統計候補とする。

- **A. 生涯イベント実績:** 交易、収穫、売買品目、研究完了、加入など、実際に発生した回数・数量をキャンペーンの有効なチェックポイント系列から得て全コロニーで集約。
- **B. 横断スナップショット合計:** 各コロニーの代表保存時点で記録した人数・動物・在庫・稼働設備を、独立コロニー間で一回ずつ合計。これはプレイ中の同一日時の世界総量でも、全期間の延べ人数・生産/取得総量でもない。正式な表示は「各コロニーの記録時点の合計」等とする。
- **C. 最多/最少コロニー:** Bと同一の代表観測値から各独立コロニーを比較し、値、記録保持コロニー、記録時点、同率を表示する。**Bの代表時点比較**と、各コロニーの全観測を対象とした**観測上の歴代最高/最低**は区別する。
- **D. 品目別全コロニー合計・最多品目:** 作物・売却品・購入品などのDef別イベント累計をコロニー間でキーごとに合算し、その総数で1位を選ぶ。「ある一コロニーの最多品目」をそのまま「全コロニー最多品目」と呼んではならない。
- **E. 横断記録・比較:** 資産、研究、施設、医療、気候、DLC状態などの実測ピーク/最低、種類別分布、記録達成コロニー。

**依然として追加しないもの:** 単一セーブを読むだけで完結する「このコロニーの現在値/最新値」の新規重複行。新しい取得元がセーブ内に存在するという理由だけの表示追加はしない。元データが単一セーブ由来だからといって、**B/C/D/Eに加工して意味ある複数コロニー統計を作る提案まで禁止してはならない**。重要度・負荷・既存値との重複で採否を決定する。

## 2. 実コードで確認した現在の動作

- `Source/RimworldStatistics.Core/Stats/MetricCalculator.cs` の `BuildView` は独立コロニーごとに観測を対応付け、除外コロニーを除く。`OverallAggregation.Sum` は対象コロニーの既知の代表値を加算する。`Max/Min` は最多/最少の値と代表コロニーキーを返す。欠測は完全値の0として扱わず `Partial/Unknown` が必要。
- `Source/RimworldStatistics.Core/Stats/SnapshotCatalog.cs` の `population.snapshot.free/prisoners/animals/mechs`, `facilities.running_generators.latest`, `stock.food_nutrition.latest`, `stock.silver.latest`, `stock.materials.latest` は**すでに `OverallAggregation.Sum`** で登録されている。したがって対応するセーブ時スナップショットが取得済みなら**全コロニー合計の算出コードは存在する**。ただし各取得ユニットは未検証・既定OFFであり、現実の取得値があるとは限らない。
- `Source/RimworldStatistics.Core/Stats/LiveCatalog.cs` の `work.harvest.top_crop`, `trade.sales.top_item`, `trade.buys.top_item` は**`IndividualOnly`**。これらの「トップ」出力自体は現在全コロニー総合ランキングではない。
- 一方、`work.harvest.by_crop`、`trade.sales.units`、`trade.buys.units` は**`Sum` かつ品目別内訳**を持つ。各コロニーで作物別収穫量や売買品目別個数を保存できた範囲では、**内訳を全コロニーで合計して最多品目を決定できる**（全コロニー版の「トップ」を独立した新規収集フックなしで派生可能）。`WorkFamily.Harvested` は実際に取れた `units`、`TradeFamily.Traded` は `TradeLine.Count` を種類別に積算する。売買額 `TradeFamily.SalesTotal/BuysTotal` と売買個数を混同しない。
- `Source/RimworldStatistics.Core/Stats/StatsCalculator.cs` の `LastWealthSum` もすでに全コロニーの代表時点の最終資産合計。これは **Bの有効な一例**。累計で獲得した資産と誤認しない表示上の区別のみ検討する。現時点で削除・降格を要求しない。

## 3. 同種の追加候補（列挙した例に限定しない）

下表の「既存合計」はコード上の定義/集約が存在すること。「候補」は本対応で新規実装されたことを意味しない。

| 分類 | 該当する既存取得元・候補 | 全コロニー集約として認めるもの | 実装前の要点 |
|---|---|---|---|
| 作物・交易 | `work.harvest.by_crop`, `trade.sales.units`, `trade.buys.units` | Def別総量、横断最多作物、最多売却品/購入品、最多取引コロニー | 品目ID照合、累計の期間、4096キー上限時Partial |
| 製造・採掘・食事 | `work.crafting.completed_by_def`, `work.mining.units_by_def`, `work.cooking.by_def` | Def別全体総量、最多品目、最多完成/採掘コロニー | `work_details` は新規かつ実験的、物品の異名/定義比較 |
| 人口 | `population.snapshot.free/prisoners/mechs` | **既存合計**、最多/最少コロニー、全観測での歴代ピーク/ボトム | 最低0は実測値のみ採用、DLC不在を0と区別 |
| 動物 | `population.snapshot.animals`, `population.snapshot.animal_defs_peak` | **動物数は既存合計**、最多/最少飼育コロニー、種類数の横断記録 | 動物Defの違い、コロニー重複 |
| スキル・健康 | `population.snapshot.augmented_peak`, `population.snapshot.skill20_pawns_peak`, `medicine.injured_peak`, `population.snapshot.oldest` | 最多/最少、最高値/保持コロニー、適用可能なら合計 | 現在の`peak`は観測最大である点を維持 |
| 食料・銀・資材 | `stock.food_nutrition.latest/silver.latest/materials.latest` | **既存合計**、最大/最小保有コロニー、材料別横断合計、観測最高保有量 | 在庫量≠生涯取得/消費量、所持容器と重複除外 |
| 武器・薬品 | `stock.weapons.peak/weapon_defs.peak/legendary_weapons.peak/persona_weapons.peak/medicine.peak` | 全コロニー観測最大、最多/最少コロニー、必要なら代表時点合計 | 武器数と種類数、Royalty条件を区別 |
| 設備・電力 | `facilities.medical_beds.peak/turrets.peak/growing_cells.peak/rooms.peak/generation_capacity.peak/battery_capacity.peak`, `facilities.running_generators.latest` | **稼働発電機は既存合計**、各設備の最多/最少コロニー、観測最大、必要なら代表時点合計 | 最大値と同一時点の合計は別指標、ワット/ワット日混同禁止 |
| 研究 | `research.snapshot.finished/finished_rate/tech_peak` | **研究完了数は既存合計**、最多/最少完了コロニー、到達技術段階の最高記録 | 研究済みの種類数は各コロニーで別々に数える；歴史的な完了イベントと区別 |
| 派閥・外交 | `diplomacy.snapshot.relations/goodwill_range` | **関係件数は既存合計**、最多関係コロニー、最大/最小友好度等 | 保存済み関係は既存実装の限定的・Partialな対象 |
| DLC固有 | `biotech.pollution.cells_peak/bandwidth.used_peak`, `royalty.psylink.max` | DLC導入コロニー間の合計可能な代表値、最悪/最高記録と保持コロニー | DLC不在コロニーはN/A、使用中と上限値を区別 |
| 資産・人口履歴 | `wealth.items.latest/buildings.latest/pawns.latest`, `wealth.*.peak`, `population.mood.*` | 横断代表時点合計、最高/最低コロニー、歴代記録、記録分布 | 「最新値だけ」の新規出力は増やさず、既存系列の比較に付加価値がある時のみ |
| ゲーム設定 | `colony.mods.latest/peak/min`, `colony.start.biome` | MOD導入数最多/最少コロニー、開始バイオーム別コロニー数・プレイ日数 | 主たるプレイ実績より優先度低。キー同一性/DLC/MOD差分に注意 |
| 天候・地形 | `facilities.outdoor_temp.min/max` 等 | 最低/最高観測気温とコロニー、地形別記録 | 観測時の範囲・方法が違う値の無批判な比較禁止 |

**優先度:** (1) 既存の品目別内訳から全コロニーの最多作物/売買品を求める、(2) 人口・動物・メカノイド・在庫・稼働発電機の**既存横断合計を正確に表示・検証**し同じ入力の最多/最少コロニーを出す、(3) その他の既存スナップショットの横断記録と比較、(4) 新しい保存値・ゲーム内取得が必要な拡張。追加Harmonyフックより既存チェックポイント/セーブ読込を優先する。

## 4. 集計の意味・検証条件

- ある独立コロニーを複数回保存しても、**異なるセーブファイルの数だけ合算しない**。代表観測/直系チェックポイント/観測最大の使い分けを指標名で固定する。分岐した兄弟セーブのイベント件数を足さない。
- 種類別ランキングは `top(sum_colony(crop_count))`。**`sum(top_per_colony)` でも `top(single_colony)` でもない**。旧チェックポイントに内訳がない場合、総数が既知でも内訳ランキングはPartial/Unknown。
- 最多/最少コロニーは対象コロニー名・数値・可能なら観測時点・同率を保持。最少0は**実際に0を観測した対象コロニー**だけ。全コロニーのうち未観測・DLC非適用・除外の数も明示。
- 複数独立コロニーの状態値はゲーム内日付・セーブ日時が一致しない。**「各コロニーの記録時点の合計」**として表示し、同時保有量や累計獲得量として表現しない。
- 観測ピークはそのコロニーのデータがある区間の最大であり、未導入期間を含む歴代全期間の最大と断言しない。
- 既存331キー、プロフィール、外部MOD SDK、既定OFFの実験的取得ゲート、v1.3.0公開物は保持。具体的な実装と削除は別の承認事項。

## 5. 検証ケースの提案（将来）

独立コロニーA: 稲米10/ジャガイモ3、B: 稲米2/ジャガイモ20の場合、各自の最多品目は稲米・ジャガイモだが、**全体最多はジャガイモ23対稲米12**。同じAの直接親子セーブ3件があってもAを3回足さない。人口はA=10、B=5なら代表時点の**横断合計15、最多A=10、最少B=5**。Cが未観測なら合計15は既知2コロニー分だけでPartial、Cを0扱いしない。

既存参照: `MetricCalculator.BuildView/Combine/Aggregate`, `LiveCatalog`, `SnapshotCatalog`, `WorkFamily.Harvested/Build`, `TradeFamily.Traded/Build`, `StatsCalculator.Calculate`。
