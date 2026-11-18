# 2026-11-18：追加MOD候補調査（第7波）・既存対応MOD第2段階拡張・全候補ティア分類台帳

> **位置付け**：本文書は `main`（`17e4934`）時点での対応済みMOD（[supported-mods.md](../supported-mods.md) 掲載 103件 ＋ 内蔵作者MOD 2件 ＝ 実質 105件）および既存候補 58件を精査し、**① 既存対応MODの第2段階追加指標、② 既存候補 58件の実装優先度ティア（Tier 1〜3・保留再評価）、③ 未登録の新規 RimWorld 1.7 対応候補 12件（第7波 `W01`〜`W12`）、④ 1.7 復活ウォッチリスト 8件（`RW01`〜`RW08`）** を記録する調査・設計台帳である。実装前の候補を対応済みとして [supported-mods.md](../supported-mods.md) に掲載してはならない。全体監査レポート [2026-11-18-main-comprehensive-audit-and-roadmap-ja.md](../research/2026-11-18-main-comprehensive-audit-and-roadmap-ja.md) も参照。

- 配置パス：`docs/planned-mod-statistics/2026-11-18-seventhwave-and-tier-roadmap-ja.md`
- 作成日：2026-11-18（日本時間）

---

## 1. 既存対応済みMOD（105件）への「第2段階メトリクス」追加候補

[2026-11-13-secondary-implementation-scope.md](2026-11-13-secondary-implementation-scope.md) の Tasks 26〜34 は（証拠不足で免除確定した [2026-11-13-evidence-gated-outcome-disposition.md](2026-11-13-evidence-gated-outcome-disposition.md) の 3項目を除き）すべて実装完了している。一方で、その後に第1スライス（代表的な 1指標のみ）で統合された大型MODには、以下の通り高価値かつ確定境界が明確な第2段階指標の追加余地がある。

| 対応済みMOD | Workshop ID | 現在の収集指標 | 追加候補となる第2段階指標 | 成功判定・二重計上防止ゲート |
| --- | --- | --- | --- | --- |
| **Hospital** | `2992224079` | 入院患者受入数 | **完治退院患者数**、退院時に実受領した**治療報酬シルバー累計** | 死亡・途中離脱・拒否を退院成功から除外。通常交易売却銀と重複しない専用報酬授受完了点のみ計上。 |
| **A RimWorld of Magic** | `1201382956` | 回復呪文発動数 | **全クラス呪文・スキル発動完了数（AbilityDef別内訳）**、魔導書・巻物による**クラス覚醒数** | クールダウン・マナ消費確定後の発動成功点のみ計測。既存の回復呪文指標との包含関係（全体 vs 回復特化）をラベルで明示。 |
| **Dubs Rimatomics** | `1127530465` | プルトニウム再処理量 | SACS エネルギー兵器の**迎撃・砲撃完了数**、原子炉**スクラム／メルトダウン発生数** | 毎 tick ビーム照射ではなく迎撃飛翔体撃墜・砲撃着弾・緊急停止確定遷移のみをフック。 |
| **Altered Carbon 2: ReSleeved** | `2196278117` | 神経スタック移植数 | 培養槽での**スリーブ育成完了数**、遠隔転送（**ニードルキャスト**）完了数 | バニラ Biotech 成長槽や通常ポーン加入との二重計上を避け、スリーブ実体生成・意識転送確定点のみを計測。 |
| **Alpha Mechs** | `2973169158` | メカ変換数 | 追加高階位メカボスの**最終形態討伐数** | 多段階変身や召喚時ではなく、最終形態の死亡確定時のみ 1討伐として記録。 |
| **Alpha Genes** | `2891845502` | 採取遺伝子パック数 | 遺伝子吸収・変異付与アビリティの**確定成功回数** | 詠唱開始・中断を除外し、対象への遺伝子状態変更確定後のみ記録。 |
| **Empire Refactored** | `3701480464` | 新規従属入植地創設数 | 従属入植地からプレイヤー本拠地へ**実到着・配置された税収シルバー／物資価値** | 帳簿上の予定額ではなく、マップ上へのドロップ／インベントリ搬入確定分のみ計上。 |
| **Vanilla Gravship Expanded Ch.1 & 2** | `3609835606` | 整備タスク・ワープ・ポイント | 新規獲得した **Gravdata 総量** | 既存のブラックボックス格納／取出による往復増減を除外し、調査・解析での純増分のみ記録。 |
| **World Domination 2.0** / **Rim War (Continued)** | `3546683812` / `2222935097` | 都市占領・外交行動等 | **プレイヤー自身の部隊が勝利したワールド戦闘・拠点制圧数** | NPC 派閥同士の自動戦闘（高頻度ノイズ）を厳密に除外し、自派閥編成部隊の勝利確定点のみ計上。 |

