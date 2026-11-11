# Rimworld-Statistics 全404項目の表示経路・初期非表示キー監査（2026-11-11）

調査固定ソース：`pengin0503/Rimworld-Statistics@336f9ffd19c5a17d792716a37961a6229de8d838`（未マージ PR #102）。**調査のみで統計実装・取得設定は変更しない。**

## 結論

- 固定カタログ **404項目**（基本321、コロニー間派生83）すべてが、`MetricRegistry.Compose` と「詳細 → すべて」から到達可能な設計。
- 初期画面「詳細 → 生涯累積／全コロニー」の対象は **143項目**。**261項目**は初期表示に出ないが、適切な表示モードに変更すれば登録済みの値を参照できる。
- **12項目**は「コロニー別」のみ、**105項目**は「全コロニー」のみ、**287項目**は双方で表示対象。フィルターなし・未測定のみフィルターOFFが前提。
- 実験的取得ユニットはデフォルトで新規記録が無効だが、表示対象からは外されない。SDK由来の追加項目はプロフィールの保存済み有効なProvider定義に依存し、この404には含まれない。

## 数値が保存されていても「—／不明」に見える条件

1. `MetricCalculator.ColonyRow` は一般に最後の代表保存・checkpointから値を読む。**過去の記録に数値が存在しても、選ばれた観測に当該キーがなければUnknown**になり得る。古い保存へのfallbackはSDK項目と`factions`など限定的。個別の実セーブで発生したかは未実証。
2. `MetricCalculator.Attach` により既存コロニーと一意に結び付かない数値観測は、`Unattributed`件数としてのみ報告され、数値はどのコロニーにも集計されない。
3. 不一致のmetric revision、checkpointの競合、検疫（quarantine）、ユーザー除外コロニー、DevMode限定エンディング等は、記録が残っていても集計・表示対象外になることがある。
4. SDK定義を失ったデータや、**ユーザーが明示的に削除した旧キー**は、保存データを破棄しなくても現行表示カタログに現れない。
5. 「計測済みのみ」をONにするとUnknown/対象外は消える（実測0は残る）。検索・カテゴリ・生涯累積の絞込みも項目を非表示にする。

## 初期画面に表示されない全261キー（PR #102）

### A. コロニー別でのみ表示（12件）

| 登録キー | 名称・内容 | C# 定義参照 |
|---|---|---|
| `colony.mods.latest` | MOD数（最新のセーブ） | `MetricKeys.ModsLatest` |
| `colony.mods.min` | MOD数の最小 | `"colony.mods.min"` |
| `colony.mods.peak` | MOD数の最大 | `"colony.mods.peak"` |
| `colony.start.biome` | 開始地点のバイオーム | `ColonyStartSnapshot.Biome` |
| `diplomacy.snapshot.goodwill_range` | 勢力の友好度範囲 | `FactionStateSnapshot.GoodwillRange` |
| `population.snapshot.passion` | スキル別情熱人数 | `RosterSnapshot.Passions` |
| `research.live.completed_ticks` | 研究の完了日（一覧） | `ResearchFamily.CompletedTicks` |
| `trade.buys.top_item` | 最も多く買った品 | `TradeFamily.TopBought` |
| `trade.sales.top_item` | 最も多く売った品 | `TradeFamily.TopSold` |
| `wealth.total.first` | 初期資産（最初の観測） | `MetricKeys.WealthTotalFirst` |
| `wealth.total.peak_day` | 資産が最高だった日（観測日） | `MetricKeys.WealthTotalPeakDay` |
| `work.harvest.top_crop` | 最も多く収穫した作物 | `WorkFamily.TopCrop` |

### B. 全コロニーでのみ表示されるランキング／派生（105件）

