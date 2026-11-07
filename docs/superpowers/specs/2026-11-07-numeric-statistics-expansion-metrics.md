# 数値統計の確定台帳

[詳細設計](2026-11-07-numeric-statistics-expansion-design.md) · [取得元・検証条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md) · [実装計画](../plans/2026-11-07-numeric-statistics-expansion.md)

一次整理の維持122件・統合201件を1行ずつ掲載する。保留37件・原則除外56件は一次整理を参照。採用状態は今回の取得可否であり、一次分類を変更しない。候補IDは仕様参照専用。キーの `+` は複数出力、`=ID` は同じ取得・集計を共有する表示上の別名または絞り込み。別名は別カウンターを作らない。

- **採用**は設計と取得方法が決まった行。対応版の試験を通過してから表示を有効化する。
- **条件付き**は、取得元資料に定めた条件を満たした行だけを有効化する。未成立時は当該行を表示しない。代替の推定値・疑似イベントを実装しない。
- **見送り**は取得根拠が不足しており、A〜Dの実装・リリース条件から外す。一次整理の元IDは保持する。
- 各節のリンクは取得方法と旧セーブ可否を定める。高・中・低は原案の有用性順位で、同じ作業単位の表示順に使う。実装順はPhaseと検証条件を優先する。
- 回・個・人・頭・台・種類は整数、日数は tick÷60000、年数は tick÷3600000。金額はシルバー相当、率は割合の小数を％表記。期間のない平均・分母0・証拠不足は `—`。個別値から全体値への共通規則は詳細設計 §3 を参照。


## Phase A · wealth

採用 · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#wealth)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `H-001` | アイテム資産の最高額・最終額 | 資産・成長 | 高 | `wealth.items.peak+wealth.items.latest` | Wealth_Items の最大と末尾。通算は最高1件と各代表末尾の合計 |
| `H-002` | 建築物資産の最高額・最終額 | 資産・成長 | 高 | `wealth.buildings.peak+wealth.buildings.latest` | Wealth_Buildings の最大と末尾。同上 |
| `H-003` | 人員・動物資産の最高額・最終額 | 資産・成長 | 高 | `wealth.pawns.peak+wealth.pawns.latest` | Wealth_Pawns の最大と末尾。同上 |
| `H-004` | 総資産の平均値 | 資産・成長 | 高 | `wealth.total.mean` | Wealth_Total の有効観測値合計÷点数。全体は点数加重 |
| `H-006` | 初期資産 | 資産・成長 | 高 | `wealth.total.first` | 開始整合した Wealth_Total の先頭観測額。個別のみ |
| `H-007` | 資産純増額（最終−初期） | 資産・成長 | 高 | `wealth.total.net` | 代表系列の末尾−開始整合した先頭。負値可、全体は合計 |
| `H-008` | 資産が最高だったゲーム内日 | 資産・成長 | 高 | `wealth.total.peak_day` | 最大値に最初に達した観測インデックス÷2。個別のみ |
| `H-009` | 資産の最大増加額 | 資産・成長 | 高 | `wealth.total.max_gain_half_day` | 連続2点の後−前の最大正差（観測窓あり・増加なしなら0） |
| `H-010` | 資産の最大減少額 | 資産・成長 | 高 | `wealth.total.max_loss_half_day` | 連続2点の前−後の最大正差（観測窓あり・減少なしなら0） |
| `H-011` | 資産の最大減少率 | 資産・成長 | 高 | `wealth.total.max_loss_rate_half_day` | 前>0の連続2点で max(0,(前−後)÷前)。対象窓なしは— |
| `H-012` | 15日間の最大資産増加額 | 資産・成長 | 中 | `wealth.total.max_gain_15d` | インデックス差30の増加額。H-009と同じ非負規則 |
| `H-013` | 15日間の最大資産減少額 | 資産・成長 | 中 | `wealth.total.max_loss_15d` | インデックス差30の減少額。H-010と同じ非負規則 |
| `H-014` | 年間最大資産増加率 | 資産・成長 | 中 | `wealth.total.max_growth_rate_year` | インデックス差120で開始額>0の (後−前)÷前 の最大。負値可 |
| `H-015` | 最長連続資産増加期間 | 資産・成長 | 中 | `wealth.total.longest_growth` | 厳密に増加した連続区間数÷2。同値/欠損で切る |
| `H-016` | 資産10万到達までの日数 | 資産・成長 | 高 | `wealth.total.first_100k` | 開始整合系列で初めて100000以上となるインデックス÷2。全体は最短 |
| `H-018` | 資産100万到達までの日数 | 資産・成長 | 中 | `wealth.total.first_1m` | H-016の閾値1000000 |
| `H-019` | 最大建築物資産比率 | 資産・成長 | 中 | `wealth.buildings.max_share` | 同一インデックスの Buildings÷Total の最大。Total>0、系列同期条件あり |

## Phase A · population

採用 · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#population)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `H-020` | 自由入植者数の平均値 | 人口・心情 | 高 | `population.free.mean` | FreeColonists の観測値平均。全体は点数加重 |
| `H-021` | 自由入植者数の最小値 | 人口・心情 | 中 | `population.free.min` | FreeColonists の最小観測人数。全体は最小 |
| `H-023` | 自由入植者10人到達までの日数 | 人口・心情 | 中 | `population.free.first_10` | 開始整合系列で最初に10人以上となるインデックス÷2。全体は最短 |
| `H-024` | 最大囚人数 | 人口・心情 | 高 | `population.prisoners.peak` | Prisoners の最大観測人数 |
| `H-025` | 平均囚人数 | 人口・心情 | 中 | `population.prisoners.mean` | Prisoners の観測値平均。全体は点数加重 |
| `H-026` | 平均心情 | 人口・心情 | 高 | `population.mood.mean` | ColonistMood（0〜100）で0の曖昧値を除いた観測値平均。全体は点数加重 |
| `H-027` | 最低平均心情 | 人口・心情 | 高 | `population.mood.min` | 同じ有効点の最小 |
| `H-028` | 最高平均心情 | 人口・心情 | 中 | `population.mood.max` | 同じ有効点の最大 |
| `H-029` | 平均心情50%未満の日数 | 人口・心情 | 中 | `population.mood.below_50d` | 両端が有効な半日区間の左端<50の区間数÷2。合計 |
| `H-030` | 平均心情80%以上の日数 | 人口・心情 | 中 | `population.mood.at_least_80d` | H-029と同じ区間で左端>=80。合計 |

## Phase A · vanilla

採用 · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#vanilla)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `H-031` | 年平均襲撃回数（既存累積カウンター由来） | 維持 | 高 | `defense.raids_per_year` | 代表セーブの numRaidsEnemy ÷ (ticksGame÷3600000)。全体は合計カウンター÷合計年数 |
| `H-032` | 年平均入植者死亡数（既存累積カウンター由来） | 維持 | 中 | `population.deaths_per_year` | 同じ分母で colonistsKilled を使用 |
| `H-033` | 最後に観測したMOD数・最大MOD数・最小MOD数 | 維持 | 低 | `colony.mods.latest+colony.mods.peak+colony.mods.min` | meta/modIds/li の有効エントリ数。末尾は代表セーブ、極値は観測横断。全体のMOD数は表示しない |

## Phase A · quests

採用 · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#quests)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-001` | クエスト成功数・失敗数 | クエスト | 高 | `quest.retained.success+quest.retained.failure` | 保存リストの受諾済み ended Success / Fail をID別に各1件。全体は合計 |
| `S-002` | クエスト成功率 | クエスト | 中 | `quest.retained.success_rate` | Success÷(Success+Fail)。全体は分子/分母合算 |
| `Q-006` | クエスト種類別の達成数 | クエスト | 中 | `quest.retained.success_by_root` | S-001の成功側を root Def別に集計 |
| `Q-007` | 期限切れクエスト数 | クエスト | 中 | `quest.retained.expired` | 未受諾かつ有効な acceptanceExpireTick<=保存tick の保存内件数 |
| `N3-X-001` | 受諾したクエストの総数 | クエスト | 高 | `quest.retained.accepted` | acceptanceTick>=0 または initiallyAccepted の保存内件数。進行中含む |
| `N3-X-003` | クエストの平均達成所要日数 | クエスト | 高 | `quest.retained.success_duration_mean` | Success の (cleanupTick−acceptanceTick) の平均日数。有効時間の合計÷件数 |
| `N3-X-004` | 最短達成クエストの所要日数 | クエスト | 中 | `quest.retained.success_duration_min` | 同じ標本の最短日数 |
| `N3-X-005` | 最長達成クエストの所要日数 | クエスト | 中 | `quest.retained.success_duration_max` | 同じ標本の最長日数 |