> **2026-11-18 実装状況メモ**：RimWorld 1.7 実選択アセンブリが確認できた 8件（**Hospital**、**Dubs Rimatomics**、**Altered Carbon 2: ReSleeved**、**Alpha Mechs**、**Alpha Genes**、**Empire Refactored**、**Vanilla Gravship Expanded**、**World Domination 2.0**）の計 15指標を `Stage2ModOutcomes` として実装済み。**A RimWorld of Magic**（上流 `TorannD/RWoM` は `1.6` 止まり）および **Rim War (Continued)**（上流 `TorannD/RimWar---Threaded` は `1.6` 止まり）は 1.7 実配布DLL未確認のため推測実装を行わず保留とする。

---

## 2. `active-candidates.md` 既存 58件の優先度ティア分類

### 2.1 Tier 1：即戦力・高価値（12件 — 1.7 実DLL署名確認後に最優先で実装推奨）

既存の汎用統計（作業ビル・採掘・手術・襲撃等）と明確に異なる独自ドメイン成果を持ち、非 tick の完了遷移フックを設計しやすい最優先グループ。

| ID | MOD | Workshop ID | 提案メトリクス（確定成果のみ） | 実装前の最終確認ポイント |
| --- | --- | --- | --- | --- |
| `S02` | **Gravship Voyage** | `3790613573` | 航路三角測量完了数、JT スラスター統合数、星間ジャンプ（NG+ 移行）完了数 | Odyssey 汎用発進統計との切り分け、NG+ 移行時のキャンペーン引継ぎ境界確認。 |
| `F06` | **SRTS Expanded (continued)** | `1845423808` | 機体 Def 別の有人／貨物シャトル着陸完了数、戦術／絨毯爆撃実行確定数 | バニラ輸送ポッドおよび Odyssey シャトルとの型分離。 |
| `R01` | **NewRatkinPlus** | `1578693166` | ハムスター発電輪の実稼働完了ジョブ数（※種族自体は HAR 共通基盤で分類済） | 毎 tick 発電量ではなくジョブ正常完了または規定稼働達成遷移の確認。 |
| `S31` | **Quarry [Adopted]** | `2007576583` | 採石場から実採掘・配置された資源総数（鉱石／石材／スクラップ Def 別内訳） | バニラ `Mineable` 破壊を通らない独自 `JobDriver` 産出生成完了点の確認。 |
| `S29` | **Mines 2.0** | `2503894706` | 鉱山設備での採掘作業完了回数・産出鉱石数 | バニラ `Bill_Production.Notify_BillDone` を継承している場合の汎用製作統計との重複有無確認。 |
| `S37` | **Biotech Cloning (Continued)** | `3348781223` | クローン培養槽からのクローン個体誕生確定数 | 通常出産・バニラ培養槽との区別、生成ポーンのスポーン成功後フック確認。 |
| `S41` | **Integrated Implants** | `3223443793` | 追加特殊インプラント移植・改修成功数 | 汎用医療手術完了統計とのスコープ明示、および専用アビリティ発動有無の確認。 |
| `S42` | **Gene Remover Continued** | `3280784251` | ポーンからの指定遺伝子確定除去完了数 | キャンセル・中断を除外し、`Pawn_GeneTracker.RemoveGene` 実行確定点をフック。 |
| `S43` | **Gene Ripper** | `3524806362` | 犠牲抽出による確定 Genepack 実生成数 | バニラ `Building_GeneExtractor` との別クラス確認、アイテム実スポーン後フック。 |
| `S15` | **Rimesis** | `3767949538` | 宿敵 Rimesis の撃退回数、完全討伐／捕獲回数、War Camp 攻略数 | 一時撤退（再襲撃あり）と恒久決着（死亡・勧誘）のフラグ分離。 |
| `S51` | **Psychology — 1.7（RADMIKO / Resurrected）** | `3829501925` / `3838502425` | 市長選挙当選・就任回数、不安症（Anxiety）治療完了数 | 現行 1.7 の 2系統 `packageId`・アセンブリ名両対応と治療成功判定の確認。 |
| `S52` | **ISEKAI RPG LEVELING** | `3657580708` | レベルアップ確定回数、最高到達レベル（スナップショット）、ランク昇格・装備強化成功数 | プレイヤー入植者のみへの限定（敵・来訪者レベル除外）。 |