| 登録キー | 名称・内容 | C# 定義参照 |
|---|---|---|
| `cross.max.biotech.mechs.created` | 最大コロニー：製造したメカノイド総数 | `cross.max.BiotechFamily.MechsCreated` |
| `cross.max.colony.mods.latest` | 最大コロニー：MOD数（最新のセーブ） | `cross.max.MetricKeys.ModsLatest` |
| `cross.max.combat.hostile_kills` | 最大コロニー：敵の撃破数 | `cross.max.HealthDeathFamily.HostileKills` |
| `cross.max.diplomacy.snapshot.relations` | 最大コロニー：友好・敵対勢力数 | `cross.max.FactionStateSnapshot.Relations` |
| `cross.max.medicine.consumed` | 最大コロニー：使った医薬品の数 | `cross.max.MedicineFamily.MedicineUsed` |
| `cross.max.medicine.tends` | 最大コロニー：治療を行った回数 | `cross.max.MedicineFamily.Tends` |
| `cross.max.membership.first_join` | 最大コロニー：新規加入者数 | `cross.max.MembershipFamily.FirstJoin` |
| `cross.max.population.snapshot.animals` | 最大コロニー：飼育動物数 | `cross.max.RosterSnapshot.Animals` |
| `cross.max.population.snapshot.free` | 最大コロニー：生存中の自由入植者 | `cross.max.RosterSnapshot.Free` |
| `cross.max.population.snapshot.mechs` | 最大コロニー：自軍メカノイド数 | `cross.max.RosterSnapshot.Mechs` |
| `cross.max.population.snapshot.prisoners` | 最大コロニー：生存中のコロニー囚人 | `cross.max.RosterSnapshot.Prisoners` |
| `cross.max.population.snapshot.slaves` | 最大コロニー：生存中のコロニー奴隷 | `cross.max.RosterSnapshot.Slaves` |
| `cross.max.research.live.completed` | 最大コロニー：記録中に完了した研究の数 | `cross.max.ResearchFamily.Completed` |
| `cross.max.research.snapshot.finished` | 最大コロニー：完了した研究の数 | `cross.max.ResearchStateSnapshot.Finished` |
| `cross.max.research.snapshot.finished_rate` | 最大コロニー：研究完了率 | `cross.max.ResearchStateSnapshot.FinishedRate` |
| `cross.max.royalty.psycasts` | 最大コロニー：サイキック能力の発動成功回数 | `cross.max.RoyaltyFamily.Psycasts` |
| `cross.max.stock.food_nutrition.latest` | 最大コロニー：最後の食料備蓄栄養量 | `cross.max.StockSnapshot.FoodLatest` |
| `cross.max.stock.materials.latest` | 最大コロニー：スチール・コンポーネント所持数 | `cross.max.StockSnapshot.MaterialsLatest` |
| `cross.max.stock.silver.latest` | 最大コロニー：最後のシルバー所持数 | `cross.max.StockSnapshot.SilverLatest` |
| `cross.max.trade.buys.total` | 最大コロニー：交易の購入額の合計 | `cross.max.TradeFamily.BuysTotal` |
| `cross.max.trade.buys.units` | 最大コロニー：交易で買った品の個数 | `cross.max.TradeFamily.BuysUnits` |
| `cross.max.trade.count` | 最大コロニー：商人との取引回数 | `cross.max.TradeFamily.Deals` |
| `cross.max.trade.sales.total` | 最大コロニー：交易の売上の合計 | `cross.max.TradeFamily.SalesTotal` |
| `cross.max.trade.sales.units` | 最大コロニー：交易で売った品の個数 | `cross.max.TradeFamily.SalesUnits` |
| `cross.max.wealth.buildings.latest` | 最大コロニー：建築物資産の最終額 | `cross.max.MetricKeys.WealthBuildingsLatest` |
| `cross.max.wealth.items.latest` | 最大コロニー：アイテム資産の最終額 | `cross.max.MetricKeys.WealthItemsLatest` |
| `cross.max.wealth.pawns.latest` | 最大コロニー：人員・動物資産の最終額 | `cross.max.MetricKeys.WealthPawnsLatest` |
| `cross.max.work.crafting.units` | 最大コロニー：製作した品の総数 | `cross.max.WorkFamily.CraftedUnits` |
| `cross.max.work.harvest.units` | 最大コロニー：作物の収穫量 | `cross.max.WorkFamily.HarvestUnits` |
| `cross.max.work.mining.units` | 最大コロニー：採掘した資源量 | `cross.max.WorkFamily.MiningUnits` |
| `cross.min.biotech.mechs.created` | 最小コロニー：製造したメカノイド総数 | `cross.min.BiotechFamily.MechsCreated` |
| `cross.min.colony.mods.latest` | 最小コロニー：MOD数（最新のセーブ） | `cross.min.MetricKeys.ModsLatest` |
| `cross.min.combat.hostile_kills` | 最小コロニー：敵の撃破数 | `cross.min.HealthDeathFamily.HostileKills` |
| `cross.min.diplomacy.snapshot.relations` | 最小コロニー：友好・敵対勢力数 | `cross.min.FactionStateSnapshot.Relations` |
| `cross.min.medicine.consumed` | 最小コロニー：使った医薬品の数 | `cross.min.MedicineFamily.MedicineUsed` |
| `cross.min.medicine.tends` | 最小コロニー：治療を行った回数 | `cross.min.MedicineFamily.Tends` |
| `cross.min.membership.first_join` | 最小コロニー：新規加入者数 | `cross.min.MembershipFamily.FirstJoin` |
| `cross.min.population.snapshot.animals` | 最小コロニー：飼育動物数 | `cross.min.RosterSnapshot.Animals` |
| `cross.min.population.snapshot.free` | 最小コロニー：生存中の自由入植者 | `cross.min.RosterSnapshot.Free` |
| `cross.min.population.snapshot.mechs` | 最小コロニー：自軍メカノイド数 | `cross.min.RosterSnapshot.Mechs` |
| `cross.min.population.snapshot.prisoners` | 最小コロニー：生存中のコロニー囚人 | `cross.min.RosterSnapshot.Prisoners` |
| `cross.min.population.snapshot.slaves` | 最小コロニー：生存中のコロニー奴隷 | `cross.min.RosterSnapshot.Slaves` |
| `cross.min.research.live.completed` | 最小コロニー：記録中に完了した研究の数 | `cross.min.ResearchFamily.Completed` |
| `cross.min.research.snapshot.finished` | 最小コロニー：完了した研究の数 | `cross.min.ResearchStateSnapshot.Finished` |
| `cross.min.research.snapshot.finished_rate` | 最小コロニー：研究完了率 | `cross.min.ResearchStateSnapshot.FinishedRate` |
| `cross.min.royalty.psycasts` | 最小コロニー：サイキック能力の発動成功回数 | `cross.min.RoyaltyFamily.Psycasts` |
| `cross.min.stock.food_nutrition.latest` | 最小コロニー：最後の食料備蓄栄養量 | `cross.min.StockSnapshot.FoodLatest` |
| `cross.min.stock.materials.latest` | 最小コロニー：スチール・コンポーネント所持数 | `cross.min.StockSnapshot.MaterialsLatest` |
| `cross.min.stock.silver.latest` | 最小コロニー：最後のシルバー所持数 | `cross.min.StockSnapshot.SilverLatest` |
| `cross.min.trade.buys.total` | 最小コロニー：交易の購入額の合計 | `cross.min.TradeFamily.BuysTotal` |
| `cross.min.trade.buys.units` | 最小コロニー：交易で買った品の個数 | `cross.min.TradeFamily.BuysUnits` |
| `cross.min.trade.count` | 最小コロニー：商人との取引回数 | `cross.min.TradeFamily.Deals` |
| `cross.min.trade.sales.total` | 最小コロニー：交易の売上の合計 | `cross.min.TradeFamily.SalesTotal` |
| `cross.min.trade.sales.units` | 最小コロニー：交易で売った品の個数 | `cross.min.TradeFamily.SalesUnits` |
| `cross.min.wealth.buildings.latest` | 最小コロニー：建築物資産の最終額 | `cross.min.MetricKeys.WealthBuildingsLatest` |
| `cross.min.wealth.items.latest` | 最小コロニー：アイテム資産の最終額 | `cross.min.MetricKeys.WealthItemsLatest` |
| `cross.min.wealth.pawns.latest` | 最小コロニー：人員・動物資産の最終額 | `cross.min.MetricKeys.WealthPawnsLatest` |
| `cross.min.work.crafting.units` | 最小コロニー：製作した品の総数 | `cross.min.WorkFamily.CraftedUnits` |
| `cross.min.work.harvest.units` | 最小コロニー：作物の収穫量 | `cross.min.WorkFamily.HarvestUnits` |
| `cross.min.work.mining.units` | 最小コロニー：採掘した資源量 | `cross.min.WorkFamily.MiningUnits` |
| `cross.sum.population.snapshot.passion` | 合計：スキル別情熱人数 | `cross.sum.RosterSnapshot.Passions` |
| `cross.top.biotech.child_stages` | 最多品目／分類：子供の成長段階到達数 | `cross.top.BiotechFamily.ChildStages` |
| `cross.top.combat.kills.victim_kind` | 最多品目／分類：撃破した敵の種類 | `cross.top.HealthDeathFamily.KillsByVictimKind` |
| `cross.top.ideology.ritual.results` | 最多品目／分類：儀式の結果別開催数 | `cross.top.IdeologyFamily.RitualResults` |
| `cross.top.medicine.surgery.count` | 最多品目／分類：手術の回数 | `cross.top.MedicineFamily.Surgeries` |
| `cross.top.membership.route` | 最多品目／分類：加入経路別の人数 | `cross.top.MembershipFamily.Routes` |
| `cross.top.mental.breaks.by_def` | 最多品目／分類：種類別の精神崩壊 | `cross.top.MentalFamily.BreaksByDef` |
| `cross.top.mental.inspiration.by_def` | 最多品目／分類：種類別のひらめき | `cross.top.MentalFamily.InspirationsByDef` |
| `cross.top.population.death.causes` | 最多品目／分類：入植者の死因 | `cross.top.HealthDeathFamily.DeathCauses` |
| `cross.top.population.snapshot.passion` | 最多品目／分類：スキル別情熱人数 | `cross.top.RosterSnapshot.Passions` |
| `cross.top.raid.arrival_mode` | 最多品目／分類：到着方法別の襲撃回数 | `cross.top.RaidFamily.ByArrivalMode` |
| `cross.top.raid.factions` | 最多品目／分類：勢力別の襲撃回数 | `cross.top.RaidFamily.ByFaction` |
| `cross.top.stock.materials.latest` | 最多品目／分類：スチール・コンポーネント所持数 | `cross.top.StockSnapshot.MaterialsLatest` |
| `cross.top.trade.buys.units` | 最多品目／分類：交易で買った品の個数 | `cross.top.TradeFamily.BuysUnits` |
| `cross.top.trade.count` | 最多品目／分類：商人との取引回数 | `cross.top.TradeFamily.Deals` |
| `cross.top.trade.sales.units` | 最多品目／分類：交易で売った品の個数 | `cross.top.TradeFamily.SalesUnits` |
| `cross.top.work.construction.material_units` | 最多品目／分類：建築で消費した資源 | `cross.top.WorkFamily.ConstructionMaterials` |
| `cross.top.work.cooking.by_def` | 最多品目／分類：種類別の調理数 | `cross.top.WorkFamily.MealsByDef` |
| `cross.top.work.crafting.completed_by_def` | 最多品目／分類：製作完了品の種類別累計 | `cross.top.WorkDetailsFamily.CraftedByDef` |
| `cross.top.work.crafting.quality` | 最多品目／分類：品質別の製作数 | `cross.top.WorkFamily.QualityBreakdown` |
| `cross.top.work.crops.sown` | 最多品目／分類：植えた作物の数 | `cross.top.WorkFamily.Sown_` |
| `cross.top.work.harvest.by_crop` | 最多品目／分類：作物別の収穫量 | `cross.top.WorkFamily.HarvestByCrop` |
| `cross.top.work.mining.units_by_def` | 最多品目／分類：採掘資源の種類別累計 | `cross.top.WorkDetailsFamily.MinedByDef` |
| `ranking.by_difficulty` | 難易度別の通算日数 | `MetricRankings.ByDifficultyDays` |
| `ranking.by_difficulty.endings` | 難易度別のエンディング数 | `MetricRankings.ByDifficultyEndings` |
| `ranking.by_permadeath` | パーマデス別の通算日数 | `MetricRankings.ByPermadeathDays` |
| `ranking.by_permadeath.endings` | パーマデス別のエンディング数 | `MetricRankings.ByPermadeathEndings` |
| `ranking.crafting.peak` | 単一コロニーでの最多製作品数 | `MetricRankings.MostCrafting` |
| `ranking.death_free.peak` | 最長無死亡期間のコロニー横断記録 | `MetricRankings.LongestDeathFree` |
| `ranking.ending.fastest` | 最速のエンディング到達 | `MetricRankings.FastestEnding` |
| `ranking.ending.min_deaths` | 最少犠牲者でのエンディング | `MetricRankings.MinDeathsAtEnding` |
| `ranking.ending.min_party` | 最少人数でのエンディング達成 | `MetricRankings.MinPartyAtEnding` |
| `ranking.ending.top_scenario` | 最も多くエンディングを迎えたシナリオ | `MetricRankings.TopScenario` |
| `ranking.ending.zero_deaths` | 死者なしのエンディング | `MetricRankings.ZeroDeathEndings` |
| `ranking.endings.by_biome` | 開始バイオーム別のクリア回数 | `MetricRankings.EndingsByBiome` |
| `ranking.kills_to_colonist_deaths` | 敵撃破数と入植者死亡数の比率 | `MetricRankings.KillsToDeaths` |
| `ranking.kills.peak` | 単一コロニーでの最多敵撃破数 | `MetricRankings.MostKills` |
| `ranking.playtime.peak` | 実プレイ時間が最も長いコロニー | `MetricRankings.PlaytimePeak` |
| `ranking.population.peak` | 最多同時入植者数（全コロニー） | `MetricRankings.PopulationPeak` |
| `ranking.research.peak` | 最多研究完了数のコロニー記録 | `MetricRankings.MostResearch` |
| `ranking.survival.by_biome` | 開始バイオーム別の累計生存日数 | `MetricRankings.DaysByBiome` |
| `ranking.survival.by_storyteller` | ストーリーテラー別の通算日数 | `MetricRankings.DaysByStoryteller` |
| `ranking.trade.peak_sale` | 単一取引での最高販売額 | `MetricRankings.LargestSale` |
| `ranking.wealth.net_per_year` | 1年あたりの平均資産増加 | `MetricRankings.NetWealthPerYear` |
| `ranking.wealth.per_colonist` | 入植者1人あたりの最高資産 | `MetricKeys.WealthPerColonistMax` |