## Phase B · roster

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#roster)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-005` | 最後に観測した生存入植者数 | 人口・心情 | 高 | `population.snapshot.free` | 保存時に生存するプレイヤー自由入植者（ロッジャー除外、適格な輸送中を含む）。全体は代表値合計 |
| `S-006` | 最後に観測した囚人数 | 人口・心情 | 高 | `population.snapshot.prisoners` | 生存するコロニー囚人。過去の Prisoners の末尾とは別。全体は代表値合計 |
| `S-007` | 最後に観測した飼育動物数 | 維持 | 高 | `population.snapshot.animals` | 生存するプレイヤー所属の動物（家畜）。全体は代表値合計 |
| `S-008` | 最後に観測したメカノイド数 | 維持 | 中 | `population.snapshot.mechs` | 生存するプレイヤー所属のメカノイド。全体は代表値合計 |
| `S-021` | 強化義肢等を装着した入植者の最大人数 | 装備・所持物 | 中 | `population.snapshot.augmented_peak` | 完了した追加部位Hediffを一つ以上持つ自由入植者の最大観測人数 |
| `S-022` | スキル20の入植者の最大人数 | 装備・所持物 | 中 | `population.snapshot.skill20_pawns_peak` | 有効スキルのレベル>=20を一つ以上持つ自由入植者の最大観測人数 |
| `S-023` | 1人が同時に持ったスキル20の最多数 | 装備・所持物 | 中 | `population.snapshot.skill20_per_pawn_peak` | 個人の同時レベル>=20スキル数の最大 |
| `N1-C-001` | 最高齢の入植者 | 維持 | 中 | `population.snapshot.oldest` | 自由入植者の最大観測生物年齢（tickを年換算）。死者は含まない |
| `N1-D-008` | 最多飼育動物種数 | 動物・狩猟 | 中 | `population.snapshot.animal_defs_peak` | 同じ観測で飼育中の動物種（race ThingDef）の種類数の最大 |
| `N3-W-010` | スキルへの情熱なし・小・大の人数構成 | 維持 | 中 | `population.snapshot.passion` | 自由入植者の有効スキル別に None/Minor/Major/Other を人数カウント。個別代表のみ |
| `N4-P-011` | 最終観測時点の生存入植者の平均年齢 | 維持 | 中 | `population.snapshot.age_mean` | 自由入植者の生物年齢の合計÷有効人数。全体は人数加重 |
| `P-019` | 最大同時負傷者数 | 維持 | 中 | `medicine.injured_peak` | 生存自由入植者で未治癒 Injury を持つ人数の保存時観測最大。逐次更新しない |

## Phase B · stock

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#stock)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-009` | 最後に観測した食料備蓄量 | 維持 | 中 | `stock.food_nutrition.latest` | ホームマップの食用栄養を持つ食品のスタック数×一個栄養。全体は合計 |
| `S-010` | 最後に観測したシルバー所持量 | 維持 | 中 | `stock.silver.latest` | ホームマップの Silver 個数。全体は合計 |
| `S-011` | 最後に観測したスチール・コンポーネント所持量 | 維持 | 中 | `stock.materials.latest` | Steel / ComponentIndustrial / ComponentSpacer を別行で各個数。全体は合計 |
| `S-017` | 最大武器保有数 | 装備・所持物 | 中 | `stock.weapons.peak` | ホームマップ保有武器の最大観測個数 |
| `S-018` | 最大武器種類数 | 装備・所持物 | 中 | `stock.weapon_defs.peak` | 同時保有武器の ThingDef 種類数の最大 |
| `S-019` | 最大レジェンダリー武器保有数 | 装備・所持物 | 中 | `stock.legendary_weapons.peak` | Legendary の保有武器最大観測個数 |
| `S-020` | 最大ペルソナ武器保有数 | 装備・所持物 | 中 | `stock.persona_weapons.peak` | ペルソナ用バインドCompを持つ保有武器最大観測個数。Royalty |
| `N1-A-004` | 最大医薬品備蓄量 | 維持 | 中 | `stock.medicine.peak` | 医薬品分類の最大観測個数 |
| `N1-A-005` | 最低食料備蓄量 | 維持 | 高 | `stock.food_nutrition.min` | S-009と同じ範囲で最小観測量 |

## Phase B · facilities

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#facilities)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-016` | 同時稼働拠点の最大数（保存観測内） | 維持 | 中 | `facilities.home_maps.peak` | 同時ロード中の IsPlayerHome マップ数の最大。非ロードの所有拠点は含めない |
| `S-024` | 最大病床数 | 維持 | 中 | `facilities.medical_beds.peak` | プレイヤー所属の医療指定ベッドの最大観測台数（寝床枠数ではなくThing数） |
| `S-025` | 最大防衛タレット数 | 維持 | 中 | `facilities.turrets.peak` | プレイヤー所属の建設済み防衛タレットの最大観測台数。通電不要 |
| `N1-A-001` | 最大栽培面積 | 栽培・収穫 | 高 | `facilities.growing_cells.peak` | ホームマップの栽培ゾーン実セル数（重複除外）の最大。水耕槽は別設備 |
| `N1-A-003` | 最大部屋数 | 維持 | 中 | `facilities.rooms.peak` | 同時に存在する閉じた屋内 Room の最大観測数。エッジ接触・屋外除外 |
| `N1-D-001` | 最大発電能力 | 維持 | 高 | `facilities.generation_capacity.peak` | 設置済みプレイヤー発電機の定格Wの和の最大。実発電量と区別 |
| `N1-D-002` | 最大蓄電容量 | 維持 | 中 | `facilities.battery_capacity.peak` | 設置済みプレイヤーバッテリーの定格Wdの和の最大。残量と区別 |
| `N1-D-004` | 発電方式別の稼働設備数 | 維持 | 中 | `facilities.running_generators.latest` | 観測時に発電出力>0の設備を ThingDef 別に数える |
| `N4-C-001` | セーブで観測した最低屋外気温 | 維持 | 中 | `facilities.outdoor_temp.min` | 正常セーブ時にホームマップの屋外温度を読み全観測の最小°C |
| `N4-C-002` | セーブで観測した最高屋外気温 | 維持 | 中 | `facilities.outdoor_temp.max` | 同じ観測の最大°C |

## Phase B · research_state

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#research-state)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-012` | 研究完了数・研究総数に対する完了割合 | 研究 | 高 | `research.snapshot.finished+research.snapshot.finished_rate` | 代表観測で完了した有効Def数、対象Def数に対する比。全体は件数と分母加重率 |
| `S-013` | 完了研究の最高技術段階 | 研究 | 中 | `research.snapshot.tech_peak` | 同じ完了集合の最大 TechLevel。表示は段階名併記、足さない |

## Phase B · factions

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#factions)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-014` | 友好勢力数・敵対勢力数 | 維持 | 中 | `diplomacy.snapshot.relations` | プレイヤーから見た有効非プレイヤー勢力の Ally / Hostile 各件数。全体は代表値合計 |
| `S-015` | 勢力との最高・最低友好度（セーブ時点） | 維持 | 中 | `diplomacy.snapshot.goodwill_range` | 同じ集合で友好度を保持する勢力への baseGoodwill の最小・最大（最新セーブ） |