### 2.2 Tier 2：1.7 配布DLLの IL 監査次第で実装可能な中核候補（27件）

- **Odyssey・海底・地下・多階層遺跡探索（7件）**：
  - `S06` GravTide (`3779600989`)、`S07` As above, So below 2 (`3776015553`)、`S08` Colony dungeon! (`3828091246`)、`S09` Ancestral Ruins (`3831032590`)、`S10` Rimworld Exploration Mode (`2941608795`)、`S11` Ancient urban ruins (`3316062206`)、`S33` Rimsential - Spaceports (`3225120958`)
  - **共通ゲート**：サブマップ生成・再訪問でコロニー帰属が分裂したり発見数が二重加算されたりしない一意サイト ID・完了フラグの確認。
- **高難度ボス・メカノイド・車両襲撃・戦略外交（8件）**：
  - `S14` Kraltech Industries (`3820851894`)、`S16` Back For Vengeance (`3761967749`)、`S18` Mechanoid Mechanitor (`3811415429`)、`S19` Glitterworld Destroyer 5 MechFusion (`3549290904`)、`S21` VVE Raid (`3720926003`)、`S24` Horrors (`3839223635`)、`S26` Dynamic Diplomacy (`3220299022`)、`S27` Hearts of RimWorld (`3833698946`)
  - **共通ゲート**：多段階変身ボスの最終死亡のみ計上、搭乗兵下車と車両破壊の分離、AI 派閥間自動外交の除外。
- **自動化・キャンペーン・種族派閥（3件）**：
  - `C06` The Dead Man's Switch II (`3819799478`)、`R03` [OA]Ratkin Faction: Oberonia aurea (`3159926804`)、`S30` Misc. Robots (`724602224`)
- **Anomaly・帝国・心理・教育・超能力（9件）**：
  - `S39` Dark Psychic Rituals (`3596468709`)、`S44` [AA] Empire Expanded Forces (`3837526614`)、`S46` Corrosion / Anomaly (`3731466157`)、`S47` Mechanoid Repurposing (`3799572082`)、`S48` Anomaly Mutations Expanded (`3836076384`)、`S50` Progression: Therapy (`3753615736`)、`S54` Psycasts² (`3761869362`)、`S55` Teenage Years (`3757880146`)、`S56` Rim Academia (`3827963827`)
  - **補足**：バニラ側に Anomaly 心霊儀式収集ファミリー（`PsychicRitual` 完了＋Def 内訳）を新設すれば、`S39` 等は専用パッチ不要で自動収集できる可能性が高い。

### 2.3 Tier 3：既存汎用統計・`ModdedIncidentFamily` で実質カバー濃厚な整理候補（11件）

以下の 11件は XML Def 拡張・常時ステータス補正・汎用襲撃／栽培／手術の継承が中心であり、1.7 配布パッケージに独自 C# 完了イベントが存在しないことを確認した段階で「汎用統計で網羅済み（専用キー見送り）」へ整理するのが妥当である。

- `S05` ReGrowth 2（バイオーム・岩塊中心）、`S12` [RH2] Faction: V.O.I.D.、`S17` Apex Mechanoids、`S22` Rimsenal Federation、`S23` Rimsenal Feral、`S36` VGP Vegetable Garden、`S40` EPOE-Forked、`S45` Enhanced Ideology、`S57` H.P. Lovecraft Storyteller Revived（`ModdedIncidentFamily` で収集済み）、`S60` Rimpsyche - Personality Core。
- `S25` Zombieland 1.7 (Unofficial)：大量発生するゾンビ死亡ごとの個別フックは TPS 上非推奨。専用血清による感染治療完了や巣窟根絶などの低頻度確定イベントが確認できる場合のみ対象とする。

