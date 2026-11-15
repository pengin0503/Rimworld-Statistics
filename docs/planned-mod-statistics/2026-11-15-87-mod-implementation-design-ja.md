# 87件のMOD統計実装設計（2026-11-15）

状態：**全87件の実装方式・受入判定を明文化した初期設計**。調査済みの情報だけでHarmonyフックを全件確定できるという意味ではない。最新 main 参照SHA: cf06f60e2b73adb513c59f539910f907f12babc1

[現行候補](active-candidates.md)および[87件の個別事前調査](2026-11-15-87-mod-implementation-research-ja.md)に基づき、既存のRimworld Statistics基盤を再利用して段階的に実装する。

## 1. 全体設計

統計コードに任意MOD対応を内蔵する。プラグイン/SDKや保存形式を87件分新設するのではなく、既存のoptional adapter登録・LiveMetrics・Checkpoint・Profile・保存観測機構を利用する。

| 設計レーン | 件数 |
|---|---:|
| P1/成功イベント | 3 |
| P2/状態観測 | 5 |
| P3/汎用分類 | 19 |
| P4/版差確認 | 3 |
| P5/固有イベント | 49 |
| P7/互換待ち | 8 |
| **合計** | **87** |

### ホスト側で使用する現行コード

| 処理 | 使用する実装・責務 |
|---|---|
| 登録 | Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs からMOD別Build()を追加。ModProviderAdapters.cs が登録とイベント/スナップショット適合を判定 |
| パッケージの存在 | ModProviderGameDiscovery.cs、ModMetricDescriptor.RequiredPackages、RequiredDlc、RequiredDefNames。About.xmlの実PackageIdと1.7のロードDLLを基準にする |
| 成功イベント | IntegratedMods/NewModOutcomes/Source/OutcomeMetric.cs の Count / Observing / Record を単純件数に再利用。必要ならModMetricDescriptorと専用familyで数量・複数値を扱う |
| 保存値 | MetricObservationSource.Checkpointで確定イベント増分、SaveSnapshotで稼働数・所属・存在等の観測値。OverallAggregation.Sum / Max を意味に合わせて選択 |
| 定義と表示 | ModMetricDescriptor のStableKey（mod.実PackageId.family.metric）、Revision、Category、Unit、HasBreakdownを使用。翻訳キーを追加し既定で利用可能な統計を表示 |
| ゲートと安全 | MetricGates.IsRecording、ModProviderAdaptersの個別失敗隔離。任意MOD不在・署名違い・DLC不在なら該当familyだけ不適合。過去プロファイルは消さない |
| 汎用との統合 | Patches/Metrics の既存キル、襲撃、資産、交易、製作、研究、医療、種族等の記録と突合。Def内訳で既に得られる場合、新規familyを増やさない |

### 統計の不変条件

1. **成功確定後**にのみ1件を記録：実spawn、最終討伐、完成、Quest成功、Gene/Hediffの実差分、実移動、実受領、実決済。開始/未確定/失敗/ロード時再生成はゼロ。
2. 数量は公称能力ではなく実受入量。最大値は累計しない。継続的な状態観測は過去の生涯イベント数へ流用しない。
3. ワールド・拠点・ポーン・車両などは安定IDで所有者と一意成功を特定。分岐セーブ・再ロード・同一イベント多重通知・原版とforkの競合による二重加算を防ぐ。
4. 記録キーは実配布About.xmlのPackageIdで確定する。表示名から推測しない。後で名寄せする場合も既存のStableKeyと過去記録を破壊しない。
5. 他MOD不在ならリフレクションもパッチもせず、失敗は対象ファミリーだけ閉じる。ゲームプレイを停止させない。MOD削除後も旧統計を閲覧できるようにする。
6. 毎tickの大規模走査やゲーム中のファイル/ネットワークアクセスを新設しない。すでに存在する更新点の確定成果だけを軽量に取得する。
7. 原資料の提案統計が全て収集可能と決めつけない。固有成果が実証できない場合は無意味な指標を作らず、具体的な理由を残して次に進む。

## 2. 着手するバッチと設計確定の条件

| 順位 | 対象 | 最初に実証すること |
|---|---|---|
| 1 | A02 Vanilla Furniture Expanded – Power / B07 Helixien Gas / S28 Recycling | 公開ソース上のメルトダウン確定、配管の実受入、処理後の実配置を1.7選択DLLで照合。false/多重通知テスト |
| 2 | S03 Alpha Biomes / B06 Vanilla Landmarks / 戦闘派閥Def別統計 | 既存LandformOutcomesと汎用戦闘統計で取得できるものを先に抽出。重複する専用統計は追加しない |
| 3 | S13 Alpha Mechs / S37 Biotech Cloning (Continued) / S38 Anomalies Expected | 1.7実配布DLLとソース一致、内部spawnと成果確定の違い、維持版fork差分を監査 |
| 4 | F06 SRTS / S01 Deadlife / S02 Gravship Voyage / S29 Mines 2.0 / S32 RimMarket | ランディング・章の最終達成・資源配置・決済などの成功イベントを特定 |
| 5 | 残りの固有成功イベント系（P5） | 優先度の高いMODから上流ソース/1.7 DLLと実セーブを調べ、確定pointのあるものを順次実装 |
| 6 | HOLD-01～08（P7） | 1.7正式互換/必要DLC・DLLの問題が解消するまでpatch登録なし |