## Phase C · raid

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#raid)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `B-004` | 最大規模の襲撃（人数・戦力など定義要） | 襲撃・防衛 | 高 | `raid.pawns.max` | 成功した敵襲の到着確定時に敵Pawnの実数。ポイントは別 |
| `B-005` | 襲撃の種類別回数 | 襲撃・防衛 | 高 | `raid.arrival_mode` | 成功件数を到着モードDefで分類。襲撃種と戦術は独立軸 |
| `B-008` | 最長無襲撃期間 | 襲撃・防衛 | 中 | `raid.longest_gap` | 観測範囲内の連続敵襲間隔の最大。先頭・末尾の打ち切り除外 |
| `B-016` | 1襲撃での最多敵撃破数 | 襲撃・防衛 | 高 | `raid.kills.max` | 一つの追跡襲撃に属するB-001の最大件数 |
| `B-023` | 虫の襲来回数 | 襲撃・防衛 | 中 | `raid.infestation.count` | 虫の Infestation 系の成功発生回数。虫由来の他イベントを含めない |
| `B-024` | メカノイドクラスター出現回数 | 襲撃・防衛 | 中 | `raid.cluster.count` | メカノイドクラスター出現の成功回数。通常のメカ襲撃を含めない |
| `B-025` | 人狩り動物集団の襲来回数 | 襲撃・防衛 | 中 | `raid.manhunter.count` | 人狩り動物集団 incident の成功回数 |
| `B-026` | 包囲戦の発生回数 | 襲撃・防衛 | 中 | `raid.strategy.siege` | 襲撃戦術 Siege の件数 |
| `B-027` | 壁破壊型襲撃の回数 | 襲撃・防衛 | 中 | `raid.strategy.breach` | 壁破壊戦術のバニラDef許可表に一致した件数。トンネルと混同しない |
| `B-028` | 降下襲撃の回数 | 襲撃・防衛 | 中 | `raid.arrival.drop` | バニラ降下到着モードの許可表に一致した件数 |
| `N1-B-007` | 最大襲撃ポイント | 襲撃・防衛 | 高 | `raid.points.max` | 生成成功に使った最終 IncidentParms.points の最大 |
| `N1-B-008` | 襲撃の平均継続時間 | 襲撃・防衛 | 中 | `raid.duration.mean` | 追跡できた襲撃の敵対終了tick−到着tickの平均日数 |
| `N1-B-009` | 入植者がダウンせず撃退した襲撃回数 | 襲撃・防衛 | 高 | `raid.repelled.no_down` | 追跡襲撃で守備側入植者ダウン0、かつ終了理由が退却/全滅の件数 |
| `N1-B-010` | 襲撃の撃退率 | 襲撃・防衛 | 中 | `raid.repelled.rate` | 退却または全滅÷帰結が確定した追跡襲撃数。中断/判定不能は別表示 |
| `N2-C-001` | 敵襲の平均発生間隔 | 襲撃・防衛 | 高 | `raid.gap.mean` | B-008と同じ完結した間隔の平均。全体は区間数加重 |
| `N2-C-002` | 敵襲の最大発生間隔 | 襲撃・防衛 | 中 | `=B-008` | 同一値、単独表示を増やさない |
| `N2-C-004` | 最長の襲撃継続時間 | 襲撃・防衛 | 中 | `raid.duration.max` | N1-B-008と同じ標本の最大 |
| `N2-C-011` | 敵が退却した襲撃の回数 | 襲撃・防衛 | 中 | `raid.retreated` | 敵の一部が生存退却し敵対が終わった追跡襲撃件数 |
| `N2-C-012` | 敵が全滅した襲撃の回数 | 襲撃・防衛 | 中 | `raid.wiped` | 退却者なし・追跡敵全員死亡で終了した件数。捕虜ありは別帰結 |
| `N3-X-011` | 襲撃してきた勢力別の襲撃回数 | 襲撃・防衛 | 中 | `raid.factions` | 敵襲の実行勢力 Def別件数 |
| `N5-B-008` | 最も短時間で撃退した襲撃の所要時間 | 襲撃・防衛 | 中 | `raid.duration.min_repelled` | 退却または全滅で終わった同一標本の最短日数 |
| `N5-M-001` | 最初の敵襲を受けるまでのゲーム内日数 | 維持 | 中 | `raid.first_day` | ゲーム開始から完全計測した代表経路で最初の敵襲到着tick÷60000 |

## Phase C · health_death

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#health-death)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `B-001` | 敵の累計撃破数 | 撃破・攻撃手段 | 高 | `combat.hostile_kills` | 死亡前にプレイヤー敵対、確定死の原因がプレイヤー側と判定できた件数 |
| `B-020` | 最長無死亡期間 | 維持 | 高 | `population.death.longest_gap` | 観測範囲内の連続する自由入植者死亡の最大間隔。両端打ち切り除外 |
| `B-021` | 襲撃による死者数・その他原因の死者数 | 死亡・死因 | 高 | `population.death.raid_causes` | 襲撃所属攻撃者が原因の入植者死亡件数とその他確定原因・不明の内訳 |
| `B-022` | 個人の最多撃破数 | 撃破・攻撃手段 | 高 | `combat.hostile_kills.per_pawn_max` | B-001のプレイヤーPawn帰属件数の最多値。原因のみ判明分は個人に加えない |
| `P-004` | 入植者の死亡原因別人数 | 死亡・死因 | 高 | `population.death.causes` | 死亡直前の自由入植者の確定死亡を死因カテゴリ別に各1件 |
| `N1-B-004` | トラップによる敵撃破数 | 撃破・攻撃手段 | 高 | `combat.kills.trap` | B-001のうち原因の施設が自軍トラップ |
| `N1-B-006` | 敵の種類別撃破数（人間・虫・メカノイド等） | 撃破・攻撃手段 | 高 | `combat.kills.victim_kind` | B-001のうち被撃破者を人型/虫/メカ/その他に排他分類 |
| `N1-C-002` | 最も高齢で死亡した入植者 | 維持 | 中 | `population.death.oldest` | 死亡直前の自由入植者の生物年齢最大 |
| `N1-C-009` | 病気による死亡者の割合 | 死亡・死因 | 高 | `population.death.disease_rate` | 病気が直接死因の人数÷死因分類済みの自由入植者死亡。分母併記 |
| `N2-C-005` | タレットが敵を撃破した総数 | 撃破・攻撃手段 | 高 | `combat.kills.turret` | B-001のうち加害主体が自軍タレット |
| `N2-C-006` | 飼育動物による敵撃破数 | 撃破・攻撃手段 | 中 | `combat.kills.tame_animal` | B-001のうち加害主体がプレイヤー家畜 |
| `N2-C-009` | 家畜が敵襲で死亡した頭数 | 死亡・死因 | 中 | `animals.death.raid` | プレイヤー家畜の確定死亡のうち襲撃所属攻撃者が直接原因の頭数 |
| `N3-H-003` | 病気を直接死因とする死亡人数 | 死亡・死因 | 高 | `population.death.disease` | P-004の病気区分の件数 |
| `N3-X-006` | 遠征中に死亡した入植者人数 | 死亡・死因 | 中 | `caravan.member_deaths` | 死亡直前にプレイヤーキャラバン所属の自由入植者の死亡件数 |
| `N5-B-001` | 射撃武器による敵撃破数 | 撃破・攻撃手段 | 高 | `combat.kills.ranged` | B-001のうち手段が射撃武器（爆発分類優先） |
| `N5-B-002` | 近接武器による敵撃破数 | 撃破・攻撃手段 | 高 | `combat.kills.melee` | B-001のうち装備武器の近接攻撃 |
| `N5-B-003` | 素手による敵撃破数 | 撃破・攻撃手段 | 中 | `combat.kills.unarmed` | B-001のうち武器を使わない近接攻撃 |
| `N5-B-004` | 爆発で倒した敵の数 | 撃破・攻撃手段 | 中 | `combat.kills.explosive` | B-001のうち爆発の明示証拠あり |
| `N5-B-005` | 火災・環境ダメージによる敵撃破数 | 撃破・攻撃手段 | 中 | `combat.kills.environment` | B-001のうち自軍帰属が明示された火災・環境。自然死や帰属不明は含めない |
| `N5-B-009` | Biotech：自軍メカノイドの敵撃破数 | 撃破・攻撃手段 | 中 | `combat.kills.player_mech` | B-001のうち加害主体が自軍メカノイド。Biotech |
| `N5-M-003` | 最初の入植者死亡までの日数 | 維持 | 中 | `population.death.first_day` | 開始から完全計測時の最初の自由入植者死亡tick÷60000 |
| `A-003` | 飼育動物の死亡数 | 動物・狩猟 | 中 | `animals.death.count` | 死亡直前がプレイヤー所属の動物の確定死亡件数 |