### C. 両画面で表示できるが、生涯累積の初期フィルターから外れる（144件）

| 登録キー | 名称・内容 | C# 定義参照 |
|---|---|---|
| `biotech.bandwidth.used_peak` | メカニター最高使用帯域 | `BiotechStateSnapshot.BandwidthUsedPeak` |
| `biotech.birth.mothers` | 出産した入植者のユニーク人数 | `BiotechFamily.BirthMothers` |
| `biotech.child_stages` | 子供の成長段階到達数 | `BiotechFamily.ChildStages` |
| `biotech.mechs.created` | 製造したメカノイド総数 | `BiotechFamily.MechsCreated` |
| `biotech.mechs.repairs` | 完了したメカノイド修理回数 | `BiotechFamily.MechRepairs` |
| `biotech.pollution.cells_peak` | ホーム拠点の最大汚染セル数 | `BiotechStateSnapshot.PollutionCellsPeak` |
| `biotech.pollution.cleaned` | 入植者が除染したマップセル数 | `BiotechFamily.PollutionCleaned` |
| `biotech.waste.created` | 生成された毒性廃棄物パック数 | `BiotechFamily.WasteCreated` |
| `biotech.waste.disposed` | 無害化処分した毒性廃棄物パック数 | `BiotechFamily.WasteDisposed` |
| `caravan.concurrent_max` | 同時に運行したキャラバンの最大数 | `CaravanFamily.ConcurrentMax` |
| `caravan.distance_max` | 1回の遠征で進んだ最長距離 | `CaravanFamily.LongestDistance` |
| `caravan.duration_max` | 最長の遠征日数 | `CaravanFamily.LongestExpedition` |
| `combat.hostile_kills.per_pawn_max` | 個人の最多撃破数 | `HealthDeathFamily.KillsPerPawnMax` |
| `defense.raids_per_year` | 1年あたりの敵の襲撃 | `MetricKeys.RaidsPerYear` |
| `diplomacy.snapshot.relations` | 友好・敵対勢力数 | `FactionStateSnapshot.Relations` |
| `facilities.battery_capacity.peak` | 最大蓄電容量 | `FacilitiesSnapshot.BatteryPeak` |
| `facilities.generation_capacity.peak` | 最大発電定格容量 | `FacilitiesSnapshot.GenerationPeak` |
| `facilities.growing_cells.peak` | 栽培ゾーン最大面積 | `FacilitiesSnapshot.GrowingCellsPeak` |
| `facilities.home_maps.peak` | 同時にロードされた拠点最多数 | `FacilitiesSnapshot.HomeMapsPeak` |
| `facilities.outdoor_temp.max` | 保存時の最高屋外気温 | `FacilitiesSnapshot.OutdoorTempMax` |
| `facilities.outdoor_temp.min` | 保存時の最低屋外気温 | `FacilitiesSnapshot.OutdoorTempMin` |
| `facilities.turrets.peak` | 防衛タレット最多数 | `FacilitiesSnapshot.TurretsPeak` |
| `food.harvest.per_growing_cell` | 栽培セル日あたりの収穫量 | `FoodFamily.HarvestPerGrowingCell` |
| `ideology.conversion.days_mean` | 改宗までの平均日数 | `IdeologyFamily.ConversionDaysMean` |
| `ideology.conversions` | 改宗が成立した回数 | `IdeologyFamily.Conversions` |
| `ideology.ritual.results` | 儀式の結果別開催数 | `IdeologyFamily.RitualResults` |
| `ideology.ritual.success_rate` | 儀式成功率 | `IdeologyFamily.RitualSuccessRate` |
| `ideology.role_appointments` | 思想役職への任命回数 | `IdeologyFamily.RoleAppointments` |
| `medicine.disease.duration_mean` | 病気の平均罹患日数 | `MedicineFamily.DiseaseDurationMean` |
| `medicine.hospital.stay_mean` | 平均入院期間 | `MedicineFamily.HospitalStayMean` |
| `medicine.infection.duration_max` | 感染症の最長罹患日数 | `MedicineFamily.InfectionDurationMax` |
| `medicine.injured_peak` | 同時負傷入植者の最多人数 | `RosterSnapshot.InjuredPeak` |
| `medicine.injuries.per_pawn_max` | 最も多く負傷した入植者 | `MedicineFamily.InjuriesPerPawnMax` |
| `medicine.resurrections.per_pawn_max` | 同じ入植者の最多蘇生回数 | `MedicineFamily.ResurrectionsPerPawnMax` |
| `medicine.surgery.best_doctor` | 最も多く手術に成功した医師 | `MedicineFamily.BestSurgeon` |
| `medicine.surgery.success_rate` | 手術の成功率 | `MedicineFamily.SurgerySuccessRate` |
| `medicine.tend_quality_mean` | 平均治療品質 | `MedicineFamily.TendQualityMean` |
| `medicine.tends.best_doctor` | 最も多く治療した医師 | `MedicineFamily.BestTender` |
| `membership.captive_max` | 最長の囚われ期間 | `MembershipFamily.CaptiveMax` |
| `membership.first_day` | 最初の新規加入までの日数 | `MembershipFamily.FirstJoinDay` |
| `membership.join_age_mean` | 加入時の平均年齢 | `MembershipFamily.JoinAgeMean` |
| `membership.tenure_mean` | 加入後の平均在籍期間 | `LifetimeFamily.TenureMean` |
| `mental.breaks.by_def` | 種類別の精神崩壊 | `MentalFamily.BreaksByDef` |
| `mental.inspiration.by_def` | 種類別のひらめき | `MentalFamily.InspirationsByDef` |
| `odyssey.gravship.distance` | グラヴシップの確定移動距離合計 | `OdysseyFamily.Distance` |
| `odyssey.gravship.flights` | グラヴシップの完了移動数 | `OdysseyFamily.Flights` |
| `odyssey.gravship.new_map_landings` | 新規マップへの着陸数 | `OdysseyFamily.NewMapLandings` |
| `odyssey.gravship.unique_tiles` | 到着したユニーク地点数 | `OdysseyFamily.UniqueTiles` |
| `population.death.disease_rate` | 病死の割合 | `HealthDeathFamily.DiseaseDeathRate` |
| `population.death.first_day` | 最初の入植者死亡までの日数 | `HealthDeathFamily.FirstDeathDay` |
| `population.death.longest_gap` | 最長無死亡期間 | `HealthDeathFamily.LongestDeathGap` |
| `population.death.oldest` | 最も高齢で死亡した入植者の年齢 | `HealthDeathFamily.OldestDeathAge` |
| `population.deaths_per_year` | 1年あたりの入植者死亡 | `MetricKeys.DeathsPerYear` |
| `population.free.first_10` | 入植者10人到達（観測日） | `MetricKeys.FreeColonistsFirst10` |
| `population.free.mean` | 入植者数の平均（観測） | `MetricKeys.FreeColonistsMean` |
| `population.free.min` | 入植者数の最小（観測） | `MetricKeys.FreeColonistsMin` |
| `population.life.mean` | 歴代入植者の平均寿命 | `LifetimeFamily.LifeMean` |
| `population.mood.at_least_80d` | 心情80%以上の日数（観測） | `MetricKeys.MoodAtLeast80Days` |
| `population.mood.below_50d` | 心情50%未満の日数（観測） | `MetricKeys.MoodBelow50Days` |
| `population.mood.max` | 平均心情の最高（観測） | `MetricKeys.MoodMax` |
| `population.mood.mean` | 平均心情の平均（観測） | `MetricKeys.MoodMean` |
| `population.mood.min` | 平均心情の最低（観測） | `MetricKeys.MoodMin` |
| `population.prisoners.mean` | 囚人数の平均（観測） | `MetricKeys.PrisonersMean` |
| `population.prisoners.peak` | 囚人数の最大（観測） | `MetricKeys.PrisonersPeak` |
| `population.skill.join_gain_peak` | 加入以降の最大スキル上昇幅 | `LifetimeFamily.JoinGainPeak` |
| `population.snapshot.age_mean` | 生存入植者の平均年齢 | `RosterSnapshot.AgeMean` |
| `population.snapshot.animal_defs_peak` | 飼育動物の最多種類数 | `RosterSnapshot.AnimalDefsPeak` |
| `population.snapshot.animals` | 飼育動物数 | `RosterSnapshot.Animals` |
| `population.snapshot.augmented_peak` | 強化義肢装着入植者の最多人数 | `RosterSnapshot.AugmentedPeak` |
| `population.snapshot.free` | 生存中の自由入植者 | `RosterSnapshot.Free` |
| `population.snapshot.mechs` | 自軍メカノイド数 | `RosterSnapshot.Mechs` |
| `population.snapshot.oldest` | 生存入植者の最高齢 | `RosterSnapshot.Oldest` |
| `population.snapshot.prisoners` | 生存中のコロニー囚人 | `RosterSnapshot.Prisoners` |
| `population.snapshot.skill20_pawns_peak` | スキル20の入植者最多人数 | `RosterSnapshot.Skill20PawnsPeak` |
| `population.snapshot.skill20_per_pawn_peak` | 入植者1人のスキル20最多種類数 | `RosterSnapshot.Skill20PerPawnPeak` |
| `population.snapshot.slaves` | 生存中のコロニー奴隷 | `RosterSnapshot.Slaves` |
| `quest.retained.success_duration_max` | 成功クエストの最長日数（受諾から終了処理まで） | `MetricKeys.QuestSuccessDurationMax` |
| `quest.retained.success_duration_mean` | 成功クエストの平均日数（受諾から終了処理まで） | `MetricKeys.QuestSuccessDurationMean` |
| `quest.retained.success_duration_min` | 成功クエストの最短日数（受諾から終了処理まで） | `MetricKeys.QuestSuccessDurationMin` |
| `quest.retained.success_rate` | クエスト成功率（保存内） | `MetricKeys.QuestSuccessRate` |
| `raid.duration.max` | 最長の襲撃継続日数 | `RaidFamily.DurationMax` |
| `raid.duration.mean` | 襲撃の平均継続日数 | `RaidFamily.DurationMean` |
| `raid.duration.min_repelled` | 最も短時間で撃退した襲撃 | `RaidFamily.DurationMinRepelled` |
| `raid.first_day` | 最初の敵襲までの日数 | `RaidFamily.FirstDay` |
| `raid.gap.mean` | 敵襲の平均発生間隔 | `RaidFamily.MeanGap` |
| `raid.kills.max` | 1回の襲撃での最多撃破数 | `RaidFamily.KillsMax` |
| `raid.longest_gap` | 最長無襲撃期間 | `RaidFamily.LongestGap` |
| `raid.pawns.max` | 最大規模の襲撃（人数） | `RaidFamily.PawnsMax` |
| `raid.points.max` | 最大の襲撃ポイント | `RaidFamily.PointsMax` |
| `raid.repelled.rate` | 襲撃の撃退率 | `RaidFamily.RepelledRate` |
| `research.live.duration_max` | 研究の最長所要日数 | `ResearchFamily.DurationMax` |
| `research.live.duration_min` | 研究の最短所要日数 | `ResearchFamily.DurationMin` |
| `research.live.first_day` | 最初の研究完了までの日数 | `ResearchFamily.FirstDay` |
| `research.snapshot.finished` | 完了した研究の数 | `ResearchStateSnapshot.Finished` |
| `research.snapshot.finished_rate` | 研究完了率 | `ResearchStateSnapshot.FinishedRate` |
| `research.snapshot.tech_peak` | 完了研究の最高技術段階 | `ResearchStateSnapshot.TechPeak` |
| `royalty.highest_title` | 最高到達爵位 | `RoyaltyFamily.HighestTitle` |
| `royalty.honor` | 帝国名誉の獲得・消費 | `RoyaltyFamily.Honor` |
| `royalty.permit_uses` | 帝国許可証の使用成立回数 | `RoyaltyFamily.PermitUses` |
| `royalty.psycasts` | サイキック能力の発動成功回数 | `RoyaltyFamily.Psycasts` |
| `royalty.psylink.max` | 最高サイリンクレベル | `RoyaltyStateSnapshot.PsylinkPeak` |
| `royalty.quest_success` | 帝国関係クエストの達成数 | `RoyaltyQuestSnapshot.Success` |
| `royalty.title_grants` | 獲得した爵位の回数 | `RoyaltyFamily.TitleGrants` |
| `stock.food_nutrition.latest` | 最後の食料備蓄栄養量 | `StockSnapshot.FoodLatest` |
| `stock.food_nutrition.min` | 食料備蓄栄養量の最小値 | `StockSnapshot.FoodMin` |
| `stock.legendary_weapons.peak` | レジェンダリー武器最多数 | `StockSnapshot.LegendaryWeaponsPeak` |
| `stock.materials.latest` | スチール・コンポーネント所持数 | `StockSnapshot.MaterialsLatest` |
| `stock.medicine.peak` | 医薬品備蓄最多数 | `StockSnapshot.MedicinePeak` |
| `stock.persona_weapons.peak` | ペルソナ武器最多数 | `StockSnapshot.PersonaWeaponsPeak` |
| `stock.silver.latest` | 最後のシルバー所持数 | `StockSnapshot.SilverLatest` |
| `stock.weapon_defs.peak` | 保有武器の最多種類数 | `StockSnapshot.WeaponDefsPeak` |
| `stock.weapons.peak` | 保有武器最多数 | `StockSnapshot.WeaponsPeak` |
| `trade.buys.peak` | 1回の取引の最大購入額 | `TradeFamily.BuysPeak` |
| `trade.orbital.count` | 商船との取引回数 | `TradeFamily.OrbitalDeals` |
| `trade.sales.peak` | 1回の取引の最大販売額 | `TradeFamily.SalesPeak` |
| `trade.sales.peak_unit` | 最高の単品販売価格 | `TradeFamily.SalesPeakUnit` |
| `trade.visiting_caravan.count` | 訪れた商隊との取引回数 | `TradeFamily.VisitingCaravanDeals` |
| `wealth.buildings.latest` | 建築物資産の最終額 | `MetricKeys.WealthBuildingsLatest` |
| `wealth.buildings.max_share` | 建築物資産の最大比率（観測） | `MetricKeys.WealthBuildingsMaxShare` |
| `wealth.buildings.peak` | 建築物資産の最高額 | `MetricKeys.WealthBuildingsPeak` |
| `wealth.items.latest` | アイテム資産の最終額 | `MetricKeys.WealthItemsLatest` |
| `wealth.items.peak` | アイテム資産の最高額 | `MetricKeys.WealthItemsPeak` |
| `wealth.pawns.latest` | 人員・動物資産の最終額 | `MetricKeys.WealthPawnsLatest` |
| `wealth.pawns.peak` | 人員・動物資産の最高額 | `MetricKeys.WealthPawnsPeak` |
| `wealth.total.first_100k` | 資産10万到達（観測日） | `MetricKeys.WealthTotalFirst100K` |
| `wealth.total.first_1m` | 資産100万到達（観測日） | `MetricKeys.WealthTotalFirst1M` |
| `wealth.total.longest_growth` | 資産が増え続けた最長期間（観測） | `MetricKeys.WealthTotalLongestGrowth` |
| `wealth.total.max_gain_15d` | 15日間の最大資産増加（観測） | `MetricKeys.WealthTotalMaxGain15D` |
| `wealth.total.max_gain_half_day` | 半日での最大資産増加（観測） | `MetricKeys.WealthTotalMaxGainHalfDay` |
| `wealth.total.max_growth_rate_year` | 1年間の最大資産成長率（観測） | `MetricKeys.WealthTotalMaxGrowthRateYear` |
| `wealth.total.max_loss_15d` | 15日間の最大資産減少（観測） | `MetricKeys.WealthTotalMaxLoss15D` |
| `wealth.total.max_loss_half_day` | 半日での最大資産減少（観測） | `MetricKeys.WealthTotalMaxLossHalfDay` |
| `wealth.total.max_loss_rate_half_day` | 半日での最大資産減少率（観測） | `MetricKeys.WealthTotalMaxLossRateHalfDay` |
| `wealth.total.mean` | 総資産の平均（観測） | `MetricKeys.WealthTotalMean` |
| `wealth.total.net` | 資産の純増（最終 − 初期） | `MetricKeys.WealthTotalNet` |
| `work.construction.best_worker` | 最も多く建築した入植者 | `WorkFamily.BestBuilder` |
| `work.cooking.best_worker` | 最も多く調理した入植者 | `WorkFamily.BestCook` |
| `work.crafting.best_price` | 最高価格の製作品 | `WorkFamily.BestPrice` |
| `work.crafting.best_quality` | 製作した最高品質 | `WorkFamily.BestQuality` |
| `work.crafting.best_worker` | 最も多く製作した入植者 | `WorkFamily.BestCrafter` |
| `work.crafting.daily_peak` | 1日の最多製作数 | `WorkFamily.DailyPeak` |
| `work.harvest.best_worker` | 最も多く収穫した入植者 | `WorkFamily.BestHarvester` |
| `work.harvest.year_peak` | 1年の最多収穫量 | `WorkFamily.HarvestYearPeak` |
| `work.mining.best_worker` | 最も多く採掘した入植者 | `WorkFamily.BestMiner` |