### 2.4 保留 8件（`HOLD-01`〜`HOLD-08`）の再評価

- **昇格有力（3件）**：
  - **`HOLD-08` WVC - Xenotypes and Genes (`2886992038`)**：公開 GitHub リポジトリが活発に更新されており、Steam 1.7 正式安定次第、即 Tier 1 候補へ昇格可能。
  - **`HOLD-01` Medieval Overhaul (`3219596926`)**：本体 1.7 アセンブリ安定確認後、独自製鉄炉・皮なめし樽・ダンジョン遺跡攻略の完了イベントを監査。
  - **`HOLD-07` Vanilla Anomaly Expanded – Insanity (`3342412435`)**：VE 公式の 1.7 修正反映後に精神汚染イベント・専用対処完了を監査。
- **継続監視（2件）**：`HOLD-03` What the Hack?! (`3372841828`)、`HOLD-05` Reinforced Mechanoid 2 (`3541404610`)（1.7 安定性確認待ち）。
- **対象外確定推奨（3件）**：`HOLD-02` Vanilla Temperature Expanded（常時空調状態のみ）、`HOLD-04` Fortifications - Industrial（汎用建設・タレット撃破で網羅）、`HOLD-06` VFE Mechanoids（公式 1.6 以降更新終了・分割後継 MOD は対応済み）。

---

## 3. 第7波：未登録の新規 RimWorld 1.7 対応候補（`W01`〜`W12`、12件）

既存の [supported-mods.md](../supported-mods.md) および [active-candidates.md](active-candidates.md) のいずれにも未登録であり、RimWorld 1.7 対応状況と独自の確定成果イベントが見込める新規候補 12件を登録する。