## Phase C · down

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#down)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `B-002` | 敵の累計無力化数 | 撃破・攻撃手段 | 高 | `=B-014` | 意味を「敵のダウン」に統合。撃破を追加しない |
| `B-013` | 入植者ダウン回数 | 維持 | 高 | `combat.colonists_downed` | 自由入植者の非ダウン→ダウンの成功遷移件数。即死のみは除外 |
| `B-014` | 敵ダウン回数 | 維持 | 高 | `combat.enemies_downed` | プレイヤー敵対Pawnの同遷移件数。再起後の再ダウンは別件 |
| `N4-C-011` | Biotech：汚染中毒によってダウンした回数 | 維持 | 中 | `combat.downed.toxic` | 明示的原因が汚染中毒の入植者ダウン。Biotech |
| `N4-P-009` | 社交喧嘩によってダウンした人数 | 加入・人間関係 | 中 | `combat.downed.social_fight` | 社交喧嘩を直接原因とする自由入植者ダウン件数 |

## Phase C · membership

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#membership)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `P-001` | 累計新規加入者数 | 加入・人間関係 | 高 | `membership.first_join` | 初期入植者・出生を除く初回の自由入植者化。人物IDごと1人 |
| `P-002` | 囚人を勧誘して加入した人数 | 加入・人間関係 | 高 | `membership.prisoner_recruit` | 直前にコロニー囚人で勧誘成功した自由入植者。再加入は別区分 |
| `P-003` | 累計出生数 | 加入・人間関係 | 高 | `membership.human_birth` | 実際に生きて生まれたプレイヤー側人型の子の人数。Biotech |
| `P-009` | 歴代入植者の累計ユニーク人数 | 加入・人間関係 | 高 | `membership.unique` | 初期ロスター・新規・出生の自由入植者ID集合。再加入重複なし、全体はコロニー別件数合計 |
| `N1-C-003` | 加入経路別の入植者数 | 加入・人間関係 | 高 | `membership.route` | P-001の加入経路別人数。囚人勧誘・明示した別経路・その他/不明を排他分類 |
| `N1-C-004` | 離脱後に再加入した人数 | 加入・人間関係 | 中 | `membership.rejoin` | 明示的離脱後に自由入植者へ復帰したユニーク人数 |
| `N4-P-003` | 加入時の平均年齢 | 維持 | 中 | `membership.join_age_mean` | P-001時点の生物年齢合計÷有効人数 |
| `P-018` | 奴隷を解放した人数 | 加入・人間関係 | 中 | `membership.slave_freed` | 奴隷の身分解除が完了した人数。囚人釈放と別 |
| `P-027` | 囚人の最短・最長勧誘期間 | 維持 | 中 | `membership.recruit_time` | コロニー囚人化からP-002までの完結区間の最短・最長日数 |
| `P-028` | 誘拐された入植者数 | 維持 | 中 | `membership.kidnapped` | 誘拐で勢力の拘束リストに入った自由入植者の件数 |
| `N4-P-005` | 誘拐された入植者を救出した人数 | 維持 | 中 | `membership.kidnap_rescued` | 追跡中の誘拐状態から救出経路で復帰した人数 |
| `N4-P-006` | 最長囚われ期間（入植者） | 維持 | 中 | `membership.captive_max` | 追跡できた誘拐から解放までの完結拘束期間の最長 |
| `N5-M-002` | 最初の新規入植者加入までの日数 | 維持 | 中 | `membership.first_day` | P-001最初のtick÷60000。開始から完全計測時のみ |

## Phase C · lifetime

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#lifetime)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `P-010` | 歴代入植者の最長生存日数 | 維持 | 高 | `=N1-C-002` | 自由入植者として死亡した人物のゲーム内生物年齢の最大。N1-C-002と同値 |
| `P-020` | 歴代入植者の平均生存日数 | 維持 | 中 | `population.life.mean` | 同じ死亡時生物年齢の平均。現在生存者はN4-P-011 |
| `P-021` | 加入後の平均在籍期間 | 維持 | 中 | `membership.tenure_mean` | 追跡済みの加入→死亡/離脱の完結区間の合計日数÷区間数。右打ち切り除外 |
| `P-023` | スキル20到達回数（人物×スキルの初到達） | 維持 | 中 | `population.skill.first_20` | 自由入植者の (人物ID,スキルDef) が計測中に20へ初到達した組数 |
| `P-024` | 加入以降の最大スキル上昇幅 | 維持 | 中 | `population.skill.join_gain_peak` | 加入時スキルからの同スキルレベル増加の最大。ベースラインなしは除外 |

## Phase C · mental

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#mental)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `P-005` | 精神崩壊の発生回数 | 加入・人間関係 | 中 | `mental.breaks.count` | 心情起因で成功した精神崩壊の件数。デバッグ強制や薬物・超能力由来を混ぜない |
| `P-006` | 精神崩壊の種類別回数 | 加入・人間関係 | 中 | `mental.breaks.by_def` | P-005のMentalBreakDef別件数 |
| `P-025` | インスピレーション発生回数・種類別回数 | 加入・人間関係 | 中 | `mental.inspiration.count+mental.inspiration.by_def` | 成功したひらめき開始を全件/Def別で数える |

## Phase C · social

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#social)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `P-012` | 結婚回数 | 加入・人間関係 | 中 | `social.marriages` | 既婚関係が成立したペア遷移件数 |
| `P-013` | 離婚回数 | 加入・人間関係 | 中 | `social.divorces` | 離婚による既婚解除件数。死別・一般解除を除く |
| `P-014` | 恋人関係成立回数 | 加入・人間関係 | 中 | `social.lovers` | 恋人関係への新規遷移件数。ペア両向き通知は1件 |
| `P-015` | 社交喧嘩の発生回数 | 加入・人間関係 | 中 | `social.fights` | 社交喧嘩の開始成立件数。二人で1件 |
| `P-016` | 囚人の脱獄回数 | 加入・人間関係 | 高 | `social.prison_breaks` | 1グループの囚人脱獄開始成立を1件。囚人数と別 |
| `P-017` | 囚人を釈放した人数 | 加入・人間関係 | 中 | `social.prisoners_released` | 自由釈放が完了したコロニー囚人の人数。捕虜逃走を除く |
| `B-003` | 敵捕虜の獲得数 | 撃破・攻撃手段 | 高 | `social.prisoners_captured` | 敵対Pawnの捕縛完了でコロニー囚人へ移行した人数 |
| `N1-D-009` | 人と動物の絆が成立した回数 | 動物・狩猟 | 中 | `social.animal_bonds` | 入植者と自軍動物の絆成立件数。片側だけ数える |

