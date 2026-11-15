# 2026-11-15：追加MOD候補の調査台帳（第6波・人気MOD中心・日本語）

> **履歴資料（2026-11-15整理）**：本文の「候補」「予定」「未確認」は調査当時の記録であり、現在の未実装候補一覧ではありません。実装済みの項目は[対応MOD一覧](../supported-mods.md)へ整理済み。現行の未実装項目は[active-candidates.md](active-candidates.md)を参照してください。原資料の技術的な根拠は保存します。


この文書は、既存の[公開対応MOD一覧](../supported-mods.md)（80件）および[人気27候補](2026-11-14-popular-mod-discovery-register.md)、[種族6候補](2026-11-14-popular-race-mod-audit.md)、[第5波5候補](2026-11-14-fifthwave-additional-candidates-ja.md)と**重複しない、新規調査のRimWorld 1.7対応候補60件**を記録する。**S01〜S60の60件すべてを実装候補として登録する**。末尾に記載された当初見送り・保留の8件も、除外せず**再評価対象の実装候補**として登録する（合計68件）。優先度と懸念点は調査時点の情報であり、実装済み・検証済みを意味しない。

- 配置想定パス：`docs/planned-mod-statistics/2026-11-15-sixthwave-popular-mods-register-ja.md`
- 作成基準日：2026-11-15

---

## A. クエスト・探索・バイオーム・ワールド系（11件）

