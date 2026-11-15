# 現在のMOD実装候補（2026-11-15時点）

**現行の未対応・未完了候補：58件。** このページを唯一の実装候補一覧とする。既に対応済みのMODは[対応MOD一覧](../supported-mods.md)に掲載し、ここでは再列挙しない。以下は**候補**であり、実装済み・完全な1.7互換性検証済みを意味しない。

2026-11-15の整理では、過去の候補台帳に登録されていた168件のうち、**80件は既に対応MOD一覧に掲載済み**、**1件は汎用統計で扱うため専用MOD対応を見送った項目（Vanilla Books Expanded）**、残る**87件が現在の実装候補**となった。その後、一括実装で対応済みになったMOD（A02/B07/S28/B06/C02/D05/S03/C05/S13/S58/H05/S01/S04/S38/S59/A04/N03/S32）を順次[対応MOD一覧](../supported-mods.md)へ移行し、既存基盤・汎用統計で網羅済みの以下11件を見送り（除外）とした：
- **S34 Vanilla Vehicles Expanded – Tier 3**：車両の搭乗・アップグレード・残骸修復・所有スナップショットは既存のVehicle FrameworkおよびVanilla Vehicles ExpandedアダプターがDef別内訳付きで取得しており、Tier 3独自C#は心情Thoughtのみ。
- **C03 Vanilla Races Expanded – Highmate**：1.7/1.6アセンブリ監査の結果、ゼノタイプ・遺伝子分布は既存のMembership/Roster基盤が収集しており、C#機能（`CompAbilityEffect_InitiateLovin`・`InteractionWorker_FlirtingAttempt`・`PsychicBond`追加特性）は汎用恋愛・交際・精神絆と重複するため専用統計を見送り。
- **D03 Vanilla Ideology Expanded – Memes and Structures**：1.7配布DLL（`VanillaMemesExpanded.dll`）監査の結果、全追加儀式（`RitualOutcomeEffectWorker_*`）はバニラ`RitualOutcomeEffectWorker_FromQuality`継承のため既存Ideology儀式収集器が結果・内訳とも自動収集しており、`CompAbility*`も既存汎用統計（取引・調教・集会等）と重複するため専用統計を見送り。
- **D04 Vanilla Traits Expanded**：1.7配布DLL（`VanillaTraitsExpanded.dll`）監査の結果、特性保有は既存のRoster基盤が収集し、能力・心情・精神崩壊補正や`JobDriver_StealItems`等も既存汎用統計に現れるため専用統計を見送り。
- **S35 Vanilla Base Generation Expanded**：公開ソース・配布構成監査の結果、独自DLLを持たない純粋XML拠点生成拡張であり、拠点制圧・マップ破壊は既存の汎用統計で取得されるため専用統計を見送り。
- **S49 Vanilla Psycasts Expanded – Hemosage**：公開ソースおよび`Hemosage.xml`監査の結果、独自DLLを持たず全追加サイキャスト能力が`VanillaPsycastsExpanded.AbilityExtension_Psycast`を使用しており、既存の`VpeCastMetricPatch`で発動回数・Def内訳とも自動計測済みのため専用統計を見送り。
- **S53 Vanilla Storytellers Expanded - Perry Persistent**：公開ソース監査の結果、独自DLLを持たない純粋XMLストーリーテラー・インシデント定義拡張であり、イベント発生は既存汎用統計で取得されるため専用統計を見送り。
- **S20 Dire Raids (Continued)**：1.7公開ソースおよび`1.7/Assemblies/DireRaids.dll`監査の結果、唯一の独自イベント実装である`DireRaids.IncidentWorker_DireRaidEnemy`がバニラ`IncidentWorker_RaidEnemy`を継承しており、既存の`RaidMetricPatches.cs`（`IncidentWorker.TryExecute`）が`DireRaidEnemy`のDef別回数・規模・戦略・派閥まで完全収集しているため、二重計上回避のため専用統計を見送り。
- **R04 Gloomy Dragonian race / R05 Rabbie The Moonrabbit race / R06 Yuran race**：[人気種族・ゼノタイプMOD監査（2026-11-14）](2026-11-14-popular-race-mod-audit.md)の確定方針に基づき、既存の共通`Humanoid Alien Races`分類層（`GameFacts.Species`）が加入・死亡・在籍統計を種族Def別に収集済みであり、1.7固有の確定成果イベントが公開ソース上未確認・専用アダプター不要のため新規実装を見送り（※本ページのルールに従い、固有統計未実装のため[対応MOD一覧](../supported-mods.md)への個別移動は行わない）。

## 第3・第4波の未対応候補（1件）

