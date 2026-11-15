# 87件のMOD統計実装に向けた事前調査（2026-11-15）

対象リポジトリ：`pengin0503/Rimworld-Statistics`、調査時点main：`3f93cca7a017d1af4b6dd4d6689c6757d8a92166`。対象は[現在の実装候補87件](active-candidates.md)の登録IDに厳密に一致する。

**結論：87件全体の実装候補に対し、原資料を再照合し、既存集計との重複を検討し、実装時に調べるべき成功判定・危険箇所を1件ずつ整理した。** この文書は統計実装やRimWorld 1.7でのゲーム内検証の完了を意味しない。上流ソースの実確認は下記E2/E3に限られ、その他は台帳由来の提案を実装仮説として具体化したもの。DLL・メソッド署名が未検証なら新規統計の登録を行わない。

## 調査の到達点

| 状態 | 件数 | 解釈 |
|---|---:|---|
| E3 | 5 | 上流ソースの具体的な処理・ローダを読んだ（ただし正式実装フックは未検証） |
| E2 | 3 | 上流ソースの構造・対応コードを確認した（フック未確定） |
| E1 | 4 | Workshop/modLang等の互換・依存情報を確認した（DLL未照合） |
| E0 | 56 | 既存台帳とホスト実装から技術的な実装方針を立案（外部成果フック未検証） |
| G | 11 | 固有達成の存在自体を再確認すべき候補 |
| B | 8 | 1.7互換性・供給状態などのブロッカー付き候補 |
| **計** | **87** | **候補は削除しない** |

## ホストコードとの照合と共通実装方針

現行Rimworld-Statisticsには、`IntegratedMods/PopularModOutcomes/Source/PopularOutcomes.cs`、`LandformOutcomes.cs`、`RaceBirthOutcomes.cs`、`NewModOutcomes/Source/OutcomeMetric.cs` が存在する。これらはMODのPackageIdを指定したoptional adapter、`MetricGates.IsRecording`、`GameFacts.Record`、`QualifiedDefKey(packageId,defName)`、ロード・リプレイ抑制やcheckpoint集計の基盤として参照可能である。

1. **既存統計の利用を先に判断**：バニラの製作・手術・資産・キル・襲撃・クエスト成功・出産・交易・研究を既に数えているケースでは、MOD固有の内訳だけを付け、全体累計へ二重加算しない。データ粒度が同じなら重複collectorを作らない。
2. **結果確定後だけ計測**：ポーン/機体の実spawn、アイテムの実placement、産出・受領の実量、クエストの成功状態、世界マップタイルへの実到達、治療後のHediff消滅など。予約・実行開始・UI表示・警告・仮想定格出力を実績にしない。
3. **増分・最大・状態の区別**：生涯個数は確定イベントのincrement、資産最高・深度などはmax、現在の稼働数/個体数はsnapshotで記録。tick積算の取扱いは専用測定根拠がある場合に限定する。
4. **所有者とセーブの境界**：自派閥・プレイヤー拠点への帰属を必須とし、NPC間の戦争や他派閥killを混入させない。ロード復元・リトライ・分岐セーブ・重複サイト訪問は保存済み一意IDまたは状態遷移で除外。
5. **任意MOD/DLCの隔離**：必須パッケージの存在と実ロードDLLを照合し、対象MODが無い環境ではreflection/patchをしない。Anomaly、Odyssey等が無いときも起動可能とし、過去の記録は保持する。
6. **負テスト**：取消、割込み、デバッグ処理、開始後失敗、設備破壊、二段階変身、二重報酬、セーブロード、MOD抜去、別派閥、追加時の古い記録、TPS/コストを個別に確認する。

## 上流コードで確かめた実装ポイント