## Phase C · medicine

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#medicine)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `P-007` | 救助成功回数 | 維持 | 中 | `medicine.rescues` | Rescueの運搬で安全なベッドへの収容完了件数。普通の運搬・捕獲と別 |
| `P-008` | 蘇生成功回数 | 維持 | 中 | `medicine.resurrections` | 自由入植者の成功した蘇生件数 |
| `P-026` | 新規依存症の発症回数 | 維持 | 中 | `medicine.addictions` | 初回発症した依存症Hediffの追加件数。重症度更新は除外 |
| `M-001` | 治療を行った回数 | 維持 | 中 | `medicine.tends` | 治療実行1回を1件。治療したHediff数と区別 |
| `M-002` | 手術回数・成功率 | 維持 | 中 | `medicine.surgery.count+medicine.surgery.success_rate` | 実際に施行された成功/失敗手術。中止除外、分母は結果判明の施行数 |
| `M-003` | 新しく発症した病気の件数 | 維持 | 中 | `medicine.disease.onsets` | 疾病カテゴリHediffの新規追加件数。初期生成除外 |
| `M-004` | 感染症からの回復回数 | 維持 | 中 | `medicine.infection.recovery` | 感染症が治癒完了で除去された件数。死亡/切断による除去は除く |
| `M-005` | 食中毒発生回数 | 維持 | 中 | `medicine.food_poisoning` | 食中毒Hediffの非罹患→罹患件数 |
| `M-006` | 四肢など身体部位を失った回数 | 維持 | 中 | `medicine.part_loss` | MissingPart確定追加件数。子部位の連鎖・手術で計画的に外した部位を二重計上しない |
| `M-007` | 義肢・人工臓器の装着回数 | 維持 | 中 | `medicine.implants` | 手術成功で義肢・人工臓器が実際に装着された数 |
| `N1-C-007` | 平均治療品質 | 維持 | 高 | `medicine.tend_quality_mean` | M-001のうち実際に適用されたHediff治療品質の合計÷適用数。％ |
| `N1-C-010` | 医薬品の累計消費数 | 維持 | 中 | `medicine.consumed` | 治療に実消費した医薬品のスタック個数 |
| `N3-H-001` | 病気が治癒するまでの平均罹患日数 | 維持 | 中 | `medicine.disease.duration_mean` | 発症から治癒まで追跡した同一Hediff区間の平均日数 |
| `N3-H-002` | 感染症の最長罹患日数 | 維持 | 中 | `medicine.infection.duration_max` | 感染症の同じ完結区間の最長日数 |
| `N3-H-004` | 薬物の過剰摂取発生回数 | 維持 | 中 | `medicine.overdoses` | 過剰摂取Hediffの新規発症件数。重症度更新除外 |
| `N3-H-005` | 薬物の禁断症状を新たに発症した回数 | 維持 | 中 | `medicine.withdrawal` | 禁断症状Hediffの新規発症件数 |
| `N3-H-007` | 永久的な傷痕を新たに負った回数 | 維持 | 中 | `medicine.scars` | Injuryの永久化遷移件数。同じ傷1回 |
| `N3-H-010` | 最多手術成功数の医師と件数 | 維持 | 高 | `medicine.surgery.best_doctor` | M-002の成功件数の医師別最多。医師不明は除外 |
| `N3-H-011` | 平均入院期間 | 維持 | 中 | `medicine.hospital.stay_mean` | 同じ自由入植者の医療指定ベッドへの入床→退床の完結区間平均日数 |
| `N3-H-012` | 最も多く治療を行った医師と件数 | 維持 | 高 | `medicine.tends.best_doctor` | M-001の医師別最多。医師不明は除外 |
| `N4-C-010` | 野生動物の攻撃で負傷した件数 | 維持 | 中 | `medicine.wild_attack.injuries` | 野生動物の攻撃に帰属する入植者の新規 Injury 件数 |
| `N4-P-001` | 敵対者を救助した人数 | 維持 | 中 | `medicine.rescues.hostile` | P-007で救助開始時にプレイヤー敵対だった被救助者件数 |
| `N4-P-002` | 中立NPCを治療した回数 | 維持 | 中 | `medicine.tends.neutral` | M-001で対象が中立NPCの治療件数。通常集計の部分集合として明示 |
| `N4-P-007` | 同じ入植者の最多蘇生回数 | 維持 | 中 | `medicine.resurrections.per_pawn_max` | P-008の同一人物最多件数 |
| `N4-P-008` | 最多負傷件数の入植者 | 維持 | 中 | `medicine.injuries.per_pawn_max` | 新規 Injury 件数の入植者別最多 |
| `N3-X-010` | 遠征先で救助した人数 | 維持 | 中 | `medicine.rescues.away` | P-007のうちホームマップ外で遠征中の救助件数 |

## Phase C · disaster

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#disaster)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `D-001` | 死の灰に覆われた累計日数 | 災害・環境 | 中 | `environment.toxic_fallout.days` | プレイヤーホームが一つ以上死の灰の影響を受けるtickの和集合÷60000 |
| `D-002` | 太陽フレアの発生回数 | 災害・環境 | 中 | `environment.solar_flare.count` | 太陽フレアの確定開始件数 |
| `D-003` | 寒波の発生回数 | 災害・環境 | 中 | `environment.cold_snap.count` | 寒波の確定開始件数 |
| `D-004` | 熱波の発生回数 | 災害・環境 | 中 | `environment.heat_wave.count` | 熱波の確定開始件数 |
| `D-005` | 火山の冬の発生回数 | 災害・環境 | 中 | `environment.volcanic_winter.count` | 火山の冬の確定開始件数 |
| `D-006` | 作物の病害発生回数 | 災害・環境 | 中 | `environment.crop_blight.count` | 病害incidentの成功件数。感染株数ではない |
| `D-007` | 火災の発生回数 | 災害・環境 | 中 | `environment.fire.ignitions` | ホームマップで火がなかったセルへの非延焼着火成功件数。延焼と再燃を区別 |
| `D-008` | 火災で失った建築物の数 | 災害・環境 | 中 | `environment.fire.buildings_lost` | 火災の直接ダメージで破壊された自軍完成建築物の数 |
| `N4-C-005` | 日食の発生回数 | 災害・環境 | 中 | `environment.eclipse.count` | 日食の確定開始件数 |
| `N4-C-006` | 隕石落下の発生回数 | 災害・環境 | 中 | `environment.meteor.count` | 隕石incidentの成功件数。落下物数ではない |
| `N4-C-012` | 太陽フレアによる設備停止の累計日数 | 災害・環境 | 中 | `environment.solar_flare.days` | 太陽フレアの電力遮断対象ホームマップが存在するtickの和集合÷60000。個別設備の停止時間は集めない |
| `N4-R-010` | 短絡事故（Zzzt）が発生した回数 | 維持 | 中 | `environment.short_circuit.count` | 短絡incidentの成功件数 |