| ID | MOD | ジャンル | 1.7対応状況 | 提案メトリクス（確定成果）と設計上の関門 | 優先度 |
| --- | --- | --- | --- | --- | --- |
| `W01` | [Prison Labor](https://steamcommunity.com/sharedfiles/filedetails/?id=1899474310) | 囚人労働・運営 | 1.7 対応済 | 囚人労働による実生産・採掘・収穫完了数、更生・動機付けイベント。現行の `pawn.Faction == OfPlayer` 作業フィルタの死角解消と直結。 | **P0** |
| `W02` | [MedPod](https://steamcommunity.com/sharedfiles/filedetails/?id=2153065191) | 高度医療設備 | 1.7 対応済 | 自動医療ポッドによる治療・欠損部位再生サイクル完了回数（人間用／動物用内訳）。バニラ医師の `Tend` / 手術レシピを通らない独自治療経路。 | **P0** |
| `W03` | [Alpha Implants](https://steamcommunity.com/sharedfiles/filedetails/?id=3275091331) | インプラント・Biotech | 1.5–1.7 対応（公開GitHub `juanosarg` 有） | 特殊インプラント・動物用インプラント移植完了数、専用アビリティ発動完了数。汎用手術統計との重複範囲を上流ソースで確定可能。 | **P1** |
| `W04` | [Alpha Vehicles - Neolithic](https://steamcommunity.com/sharedfiles/filedetails/?id=3028675048) / [Early Cars](https://steamcommunity.com/sharedfiles/filedetails/?id=3337198188) | 乗り物（VF系） | 1.7 対応（公開GitHub有） | 既存の `Vehicle Framework` 汎用統計で完全網羅済みか、動物牽引・初期車両固有の確定イベントがあるかを上流コードで確定。 | **P2（網羅確認）** |
| `W05` | [Biomes! Islands](https://steamcommunity.com/sharedfiles/filedetails/?id=2038001322) | 海洋・島嶼バイオーム | 1.7 対応済 | 既存の `Biomes! Caverns` / `Alpha Biomes` と同方式の島嶼・環礁・水中バイオーム別拠点運営実績および固有資源採取。 | **P1** |
| `W06` | [Biomes! Prehistoric](https://steamcommunity.com/sharedfiles/filedetails/?id=2860715703) | 先史バイオーム・恐竜 | 1.7 対応確認対象 | 恐竜卵の孵化・調教実績、琥珀・化石からの遺伝子／個体復元完了数（既存 `Biomes! Fossils` との境界分離を確認）。 | **P1** |
| `W07` | [RimNauts 2](https://steamcommunity.com/sharedfiles/filedetails/?id=2880599514) | 軌道・衛星拠点 | 1.7 対応確認対象 | 月面・小惑星・人工衛星マップの創設数、宇宙鉱物採掘完了数。Odyssey 軌道層および SOS2 との重複有無を DLL で確認。 | **P1** |
| `W08` | [Universum](https://steamcommunity.com/sharedfiles/filedetails/?id=2954266198) | 宇宙空間・真空環境 | 1.7 対応確認対象 | 宇宙バイオーム拠点創設数・真空環境下での固有設備完了イベント。`RimNauts 2` とセットで監査。 | **P2** |
| `W09` | [RimQuest (Continued)](https://steamcommunity.com/sharedfiles/filedetails/?id=2263331727) | クエスト情報購入 | 1.7 対応済 | 来訪クエスト付与者（QuestGiver）からのクエスト情報購入成立回数・支払シルバー累計。通常のクエスト完了や通常商取引とは独立した意思決定。 | **P1** |
| `W10` | [Custom Base Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3813689040) | 多階層拠点・ダンジョン基盤 | 1.7 対応（新興注目基盤） | フレームワーク経由で生成されたカスタム多階層敵拠点・ダンジョンの完全制圧数（対応コンテンツMODを横断カバー可能）。 | **P1** |
| `W11` | [Faction Traits: Quests & Visitors](https://steamcommunity.com/sharedfiles/filedetails/?id=3829919233) | 派閥特性・契約 | 1.7 新作 | 派閥特性に基づく専門人材派遣受入、鉱脈取引契約、古代遺跡共同探査クエストの完了数。 | **P1** |
| `W12` | [Cash Register](https://steamcommunity.com/sharedfiles/filedetails/?id=2506046833) | 店舗運営基盤 | 1.7 対応済 | レジ担当シフト完了回数（※売上シルバー自体は既存の `Gastronomy` / `Storefront` 側と二重計上になるため、計測する場合はシフト完了等の非重複単位に限定）。 | **P2** |

---

## 4. 旧 1.6 止まり除外からの 1.7 復活ウォッチリスト（`RW01`〜`RW08`、8件）

購読者数が非常に多いものの、過去調査時点で公式 1.6 止まりまたは移植過渡期だった定番MOD群。公式更新または安定した 1.7 Continued 版の選択 DLL が確認でき次第、`active-candidates.md` の実装対象へ引き上げる。

| 監視ID | MOD | Workshop ID | 注目理由・1.7 動向と想定固有統計 |
| --- | --- | --- | --- |
| `RW01` | **Milira Race** ＋ **Milira Expanded** | `3256974620` / `3560043283` | 拡張側（`Milira Expanded`）は 1.7 対応明記。太陽炉稼働・浮遊要塞戦・専用ユニット製造などの独自成果が豊富。 |
| `RW02` | **Big and Small – Framework (& Genes)** | `2925432336` | Biotech 体格・巨人／小人系MOD群の中核基盤。1.7 安定版の専用遺伝子儀式・変身イベント有無を監視。 |
| `RW03` | **More Faction Interaction (Continued)** | `2379076640` | 外交定番MOD。年間万博（Annual Expo）入賞回数、派閥開拓支援、射撃大会等の確定成果イベントが豊富。 |
| `RW04` | **Questionable Ethics Enhanced (Continued)** | `2850854272` | 培養槽による臓器培養完了数・クローン育成完了数・神経ステープル施術数など確定イベント境界が極めて明確。 |
| `RW05` | **Research Reinvented** | `2868392160` | バニラ研究とは異なる「プロトタイプ試作」「現物逆設計（リバースエンジニアリング）」「実地調査」の完了回数。 |
| `RW06` | **RimCities** | `1775170117` | 都市マップ防衛・都市襲撃・暗殺クエスト完遂数。1.7 公式／Continued 版の安定待ち。 |
| `RW07` | **Kurin HAR Edition / Deluxe Edition** | `2326430787` / `2670355481` | 1.7 パッチ並存状態。種族分類自体は HAR 共通層で対応済みのため、固有イベント有無のみ監視。 |
| `RW08` | **Revia Race – Biotech / HAR** | `2893157989` | 血石（Bloodstone）奉納による尻尾段階（Soul Reap Tier）昇格回数という明確な固有成長成果あり。1.7 正式対応待ち。 |