| ID | MOD | 詳細資料 | 未対応理由・必要情報 |
|---|---|---|---|
| F06 | [SRTS Expanded (continued)](2026-11-13-non-ve-discovery.md) | [調査台帳](2026-11-13-non-ve-discovery.md) | Steam配布の1.7最新ZIP/DLL（`1.7`フォルダ構成・成功コールバック・SRTS系派生との切り分け）未提供（公開ミラーは1.6止まり）。 |


## 人気MOD追加調査の未対応候補（1件）

| ID | MOD | 詳細資料 | 未対応理由・必要情報 |
|---|---|---|---|
| C06 | [The Dead Man's Switch II](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478) | [調査台帳](2026-11-14-popular-mod-discovery-register.md) | Steam配布の1.7実アセンブリ（DLL/Def/About.xml）および公式ソース未公開のため、固有成功点・汎用生産/研究統計との重複監査が未実施。 |


## 種族・ゼノタイプの未対応候補（2件）

| ID | MOD | 詳細資料 | 未対応理由・必要情報 |
|---|---|---|---|
| R01 | [NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166) | [調査台帳](2026-11-14-popular-race-mod-audit.md) | 公開GitHubの`LoadFolders.xml`は1.6止まりであり、Workshop版1.7パッケージ（DLL/LoadFolders）およびハムスター発電機の非Tick実稼働確定経路の検証資料が不足。 |
| R03 | [[OA]Ratkin Faction: Oberonia aurea](https://steamcommunity.com/sharedfiles/filedetails/?id=3159926804) | [調査台帳](2026-11-14-popular-race-mod-audit.md) | Steam限定1.7配布DLL/XML未提供。独自Honor/キャンペーンの確定遷移点と通常派閥/クエスト統計との重複分離が未検証。 |


## 第6波の未対応候補（46件）

| ID | MOD | 詳細資料 | 未対応理由・必要情報 |
|---|---|---|---|
| S02 | [Gravship Voyage](https://steamcommunity.com/sharedfiles/filedetails/?id=3790613573) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL/XML未提供（Odyssey汎用宇宙船移動統計との重複回避検証が必要）。 |
| S05 | [ReGrowth 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2260097569) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 1.7配布DLL/Def（BiomeDef・固有メカニズム有無）未提供。バニラ上書き系との切り分け確認が必要。 |
| S06 | [GravTide](https://steamcommunity.com/sharedfiles/filedetails/?id=3779600989) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（Odyssey・Vehicle Framework連携後の成功点確認が必要）。 |
| S07 | [As above, So below 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3776015553) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（多層マップ階層データの永続ID・同一コロニー帰属の仕様確認が必要）。 |
| S08 | [Colony dungeon! [1.7]](https://steamcommunity.com/sharedfiles/filedetails/?id=3828091246) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（ダンジョンクリア確定フラグの確認が必要）。 |
| S09 | [Ancestral Ruins (beta)](https://steamcommunity.com/sharedfiles/filedetails/?id=3831032590) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | ベータ版・Steam配布1.7実DLL未提供（固有イベント仕様の安定確認待ち）。 |
| S10 | [Rimworld Exploration Mode](https://steamcommunity.com/sharedfiles/filedetails/?id=2941608795) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（霧探索・マップ発見状態の保存フィールド確認が必要）。 |
| S11 | [Ancient urban ruins](https://steamcommunity.com/sharedfiles/filedetails/?id=3316062206) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 公開GitHubは1.6止まり。1.7配布版パッケージ（DLL/XML）の提供待ち。 |
| S12 | [[RH2] Faction: V.O.I.D.](https://steamcommunity.com/sharedfiles/filedetails/?id=2883208829) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL/Def未提供（独自イベント有無と汎用襲撃/撃破統計での網羅可否の確定が必要）。 |
| S14 | [Kraltech Industries (1.7) (unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3820851894) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 非公式1.7移植のSteam配布DLL未提供（多段階ボス変身・最終討伐フックの確認が必要）。 |
| S15 | [Rimesis](https://steamcommunity.com/sharedfiles/filedetails/?id=3767949538) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（Rimesis個体追跡・撃退/捕獲/討伐判定の確認が必要）。 |
| S16 | [Back For Vengeance](https://steamcommunity.com/sharedfiles/filedetails/?id=3761967749) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（復讐襲撃固有IncidentDefと再襲撃区別ロジックの確認が必要）。 |
| S17 | [Apex Mechanoids](https://steamcommunity.com/sharedfiles/filedetails/?id=3792325789) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL/Def未提供（汎用メカ培養・キル内訳と独立メトリクス要否の確認が必要）。 |
| S18 | [[MAP]机械族机械师 Mechanoid Mechanitor (1.6/1.7)](https://steamcommunity.com/sharedfiles/filedetails/?id=3811415429) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（メカ種族メカニター資格・独自勝利フックの確認が必要）。 |
| S19 | [Glitterworld Destroyer 5 Expansion ：MechFusion etc](https://steamcommunity.com/sharedfiles/filedetails/?id=3549290904) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（MechFusion融合完了フックの確認が必要）。 |
| S21 | [VVE Raid](https://steamcommunity.com/sharedfiles/filedetails/?id=3720926003) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（車両襲撃・破壊確定とVehicle Framework統計の切り分け確認が必要）。 |
| S22 | [Rimsenal Faction Pack - Federation](https://steamcommunity.com/workshop/filedetails/?id=736172213) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実アセンブリ/Def未提供（汎用襲撃・キル統計との区別確認が必要）。 |
| S23 | [Rimsenal Faction Pack - Feral](https://steamcommunity.com/sharedfiles/filedetails/?id=736207111) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実アセンブリ/Def未提供（汎用襲撃・キル統計との区別確認が必要）。 |
| S24 | [Horrors (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3839223635) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（巣窟サイト制圧等の成功確定地点の確認が必要）。 |
| S25 | [Zombieland 1.7 (Unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3827574769) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 非公式1.7移植のSteam配布DLL未提供（ゾンビ治療・固有イベントの版差確認が必要）。 |
| S26 | [Dynamic Diplomacy - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3220299022) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（プレイヤー関与外交とNPC間外交の分離仕様確認が必要）。 |
| S27 | [Hearts of RimWorld - Grand Strategy Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3833698946) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（師団勝利・停戦状態フィールドのAPI確認が必要）。 |
| S29 | [Mines 2.0](https://steamcommunity.com/sharedfiles/filedetails/?id=2503894706) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（通常作業ビルとの差異・独自採掘フック確認が必要）。 |
| S30 | [Misc. Robots](https://steamcommunity.com/sharedfiles/filedetails/?id=724602224) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（ロボット作業完了・ベース起動単位のAPI確認が必要）。 |
| S31 | [Quarry [Adopted]](https://steamcommunity.com/workshop/filedetails/?id=2007576583) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（実ドロップ生成フックと資源採掘量のAPI確認が必要）。 |
| S33 | [Rimsential - Spaceports (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3225120958) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 公開GitHubは1.6止まり。1.7配布版DLLの提供待ち（汎用交易・輸送シャトルとの切り分けが必要）。 |
| S36 | [VGP Vegetable Garden](https://steamcommunity.com/sharedfiles/filedetails/?id=2007061826) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実XML/DLL未提供（固有作業・通常栽培との重複可否確認が必要）。 |
| S37 | [Biotech Cloning (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3348781223) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 公開ソースは1.4以前のみ。1.7維持版の選択DLL未提供（版差・通常培養との重複検証が必須）。 |
| S39 | [Dark Psychic Rituals: The Following](https://steamcommunity.com/sharedfiles/filedetails/?id=3596468709) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（Anomaly固有儀式と汎用儀式収集の重複確認が必要）。 |
| S40 | [Expanded Prosthetics and Organ Engineering - Forked](https://steamcommunity.com/sharedfiles/filedetails/?id=1949064302) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 公開ソースは1.4止まり。1.7配布版DLL/XML未提供（汎用製作・医療手術統計で網羅可能か確認が必要）。 |
| S41 | [Integrated Implants](https://steamcommunity.com/sharedfiles/filedetails/?id=3223443793) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 公開ソースは1.6止まり。1.7配布版DLL未提供（独自インプラント動作と汎用手術・アビリティ統計の区別確認が必要）。 |
| S42 | [Gene Remover Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3280784251) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（遺伝子除去完了フックの確認が必要）。 |
| S43 | [Gene Ripper](https://steamcommunity.com/sharedfiles/filedetails/?id=3524806362) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（遺伝子抽出完了フック・対象生死判定の確認が必要）。 |
| S44 | [[AA] Empire Expanded Forces](https://steamcommunity.com/sharedfiles/filedetails/?id=3837526614) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL/Def未提供（Empire関連既存統計との重複回避検証が必要）。 |
| S45 | [Enhanced Ideology](https://steamcommunity.com/sharedfiles/filedetails/?id=3793123680) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（汎用Ideology統計との重複切り分けが必要）。 |
| S46 | [Corrosion / Anomaly](https://steamcommunity.com/sharedfiles/filedetails/?id=3731466157) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（Anomalyエンティティ状態・浄化フック確認が必要）。 |
| S47 | [Mechanoid Repurposing](https://steamcommunity.com/sharedfiles/filedetails/?id=3799572082) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（メカ再利用完了フックの確認が必要）。 |
| S48 | [Anomaly Mutations Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3836076384) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（変異付与・安定化イベントフック確認が必要）。 |
| S50 | [Progression: Therapy](https://steamcommunity.com/sharedfiles/filedetails/?id=3753615736) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（カウンセリング完了・効果確定フックの確認が必要）。 |
| S51 | [Psychology — 1.7 (RADMIKO continuation)](https://steamcommunity.com/sharedfiles/filedetails/?id=3829501925) / [Resurrected](https://steamcommunity.com/sharedfiles/filedetails/?id=3838502425) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | 2系統並存＋Steam配布1.7実DLL未提供（選挙・治療・互換系統差異の確認が必要）。 |
| S52 | [ISEKAI RPG LEVELING](https://steamcommunity.com/sharedfiles/filedetails/?id=3657580708) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（レベルアップ・クラス確定APIの確認が必要）。 |
| S54 | [Psycasts²](https://steamcommunity.com/sharedfiles/filedetails/?id=3761869362) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（バニラRoyaltyサイキャスト・VPEパッチとの区別確認が必要）。 |
| S55 | [Teenage Years - Identity](https://steamcommunity.com/sharedfiles/filedetails/?id=3757880146) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（Biotech成長瞬間との区別・専用イベント確認が必要）。 |
| S56 | [Rim Academia — School Life & Growing Up](https://steamcommunity.com/sharedfiles/filedetails/?id=3827963827) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（卒業・授業成果と既存教育MOD統計の区別確認が必要）。 |
| S57 | [H.P. Lovecraft Storyteller Revived](https://steamcommunity.com/sharedfiles/filedetails/?id=3824124450) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL/XML未提供（独自イベント有無の確認が必要）。 |
| S60 | [Rimpsyche - Personality Core](https://steamcommunity.com/sharedfiles/filedetails/?id=3535112473) | [調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md) | Steam配布1.7実DLL未提供（独立した完了成果フックが存在するか確認が必要）。 |


## 第6波の保留・再評価候補（8件）

元台帳の見送り・保留理由は技術的な注意事項として保持し、候補からの除外理由とはしない。

| ID | MOD | 元の保留理由（要再評価） |
|---|---|---|
| HOLD-01 | [Medieval Overhaul](https://steamcommunity.com/workshop/filedetails/?id=3219596926) | 大型定番MODだが、1.7対応はコミュニティ製Modpack側の調整が中心で本体の公式1.7アセンブリ安定性が未確定のため保留。 |
| HOLD-02 | [Vanilla Temperature Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3202046258) | 1.7対応済みだが、空調・ダクトの継続稼働や瞬間状態が中心で「独立した確定成果」を切り出しにくいため見送り。 |
| HOLD-03 | [What the Hack?! (Continued)](https://steamcommunity.com/workshop/filedetails/?id=3372841828) | 1.7環境でのハッキング不具合報告および後継作者への引き継ぎ過渡期のため保留。 |
| HOLD-04 | [Fortifications - Industrial](https://steamcommunity.com/sharedfiles/filedetails/?id=2561619583) | 1.7環境での消失不具合報告・1.7対応待ち状態が確認されたため除外。 |
| HOLD-05 | [Reinforced Mechanoid 2 (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3541404610) | 1.7環境での消失不具合報告・1.7対応待ち状態が確認されたため除外。 |
| HOLD-06 | [Vanilla Factions Expanded – Mechanoids](https://www.reddit.com/r/RimWorld/comments/1mro1kj/i_miss_the_factories_and_machines_of_vanilla/) | 1.6以降未更新のため対象外。 |
| HOLD-07 | [Vanilla Anomaly Expanded – Insanity](https://steamcommunity.com/sharedfiles/filedetails/?id=3342412435) | 1.7フォルダ未整備・ワールド開始不可報告があるため見送り。 |
| HOLD-08 | [WVC - Xenotypes and Genes](https://steamcommunity.com/sharedfiles/filedetails/?id=2886992038) | Steam版が1.7正式対応前（GitHubベータ段階）のため、正式リリース待ちとして保留。 |

詳細： [第6波調査台帳](2026-11-15-sixthwave-popular-mods-register-ja.md)

## 状態判定と更新ルール

- **対応済みへ移動する条件**：Rimworld-Statistics本体に対象MODの適切な固有統計が実装され、コード・登録キー・重複計上対策を確認できること。専用アダプターが不要な共通HAR等の分類のみでは、固有成果統計の実装済みとは判定しない。
- **実装済みになったら**：本一覧から該当MODを削除し、[対応MOD一覧](../supported-mods.md)に1行追加（既にあれば重複追加しない）。部分実装で明確な追加統計の課題が残る場合は、残件の追跡をIssueまたは技術メモで継続する。
- **実装判断**：原資料の優先度・1.7実アセンブリ・成功確定イベント・二重計上回避・DLC不在時の動作を参照。未検証の実績を対応済みにしない。
- **履歴**：元台帳、検証資料、実装時のレビューは削除せず保持する。本文の登録件数は現行候補の件数に限り、過去の累積調査件数と混同しない。