## 正式版mainも含めた差分

- 正式版v1.4.0は **415定義**、初期表示は **147件**、初期非表示は **268件**。
- PR #102では10個の旧メトリックを削除し、以前削除決定された稼働中発電機と派生ランキングも除去した。一方、奴隷数および派生ランキングが追加された。正式版のみのキーを含む完全比較は元ソース `main` を参照すること。
- SDK動的定義数・保存済み未帰属データ件数・実画面の表示一致率は実際のprofile.jsonとゲーム実行結果がないため算定していない。

## ソース

- `Source/RimworldStatistics.Core/Stats/{MetricRegistry,MetricDisplayPolicy,LiveCatalog,SnapshotCatalog,RankingDependencyCatalog,CrossColonyCatalog,MetricCalculator}.cs`
- `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`, `Source/RimworldStatistics/UI/{Dialog_RimworldStatistics,DetailsTab,OverviewTab,ColoniesTab,EndingsTab}.cs`
- `Source/RimworldStatistics.Core/Stats/{ModMetricProviderRegistry,ExperimentalAcquisitionPolicy}.cs`
- `Languages/Japanese/Keyed/RimworldStatistics.xml`, `docs/research/2026-11-09-historical-recovery-inventory.csv`


## 追加：旧来の概要統計にある「コロニー別の専用表示欄なし」