## Phase C · work

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#work)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `W-001` | 累計作物収穫量 | 栽培・収穫 | 高 | `work.harvest.units` | プレイヤー側作業者の確定した主収穫物のスタック個数合計 |
| `W-002` | 累計採掘資源量 | 維持 | 高 | `work.mining.units` | 採掘に帰属する確定産出物のスタック個数合計 |
| `W-003` | 製造アイテムの総数 | 製作・品質 | 高 | `work.crafting.units` | 自軍の製作レシピ主/副成果の確定スタック個数。調理・芸術を含み採掘・収穫・メカ孵化を除外 |
| `W-004` | 建築物完成数 | 維持 | 中 | `work.construction.count` | 自軍の完成建築物・設備の設置完成件数。再設置・枠・ブループリントは除外 |
| `W-005` | 製作した最高品質アイテムの記録 | 製作・品質 | 中 | `work.crafting.best_quality` | W-003の CompQuality を持つ製品の最高 QualityCategory。品質名を併記 |
| `W-006` | 製作品質別件数 | 製作・品質 | 中 | `work.crafting.quality` | W-003の品質別スタック個数。品質なしを別区分 |
| `W-007` | 建築で消費した資源総量 | 維持 | 中 | `work.construction.material_units` | W-004の完成時に実消費した素材の ThingDef別個数。異種素材は合算表示しない |
| `W-008` | 解体した建築物数 | 維持 | 中 | `work.deconstruction.count` | 指示による自軍建築物の解体完了件数 |
| `W-009` | 建築失敗回数 | 維持 | 中 | `work.construction.failures` | 建築失敗が確定した回数。中止やキャンセル除外 |
| `W-010` | レジェンダリー品質の自作品総数 | 製作・品質 | 高 | `work.crafting.quality.legendary` | W-006のLegendary |
| `W-011` | 名品品質の自作品総数 | 製作・品質 | 高 | `work.crafting.quality.masterwork` | W-006のMasterwork |
| `W-012` | 最高価格の自作品の価値 | 製作・品質 | 高 | `work.crafting.best_price` | W-003成果1個の生成時市場価値の最大。売価と別 |
| `W-013` | 完成した彫刻数 | 製作・品質 | 中 | `work.crafting.sculptures` | W-003の彫刻成果の個数 |
| `W-014` | 植えた作物の総数 | 栽培・収穫 | 中 | `work.crops.sown` | 自軍が栽培対象セルに正常に播種した株数 |
| `W-015` | 収穫失敗作物数 | 栽培・収穫 | 中 | `work.harvest.failures` | 自軍の収穫作業で実際に失敗した株数。枯死と別 |
| `W-017` | 調理した食事の種類別累計 | 製作・品質 | 中 | `work.cooking.by_def` | W-003中の完成食事を ThingDef 別個数 |
| `W-018` | 完成した製作品の種類数 | 製作・品質 | 中 | `work.crafting.unique_defs` | W-003で正の産出があった ThingDefの種類数 |
| `W-021` | 寒さで枯死した作物数 | 栽培・収穫 | 中 | `work.crops.cold_loss` | 自軍栽培株のうち寒さに帰属した枯死件数 |
| `W-023` | 発射した輸送ポッド数 | 維持 | 中 | `transport.pods.launched` | 発射成立したプレイヤー輸送ポッド実数。編隊1回とは別 |
| `N1-A-009` | 1日あたり最多製作品数 | 製作・品質 | 中 | `work.crafting.daily_peak` | W-003の floor(tick÷60000) 別の最大個数。完全観測日だけ |
| `N2-F-001` | 栽培した作物の種類数 | 栽培・収穫 | 中 | `work.crops.sown_defs` | W-014で正の播種があった作物Def種類数 |
| `N2-F-002` | 作物の種類別累計収穫量 | 栽培・収穫 | 高 | `work.harvest.by_crop` | W-001を元の作物Def別に合計。収穫物Defを作物Defの代わりにしない |
| `N2-F-003` | 最多収穫量を記録した作物の種類と数量 | 栽培・収穫 | 中 | `work.harvest.top_crop` | N2-F-002で最多の作物Defと個数 |
| `N2-F-005` | 寒害以外で枯死した作物の総数 | 栽培・収穫 | 中 | `work.crops.other_loss` | 自軍栽培株の寒害以外で原因確定した枯死件数。収穫・刈り取り除外 |
| `N2-F-009` | 作物の枯死率（収穫前の損失割合） | 栽培・収穫 | 中 | `work.crops.loss_rate` | 観測を開始した播種コホートの確定枯死数÷(同コホートの枯死数＋最初の成功収穫到達株数)。手動除去と未決着除外 |
| `N2-F-011` | 最も生産量の多かったゲーム内1年間の作物収穫量 | 栽培・収穫 | 中 | `work.harvest.year_peak` | W-001の floor(tick÷3600000) 別の最大個数。完全観測年のみ |
| `N2-F-012` | 調理の種類別累計食事数 | 製作・品質 | 中 | `=W-017` | 同じ食事Def別集計 |
| `N3-W-001` | 最多製作品数を達成した入植者と件数 | 製作・品質 | 高 | `work.crafting.best_worker` | W-003の作業者別個数の最大 |
| `N3-W-002` | 最多収穫量を達成した入植者と数量 | 栽培・収穫 | 高 | `work.harvest.best_worker` | W-001の作業者別個数の最大 |
| `N3-W-003` | 最多建築完了数を達成した入植者と件数 | 維持 | 中 | `work.construction.best_worker` | W-004の作業者別件数の最大 |
| `N3-W-004` | 最多調理数を達成した入植者と件数 | 製作・品質 | 中 | `work.cooking.best_worker` | W-017の作業者別食事個数の最大 |
| `N3-W-005` | 最多採掘量を達成した入植者と数量 | 維持 | 中 | `work.mining.best_worker` | W-002の作業者別個数の最大 |
| `N4-R-004` | 設備・建物の修理を完了した回数 | 維持 | 中 | `work.repair.completed` | 自軍の修理作業で実際に最大HPに復帰した回数。治療・自動再生を除く |

## Phase C · food

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#food)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `W-016` | 腐敗・劣化で失った食料総量 | 維持 | 中 | `food.spoiled.units` | 食品が腐敗・劣化の直接結果で失われたスタック個数。焼失・食事消費は除外 |
| `W-019` | 消費した食料の総栄養量 | 維持 | 中 | `food.consumed.nutrition` | 自由入植者の摂食で実際に得た栄養の累計 |
| `N2-F-010` | 食料不足が発生しなかった最長日数 | 維持 | 中 | `food.no_shortage.longest` | 開始/終了が追跡できるホームマップ食用栄養>0の連続日数の最大。空腹の有無とは別 |
| `N1-A-008` | 栽培面積あたりの平均収穫量 | 栽培・収穫 | 高 | `food.harvest.per_growing_cell` | 同じ完全観測期間の栽培ゾーン由来主収穫個数÷同期間の栽培セル日積分。個/セル日 |

## Phase C · animals

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#animals)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `A-001` | 手懐けた動物の総数 | 動物・狩猟 | 中 | `animals.tamed` | 野生→プレイヤー所属が調教成功により確定した動物件数 |
| `A-002` | 飼育動物の出生数 | 動物・狩猟 | 中 | `animals.birth` | 生存出生したプレイヤー所属動物の頭数。人型出生と別 |
| `A-004` | 家畜屠殺数 | 動物・狩猟 | 中 | `animals.slaughtered` | 家畜の死亡のうち屠殺作業由来の件数 |
| `N1-D-006` | 狩猟した野生動物の総数 | 動物・狩猟 | 高 | `animals.hunted` | 狩猟指示に帰属した野生動物の確定死亡件数。偶発戦闘を除く |

## Phase C · trade

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#trade)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `T-001` | 交易の累計売上金額 | 交易 | 高 | `trade.sales.total` | 成立明細の非通貨売却の総契約額 |
| `T-002` | 交易の累計購入金額 | 交易 | 高 | `trade.buys.total` | 成立明細の非通貨購入の総契約額 |
| `T-003` | 商人との累計取引回数 | 交易 | 高 | `trade.count` | 1件以上の非通貨明細が成立した決済数。空決済・贈答・失敗除外 |
| `T-004` | 最大1回あたり販売金額 | 交易 | 高 | `trade.sales.peak` | 決済ごとの非通貨売却額の最大 |
| `T-005` | 最大1回あたり購入金額 | 交易 | 中 | `trade.buys.peak` | 決済ごとの非通貨購入額の最大 |
| `T-006` | 最多販売アイテムの種類と数量 | 交易 | 中 | `trade.sales.top_item` | 代表経路の売却個数最多 ThingDef と個数 |
| `T-007` | 商船との取引回数 | 交易 | 中 | `trade.orbital.count` | T-003のうち商船の決済数 |
| `T-008` | キャラバン商人との取引回数 | 交易 | 中 | `trade.visiting_caravan.count` | T-003のうち訪問地上商人の決済数。自軍が他拠点で行う交易は別分類 |
| `N2-E-004` | 最高単品販売価格 | 交易 | 中 | `trade.sales.peak_unit` | 成立した販売明細の最大単価（実契約明細額÷個数） |
| `N2-E-009` | 交易相手の勢力種類数 | 交易 | 中 | `trade.partner_faction_defs` | T-003で取引した既知勢力Defの種類数。無所属・不明は別表示 |
| `N2-E-010` | 交易で売却した品目種類の総数 | 交易 | 中 | `trade.sales.unique_defs` | 非通貨売却品目ThingDefの種類数 |
| `N2-E-011` | 交易で購入した品目種類の総数 | 交易 | 中 | `trade.buys.unique_defs` | 非通貨購入品目ThingDefの種類数 |
| `N4-R-001` | 交易で売却したアイテムの累計個数 | 交易 | 高 | `trade.sales.units` | 非通貨売却スタックの個数合計 |
| `N4-R-002` | 交易で購入したアイテムの累計個数 | 交易 | 高 | `trade.buys.units` | 非通貨購入スタックの個数合計 |
| `N4-R-003` | 最多購入アイテムの種類と数量 | 交易 | 中 | `trade.buys.top_item` | 代表経路の購入個数最多 ThingDef と個数 |