- **A02 Vanilla Furniture Expanded – Power**：公開ソースの`Nuclear/CompPowerPlantNuclear.cs`に`signalMeltdown`のfalse→true設定、天候遷移、`CompBreakdownable.DoBreakdown()`がある。1.7の`loadfolders.xml`は1.6を選択。成功判定は単なる温度警告より後に設定。
  - ソース：[VanillaFurnitureExpanded-Power](https://github.com/Vanilla-Expanded/VanillaFurnitureExpanded-Power)、`1.6/Source/VanillaPowerExpanded/VanillaPowerExpanded/Nuclear/CompPowerPlantNuclear.cs`。
- **B07 Vanilla Helixien Gas Expanded**：`CompDeepExtractor_TryProducePortion.Prefix`が対象のガス井下に`PipeNet`があれば`DistributeAmongStorage(min(30, AvailableCapacity), out _)`を呼び元処理を抑制する。抽出した総量を作る際には受入量（out）を確認する必要がある。
  - ソース：[VanillaHelixienGasExpanded](https://github.com/Vanilla-Expanded/VanillaHelixienGasExpanded)、`Source/VHelixienGasE/Harmony/CompDeepExtractor.cs`。
- **S28 Vanilla Recycling Expanded**：1.7は`1.6`ロード。`CompSuperSimpleProcessor.DoAtomize()`から`TrySpawnAtCell`が呼ばれ、出力は`GenPlace.TryPlaceThing`の成否で確定する。投入物のstack減少を成功完了と混同しない。
  - ソース：[VanillaRecyclingExpanded](https://github.com/Vanilla-Expanded/VanillaRecyclingExpanded)、`1.6/Source/.../Comps/CompSuperSimpleProcessor.cs`。
- **S13 Alpha Mechs**：1.7のロード先は`1.6`。`CompChangeDef.CompTick`はポーン生成/置換の呼出しを含む。spawnだけでボス撃破や培養成功とは判断できない。
  - ソース：[AlphaMechs](https://github.com/juanosarg/AlphaMechs)、`1.6/Source/.../Comps/CompChangeDef.cs`。
- **S03 Alpha Biomes**：`loadFolders.xml`が1.7で`1.6`と各DLC追加フォルダを選択。BiomeWorkerの存在を確認。これは地形集計の設計材料であって完了カウンターの確定点ではない。
  - ソース：[AlphaBiomes](https://github.com/juanosarg/AlphaBiomes)。
- **B06 Vanilla Landmarks Expanded**：`TileMutatorWorker`群を公開ソースで確認。既存の`LandformOutcomes`の判定手法を流用できる可能性があるが、ランドマークの一意識別と実player homeへの帰属は別途検証。
  - ソース：[VanillaLandmarksExpanded](https://github.com/Vanilla-Expanded/VanillaLandmarksExpanded)。
- **S37 Biotech Cloning (Continued)**：参照先の原版リポジトリに`Patch_PregnancyUtility_ApplyBirthOutcome`と`CloneUtils.IsClone`を確認。**Continued 1.7版のソース/配布DLLと同一とは未確認**。維持版の`ApplyBirthOutcome`競合報告があるため、出生回数をそのままclone成功回数と見なさない。
  - 原版ソース：[RimWorld.Cloning](https://github.com/JTJutajoh/RimWorld.Cloning)。
- **S38 Anomalies Expected**：`loadFolders.xml`は1.7で1.6と1.7の両フォルダを列挙。`Comp_EntityDatabaseAnomaly`の`entityIncidentsAvailable`は`entityCodexEntryDef.Discovered`を読んでおり、UI/incident予約ではなく**未発見→発見の結果を更新する箇所**が必要。
  - ソース：[Anomalies-Expected](https://github.com/MrHydralisk/Anomalies-Expected)。
- **S29 Mines 2.0**：作者のWorkshop説明では2026-10-13に1.7専用ビルドを配布、1.7.4976で実動作確認済み。ただしイベントのメソッド署名は未照合。
  - [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2503894706)。
- **S01 Deadlife / A04 Stews**：modLangの静的確認では両者が1.7対応表記。DeadlifeにはAnomalyとVEF、StewsにはVEFとVanilla Cooking Expandedが必須。**配布DLLにおける成功フックは未確認**。
  - [Deadlife](https://modlang.app/en/rimworld/mods/vanillaquestsexpanded_deadlife) / [Stews](https://modlang.app/en/rimworld/mods/vanillaexpanded_vcookestews)。
- **F06 SRTS**：1.7対応を示す外部掲載は存在。ただし過去に入手した配布物のDLLは1.6用であり、実際の1.7動作・完了地点を再確認する必要がある。

## 全87件：個別の実装アプローチと残る証拠

各行の「推奨する観測点」は、コード照合済みの項目を除き**実装仮説**である。メソッド名を推測で確定扱いしない。個別のMOD機能・元のリスク・優先度はリンクした調査台帳を優先する。

### 初期の残件（2件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **F06** [SRTS Expanded (continued)](2026-11-13-non-ve-discovery.md) | SRTSシャトルが移動先マップへ実際に到着して乗員・貨物が出現した時点を、機体・航行IDで1回記録 | 最新1.7配布DLLと移動成功イベントの照合。発射・着陸予約や輸送ポッド統計との重複排除 | **E1**・配布DLL待ち | 
| **H05** [Biomes! Bees](2026-11-13-fourthwave-follow-on-research.md) | 養蜂設備に紐づく実受粉成果を、植物Growthの正の変化または確定収穫増分で測定 | 開発用spawn以外の通常蜂生成と自勢力施設への帰属の証拠。一般植物成長と分離 | **E0**・成功経路要証明 | 

### 追加の人気MOD（11件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **A02** [Vanilla Furniture Expanded – Power](https://steamcommunity.com/sharedfiles/filedetails/?id=2062943477) | 原子炉のメルトダウンはflag単独でなくflag新規成立＋実際の設備破損の後に1件確定 | 1.7で1.6ソース選択を確認済み。復旧・セーブ復元・複数tick重複を防止 | **E3**・上流コード照合済 | 
| **A04** [Vanilla Cooking Expanded – Stews](https://steamcommunity.com/sharedfiles/filedetails/?id=2134312965) | Stewsの専用加工設備の完成バッチをVEF処理完了時にDef別記録 | 通常Cookingの製作数と二重計上しない。実生産/完成コールバックの所属を確認 | **E1**・VEF共通化を先行 | 
| **B06** [Vanilla Landmarks Expanded](https://github.com/Vanilla-Expanded/VanillaLandmarksExpanded) | 既存LandformOutcomesの観測構造を流用し、プレイヤー本拠地のLandmark/TileMutator識別情報を分類 | 表示時の読み取りでワールド生成やマッププレビューを操作しない。識別IDの永続性を確認 | **E2**・上流ツリー確認済 | 
| **B07** [Vanilla Helixien Gas Expanded](https://github.com/Vanilla-Expanded/VanillaHelixienGasExpanded) | ガス井上のCompDeepExtractor.TryProducePortionでPipeNetに実受領された量を集計 | 既存VCE Deepchemと共有可能だが、net.DistributeAmongStorage受入れ量を検証。容量0/再走査を除外 | **E3**・上流コード照合済 | 
| **C02** [Vanilla Furniture Expanded – Spacer Module](https://github.com/Vanilla-Expanded/VanillaFurnitureExpanded-Spacer) | Spacer修理設備が実際に回復させたHP差分と対象Defの完了修理件数 | 通常修理Job・遠隔修理の二重計上防止。差分0・最大HPを除外 | **E0**・成功フック要確認 | 
| **C03** [Vanilla Races Expanded – Highmate](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Highmate) | Highmate特有の関係成立・専用行動の完了後に成立状態を計測 | 一般Lovin/交際/妊娠との重複と他MODによる同名相互作用を回避 | **G**・固有成果要再評価 | 
| **C05** [Mechanoid Upgrades](https://steamcommunity.com/sharedfiles/filedetails/?id=3365118555) | Mechanoid Upgradesの付与・除去結果を機体/upgrade IDで記録 | 入れ替え・初期装備・ロード復元・RimWorld既存メカ成長の混同を排除 | **E0**・ソース・DLL要確認 | 
| **C06** [The Dead Man's Switch II](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478) | Dead Man's Switch II固有の研究段階・機体解放・シナリオ勝利の最終確定状態 | 製作数/研究数で代替可能な候補は専用統計にしない。旧版とは区別 | **E0**・ソース・DLL要確認 | 
| **D03** [Vanilla Ideology Expanded – Memes and Structures](https://github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Memes) | Trading Fair等MOD固有機能の確定取引・召喚完了だけを記録 | 既存Ideology儀式・交易イベントとの重複。XMLのみなら新規アダプター見送り | **G**・固有成果要再評価 | 
| **D04** [Vanilla Traits Expanded](https://github.com/Vanilla-Expanded/VanillaTraitsExpanded) | Vanilla Traits固有の有意な出来事のみ、トレイトDefと成功副作用で限定計数 | 心情/ダメージ/逃走の既存計数を流用可否判断。汎用トレイト所持回数は作らない | **G**・固有成果要再評価 | 
| **D05** [Vanilla Weapons Expanded – Non-Lethal](https://github.com/Vanilla-Expanded/VanillaWeaponsExpanded-NonLethal) | Non-Lethal武器固有の確定無力化成功を命中後の結果と損傷Defから抽出 | 通常キル・ダウン・状態異常のgeneric値に重複させない。射撃試行は除外 | **G**・固有成果要再評価 | 

### 種族・ゼノタイプ（5件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **R01** [NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166) | Ratkin発電輪の実稼働/出力を所有設備と結び付け、成功した電力生産のみ測る | 定格電力や在庫snapshotsの通算加算は不正。電力単位と時間積分の妥当性が必要 | **E0**・専用成果・DLL要確認 | 
| **R03** [[OA]Ratkin Faction: Oberonia aurea](https://steamcommunity.com/sharedfiles/filedetails/?id=3159926804) | Oberonia固有の爵位・名誉の昇格確定、派閥関連の達成マイルストーンを記録 | バニラ称号や他種族HAR分類で代用できるものは作らない | **E0**・固有成果要再評価 | 
| **R04** [Gloomy Dragonian race](https://steamcommunity.com/sharedfiles/filedetails/?id=2960593459) | Dragonianは既存HAR種族分類で加入・死亡・人数ピークを集計する案を第一候補に | 独自MODとしての実装済み表示は固有達成が見つかった場合のみ。新規キー乱立を避ける | **G**・共通分類優先 | 
| **R05** [Rabbie The Moonrabbit race](https://steamcommunity.com/sharedfiles/filedetails/?id=1837246563) | RabbieはHAR分類優先、惑星儀式・依存の発症/治療等に確定イベントがある場合のみ専用化 | 一時的なHediff・移動や消失から成果を推測しない | **G**・共通分類優先 | 
| **R06** [Yuran race](https://steamcommunity.com/sharedfiles/filedetails/?id=2844129100) | YuranはHAR分類優先、血統継承・巫女任命の固有完遂のみ新規イベント | 単なる種族・遺伝子Defの存在は専用統計と見なさない | **G**・共通分類優先 | 

### 第5波（1件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **N03** [Caravan Adventures](https://steamcommunity.com/sharedfiles/filedetails/?id=2558957509) | Caravan Adventures固有の報酬・賞金クレジットが実決済された量とストーリー章完了を計上 | NPC交換と汎用交易/クエスト成功の二重計上。配布DLLの成功関数を特定 | **E0**・配布DLL要確認 | 

### 第6波（60件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **S01** [Vanilla Quests Expanded – Deadlife](https://steamcommunity.com/sharedfiles/filedetails/?id=3497226454) | Deadlifeのサイロ電力復旧・司令部攻略ステージの最終quest stateを観測 | Anomaly DLC、VEFが必要。一般クエスト成功と二重計上せず固有ステージIDを採る | **E1**・成功フック要確認 | 
| **S02** [Gravship Voyage](https://steamcommunity.com/sharedfiles/filedetails/?id=3790613573) | Gravship Voyageの星間航行エンディング/航路段階完了をセーブ横断の一意IDで記録 | 世界移行・NG+・再ロードで履歴重複しない。Odysseyと分離した統計 | **E0**・成功フック要確認 | 
| **S03** [Alpha Biomes](https://github.com/juanosarg/AlphaBiomes) | Alpha Biomes固有BiomeDefのホームマップ数・生存日数・最高資産を既存の地形分類系へ追加 | 1.7は1.6フォルダ。プレビュー/仮マップ除外、資産は最大値と通算時間を区別 | **E2**・上流ツリー確認済 | 
| **S04** [Biomes! Caverns](https://steamcommunity.com/sharedfiles/filedetails/?id=2969748433) | Biomes! Cavernsは地下BiomeDefおよび実制圧サイトの固有キーで分類 | 複層マップを同一コロニーへ帰属させ、同一滞在日数の重複加算を回避 | **E0**・マップモデル要監査 | 
| **S05** [ReGrowth 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2260097569) | ReGrowth 2の独自BiomeDefと採掘対象を定義単位で分離 | バニラ上書きDefは通常Biome/採掘統計に吸収し、固有成果のみ追加 | **E0**・固有Def要確認 | 
| **S06** [GravTide](https://steamcommunity.com/sharedfiles/filedetails/?id=3779600989) | GravTideで潜水・進水が完了して到着/生成物を伴う状態を計測 | OdysseyとVehicle Framework連携の各成功点が一致するか確認 | **E0**・移動・所有権要監査 | 
| **S07** [As above, So below 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3776015553) | 多層マップの最大地下深度/天空高度、階層到達・構築成功回数を別々に計測 | 階層を別コロニー化しない。標高はmaximum、建造はlifetime counter | **E0**・親マップ識別要監査 | 
| **S08** [Colony dungeon! [1.7]](https://steamcommunity.com/sharedfiles/filedetails/?id=3828091246) | Colony dungeonの入口開放・ギルドミッション最終報酬受領を記録 | 未完了遠征や発生だけのイベントを排除。専用クラス名はDLLから確定 | **E0**・成功フック要確認 | 
| **S09** [Ancestral Ruins (beta)](https://steamcommunity.com/sharedfiles/filedetails/?id=3831032590) | Ancestral Ruinsの独自サイト初回発見と遺産回収成功を区別 | サイト安定IDで再訪・枝分かれ・一時マップ重複を防ぐ | **E0**・安定ID要確認 | 
| **S10** [Rimworld Exploration Mode](https://steamcommunity.com/sharedfiles/filedetails/?id=2941608795) | 探索Modeが新規開示した未踏tile IDの集合差分・初発見数を取得 | リロードで過去tileを数え直さず、全ワールド毎tickスキャンを避ける | **E0**・状態遷移要確認 | 
| **S11** [Ancient urban ruins](https://steamcommunity.com/sharedfiles/filedetails/?id=3316062206) | Ancient urban ruinsで実際にプレイヤーが制圧したフロアを一意に記録 | 公式1.7動作が不明なため互換確認前に統計を登録しない | **E0**・互換性待ち | 
| **S12** [[RH2] Faction: V.O.I.D.](https://steamcommunity.com/sharedfiles/filedetails/?id=2883208829) | V.O.I.D.固有FactionDef由来の襲撃撃退と重要ボス撃破を既存戦闘イベントから内訳化 | 同じ撃退を汎用襲撃＋固有襲撃として二重加算しない。称号・勧誘は別種の成果 | **E0**・汎用収集器優先 | 
| **S13** [Alpha Mechs](https://steamcommunity.com/sharedfiles/filedetails/?id=2973169158) | Alpha Mechs追加メカのpawnKind/Def別撃破、確定したアップグレード生成物を観測 | 1.7で1.6コード読込。CompChangeDefで一時的な再spawnがあり、生成＝撃破と混同しない | **E3**・上流コード照合済 | 
| **S14** [Kraltech Industries (1.7) (unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3820851894) | Kraltech多段階ボスの最終形態死亡および専用報酬確定を一度だけ計数 | 中間形態の消滅・変身・ロードで重複しない | **E0**・ボス状態要監査 | 
| **S15** [Rimesis](https://steamcommunity.com/sharedfiles/filedetails/?id=3767949538) | Rimesis個体IDを追跡し撃退/捕獲/完全討伐の三つを別に計測 | 追跡者の再出現・再襲撃を初回撃破と混ぜない | **E0**・個体ID要監査 | 
| **S16** [Back For Vengeance](https://steamcommunity.com/sharedfiles/filedetails/?id=3761967749) | 復讐襲撃の固有IncidentDefを判定し勝利/最終決着だけを記録 | 普通の襲撃と混同せず、逃走→再来をfinal killにしない | **E0**・イベントDef要監査 | 
| **S17** [Apex Mechanoids](https://steamcommunity.com/sharedfiles/filedetails/?id=3792325789) | Apex系メカPawnKindとボス死亡を既存キルイベント内訳で集計 | 製造完了はバニラMech培養と別フックであれば追加 | **E0**・汎用収集器優先 | 
| **S18** [[MAP]机械族机械师 Mechanoid Mechanitor (1.6/1.7)](https://steamcommunity.com/sharedfiles/filedetails/?id=3811415429) | メカニター資格の確定遷移とSymbiosis Covenant/独自勝利確定を記録 | 人間pawn前提のバニラコードパッチを回避。種族type guard必須 | **E0**・非人間前提監査 | 
| **S19** [Glitterworld Destroyer 5 Expansion ：MechFusion etc](https://steamcommunity.com/sharedfiles/filedetails/?id=3549290904) | MechFusionの融合結果として新メカがプレイヤーへ確定生成された時を計測 | 開始時の材料消費や通常培養では不十分。前提MOD有無を守る | **E0**・生成結果要監査 | 
| **S20** [Dire Raids (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3259909708) | Dire Raid専用IncidentDefから襲撃完了・撃退だけを汎用raid内訳として区別 | 一般襲撃との二重計上や外部派閥MODへの誤帰属を除外 | **E0**・汎用収集器優先 | 
| **S21** [VVE Raid](https://steamcommunity.com/sharedfiles/filedetails/?id=3720926003) | 敵車両の実破壊/無力化と敵車両を含む襲撃の終了をvehicle IDで記録 | 搭乗者の降車/燃料切れ/撤退は撃破ではない。Vehicle Frameworkを利用 | **E0**・既存車両基盤優先 | 
| **S22** [Rimsenal Faction Pack - Federation](https://steamcommunity.com/workshop/filedetails/?id=736172213) | Federation由来FactionDefの戦闘勝利・Bions撃破を既存襲撃/キル内訳で計測 | 派閥の表示名ではなくpackage＋Defを使用 | **E0**・汎用収集器優先 | 
| **S23** [Rimsenal Faction Pack - Feral](https://steamcommunity.com/sharedfiles/filedetails/?id=736207111) | Feral派閥襲撃撃退と専用変異生物の死亡をDef別に記録 | 一般Pawn death・animals deathとの重複と自勢力帰属に注意 | **E0**・汎用収集器優先 | 
| **S24** [Horrors (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3839223635) | Horrorsの敵対生物撃破と巣窟サイトの確定制圧を別計測 | 毎tick増殖する巣穴数を生涯討伐数に加算しない | **E0**・サイト完了判定要確認 | 
| **S25** [Zombieland 1.7 (Unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3827574769) | ゾンビ死亡はpawn kind/専用Hediff由来を分類し、治療はHediff消滅後の確定回復で測る | 非公式移植の安定性と感染/他の病気統計の重複を確認 | **E0**・非公式配布監査 | 
| **S26** [Dynamic Diplomacy - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3220299022) | Dynamic Diplomacyのプレイヤー関与した外交戦勝利とNPC間勢力イベントを区別 | NPC同士のイベントは自勢力成果でない。Rim War/RimPactsと重複 | **E0**・帰属条件要監査 | 
| **S27** [Hearts of RimWorld - Grand Strategy Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3833698946) | Grand Strategyの師団勝利・停戦成立を独自世界状態の確定差分で記録 | 初期版の実装互換性・他外交MODとの排他を検証 | **E0**・互換性待ち | 
| **S28** [Vanilla Recycling Expanded](https://github.com/Vanilla-Expanded/VanillaRecyclingExpanded) | CompSuperSimpleProcessor.DoAtomizeに限定しTrySpawnAtCell成功した実出力をDef別・数量別に計測 | 1.7は1.6実装。入力減少だけで成功扱いにしない。通常bill出力との重複排除 | **E3**・上流コード照合済 | 
| **S29** [Mines 2.0](https://steamcommunity.com/sharedfiles/filedetails/?id=2503894706) | Mines 2.0の専用Mine作業完了後に実際に生成された鉱石/チャンク数を数える | 1.7専用ビルドは説明で確認。汎用製作/採掘統計の重複を調べる | **E1**・配布DLL要確認 | 
| **S30** [Misc. Robots](https://steamcommunity.com/sharedfiles/filedetails/?id=724602224) | Misc. Robotsが完了した修理/自動化ジョブを独立記録し、ロボット種別を区別 | ロボットを入植者数/死亡数に混ぜず、普通のWork完了件数とは差別化 | **E0**・ロボット型要監査 | 
| **S31** [Quarry [Adopted]](https://steamcommunity.com/workshop/filedetails/?id=2007576583) | Quarryで生成された資源を採掘成功後の配置数で集計 | 普通のMineで産出する資源や同一チャンク処理の二重計上を排除 | **E0**・配置フック要確認 | 
| **S32** [RimMarket](https://steamcommunity.com/sharedfiles/filedetails/?id=3817820660) | RimMarket取引が最終決済し実入金・配送完了した時だけ資産計上 | バニラ交易売買/Hotel売上と重複。返金/キャンセルを負テスト | **E0**・決済フック要確認 | 
| **S33** [Rimsential - Spaceports (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3225120958) | Spaceportsのシャトル実着発と給油販売代金の確定受領を別の統計へ | 帝国シャトルなど一般Royaltyイベントを除外 | **E0**・施設帰属要確認 | 
| **S34** [Vanilla Vehicles Expanded – Tier 3](https://github.com/Vanilla-Expanded/VanillaVehiclesExpanded-Tier3) | VVE Tier3車両の残骸修復がVehicle Framework経由でプレイヤー車両化した時だけ記録 | 既存VVE/Vehicle Frameworkカウンターに内訳を追加、同一修復を二度記録しない | **E0**・既存車両基盤優先 | 
| **S35** [Vanilla Base Generation Expanded](https://steamcommunity.com/workshop/filedetails/?id=3209927822) | 拡張生成された敵拠点での制圧を生成Def/マップタグで分類 | 通常拠点破壊で既に取得できれば専用統計は作らない | **G**・固有成果要再評価 | 
| **S36** [VGP Vegetable Garden](https://steamcommunity.com/sharedfiles/filedetails/?id=2007061826) | VGP固有の肥料・土壌改良が実際に完了した成果だけを計測 | 普通の栽培/収穫数は既存作物統計があるため再実装しない | **G**・固有成果要再評価 | 
| **S37** [Biotech Cloning (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3348781223) | Clone出生により生存Pawnとクローン属性の成立を確認し1生成とする | 旧元ソースのApplyBirthOutcome介入は確認済。Continuedの1.7 DLLは別確認。通常出生統計との重複 | **E2**・維持版DLL要照合 | 
| **S38** [Anomalies Expected](https://steamcommunity.com/sharedfiles/filedetails/?id=3240752689) | 異常存在Codex登録の未発見→発見確定差分と実収容/討伐を別メトリクス | 1.7は1.6＋1.7フォルダ。データベースUI・incident予約は成功ではない。Anomalyゲート | **E3**・上流コード照合済 | 
| **S39** [Dark Psychic Rituals: The Following](https://steamcommunity.com/sharedfiles/filedetails/?id=3596468709) | 闇の超常儀式の結果が成功し実際に報酬効果を適用した時だけ記録 | 通常Anomaly ritual結果と重複しないDef単位判定 | **E0**・Anomalyゲート | 
| **S40** [Expanded Prosthetics and Organ Engineering - Forked](https://steamcommunity.com/sharedfiles/filedetails/?id=1949064302) | EPOE固有高度義体の移植完了/解体後に成果として確定した数を記録 | 医療手術成功一般値/汎用bill製作数への二重加算を避ける | **E0**・汎用手術との比較 | 
| **S41** [Integrated Implants](https://steamcommunity.com/sharedfiles/filedetails/?id=3223443793) | Integrated Implantsの固有モジュールがポーンへ実装された成功結果を記録 | ApplyOnPawn前/手術予約/失敗時は計上しない | **E0**・成功フック要確認 | 
| **S42** [Gene Remover Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3280784251) | 指定Geneの除去実行後にGeneTracker差分が確定したときだけ計数 | 生体遺伝子編集と既存genemoddingを重複させない | **E0**・Biotechゲート | 
| **S43** [Gene Ripper](https://steamcommunity.com/sharedfiles/filedetails/?id=3524806362) | Gene Ripper抽出で実際にgenepackが受領・生成された場合のみ集計 | 対象死亡だけで成功扱いしない。死亡統計は別計上 | **E0**・Biotechゲート | 
| **S44** [[AA] Empire Expanded Forces](https://steamcommunity.com/sharedfiles/filedetails/?id=3837526614) | Empire Expanded Forces固有宮廷戦勝利・諜報摘発・クエスト決着の専用成果を記録 | VFE Empire/Desertersと同一操作を二重加算しない | **E0**・Royaltyゲート | 
| **S45** [Enhanced Ideology](https://steamcommunity.com/sharedfiles/filedetails/?id=3793123680) | Enhanced Ideologyの独自論争に実際に勝利した時、改宗が成立した時を区別 | バニラ改宗儀式や既存思想信仰数と二重加算しない | **E0**・Ideologyゲート | 
| **S46** [Corrosion / Anomaly](https://steamcommunity.com/sharedfiles/filedetails/?id=3731466157) | Corrosionが新規発症または治癒した状態遷移をポーンIDで記録 | ロード再現・精神崩壊の繰返し表示を新イベントと誤判定しない | **E0**・Anomalyゲート | 
| **S47** [Mechanoid Repurposing](https://steamcommunity.com/sharedfiles/filedetails/?id=3799572082) | 培養器での成功生成/確定失敗後に実際の義体を取得した結果を記録 | 単なる製作計画や確率抽選は件数にしない | **E0**・Biotechゲート | 
| **S48** [Anomaly Mutations Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3836076384) | Void研究解放と新規儀式完成・器官導入成功を別メトリクスで記録 | 一般研究完了/儀式完了に内包されていれば専用化を検討し直す | **E0**・Anomalyゲート | 
| **S49** [Vanilla Psycasts Expanded – Hemosage](https://steamcommunity.com/sharedfiles/filedetails/?id=2990596478) | Hemosage固有血魔法の変容・献血等の成功成果だけ新設 | VPE共通サイキャスト使用回数で既に取得できるものは作らない | **G**・汎用VPE比較 | 
| **S50** [Progression: Therapy](https://steamcommunity.com/sharedfiles/filedetails/?id=3753615736) | セラピーセッション終了時の完了・心情/精神状態の改善成立を計測 | 開始と途中退席を数えず、正常な通常会話や精神安定効果と重複しない | **E0**・成功フック要確認 | 
| **S51** [Psychology — 1.7 (RADMIKO continuation)](https://steamcommunity.com/sharedfiles/filedetails/?id=3829501925) / [Resurrected](https://steamcommunity.com/sharedfiles/filedetails/?id=3838502425) | Psychologyの市長選挙実当選と任期満了を固有統計とする | 1.7の異なる再ビルドはpackage ID・ABIを分離。一般社交との重複なし | **E0**・複数fork比較要 | 
| **S52** [ISEKAI RPG LEVELING](https://steamcommunity.com/sharedfiles/filedetails/?id=3657580708) | ISEKAIのランクF→SSS昇格確定イベントと鍛造完了成果を記録 | 継続XPや一時的状態を累計カウントしない | **E0**・レベル定義要確認 | 
| **S53** [Vanilla Storytellers Expanded - Perry Persistent](https://steamcommunity.com/sharedfiles/filedetails/?id=2149702069) | Perry Persistent固有イベントの実発生/完了をIncidentDefと開始時のStoryteller IDで記録 | ストーリーテラー切替後に事後帰属を誤らない。一般Incidentとの重複回避 | **E0**・一意イベント要確認 | 
| **S54** [Psycasts²](https://steamcommunity.com/sharedfiles/filedetails/?id=3761869362) | Psycasts²の覚醒/階級昇格・Apotheosis到達を確定遷移で記録 | VPEやRoyalty標準精神力Lv到達と混ぜない | **E0**・Royaltyゲート | 
| **S55** [Teenage Years - Identity](https://steamcommunity.com/sharedfiles/filedetails/?id=3757880146) | Teenage Yearsの宿題完了・成人移行結果を生存Pawn IDとともに保存 | 授業参加途中/宿題開始や別の教育MODとの重複を排除 | **E0**・Biotechゲート | 
| **S56** [Rim Academia — School Life & Growing Up](https://steamcommunity.com/sharedfiles/filedetails/?id=3827963827) | Rim Academiaの授業最終出席・卒業・実際の再加入結果だけ記録 | 中断・不在・強制退学を成功扱いしない | **E0**・教育結果要確認 | 
| **S57** [H.P. Lovecraft Storyteller Revived](https://steamcommunity.com/sharedfiles/filedetails/?id=3824124450) | Lovecraft storyteller固有OmenDefの確定イベント発生/解決を記録 | 通常Storytellerイベントと同一rootの二重計上を避け、旧版packageを区別 | **E0**・一意イベント要確認 | 
| **S58** [RimTalk](https://steamcommunity.com/sharedfiles/filedetails/?id=3551203752) | RimTalkがLLM応答を受けてゲーム内の会話として実際に採用・表示した完了件数を計測 | API通信・再試行・メッセージ再生を数えない。外部プロンプトや個人情報を保存しない | **E0**・privacy/非同期監査 | 
| **S59** [Way Better Romance](https://steamcommunity.com/sharedfiles/filedetails/?id=2877731755) | Way Better Romance固有のデート・関係成立を事後のソーシャル状態で記録 | 普通のLovin、妊娠、ベッドの共有を追加イベントとしない | **G**・固有成果要再評価 | 
| **S60** [Rimpsyche - Personality Core](https://steamcommunity.com/sharedfiles/filedetails/?id=3535112473) | Rimpsyche固有の関係性確定イベントが存在する場合のみ集計 | 複数モジュール・1.7移行版の導入差を分離し、性格snapshotは生涯累計しない | **E0**・モジュール毎ABI | 

### 旧保留・再評価（8件）

| ID / MOD | 実装するなら観測すべき確定成果 | 残る検証・二重計上リスク | 調査状態 |
|---|---|---|---|
| **HOLD-01** [Medieval Overhaul](https://steamcommunity.com/workshop/filedetails/?id=3219596926) | Medieval Overhaulの独自製作/交易/工房成果のうち汎用で取得不可なものだけ調べる | 1.7の実動作・正規配布アセンブリ確認までは着手しない | **B**・互換性待ち | 
| **HOLD-02** [Vanilla Temperature Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3202046258) | Vanilla Temperature Expandedの独自設備が故障・修復完了した固有確定イベントがあるか判定 | 温度/ダクト容量/稼働状態の瞬間値を生涯合算しない | **B**・固有成果要再評価 | 
| **HOLD-03** [What the Hack?! (Continued)](https://steamcommunity.com/workshop/filedetails/?id=3372841828) | What the Hack?!の制御下メカ化・ハッキング成功を最終状態で測定 | 1.7実配布DLLと既知のハッキング不具合の解消確認まで凍結 | **B**・互換性待ち | 
| **HOLD-04** [Fortifications - Industrial](https://steamcommunity.com/sharedfiles/filedetails/?id=2561619583) | Fortifications Industrialの固有施設・防衛目標の成功した製作や撃退実績を検討 | 1.7欠損報告が解決し固有成果が判明するまで専用キー作成しない | **B**・互換性待ち | 
| **HOLD-05** [Reinforced Mechanoid 2 (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3541404610) | Reinforced Mechanoid 2の固有メカ製造成功・ボス撃破をDef別で検討 | 1.7不具合が解決しメカ追加が通常KILL・build以上の意味を持つか確認 | **B**・互換性待ち | 
| **HOLD-06** [Vanilla Factions Expanded – Mechanoids](https://www.reddit.com/r/RimWorld/comments/1mro1kj/i_miss_the_factories_and_machines_of_vanilla/) | 旧VFE Mechanoidsはfactory生産の独自成功経路が1.7で利用可能か再評価 | 現行対応がない可能性あり。1.6ファイルの流用可否を確認するまで停止 | **B**・互換性待ち | 
| **HOLD-07** [Vanilla Anomaly Expanded – Insanity](https://steamcommunity.com/sharedfiles/filedetails/?id=3342412435) | Anomaly Insanity固有存在の収容・儀式完了を確定イベントで検討 | 1.7開始不能報告とロード問題解消が前提。Anomalyなし環境では対象外 | **B**・互換性待ち | 
| **HOLD-08** [WVC - Xenotypes and Genes](https://steamcommunity.com/sharedfiles/filedetails/?id=2886992038) | WVCの特有遺伝子付与/抽出完了をGeneTracker差分と照合 | Steam安定版の1.7正式対応・Github betaとの差を先に確認 | **B**・互換性待ち | 

## 着手推奨の順序（難易度・依存からの仮順位）

1. **既存データモデルを再利用**：S03/B06（地形・ランドマーク）、S12/S17/S20/S22/S23/S25（敵派閥・キル/襲撃内訳）、S34/S21（車両と修復）。最初にhostのgeneric collectorで同じ成功イベントがすでに取れているか調べる。
2. **成功確定ソースを読めたもの**：A02/B07/S28。単独の小規模アダプターから実装開始。数量・false/true・最終状態の検証を最優先。
3. **上流構造を確認したもの**：S13/S38/S37。実1.7ロードDLLとソース署名を固定してから、変化点をpatch。S37は維持版の配布バイナリ照合が必須。
4. **独立した成果が強いが未照合のもの**：S01/S02/S14/S15/S19/S29/S32/S51/S52/N03。確定報酬やユニークなエンディング/選挙/ボス撃破を優先してILを解析する。
5. **固有成果が未立証または互換性待ち**：R04/R05/R06、D03/D04/D05、S35/S36/S49/S59、HOLD-01〜08。これらも候補に残すが、効果がgenericで代替できる場合は無意味な統計を増やさない。

## 実装前に取得するファイルと検証方法

対象のMODごとに`About/About.xml`、`LoadFolders.xml`、1.7で選択される`Assemblies/*.dll`、関連`Defs`/`Patches`、取得可能なら同一ビルドのソース・コミットを確認。**少なくとも最初の実装バッチでは上記の成功関数・引数・結果とDLL内のメソッド署名が一致する証拠を残す。** ソースが見つからないMODはHarmonyパッチ先の想像で着手しない。

ゲートとなるテスト：実成功1件→+1、開始のみ/失敗→0、ロード→0、重複イベント→0、別派閥→0、別セーブ分岐→一意に集計、オプションMODなし起動→正常、MOD除去→既存記録保持。数量の場合は分割受入れと容量不足、戦闘は段階的変身、移動は世界・マップ境界、社会は実関係成立を負テストに含める。

**今回の変更対象は調査資料のみ。** 実装済み/対応MODの公開一覧には追加しない。GitHub Release・Steam Workshopは変更しない。

## 関連文書

- [現行87件一覧](active-candidates.md)
- [第6波の元データ（60＋8件）](2026-11-15-sixthwave-popular-mods-register-ja.md)
- [人気MOD調査台帳](2026-11-14-popular-mod-discovery-register.md)
- [種族調査台帳](2026-11-14-popular-race-mod-audit.md)
- [実装チェックリスト](implementation-checklist.md)
- [対応済み一覧](../supported-mods.md)