上記の404/415件は数値レジストリの定義数であり、**別経路で記録されるv1基本統計も監査対象に含める必要がある**。\
`StatsCalculator.ColonyStats` は次の数値を**コロニー単位で計算・保持**するが、`ColoniesTab.Columns` の表には専用列がなく、`DetailsTab` に同一の基礎統計をコロニー別で再提示する定義もない。

| コロニー別の記録値 | 現在見える場所 | 欠ける表示 |
|---|---|---|
| `ColonyStats.Threats`（脅威イベント数） | `OverviewTab` の全コロニー合計 | **各コロニーの脅威件数**の専用欄なし |
| `ColonyStats.Departed`（脱出／離脱者数） | `OverviewTab` の全コロニー合計と `EndingsTab` の各エンディング記録 | **コロニー別累計の離脱者数**の専用欄なし |
| `ColonyStats.MaxPopulation`（最大人口） | `OverviewTab` の全コロニー最大値と `ranking.population.peak` の最大コロニー順位 | **各コロニーの最大人口**の専用欄なし |
| `ColonyStats.MaxRealPlay`（実プレイ時間） | `OverviewTab` の全コロニー合計と `ranking.playtime.peak` の最大コロニー順位 | **各コロニーの実プレイ時間**の専用欄なし |

`ColonyStats.Difficulty` と `ColonyStats.Permadeath` は数値統計ではなく記録済みの属性。これらも `ColoniesTab` に個別の専用列はなく、`MetricRankings` の難易度別／パーマデス別集計に利用される。**いずれもデータ未記録のためではなく、UIの列・定義の欠落によるコロニー別非表示**である。全コロニーの値まで完全に見えないという意味ではない。

対象コード：`Source/RimworldStatistics.Core/Stats/StatsCalculator.cs`（`ColonyStats`, `StatsResult`）、`Source/RimworldStatistics/UI/{ColoniesTab,OverviewTab,EndingsTab}.cs`、`Source/RimworldStatistics.Core/Stats/MetricRankings.cs`。この調査では表示欄の追加は行っていない。