## Phase C · diplomacy

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#diplomacy)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `T-009` | 贈り物による友好度上昇量の累計 | 維持 | 中 | `diplomacy.gift_goodwill` | 贈答決済の成立直前後の相手勢力の正の実友好度差。上限で実増加0なら0 |
| `T-010` | 友好勢力から敵対勢力へ変化した回数 | 維持 | 中 | `diplomacy.ally_to_hostile` | 同一勢力の直前 Ally→直後 Hostile の遷移件数 |
| `T-011` | 敵対勢力と和解した回数 | 維持 | 中 | `diplomacy.reconciled` | Hostile→Neutral/Ally の遷移件数 |
| `N2-E-007` | 派閥に軍事支援を要請した回数 | 維持 | 中 | `diplomacy.aid_requested` | 友好勢力が実際に受理した軍事支援要請件数 |
| `N2-E-008` | 外部勢力の援軍が実際に到着した回数 | 維持 | 中 | `diplomacy.aid_arrived` | 外部勢力援軍の到着成立件数。予約・要請と別 |

## Phase C · research

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#research)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `Q-001` | 研究完了数（イベント累計） | 研究 | 高 | `research.live.completed` | 追跡開始後の未完了→完了の研究Def数。S-012の保有研究数と別 |
| `Q-002` | 研究完了履歴・完了日時（数値一覧） | 研究 | 中 | `research.live.completed_ticks` | Q-001のDef別初回完了tickの数値一覧。表示はゲーム内日、個別のみ |
| `Q-003` | 研究完了までの最短日数 | 研究 | 中 | `research.live.duration_min` | 同一プロジェクトへの最初の正の実研究作業から完了までの最短日数。中断中も経過に含む |
| `Q-004` | 研究完了までの最長日数 | 研究 | 中 | `research.live.duration_max` | 同じ標本の最長日数 |
| `N3-W-011` | 特定研究の開始から完了まで最長日数 | 研究 | 中 | `=Q-004` | 同じプロジェクト別所要日数の最大 |
| `N5-M-004` | 最初の研究完了までの日数 | 研究 | 高 | `research.live.first_day` | 開始から完全計測した場合の最初のQ-001完了tick÷60000 |

## Phase C · caravan

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#caravan)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `Q-008` | キャラバン出発回数 | 遠征 | 中 | `caravan.departures` | 自軍の地上キャラバンがマップ出発で新規成立した回数。合流・分割・再生成は除外 |
| `Q-009` | キャラバン全滅回数 | 遠征 | 中 | `caravan.lost` | 追跡中の地上キャラバンが全員死亡した件数。到着消滅・合流は除外 |
| `Q-010` | キャラバン総移動距離 | 遠征 | 中 | `caravan.tiles_travelled` | 自軍キャラバンが徒歩経路で完了した隣接タイル遷移数。テレポート除外 |
| `Q-011` | 訪問したワールドタイルのユニーク種類数 | 遠征 | 中 | `caravan.unique_tiles` | Q-010で到着した (layer,tile) のユニーク数。同一タイルの再訪は加算しない |
| `Q-012` | 敵拠点の攻略数 | 遠征 | 高 | `exploration.enemy_sites_cleared` | プレイヤーの戦闘勝利で破壊された敵勢力所有の世界拠点数 |
| `Q-013` | 所有拠点放棄回数 | 遠征 | 中 | `exploration.settlements_abandoned` | 自軍所有拠点の任意放棄の確定件数。グラヴシップ離陸等の自動消失は除外 |
| `Q-014` | 古代の脅威を開封した回数 | 遠征 | 中 | `exploration.ancient_danger_opened` | バニラ古代の脅威の封印を解除した構造物数。同じもの1回 |
| `N2-E-001` | 最長キャラバン遠征日数 | 遠征 | 高 | `caravan.duration_max` | 出発から帰還/任務完了まで追跡できた完結遠征の最長日数 |
| `N2-E-003` | キャラバン遠征中に襲撃された回数 | 遠征 | 中 | `caravan.ambushes` | 追跡キャラバンが対象の待ち伏せ発生成功件数 |
| `N3-X-007` | 1回のキャラバンで進んだ最長距離 | 遠征 | 中 | `caravan.distance_max` | 完結した1遠征内の Q-010 の最大 |
| `N3-X-008` | 新しい拠点を設立した回数 | 遠征 | 中 | `exploration.settlements_founded` | プレイヤーの新拠点成立数。ロード・移設・シナリオ初期拠点は除外 |
| `N4-R-011` | 同時に運行していたキャラバンの最大数 | 遠征 | 中 | `caravan.concurrent_max` | 同時に存在する自軍地上 Caravan 数の最大。移動中・停止中を含む |

## Phase D · royalty

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#royalty)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `L-001` | Royalty：獲得爵位回数 | 維持 | 中 | `royalty.title_grants` | プレイヤー側Pawnに同一帝国で昇爵が成立した件数。降爵・再ロード除外 |
| `L-002` | Royalty：許可証の使用回数 | 維持 | 中 | `royalty.permit_uses` | 帝国許可証の発動成立件数。UI選択・キャンセル除外 |
| `L-003` | Royalty：帝国関係クエストの達成数 | 維持 | 中 | `royalty.quest_success` | S-001のうち帝国関連と明示できる保存クエスト成功数 |
| `L-004` | Royalty：最高到達爵位 | 維持 | 中 | `royalty.highest_title` | 観測した帝国爵位の最高 seniority。爵位ラベルと数値を併記 |
| `L-005` | Royalty：サイキック能力使用回数 | 維持 | 中 | `royalty.psycasts` | 自由入植者のバニラ超能力発動成功件数。チャージ失敗除外 |
| `L-006` | Royalty：サイリンク最高レベル | 維持 | 中 | `royalty.psylink.max` | 正常セーブ時の自由入植者の最大サイリンクレベルの最大観測 |
| `N1-E-006` | Royalty：獲得・消費した名誉の累計 | 維持 | 中 | `royalty.honor` | 帝国で実際に増えた/減った名誉点の正負別累計。消費と失効を区別し消費のみ後者へ |

## Phase D · ideology

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#ideology)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `L-007` | Ideology：改宗回数 | 維持 | 中 | `ideology.conversions` | プレイヤー側の対象Pawnで思想A→Bへの変更が成立した件数。生成時は除外 |
| `L-008` | Ideology：儀式開催数・結果 | 維持 | 中 | `ideology.ritual.results` | 一つの儀式の確定終了を結果Def別件数。中断は別 |
| `L-009` | Ideology：儀式成功率 | 維持 | 中 | `ideology.ritual.success_rate` | 既知の成功結果÷成功/失敗が分類できた開催数 |
| `L-010` | Ideology：改宗までの平均日数 | 維持 | 中 | `ideology.conversion.days_mean` | プレイヤー側の明示的な改宗開始から対象思想へ成功するまでの完結日数の平均 |
| `N1-E-007` | Ideology：指導者・専門職への任命回数 | 維持 | 中 | `ideology.role_appointments` | 指導者・専門職の実割当成立件数。解除と同一役の再設定除外 |

## Phase D · biotech

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#biotech)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `L-011` | Biotech：出生数（DLC集計） | 維持 | 中 | `=P-003` | 同一出生カウンターをBiotechカテゴリで参照 |
| `L-012` | Biotech：製造したメカノイド総数 | 維持 | 中 | `biotech.mechs.created` | プレイヤー製造のメカノイドが生産完了で成立した個体数 |
| `L-014` | Biotech：出産を経験した入植者数 | 維持 | 中 | `biotech.birth.mothers` | P-003の出産者のうち自由入植者のユニーク人数 |
| `L-015` | Biotech：子供の成長段階到達数 | 維持 | 中 | `biotech.child_stages` | プレイヤー側の子が到達した成長段階別件数。個体×段階1件 |
| `L-016` | Biotech：メカノイドの累計修理回数 | 維持 | 中 | `biotech.mechs.repairs` | 自軍メカノイドが修理作業で全損傷解消に到達した件数 |
| `N1-E-001` | Biotech：毒性廃棄物パックの累計生成数 | 維持 | 中 | `biotech.waste.created` | 実生成された毒性廃棄物パック個数 |
| `N1-E-002` | Biotech：毒性廃棄物パックの累計処分数 | 維持 | 中 | `biotech.waste.disposed` | プレイヤーの無害化処分で消失したパック個数。輸出・崩壊による汚染は別 |
| `N1-E-003` | Biotech：汚染を除去したタイルの総数 | 維持 | 中 | `biotech.pollution.cleaned` | プレイヤーの除染で汚染→非汚染となったマップセル数。世界タイルは含めない |
| `N1-E-004` | Biotech：最大汚染面積 | 維持 | 中 | `biotech.pollution.cells_peak` | 正常セーブ時のホームマップ汚染セル数の総和の最大 |
| `N1-E-005` | Biotech：最高メカノイド制御帯域使用量 | 維持 | 中 | `biotech.bandwidth.used_peak` | 正常セーブ時に観測したメカニター1人の使用帯域最大 |