| ID | MOD | ジャンル | 1.7対応状況 | 独立した結果として計測できるもの・設計上の関門 | 優先度 |
|---|---|---|---|---|---|
| S01 | [Vanilla Quests Expanded – Deadlife](https://steamcommunity.com/sharedfiles/filedetails/?id=3497226454) | クエスト・探索 | 1.5 / 1.6 / 1.7 対応 | 封印サイロの突破・電力復旧数、地図テーブル解読による司令部サイロ発見・攻略完了数。汎用クエスト統計との二重計上回避が必須。 | **P0** |
| S02 | [Gravship Voyage](https://steamcommunity.com/sharedfiles/filedetails/?id=3790613573) | 宇宙・航行（Odyssey） | 1.7 動作・修正明記 | 航路三角測量の完了、Johnson-Tanakaスラスター統合、AIコア起動、星間航行エンディング（NG+移行）達成。セーブ移行時の二重計上防止が関門。 | **P0** |
| S03 | [Alpha Biomes](https://github.com/juanosarg/AlphaBiomes) | バイオーム | 1.5 / 1.6 / 1.7 対応（[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=1841354677)） | 12種以上の独自バイオーム別コロニー数・生存日数・達成富の横断分類（`Geological Landforms` 方式）。マッププレビュー生成の除外が必須。 | **P1** |
| S04 | [Biomes! Caverns](https://steamcommunity.com/sharedfiles/filedetails/?id=2969748433) | 地下バイオーム | 1.4–1.7 対応 | 4種の地下バイオーム別滞在・運営実績、強敵クリーチャー討伐・トロフィー獲得、菌類栽培収穫量。多階層MOD併用時のバイオームDef解決が関門。 | **P1** |
| S05 | [ReGrowth 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2260097569) | バイオーム拡張 | 1.7 統合対応済 | 1.7統合のAspen Forestバイオームでの運営実績、大型ボルダー採掘完了数。バニラ上書き要素と固有Defの切り分けが必要。 | **P1** |
| S06 | [GravTide](https://steamcommunity.com/sharedfiles/filedetails/?id=3779600989) | 海底拠点（Odyssey） | 1.6 / 1.7 対応 | 沿岸造船所での船体・潜水艦進水完了数、海底への潜水完了回数、海底油田プラットフォームの稼働実績。 | **P1** |
| S07 | [As above, So below 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3776015553) | 多階層探索 | 1.6 / 1.7 対応 | 到達した地下・天空階層の最大深度/高度、階層間階段・エレベーター構築数。階層ごとにマップ別統計を分断・重複させない親マップ集約が関門。 | **P2** |
| S08 | [Colony dungeon! [1.7]](https://steamcommunity.com/sharedfiles/filedetails/?id=3828091246) | ダンジョン探索 | 1.7 専用 | ダンジョン入口解放数、ギルドミッション・懸賞金達成数、自動採掘・戦利品回収量。未完了遠征の除外とクラス変更耐性が必要。 | **P2** |
| S09 | [Ancestral Ruins (beta)](https://steamcommunity.com/sharedfiles/filedetails/?id=3831032590) | 過去遺跡 | 1.7 対応（2026-11公開） | 過去の自コロニー遺跡発見数、年代記の柱調査完了数、遺産回収実績。再訪問による発見数重複を防ぐ一意ID管理が関門。 | **P2** |
| S10 | [Rimworld Exploration Mode](https://steamcommunity.com/sharedfiles/filedetails/?id=2941608795) | ワールド探索 | 1.4–1.7 対応 | 未踏タイル踏破数、未知バイオーム・派閥拠点の発見数、地図解読・衛星ハッキングによる領域開示回数。毎tick走査を避け状態遷移のみフック。 | **P2** |
| S11 | [Ancient urban ruins](https://steamcommunity.com/sharedfiles/filedetails/?id=3316062206) | 古代都市探索 | 1.7 動作報告有（要安定性確認） | 地下Zレベル風の古代都市フロア到達・制圧実績。1.7での公式安定対応が確認でき次第、P1相当で再評価。 | **要検証** |

---

## B. 戦闘・派閥・メカノイド・ボス系（16件）

| ID | MOD | ジャンル | 1.7対応状況 | 独立した結果として計測できるもの・設計上の関門 | 優先度 |
|---|---|---|---|---|---|
| S12 | [[RH2] Faction: V.O.I.D.](https://steamcommunity.com/sharedfiles/filedetails/?id=2883208829) | 高難易度派閥・ボス | 1.4–1.7 対応（2026-11-10更新） | V.O.I.D.勢力の襲撃撃退数、固有ボス・ネームド個体撃破、V.O.I.D.との交渉・同盟達成。通常派閥統計と別枠で集計。 | **P0** |
| S13 | [Alpha Mechs](https://steamcommunity.com/sharedfiles/filedetails/?id=2973169158) | メカノイド・ボス | 1.5 / 1.6 / 1.7 対応 | Tier 4–6追加ボスの召喚・撃破、専用チップ回収、独自メカノイド培養完了数。召喚時ではなく死亡・チップドロップ後の確定記録が必須。 | **P0** |
| S14 | [Kraltech Industries (1.7) (unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3820851894) | 高難易度メカ・ボス | 1.6 / 1.7 対応（2026-10公開） | 変異メカボスおよび8ウェーブ究極ボス「Kraltech Titan」討伐、検問イベント解決。多段階変身ボスのため最終死亡時のみ1撃破として計上。 | **P0** |
| S15 | [Rimesis](https://steamcommunity.com/sharedfiles/filedetails/?id=3767949538) | 宿敵システム | 1.7 対応（Waylith移行済） | 宿敵Rimesisとの交戦・撃退、捕獲後の勧誘成功、完全討伐数、War Camp攻略数。撤退・再登場と完全討伐の明確な分離が必須。 | **P0** |
| S16 | [Back For Vengeance](https://steamcommunity.com/sharedfiles/filedetails/?id=3761967749) | 報復襲撃 | 1.7 対応（更新履歴明記） | 復讐者による1.5倍規模報復襲撃の撃退回数、復讐者の完全討伐・捕獲による決着数。逃走時の再襲撃と最終決着を分離して計上。 | **P0** |
| S17 | [Apex Mechanoids](https://steamcommunity.com/sharedfiles/filedetails/?id=3792325789) | メカノイド拡張 | 1.6 / 1.7 対応 | 追加ボス級メカノイド撃破数、Apex系襲撃撃退数、専用メカノイド・装備の製造完了数。バニラBiotech培養フックとのDef単位分離が必要。 | **P1** |
| S18 | [[MAP]机械族机械师 Mechanoid Mechanitor (1.6/1.7)](https://steamcommunity.com/sharedfiles/filedetails/?id=3811415429) | メカノイド主体プレイ | 1.6 / 1.7 対応 | メカノイド自身のメカニター化、Symbiosis Covenant締結、Purge Directive達成、独自ボス撃破・追加エンディング。非人間メカニターでの型ガードが関門。 | **P1** |
| S19 | [Glitterworld Destroyer 5 Expansion ：MechFusion etc](https://steamcommunity.com/sharedfiles/filedetails/?id=3549290904) | 超強化メカ・融合 | 1.6 / 1.7 対応 | ボスハント「Ashen Hive Mother」討伐、Royal Mechanoid Collectiveクエスト完了、MechFusion融合メカ生成数。前提MOD差異の安全チェックが必要。 | **P1** |
| S20 | [Dire Raids (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3259909708) | 大規模襲撃 | 1.0–1.7 対応 | 専用大脅威インシデント「Dire Raid」の撃退回数および同襲撃内での撃破数。通常襲撃統計との二重計上回避（または内訳分離）が必須。 | **P1** |
| S21 | [VVE Raid](https://steamcommunity.com/sharedfiles/filedetails/?id=3720926003) | 敵車両襲撃 | 1.6 / 1.7 対応 | 戦車・APC・輸送車等を含む車両襲撃の撃退数、敵車両の破壊・無力化数。搭乗兵の下車や撤退を車両破壊と誤認しない判定が関門。 | **P1** |
| S22 | [Rimsenal Faction Pack - Federation](https://steamcommunity.com/workshop/filedetails/?id=736172213) | 敵対派閥 | 1.0–1.7 対応 | 恒久敵対派閥Federationの襲撃撃退数、主力生体兵器Bions撃破数、拠点制圧数。通常襲撃との二重計上を避けた対Federation戦果の切り出し。 | **P2** |
| S23 | [Rimsenal Faction Pack - Feral](https://steamcommunity.com/sharedfiles/filedetails/?id=736207111) | 敵対派閥 | 1.7 対応 | 恒久敵対派閥Feralの襲撃撃退数、戦獣・ミュータント撃破数、拠点制圧数。固有非人間ユニットの撃破内訳を切り出す設計が有効。 | **P2** |
| S24 | [Horrors (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3839223635) | 敵対生物・巣窟 | 1.7 対応（2026-11公開） | Horrorsの侵入撃退数、4種モンスター撃破数、攻撃可能な巣窟サイトの制圧数。増殖する巣穴構造物ではなくサイト制圧・侵入撃退を単位とする。 | **P2** |
| S25 | [Zombieland 1.7 (Unofficial)](https://steamcommunity.com/sharedfiles/filedetails/?id=3827574769) | ゾンビサバイバル | 1.7 非公式移植（[元版](https://steamcommunity.com/sharedfiles/filedetails/?id=928376710)） | ゾンビ累計撃破数、ゾンビ感染の発症・治療成功数。非公式移植の安定性と一般キル数との切り分けを要確認。 | **P2** |
| S26 | [Dynamic Diplomacy - Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3220299022) | 動態外交 | [1.6/1.7修正アドオン](https://steamcommunity.com/sharedfiles/filedetails/3810346676)有 | NPC勢力間の征服・反乱イベント、プレイヤー参戦型アリーナ戦勝利数。既対応の `Rim War` / `RimPacts` との役割重複に注意。 | **P2** |
| S27 | [Hearts of RimWorld - Grand Strategy Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3833698946) | 領土・戦略 | 1.6 / 1.7 対応 | 師団の陸・海・空戦勝利数、同盟締結、講和会議完了数。他大型派閥MODとの非互換と初期版の安定性見極めが必要。 | **条件付き** |

---

## C. 経済・資源再生・自動化・農業・乗り物系（9件）

| ID | MOD | ジャンル | 1.7対応状況 | 独立した結果として計測できるもの・設計上の関門 | 優先度 |
|---|---|---|---|---|---|
| S28 | [Vanilla Recycling Expanded](https://github.com/Vanilla-Expanded/VanillaRecyclingExpanded) | 廃棄物・資源再生 | 1.5–1.7 対応（[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3155781848)） | 有毒廃棄パックの確定処理数、再生合金などの変換完了量。汎用クラフト・生産統計との切り分けが必要。 | **P0** |
| S29 | [Mines 2.0](https://steamcommunity.com/sharedfiles/filedetails/?id=2503894706) | 資源採掘 | 1.2–1.7 対応（1.7専用ビルド済） | 鉱山（Mine）設備でのビル完了回数および採掘資源の総産出量（鉱石・石材別）。工作台生産フックと混同しない限定フックが必要。 | **P0** |
| S30 | [Misc. Robots](https://steamcommunity.com/sharedfiles/filedetails/?id=724602224) | 自動化ロボット | 1.0–1.7 対応 | 運搬・清掃ボットの製造・購入・起動数、ロボットによるタスク完了数、修理完了数。ロボットが入植者数ピークや死亡統計に混入しないフィルタが必須。 | **P1** |
| S31 | [Quarry [Adopted]](https://steamcommunity.com/workshop/filedetails/?id=2007576583) | 資源採掘 | 1.7 動作・採用報告有 | 採石場からの確定採掘リソース量（鉱石・石材・チャンク別）。通常の採掘（Mine）統計と混同しない枠組みが必要。 | **P1** |
| S32 | [RimMarket](https://steamcommunity.com/sharedfiles/filedetails/?id=3817820660) | 商業・市場・取引 | 1.6 / 1.7 対応（2026-11-14更新） | 露店・宿収入、値切り成功数、交易ルート輸送完了数、大市開催数、奴隷オークション落札数。バニラ取引統計との二重計上回避が関門。 | **P1** |
| S33 | [Rimsential - Spaceports (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3225120958) | 宇宙港（Royalty） | 1.5–1.7 対応（2026-11-01更新） | 宇宙港へのシャトル着発数、融合燃料精製・給油販売額、6種の宇宙港イベント完了数。帝国シャトル等と混同しないクラス特定が必要。 | **P1** |
| S34 | [Vanilla Vehicles Expanded – Tier 3](https://github.com/Vanilla-Expanded/VanillaVehiclesExpanded-Tier3) | 乗り物・車両 | 1.5–1.7 対応（[Workshop](https://steamcommunity.com/workshop/filedetails/?id=3047892432)） | 戦車・自走砲・ヘリ等のTier 3車両残骸の修復完了数。既存のVVE本体アダプタとの二重計上チェックが必要。 | **P1** |
| S35 | [Vanilla Base Generation Expanded](https://steamcommunity.com/workshop/filedetails/?id=3209927822) | 拠点生成 | 1.5 / 1.6 / 1.7 対応 | 拡張された敵拠点内の金庫室攻略や特殊拠点制圧。通常の拠点破壊数と重複しない固有トリガーの有無をソース監査で確認。 | **P2** |
| S36 | [VGP Vegetable Garden](https://steamcommunity.com/sharedfiles/filedetails/?id=2007061826) | 農業・料理 | 1.7 更新進行中 | 耕作土・肥料などVGP固有の農業システム成果。単純な作物収穫数・調理数はバニラ統計と重複するため対象外とする。 | **P2** |

---

## D. DLC拡張・遺伝子・クローン・義体・アノマリー系（13件）

| ID | MOD | ジャンル | 1.7対応状況 | 独立した結果として計測できるもの・設計上の関門 | 優先度 |
|---|---|---|---|---|---|
| S37 | [Biotech Cloning (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3348781223) | クローン（Biotech） | 1.7 / .NET 10 対応（[GitHub](https://github.com/JTJutajoh/RimWorld.Cloning)） | クローン生成完了数、脳スキャン・記憶転写の成功回数。通常出生数や汎用コピーと二重計上しない切り分けが必要。 | **P0** |
| S38 | [Anomalies Expected](https://steamcommunity.com/sharedfiles/filedetails/?id=3240752689) | アノマリー拡張 | 1.7 公式対応済 | 拡張Entity Databaseへの新規異常存在登録、MOD固有の異常存在の収容・討伐実績。Codex周辺の他MOD競合を避ける安全なPostfix設計が必要。 | **P0** |
| S39 | [Dark Psychic Rituals: The Following](https://steamcommunity.com/sharedfiles/filedetails/?id=3596468709) | 超常儀式（Anomaly） | 1.6 / 1.7 対応 | 闇の研究解放後のMOD固有超常儀式の完遂回数と成功結果。バニラ汎用儀式カウンターとDef単位で分離して集計。 | **P0** |
| S40 | [Expanded Prosthetics and Organ Engineering - Forked](https://steamcommunity.com/sharedfiles/filedetails/?id=1949064302) | 義体・臓器 | 1.7 対応 | 先進バイオニクス・AIペルソナコア移植、認知症・老化治療完了、義体分解リサイクル数。バニラ汎用手術・製作との差別化が関門。 | **P1** |
| S41 | [Integrated Implants](https://steamcommunity.com/sharedfiles/filedetails/?id=3223443793) | インプラント | 1.5 / 1.6 / 1.7 対応 | MOD追加インプラント・モジュールの移植手術完了数、モジュールアップグレード完了数。手術失敗を除外し `ApplyOnPawn` 成功時のみ記録。 | **P1** |
| S42 | [Gene Remover Continued](https://steamcommunity.com/sharedfiles/filedetails/?id=3280784251) | 遺伝子除去（Biotech） | 1.7 対応報告有 | 指定遺伝子の確定除去完了数（Gene Remover実行後）。開始予約ではなく除去完了時のみ計上。 | **P1** |
| S43 | [Gene Ripper](https://steamcommunity.com/sharedfiles/filedetails/?id=3524806362) | 遺伝子抽出（Biotech） | 1.7 動作報告有 | 対象ポーンの犠牲を伴う特定遺伝子抽出の完了数。汎用遺伝子抽出統計や犠牲死との整合性確認が必要。 | **P1** |
| S44 | [[AA] Empire Expanded Forces](https://steamcommunity.com/sharedfiles/filedetails/?id=3837526614) | 帝国・宮廷（Royalty） | 1.7 専用（2026-11公開） | 貴族家門間抗争の決着、宮廷クエスト完了、属国貢物防衛成功、潜伏工作員摘発。VFE Empire / Deserters併用時の二重計上回避が関門。 | **P1** |
| S45 | [Enhanced Ideology](https://steamcommunity.com/sharedfiles/filedetails/?id=3793123680) | 信仰・論争（Ideology） | 1.6 / 1.7 対応 | 社会的改宗による即時改宗達成、イデオロギー論争の勝利回数。バニラ改宗儀式・モラルガイド能力との切り分けが必要。 | **P1** |
| S46 | [Corrosion / Anomaly](https://steamcommunity.com/sharedfiles/filedetails/?id=3731466157) | 虚無侵食（Anomaly） | 1.6 / 1.7 対応 | 精神崩壊に伴うVoid Corrosion発症、回復成功、アノマリー存在への変貌回数。ロード後の重複チェックを防ぐ状態確定フックが必須。 | **P1** |
| S47 | [Mechanoid Repurposing](https://steamcommunity.com/sharedfiles/filedetails/?id=3799572082) | メカ由来義体（Biotech） | 1.7 完全移行済 | 専用設備でのメカノイド由来インプラント培養成功・失敗回数、追加敵対派閥との戦闘成果。確率判定後の確定結果のみフック。 | **P2** |
| S48 | [Anomaly Mutations Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3836076384) | 変異儀式（Anomaly） | 1.6 / 1.7 対応（2026-11-14更新） | 追加Void研究完了、固有儀式「Void flesh conjuration」完遂数、アノマリー変異器官導入実績。バニラ研究・儀式からのDef分離が必要。 | **P2** |
| S49 | [Vanilla Psycasts Expanded – Hemosage](https://steamcommunity.com/sharedfiles/filedetails/?id=2990596478) | 血魔法（VPE/Biotech） | 1.5 / 1.6 / 1.7 対応 | 血魔法系サイキャスト固有の変容・儀式成果。VPE本体の汎用サイキャスト使用回数で代替できる場合は見送り対象。 | **P2** |

---

## E. コロニー運営・社交・教育・ストーリーテラー・RPG系（11件）

| ID | MOD | ジャンル | 1.7対応状況 | 独立した結果として計測できるもの・設計上の関門 | 優先度 |
|---|---|---|---|---|---|
| S50 | [Progression: Therapy](https://steamcommunity.com/sharedfiles/filedetails/?id=3753615736) | 精神ケア | 1.7 対応済（アセットパック更新） | セラピーセッションの完了回数、セラピーによる精神崩壊・心情の回復成功数。試行開始ではなく完了フックの特定が必要。 | **P1** |
| S51 | [Psychology — 1.7 (RADMIKO continuation)](https://steamcommunity.com/sharedfiles/filedetails/?id=3829501925) / [Resurrected](https://steamcommunity.com/sharedfiles/filedetails/?id=3838502425) | 性格・市長選挙 | 1.7 再ビルド版有 | **市長選挙の当選・任期完了**、カウンセリング治療完了回数。VSIE等の汎用社交統計にはない明確なイベント成果がある。 | **P1** |
| S52 | [ISEKAI RPG LEVELING](https://steamcommunity.com/sharedfiles/filedetails/?id=3657580708) | RPG・レベル | 1.6 / 1.7 互換明記 | レベルアップ達成数、ランク昇格（F–SSS）、鍛造強化・ルーンエンチャント完了数、ギルドクエスト完了数。XP連続加算ではなく確定イベントのみ記録。 | **P1** |
| S53 | [Vanilla Storytellers Expanded - Perry Persistent](https://steamcommunity.com/sharedfiles/filedetails/?id=2149702069) | ストーリーテラー | 1.5 / 1.6 / 1.7 対応 | 固有イベント（Man in the Coat到着等）の発生・完了数、ストーリーテラー別インシデント完了数。途中切り替え時の帰属確定が必要。 | **P1** |
| S54 | [Psycasts²](https://steamcommunity.com/sharedfiles/filedetails/?id=3761869362) | サイキャスト拡張 | 1.6 / 1.7 対応 | 瞑想による覚醒達成、巡礼クエスト完了によるTier昇格、個別サイキャストLv10到達、Apotheosis到達数。VPE本体統計との切り分けが前提。 | **P2** |
| S55 | [Teenage Years - Identity](https://steamcommunity.com/sharedfiles/filedetails/?id=3757880146) | 成長・教育 | 1.7 対応明記 | ティーンの宿題完了数、救助ティーンの滞在・加入完了数、成人移行時のアイデンティティ確定成果。連携MOD不在時のNull安全設計が必要。 | **P2** |
| S56 | [Rim Academia — School Life & Growing Up](https://steamcommunity.com/sharedfiles/filedetails/?id=3827963827) | 学校・教育 | 1.6 / 1.7 対応 | 通学・授業完了回数、校外学習完了数、卒業および見習い再加入数。襲撃等による未完了セッション除外が必要。 | **P2** |
| S57 | [H.P. Lovecraft Storyteller Revived](https://steamcommunity.com/sharedfiles/filedetails/?id=3824124450) | ストーリーテラー | 1.6 / 1.7 対応 | 13種の前兆イベント（Omen）経験回数、災害生存・完了数、黒猫・カラス加入数、霧クリーチャー撃破数。同名旧版と `packageId` 区別が必要。 | **P2** |
| S58 | [RimTalk](https://steamcommunity.com/sharedfiles/filedetails/?id=3551203752) | AI会話 | 1.5–1.7 対応（Waylith対応済） | 外部LLM連携で生成完了したポーン間会話回数など。通信失敗やリトライを計上しないフック設計が必要。 | **P2** |
| S59 | [Way Better Romance](https://steamcommunity.com/sharedfiles/filedetails/?id=2877731755) | 社交・恋愛 | 1.7 動作報告有（公式タグ未確認） | デート・ハングアウト完了回数、Hookup成立・妊娠数。公式1.7タグ付与またはAPI互換確認を条件とする。 | **条件付き** |
| S60 | [Rimpsyche - Personality Core](https://steamcommunity.com/sharedfiles/filedetails/?id=3535112473) | 性格・社交 | 1.7 移行準備中（一部モジュール1.7対応） | 性格・Disposition・Sexualityに基づく固有社交・関係イベントの確定成果。モジュール全体の1.7正式版リリース後に再評価。 | **条件付き** |

---

## 追加の実装候補：当初見送り・保留の8件（再評価対象）

以下のMODも実装候補から除外しない。表の理由欄は当初の調査で記録された技術上の懸念・保留理由をそのまま保存したものであり、今後の実装可否を再評価するための情報とする。

| MOD | 理由 |
|---|---|
| [Medieval Overhaul](https://steamcommunity.com/workshop/filedetails/?id=3219596926) | 大型定番MODだが、1.7対応はコミュニティ製Modpack側の調整が中心で本体の公式1.7アセンブリ安定性が未確定のため保留。 |
| [Vanilla Temperature Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3202046258) | 1.7対応済みだが、空調・ダクトの継続稼働や瞬間状態が中心で「独立した確定成果」を切り出しにくいため見送り。 |
| [What the Hack?! (Continued)](https://steamcommunity.com/workshop/filedetails/?id=3372841828) | 1.7環境でのハッキング不具合報告および後継作者への引き継ぎ過渡期のため保留。 |
| [Fortifications - Industrial](https://steamcommunity.com/sharedfiles/filedetails/?id=2561619583) / [Reinforced Mechanoid 2 (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=3541404610) | 1.7環境での消失不具合報告・1.7対応待ち状態が確認されたため除外。 |
| [Vanilla Factions Expanded – Mechanoids](https://www.reddit.com/r/RimWorld/comments/1mro1kj/i_miss_the_factories_and_machines_of_vanilla/) | 1.6以降未更新のため対象外。 |
| [Vanilla Anomaly Expanded – Insanity](https://steamcommunity.com/sharedfiles/filedetails/?id=3342412435) | 1.7フォルダ未整備・ワールド開始不可報告があるため見送り。 |
| [WVC - Xenotypes and Genes](https://steamcommunity.com/sharedfiles/filedetails/?id=2886992038) | Steam版が1.7正式対応前（GitHubベータ段階）のため、正式リリース待ちとして保留。 |

---

## 実装判断の共通条件

1. 公開済み対応MOD80件、既存の第1〜5波台帳・種族6候補、既存のMOD取得アダプター・汎用統計との重複確認を先に行う。
2. 対象MODの1.7で選択される実配布DLL（`LoadFolders.xml` とアセンブリ）、成功後の状態変化と自勢力帰属を検証し、初めてキーを登録する。
3. 着手・試行・予定・瞬間的な所持スナップショットを成功実績として計上しない。中断、払い戻し、ロード、枝分かれ、再利用、他勢力を負のテストに含める。
4. 過去のMODが無くなっても保存済み実績を保持し、欠測は Unknown / Partial、明示的OFFを維持する。毎tickの広域反射・ポーリングを行わない。
5. 専用アダプターが実装・検証されるまで `docs/supported-mods.md` には追加しない。