複数AIの並行作業ではMODごとに新規ファイルを分離し、IntegratedTransitionAdapters.csや翻訳XMLなど共有ファイルの登録は最後に統合する。実装する順番を上流ソースの信頼性で変えてよい。

## 3. 全87件の実装方式と受入テスト入口

下表の観測対象と既知リスクは事前調査の情報に基づく。**P1/P4であっても、実配布DLLの最終署名や完成可否が確定したという意味ではない。** 実装前に列の成功フックを追記して合格した候補から着手する。

| ID | MOD | 設計レーン | 最初に収集する成果案 | 誤計上しやすい点 | 実装確定前の必須確認 |
|---|---|---|---|---|---|
| F06 | [SRTS Expanded (continued)](2026-11-13-non-ve-discovery.md) | P5/固有イベント | SRTSシャトルが移動先マップへ実際に到着して乗員・貨物が出現した時点を、機体・航行IDで1回記録 | 最新1.7配布DLLと移動成功イベントの照合。発射・着陸予約や輸送ポッド統計との重複排除 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| H05 | [Biomes! Bees](2026-11-13-fourthwave-follow-on-research.md) | P5/固有イベント | 養蜂設備に紐づく実受粉成果を、植物Growthの正の変化または確定収穫増分で測定 | 開発用spawn以外の通常蜂生成と自勢力施設への帰属の証拠。一般植物成長と分離 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| A02 | [Vanilla Furniture Expanded – Power](https://steamcommunity.com/sharedfiles/filedetails/?id=2062943477) | P1/成功イベント | 原子炉のメルトダウンはflag単独でなくflag新規成立＋実際の設備破損の後に1件確定 | 1.7で1.6ソース選択を確認済み。復旧・セーブ復元・複数tick重複を防止 | DLL署名、成功1・失敗0・再通知0の実測 |
| A04 | [Vanilla Cooking Expanded – Stews](https://steamcommunity.com/sharedfiles/filedetails/?id=2134312965) | P5/固有イベント | Stewsの専用加工設備の完成バッチをVEF処理完了時にDef別記録 | 通常Cookingの製作数と二重計上しない。実生産/完成コールバックの所属を確認 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| B06 | [Vanilla Landmarks Expanded](https://github.com/Vanilla-Expanded/VanillaLandmarksExpanded) | P2/状態観測 | 既存LandformOutcomesの観測構造を流用し、プレイヤー本拠地のLandmark/TileMutator識別情報を分類 | 表示時の読み取りでワールド生成やマッププレビューを操作しない。識別IDの永続性を確認 | 実拠点限定、ロード後変化ゼロ、累計と最大値の区別 |
| B07 | [Vanilla Helixien Gas Expanded](https://github.com/Vanilla-Expanded/VanillaHelixienGasExpanded) | P1/成功イベント | ガス井上のCompDeepExtractor.TryProducePortionでPipeNetに実受領された量を集計 | 既存VCE Deepchemと共有可能だが、net.DistributeAmongStorage受入れ量を検証。容量0/再走査を除外 | DLL署名、成功1・失敗0・再通知0の実測 |
| C02 | [Vanilla Furniture Expanded – Spacer Module](https://github.com/Vanilla-Expanded/VanillaFurnitureExpanded-Spacer) | P5/固有イベント | Spacer修理設備が実際に回復させたHP差分と対象Defの完了修理件数 | 通常修理Job・遠隔修理の二重計上防止。差分0・最大HPを除外 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| C03 | [Vanilla Races Expanded – Highmate](https://github.com/Vanilla-Expanded/VanillaRacesExpanded-Highmate) | P3/汎用分類 | Highmate特有の関係成立・専用行動の完了後に成立状態を計測 | 一般Lovin/交際/妊娠との重複と他MODによる同名相互作用を回避 | 既存収集器でのカウント比較、全体値との二重加算なし |
| C05 | [Mechanoid Upgrades](https://steamcommunity.com/sharedfiles/filedetails/?id=3365118555) | P5/固有イベント | Mechanoid Upgradesの付与・除去結果を機体/upgrade IDで記録 | 入れ替え・初期装備・ロード復元・RimWorld既存メカ成長の混同を排除 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| C06 | [The Dead Man's Switch II](https://steamcommunity.com/sharedfiles/filedetails/?id=3819799478) | P5/固有イベント | Dead Man's Switch II固有の研究段階・機体解放・シナリオ勝利の最終確定状態 | 製作数/研究数で代替可能な候補は専用統計にしない。旧版とは区別 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| D03 | [Vanilla Ideology Expanded – Memes and Structures](https://github.com/Vanilla-Expanded/VanillaIdeologyExpanded-Memes) | P3/汎用分類 | Trading Fair等MOD固有機能の確定取引・召喚完了だけを記録 | 既存Ideology儀式・交易イベントとの重複。XMLのみなら新規アダプター見送り | 既存収集器でのカウント比較、全体値との二重加算なし |
| D04 | [Vanilla Traits Expanded](https://github.com/Vanilla-Expanded/VanillaTraitsExpanded) | P3/汎用分類 | Vanilla Traits固有の有意な出来事のみ、トレイトDefと成功副作用で限定計数 | 心情/ダメージ/逃走の既存計数を流用可否判断。汎用トレイト所持回数は作らない | 既存収集器でのカウント比較、全体値との二重加算なし |
| D05 | [Vanilla Weapons Expanded – Non-Lethal](https://github.com/Vanilla-Expanded/VanillaWeaponsExpanded-NonLethal) | P3/汎用分類 | Non-Lethal武器固有の確定無力化成功を命中後の結果と損傷Defから抽出 | 通常キル・ダウン・状態異常のgeneric値に重複させない。射撃試行は除外 | 既存収集器でのカウント比較、全体値との二重加算なし |
| R01 | [NewRatkinPlus](https://steamcommunity.com/sharedfiles/filedetails/?id=1578693166) | P5/固有イベント | Ratkin発電輪の実稼働/出力を所有設備と結び付け、成功した電力生産のみ測る | 定格電力や在庫snapshotsの通算加算は不正。電力単位と時間積分の妥当性が必要 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| R03 | [[OA]Ratkin Faction: Oberonia aurea](https://steamcommunity.com/sharedfiles/filedetails/?id=3159926804) | P5/固有イベント | Oberonia固有の爵位・名誉の昇格確定、派閥関連の達成マイルストーンを記録 | バニラ称号や他種族HAR分類で代用できるものは作らない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| R04 | [Gloomy Dragonian race](https://steamcommunity.com/sharedfiles/filedetails/?id=2960593459) | P3/汎用分類 | Dragonianは既存HAR種族分類で加入・死亡・人数ピークを集計する案を第一候補に | 独自MODとしての実装済み表示は固有達成が見つかった場合のみ。新規キー乱立を避ける | 既存収集器でのカウント比較、全体値との二重加算なし |
| R05 | [Rabbie The Moonrabbit race](https://steamcommunity.com/sharedfiles/filedetails/?id=1837246563) | P3/汎用分類 | RabbieはHAR分類優先、惑星儀式・依存の発症/治療等に確定イベントがある場合のみ専用化 | 一時的なHediff・移動や消失から成果を推測しない | 既存収集器でのカウント比較、全体値との二重加算なし |
| R06 | [Yuran race](https://steamcommunity.com/sharedfiles/filedetails/?id=2844129100) | P3/汎用分類 | YuranはHAR分類優先、血統継承・巫女任命の固有完遂のみ新規イベント | 単なる種族・遺伝子Defの存在は専用統計と見なさない | 既存収集器でのカウント比較、全体値との二重加算なし |
| N03 | [Caravan Adventures](https://steamcommunity.com/sharedfiles/filedetails/?id=2558957509) | P5/固有イベント | Caravan Adventures固有の報酬・賞金クレジットが実決済された量とストーリー章完了を計上 | NPC交換と汎用交易/クエスト成功の二重計上。配布DLLの成功関数を特定 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S01 | [Vanilla Quests Expanded – Deadlife](https://steamcommunity.com/sharedfiles/filedetails/?id=3497226454) | P5/固有イベント | Deadlifeのサイロ電力復旧・司令部攻略ステージの最終quest stateを観測 | Anomaly DLC、VEFが必要。一般クエスト成功と二重計上せず固有ステージIDを採る | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S02 | [Gravship Voyage](https://steamcommunity.com/sharedfiles/filedetails/?id=3790613573) | P5/固有イベント | Gravship Voyageの星間航行エンディング/航路段階完了をセーブ横断の一意IDで記録 | 世界移行・NG+・再ロードで履歴重複しない。Odysseyと分離した統計 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S03 | [Alpha Biomes](https://github.com/juanosarg/AlphaBiomes) | P2/状態観測 | Alpha Biomes固有BiomeDefのホームマップ数・生存日数・最高資産を既存の地形分類系へ追加 | 1.7は1.6フォルダ。プレビュー/仮マップ除外、資産は最大値と通算時間を区別 | 実拠点限定、ロード後変化ゼロ、累計と最大値の区別 |
| S04 | [Biomes! Caverns](https://steamcommunity.com/sharedfiles/filedetails/?id=2969748433) | P2/状態観測 | Biomes! Cavernsは地下BiomeDefおよび実制圧サイトの固有キーで分類 | 複層マップを同一コロニーへ帰属させ、同一滞在日数の重複加算を回避 | 実拠点限定、ロード後変化ゼロ、累計と最大値の区別 |
| S05 | [ReGrowth 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2260097569) | P2/状態観測 | ReGrowth 2の独自BiomeDefと採掘対象を定義単位で分離 | バニラ上書きDefは通常Biome/採掘統計に吸収し、固有成果のみ追加 | 実拠点限定、ロード後変化ゼロ、累計と最大値の区別 |
| S06 | [GravTide](https://steamcommunity.com/sharedfiles/filedetails/?id=3779600989) | P5/固有イベント | GravTideで潜水・進水が完了して到着/生成物を伴う状態を計測 | OdysseyとVehicle Framework連携の各成功点が一致するか確認 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S07 | [As above, So below 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3776015553) | P2/状態観測 | 多層マップの最大地下深度/天空高度、階層到達・構築成功回数を別々に計測 | 階層を別コロニー化しない。標高はmaximum、建造はlifetime counter | 実拠点限定、ロード後変化ゼロ、累計と最大値の区別 |
| S08 | [Colony dungeon! [1.7]](https://steamcommunity.com/sharedfiles/filedetails/?id=3828091246) | P5/固有イベント | Colony dungeonの入口開放・ギルドミッション最終報酬受領を記録 | 未完了遠征や発生だけのイベントを排除。専用クラス名はDLLから確定 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S09 | [Ancestral Ruins (beta)](https://steamcommunity.com/sharedfiles/filedetails/?id=3831032590) | P5/固有イベント | Ancestral Ruinsの独自サイト初回発見と遺産回収成功を区別 | サイト安定IDで再訪・枝分かれ・一時マップ重複を防ぐ | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S10 | [Rimworld Exploration Mode](https://steamcommunity.com/sharedfiles/filedetails/?id=2941608795) | P5/固有イベント | 探索Modeが新規開示した未踏tile IDの集合差分・初発見数を取得 | リロードで過去tileを数え直さず、全ワールド毎tickスキャンを避ける | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S11 | [Ancient urban ruins](https://steamcommunity.com/sharedfiles/filedetails/?id=3316062206) | P5/固有イベント | Ancient urban ruinsで実際にプレイヤーが制圧したフロアを一意に記録 | 公式1.7動作が不明なため互換確認前に統計を登録しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S12 | [[RH2] Faction: V.O.I.D.](https://steamcommunity.com/sharedfiles/filedetails/?id=2883208829) | P3/汎用分類 | V.O.I.D.固有FactionDef由来の襲撃撃退と重要ボス撃破を既存戦闘イベントから内訳化 | 同じ撃退を汎用襲撃＋固有襲撃として二重加算しない。称号・勧誘は別種の成果 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S13 | [Alpha Mechs](https://steamcommunity.com/sharedfiles/filedetails/?id=2973169158) | P4/版差確認 | Alpha Mechs追加メカのpawnKind/Def別撃破、確定したアップグレード生成物を観測 | 1.7で1.6コード読込。CompChangeDefで一時的な再spawnがあり、生成＝撃破と混同しない | 原版と1.7選択DLLのABI一致・本当の成功結果 |
| S14 | [Kraltech Industries (1.7) (unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3820851894) | P5/固有イベント | Kraltech多段階ボスの最終形態死亡および専用報酬確定を一度だけ計数 | 中間形態の消滅・変身・ロードで重複しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S15 | [Rimesis](https://steamcommunity.com/sharedfiles/filedetails/?id=3767949538) | P5/固有イベント | Rimesis個体IDを追跡し撃退/捕獲/完全討伐の三つを別に計測 | 追跡者の再出現・再襲撃を初回撃破と混ぜない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S16 | [Back For Vengeance](https://steamcommunity.com/sharedfiles/filedetails/?id=3761967749) | P5/固有イベント | 復讐襲撃の固有IncidentDefを判定し勝利/最終決着だけを記録 | 普通の襲撃と混同せず、逃走→再来をfinal killにしない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S17 | [Apex Mechanoids](https://steamcommunity.com/sharedfiles/filedetails/?id=3792325789) | P3/汎用分類 | Apex系メカPawnKindとボス死亡を既存キルイベント内訳で集計 | 製造完了はバニラMech培養と別フックであれば追加 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S18 | [[MAP]机械族机械师 Mechanoid Mechanitor (1.6/1.7)](https://steamcommunity.com/sharedfiles/filedetails/?id=3811415429) | P5/固有イベント | メカニター資格の確定遷移とSymbiosis Covenant/独自勝利確定を記録 | 人間pawn前提のバニラコードパッチを回避。種族type guard必須 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S19 | [Glitterworld Destroyer 5 Expansion ：MechFusion etc](https://steamcommunity.com/sharedfiles/filedetails/?id=3549290904) | P5/固有イベント | MechFusionの融合結果として新メカがプレイヤーへ確定生成された時を計測 | 開始時の材料消費や通常培養では不十分。前提MOD有無を守る | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S20 | [Dire Raids (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3259909708) | P3/汎用分類 | Dire Raid専用IncidentDefから襲撃完了・撃退だけを汎用raid内訳として区別 | 一般襲撃との二重計上や外部派閥MODへの誤帰属を除外 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S21 | [VVE Raid](https://steamcommunity.com/sharedfiles/filedetails/?id=3720926003) | P5/固有イベント | 敵車両の実破壊/無力化と敵車両を含む襲撃の終了をvehicle IDで記録 | 搭乗者の降車/燃料切れ/撤退は撃破ではない。Vehicle Frameworkを利用 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S22 | [Rimsenal Faction Pack - Federation](https://steamcommunity.com/workshop/filedetails/?id=736172213) | P3/汎用分類 | Federation由来FactionDefの戦闘勝利・Bions撃破を既存襲撃/キル内訳で計測 | 派閥の表示名ではなくpackage＋Defを使用 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S23 | [Rimsenal Faction Pack - Feral](https://steamcommunity.com/sharedfiles/filedetails/?id=736207111) | P3/汎用分類 | Feral派閥襲撃撃退と専用変異生物の死亡をDef別に記録 | 一般Pawn death・animals deathとの重複と自勢力帰属に注意 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S24 | [Horrors (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3839223635) | P5/固有イベント | Horrorsの敵対生物撃破と巣窟サイトの確定制圧を別計測 | 毎tick増殖する巣穴数を生涯討伐数に加算しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S25 | [Zombieland 1.7 (Unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3827574769) | P3/汎用分類 | ゾンビ死亡はpawn kind/専用Hediff由来を分類し、治療はHediff消滅後の確定回復で測る | 非公式移植の安定性と感染/他の病気統計の重複を確認 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S26 | [Dynamic Diplomacy - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3220299022) | P5/固有イベント | Dynamic Diplomacyのプレイヤー関与した外交戦勝利とNPC間勢力イベントを区別 | NPC同士のイベントは自勢力成果でない。Rim War/RimPactsと重複 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S27 | [Hearts of RimWorld - Grand Strategy Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3833698946) | P5/固有イベント | Grand Strategyの師団勝利・停戦成立を独自世界状態の確定差分で記録 | 初期版の実装互換性・他外交MODとの排他を検証 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S28 | [Vanilla Recycling Expanded](https://github.com/Vanilla-Expanded/VanillaRecyclingExpanded) | P1/成功イベント | CompSuperSimpleProcessor.DoAtomizeに限定しTrySpawnAtCell成功した実出力をDef別・数量別に計測 | 1.7は1.6実装。入力減少だけで成功扱いにしない。通常bill出力との重複排除 | DLL署名、成功1・失敗0・再通知0の実測 |
| S29 | [Mines 2.0](https://steamcommunity.com/sharedfiles/filedetails/?id=2503894706) | P5/固有イベント | Mines 2.0の専用Mine作業完了後に実際に生成された鉱石/チャンク数を数える | 1.7専用ビルドは説明で確認。汎用製作/採掘統計の重複を調べる | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S30 | [Misc. Robots](https://steamcommunity.com/sharedfiles/filedetails/?id=724602224) | P5/固有イベント | Misc. Robotsが完了した修理/自動化ジョブを独立記録し、ロボット種別を区別 | ロボットを入植者数/死亡数に混ぜず、普通のWork完了件数とは差別化 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S31 | [Quarry [Adopted]](https://steamcommunity.com/workshop/filedetails/?id=2007576583) | P5/固有イベント | Quarryで生成された資源を採掘成功後の配置数で集計 | 普通のMineで産出する資源や同一チャンク処理の二重計上を排除 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S32 | [RimMarket](https://steamcommunity.com/sharedfiles/filedetails/?id=3817820660) | P5/固有イベント | RimMarket取引が最終決済し実入金・配送完了した時だけ資産計上 | バニラ交易売買/Hotel売上と重複。返金/キャンセルを負テスト | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S33 | [Rimsential - Spaceports (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3225120958) | P5/固有イベント | Spaceportsのシャトル実着発と給油販売代金の確定受領を別の統計へ | 帝国シャトルなど一般Royaltyイベントを除外 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S34 | [Vanilla Vehicles Expanded – Tier 3](https://github.com/Vanilla-Expanded/VanillaVehiclesExpanded-Tier3) | P3/汎用分類 | VVE Tier3車両の残骸修復がVehicle Framework経由でプレイヤー車両化した時だけ記録 | 既存VVE/Vehicle Frameworkカウンターに内訳を追加、同一修復を二度記録しない | 既存収集器でのカウント比較、全体値との二重加算なし |
| S35 | [Vanilla Base Generation Expanded](https://steamcommunity.com/workshop/filedetails/?id=3209927822) | P3/汎用分類 | 拡張生成された敵拠点での制圧を生成Def/マップタグで分類 | 通常拠点破壊で既に取得できれば専用統計は作らない | 既存収集器でのカウント比較、全体値との二重加算なし |
| S36 | [VGP Vegetable Garden](https://steamcommunity.com/sharedfiles/filedetails/?id=2007061826) | P3/汎用分類 | VGP固有の肥料・土壌改良が実際に完了した成果だけを計測 | 普通の栽培/収穫数は既存作物統計があるため再実装しない | 既存収集器でのカウント比較、全体値との二重加算なし |
| S37 | [Biotech Cloning (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3348781223) | P4/版差確認 | Clone出生により生存Pawnとクローン属性の成立を確認し1生成とする | 旧元ソースのApplyBirthOutcome介入は確認済。Continuedの1.7 DLLは別確認。通常出生統計との重複 | 原版と1.7選択DLLのABI一致・本当の成功結果 |
| S38 | [Anomalies Expected](https://steamcommunity.com/sharedfiles/filedetails/?id=3240752689) | P4/版差確認 | 異常存在Codex登録の未発見→発見確定差分と実収容/討伐を別メトリクス | 1.7は1.6＋1.7フォルダ。データベースUI・incident予約は成功ではない。Anomalyゲート | 原版と1.7選択DLLのABI一致・本当の成功結果 |
| S39 | [Dark Psychic Rituals: The Following](https://steamcommunity.com/sharedfiles/filedetails/?id=3596468709) | P5/固有イベント | 闇の超常儀式の結果が成功し実際に報酬効果を適用した時だけ記録 | 通常Anomaly ritual結果と重複しないDef単位判定 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S40 | [Expanded Prosthetics and Organ Engineering - Forked](https://steamcommunity.com/sharedfiles/filedetails/?id=1949064302) | P3/汎用分類 | EPOE固有高度義体の移植完了/解体後に成果として確定した数を記録 | 医療手術成功一般値/汎用bill製作数への二重加算を避ける | 既存収集器でのカウント比較、全体値との二重加算なし |
| S41 | [Integrated Implants](https://steamcommunity.com/sharedfiles/filedetails/?id=3223443793) | P5/固有イベント | Integrated Implantsの固有モジュールがポーンへ実装された成功結果を記録 | ApplyOnPawn前/手術予約/失敗時は計上しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S42 | [Gene Remover Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3280784251) | P5/固有イベント | 指定Geneの除去実行後にGeneTracker差分が確定したときだけ計数 | 生体遺伝子編集と既存genemoddingを重複させない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S43 | [Gene Ripper](https://steamcommunity.com/sharedfiles/filedetails/?id=3524806362) | P5/固有イベント | Gene Ripper抽出で実際にgenepackが受領・生成された場合のみ集計 | 対象死亡だけで成功扱いしない。死亡統計は別計上 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S44 | [[AA] Empire Expanded Forces](https://steamcommunity.com/sharedfiles/filedetails/?id=3837526614) | P5/固有イベント | Empire Expanded Forces固有宮廷戦勝利・諜報摘発・クエスト決着の専用成果を記録 | VFE Empire/Desertersと同一操作を二重加算しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S45 | [Enhanced Ideology](https://steamcommunity.com/sharedfiles/filedetails/?id=3793123680) | P5/固有イベント | Enhanced Ideologyの独自論争に実際に勝利した時、改宗が成立した時を区別 | バニラ改宗儀式や既存思想信仰数と二重加算しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S46 | [Corrosion / Anomaly](https://steamcommunity.com/sharedfiles/filedetails/?id=3731466157) | P5/固有イベント | Corrosionが新規発症または治癒した状態遷移をポーンIDで記録 | ロード再現・精神崩壊の繰返し表示を新イベントと誤判定しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S47 | [Mechanoid Repurposing](https://steamcommunity.com/sharedfiles/filedetails/?id=3799572082) | P5/固有イベント | 培養器での成功生成/確定失敗後に実際の義体を取得した結果を記録 | 単なる製作計画や確率抽選は件数にしない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S48 | [Anomaly Mutations Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3836076384) | P5/固有イベント | Void研究解放と新規儀式完成・器官導入成功を別メトリクスで記録 | 一般研究完了/儀式完了に内包されていれば専用化を検討し直す | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S49 | [Vanilla Psycasts Expanded – Hemosage](https://steamcommunity.com/sharedfiles/filedetails/?id=2990596478) | P3/汎用分類 | Hemosage固有血魔法の変容・献血等の成功成果だけ新設 | VPE共通サイキャスト使用回数で既に取得できるものは作らない | 既存収集器でのカウント比較、全体値との二重加算なし |
| S50 | [Progression: Therapy](https://steamcommunity.com/sharedfiles/filedetails/?id=3753615736) | P5/固有イベント | セラピーセッション終了時の完了・心情/精神状態の改善成立を計測 | 開始と途中退席を数えず、正常な通常会話や精神安定効果と重複しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S51 | [Psychology — 1.7 (RADMIKO continuation)](https://steamcommunity.com/sharedfiles/filedetails/?id=3829501925) / [Resurrected](https://steamcommunity.com/sharedfiles/filedetails/?id=3838502425) | P5/固有イベント | Psychologyの市長選挙実当選と任期満了を固有統計とする | 1.7の異なる再ビルドはpackage ID・ABIを分離。一般社交との重複なし | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S52 | [ISEKAI RPG LEVELING](https://steamcommunity.com/sharedfiles/filedetails/?id=3657580708) | P5/固有イベント | ISEKAIのランクF→SSS昇格確定イベントと鍛造完了成果を記録 | 継続XPや一時的状態を累計カウントしない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S53 | [Vanilla Storytellers Expanded - Perry Persistent](https://steamcommunity.com/sharedfiles/filedetails/?id=2149702069) | P3/汎用分類 | Perry Persistent固有イベントの実発生/完了をIncidentDefと開始時のStoryteller IDで記録 | ストーリーテラー切替後に事後帰属を誤らない。一般Incidentとの重複回避 | 既存収集器でのカウント比較、全体値との二重加算なし |
| S54 | [Psycasts²](https://steamcommunity.com/sharedfiles/filedetails/?id=3761869362) | P5/固有イベント | Psycasts²の覚醒/階級昇格・Apotheosis到達を確定遷移で記録 | VPEやRoyalty標準精神力Lv到達と混ぜない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S55 | [Teenage Years - Identity](https://steamcommunity.com/sharedfiles/filedetails/?id=3757880146) | P5/固有イベント | Teenage Yearsの宿題完了・成人移行結果を生存Pawn IDとともに保存 | 授業参加途中/宿題開始や別の教育MODとの重複を排除 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S56 | [Rim Academia — School Life & Growing Up](https://steamcommunity.com/sharedfiles/filedetails/?id=3827963827) | P5/固有イベント | Rim Academiaの授業最終出席・卒業・実際の再加入結果だけ記録 | 中断・不在・強制退学を成功扱いしない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S57 | [H.P. Lovecraft Storyteller Revived](https://steamcommunity.com/sharedfiles/filedetails/?id=3824124450) | P5/固有イベント | Lovecraft storyteller固有OmenDefの確定イベント発生/解決を記録 | 通常Storytellerイベントと同一rootの二重計上を避け、旧版packageを区別 | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S58 | [RimTalk](https://steamcommunity.com/sharedfiles/filedetails/?id=3551203752) | P5/固有イベント | RimTalkがLLM応答を受けてゲーム内の会話として実際に採用・表示した完了件数を計測 | API通信・再試行・メッセージ再生を数えない。外部プロンプトや個人情報を保存しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S59 | [Way Better Romance](https://steamcommunity.com/sharedfiles/filedetails/?id=2877731755) | P5/固有イベント | Way Better Romance固有のデート・関係成立を事後のソーシャル状態で記録 | 普通のLovin、妊娠、ベッドの共有を追加イベントとしない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| S60 | [Rimpsyche - Personality Core](https://steamcommunity.com/sharedfiles/filedetails/?id=3535112473) | P5/固有イベント | Rimpsyche固有の関係性確定イベントが存在する場合のみ集計 | 複数モジュール・1.7移行版の導入差を分離し、性格snapshotは生涯累計しない | 1.7 DLLの成功commit、失敗0、所有者・再ロード・重複の除外 |
| HOLD-01 | [Medieval Overhaul](https://steamcommunity.com/workshop/filedetails/?id=3219596926) | P7/互換待ち | Medieval Overhaulの独自製作/交易/工房成果のうち汎用で取得不可なものだけ調べる | 1.7の実動作・正規配布アセンブリ確認までは着手しない | 配布・導入・DLC互換の確認 |
| HOLD-02 | [Vanilla Temperature Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3202046258) | P7/互換待ち | Vanilla Temperature Expandedの独自設備が故障・修復完了した固有確定イベントがあるか判定 | 温度/ダクト容量/稼働状態の瞬間値を生涯合算しない | 配布・導入・DLC互換の確認 |
| HOLD-03 | [What the Hack?! (Continued)](https://steamcommunity.com/workshop/filedetails/?id=3372841828) | P7/互換待ち | What the Hack?!の制御下メカ化・ハッキング成功を最終状態で測定 | 1.7実配布DLLと既知のハッキング不具合の解消確認まで凍結 | 配布・導入・DLC互換の確認 |
| HOLD-04 | [Fortifications - Industrial](https://steamcommunity.com/sharedfiles/filedetails/?id=2561619583) | P7/互換待ち | Fortifications Industrialの固有施設・防衛目標の成功した製作や撃退実績を検討 | 1.7欠損報告が解決し固有成果が判明するまで専用キー作成しない | 配布・導入・DLC互換の確認 |
| HOLD-05 | [Reinforced Mechanoid 2 (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3541404610) | P7/互換待ち | Reinforced Mechanoid 2の固有メカ製造成功・ボス撃破をDef別で検討 | 1.7不具合が解決しメカ追加が通常KILL・build以上の意味を持つか確認 | 配布・導入・DLC互換の確認 |
| HOLD-06 | [Vanilla Factions Expanded – Mechanoids](https://www.reddit.com/r/RimWorld/comments/1mro1kj/i_miss_the_factories_and_machines_of_vanilla/) | P7/互換待ち | 旧VFE Mechanoidsはfactory生産の独自成功経路が1.7で利用可能か再評価 | 現行対応がない可能性あり。1.6ファイルの流用可否を確認するまで停止 | 配布・導入・DLC互換の確認 |
| HOLD-07 | [Vanilla Anomaly Expanded – Insanity](https://steamcommunity.com/sharedfiles/filedetails/?id=3342412435) | P7/互換待ち | Anomaly Insanity固有存在の収容・儀式完了を確定イベントで検討 | 1.7開始不能報告とロード問題解消が前提。Anomalyなし環境では対象外 | 配布・導入・DLC互換の確認 |
| HOLD-08 | [WVC - Xenotypes and Genes](https://steamcommunity.com/sharedfiles/filedetails/?id=2886992038) | P7/互換待ち | WVCの特有遺伝子付与/抽出完了をGeneTracker差分と照合 | Steam安定版の1.7正式対応・Github betaとの差を先に確認 | 配布・導入・DLC互換の確認 |

## 4. テスト・受け入れ基準

- 1回成功で期待するキーに+1、失敗・キャンセル・予約・別勢力で+0。
- 一つの成果の複数callbackで+1、ロードだけで+0、分岐セーブで祖先を二重合算しない。
- 物品は実配置数、配管は実受領量、戦闘は最終撃破・解決、クローンは出生後の実ポーン状態を根拠にする。
- optional MODがなくてもRimWorldが起動。DLL版違いを検出した場合そのfamilyだけfail closed。MOD削除後も保存済み定義と値が読める。
- Tests/RimworldStatistics.Core.Tests/ModMetricProviderRegistryTests.cs と ModMetricPersistenceTests.cs を回帰試験に含める。個別MODの成功/失敗ルールは純粋関数のunit testを優先。
- 可能な場合pengin0503/RimWorld-for-mod-testの1.7環境でロード・成功・負テストを行う。Anomaly DLCがない場合はAnomaly固有機能の実ゲーム検証を要求しない。

## 5. 追加で必要になるファイル

**この設計の作成段階では、ユーザーから追加ファイルを受け取る必要はない。** 実装時は最初のMODから順番に実アセンブリを照合する。全87件のZIPを一括提出する必要はない。

| 優先度 | MOD | 必要なもの |
|---|---|---|
| 最優先 | A02 VFE Power、B07 Helixien Gas、S28 Vanilla Recycling Expanded | About/About.xml、loadFolders.xml、1.7で選択されるAssemblies/*.dll、対象Defs/Patches。公開ソースと実配布バイナリの一致を確認 |
| 次 | S37 Biotech Cloning (Continued)、S13 Alpha Mechs、S38 Anomalies Expected | 同じセット。特にCloningは原版でなく1.7のContinued実配布DLLが必要 |
| 必要時 | F06 SRTS Expanded (continued)、S02 Gravship Voyage、S29 Mines 2.0 | 実際に1.7で読み込まれるMODフォルダをZIP。過去のSRTS提供物は1.6 DLLのみだったため注意 |
| 順次 | S01 Deadlife、S14 Kraltech、S15 Rimesis、S32 RimMarket、S51 Psychology維持版など | ソースが公開されていないか、forkの差異があるときのみ対象のMODフォルダZIP |
| 保留 | HOLD-01～08 | 1.7で実動作するパッケージが確認できるまで要求しない |

圧縮物にログイン情報・Steam認証情報・.env・R2鍵を含めない。Cloudflare R2は読み取り・照合専用。GitHub Release/Workshopは明示依頼なしで更新しない。

## 6. この設計の完了条件と次の実装作業

1. 87件のIDが現行候補一覧と一対一であることを確認。
2. 各MODのAbout.xmlによるPackageId、1.7実ロードDLL、成功処理の関数名・署名・呼出し後の値を、実装前の小さな監査ノートに追加する。
3. 成功確認できたものから実装・テスト・登録する。未確認の統計は表示上0として新設しない。
4. 対応が完成したMODのみ docs/supported-mods.md に移し、現行候補一覧から削除。固有統計が実証できないケースは根拠付き保留・対象外にする。

**今回の変更範囲は設計資料のみであり、MOD本体のソース・候補数・対応済み一覧・Releaseは変更しない。**