## Phase D · anomaly

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#anomaly)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `L-017` | Anomaly：異常存在の捕獲数 | 維持 | 中 | `anomaly.containment.captured` | 異常存在が収容設備に実際に収容された件数 |
| `L-018` | Anomaly：異常存在の研究完了件数 | 維持 | 中 | `anomaly.research.completed` | 異常存在研究Defが未完了→完了に変わった数。Q-001のDLCフィルタ |
| `L-019` | Anomaly：異常存在の脱走回数 | 維持 | 中 | `anomaly.containment.escapes` | 追跡対象の収容から脱走が成立した件数。研究・解放による取り出し除外 |
| `L-020` | Anomaly：儀式の結果別回数 | 維持 | 中 | `anomaly.ritual.results` | Anomaly儀式の結果別確定件数。Ideologyと分離 |
| `L-021` | Anomaly：収容成功率・失敗回数 | 維持 | 中 | `anomaly.containment.success_rate+anomaly.containment.failed` | 収容試行の明示成功÷明示成功/失敗数、および明示失敗数 |
| `N1-E-008` | Anomaly：最長異常存在収容日数 | 維持 | 中 | `anomaly.containment.duration_max` | 同一存在の収容開始→確定終了の最長日数。終了未観測は含めない |
| `N1-E-009` | Anomaly：最大同時収容異常存在数 | 維持 | 中 | `anomaly.containment.concurrent_peak` | 同時収容存在数の最大。収容の実状態を使う |

## Phase D · odyssey

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#odyssey)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `L-022` | Odyssey：グラヴシップの移動回数 | 維持 | 中 | `odyssey.gravship.flights` | 離陸から着陸まで正常完了したプレイヤーグラヴシップ移動数 |
| `L-023` | Odyssey：グラヴシップの累計移動距離 | 維持 | 中 | `odyssey.gravship.distance` | 完了フライトの確定ルート距離合計（バニラ経路コストのタイル単位） |
| `L-024` | Odyssey：訪問した場所の数 | 維持 | 中 | `odyssey.gravship.unique_tiles` | 完了フライトの到着 (layer,tile) ユニーク数 |
| `L-026` | Odyssey：新マップへの着陸回数 | 維持 | 中 | `odyssey.gravship.new_map_landings` | L-022のうち着陸用に新規生成されたMapへの着陸数 |

## Phase D · ranking

採用 · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#ranking)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `S-003` | 難易度別の生存・クリア記録 | 維持 | 中 | `ranking.by_difficulty` | v1代表観測の難易度ラベル別のコロニー経過日数合計と採用済みエンディング件数。難易度変更履歴とは区別 |
| `S-004` | パーマデス別の生存・クリア記録 | 維持 | 中 | `ranking.by_permadeath` | v1代表観測のパーマデス別に同じ2値。明示不明の保存は不明枠 |
| `R-003` | 全コロニー中の最多同時入植者数 | ランキング・派生表示 | 中 | `ranking.population.peak` | v1の GreatestPopulation のコロニー横断最大とそのコロニー |
| `R-007` | 最速エンディング到達日数 | ランキング・派生表示 | 高 | `ranking.ending.fastest` | v1の採用済みエンディングのうちフック実測tickが有効な最短日数。後日発見トレースは除外 |
| `R-010` | 1年あたり平均資産増加額 | 資産・成長 | 高 | `ranking.wealth.net_per_year` | H-007の有効値合計÷その系列の合計観測年数（各系列 (点数−1)÷120）。1点のみは除外 |
| `R-013` | 最も多くクリアしたシナリオと回数 | ランキング・派生表示 | 中 | `ranking.ending.top_scenario` | v1代表観測のシナリオ名で分類した採用済みエンディング件数の最多分類 |
| `R-014` | ストーリーテラー別の通算生存日数 | ランキング・派生表示 | 中 | `ranking.survival.by_storyteller` | v1代表観測のストーリーテラー別にコロニー経過日数を合計 |
| `R-015` | 難易度別の累計エンディング回数 | ランキング・派生表示 | 中 | `=S-003` | S-003のエンディング件数内訳へリンク。同じ数値を二重表示しない |
| `R-016` | 最多実プレイ時間のコロニー | ランキング・派生表示 | 中 | `ranking.playtime.peak` | v1コロニー別実プレイ秒の最大とそのコロニー |

## Phase D · ranking_dependency

条件付き · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#ranking-dependency)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `R-001` | 全コロニー中の最多敵撃破数 | ランキング・派生表示 | 高 | `ranking.kills.peak` | B-001代表値の最大と該当コロニー |
| `R-002` | 全コロニー中の最長無死亡期間 | ランキング・派生表示 | 高 | `ranking.death_free.peak` | B-020代表値の最大と該当コロニー |
| `R-004` | 全コロニー中の最多製作品数 | ランキング・派生表示 | 中 | `ranking.crafting.peak` | W-003代表値の最大と該当コロニー |
| `R-005` | 最高1回あたり販売額のコロニー横断記録 | ランキング・派生表示 | 中 | `ranking.trade.peak_sale` | T-004代表値の最大と該当コロニー |
| `R-006` | 単一コロニーの最多研究完了数 | ランキング・派生表示 | 中 | `ranking.research.peak` | S-012代表完了数の最大と該当コロニー。Q-001で代用しない |
| `R-008` | 最少犠牲者でのエンディング到達 | ランキング・派生表示 | 高 | `ranking.ending.min_deaths` | エンディング確定時に保存した v1死亡カウンター値の最小。既存記録の最新値で代用しない |
| `R-009` | 入植者1人あたり最高資産 | ランキング・派生表示 | 中 | `ranking.wealth.per_colonist` | 同一インデックスの Wealth_Total÷FreeColonists (>0) の観測最大と該当コロニー |
| `R-012` | 敵撃破数と入植者死亡数の比率 | ランキング・派生表示 | 高 | `ranking.kills_to_colonist_deaths` | B-001とP-004で同一計測区間を持つコロニーの撃破総数÷入植者死亡総数 |
| `N1-F-001` | バイオーム別の累計生存日数 | ランキング・派生表示 | 高 | `ranking.survival.by_biome` | 開始タイルのバイオーム別のコロニー経過日数合計。居住地移動履歴の意味を持たない |
| `N1-F-002` | バイオーム別のエンディング達成数 | ランキング・派生表示 | 高 | `ranking.endings.by_biome` | 同じ開始タイル分類のv1採用済みエンディング件数 |
| `N1-F-003` | 無死亡エンディング達成数 | ランキング・派生表示 | 高 | `ranking.ending.zero_deaths` | R-008と同じエンディング時カウンターが明示0の件数 |
| `N1-F-004` | 最少人数でのエンディング達成 | ランキング・派生表示 | 中 | `ranking.ending.min_party` | エンディング成立時に確定参加した入植者数の最小。参加定義がある結末のみ |

## Phase D · unsupported

見送り · [取得元と条件](../../research/2026-11-07-numeric-statistics-expansion-evidence.md#unsupported)

| ID | 原候補 | 一次分類 | 優先 | メトリクスキー | 定義・集計 |
|---|---|---|---|---|---|
| `R-011` | 1襲撃あたり平均損害額 | ランキング・派生表示 | 中 | `ranking.raid.avg_damage` | 同一追跡襲撃の直接帰属資産損失額合計÷帰結確定襲撃数。セーブ前後の総資産差は使用不可 |
