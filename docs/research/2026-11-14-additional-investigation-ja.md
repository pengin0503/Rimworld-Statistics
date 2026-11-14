# Rimworld Statistics 次期開発追加調査（日本語版・第2回）

調査日：2026-11-14（日本時間）  
対象：pengin0503/Rimworld-Statistics、RimWorld 1.7  
基準：main \`dc90d3cd99e4c1bdae5240ec24a049c010304b04\`、正式公開 v1.8.0、調査PR #257  
対象作業：**調査・設計のみ**。本書は機能の実装、実ゲームでの動作確認、対応MODの正式追加を表明するものではない。

## 0. 調査の結論

1. **既存6セーブに「ゲーム時刻より古い、同一のチェックポイントを持った2ファイル」が実在**した。これはセーブ破損と断定できない。SaveReader v17は時刻境界が一致しない証拠を格下げする設計であり、履歴UIに「最後に取得した時刻」を示す改善が有益。
2. **現状のセーブ観測値グラフに未証明の連続性を描く経路**がある。同一キャンペーンIDだけで実線接続している \`SaveChartPoints\` は、親子未証明の異分岐間も結び得る。実ゲーム再現前の**設計上の懸念**であり、修正候補として回帰テストを先行する。
3. **Issue #195 の説明は一部古い**。インポートは既に5秒間隔で保存要求を間引いている。一方でロック内直列化・全ファイル読込/照合の負荷は測定対象として残る。更新時刻だけを使って保護を弱める最適化は行わない。
4. 未整理の有力な追加MOD候補は **Vanilla Storytellers Expanded - Winston Waves** と **Empire Refactored**。前者は波の「撃退完了」、後者は従属入植地の「創設・施設建設・階級上昇」が既存のバニラコロニー統計と異なる。ただし両者とも現在のユーザー配布物におけるロード対象バイナリと成功経路の検証はまだない。
5. **Caravan Adventures** はクエスト進行と賞金クレジット決済が固有ジャンルになり得る。**World Domination 2.0** と **Rim War (Continued)** は仕組みが大規模で重複も多いため、前記より低優先。**RimCitiesの元版**は1.7正式版未確認で先送り。
6. 既存の検索、絞込、お気に入り、比較、折れ線グラフ、CSV、種族別加入/死亡、64件の公開対応MOD名は**実装済みとして扱い、未実装とはしない**。

## 1. 提供済み6セーブの読み取り専用の調査

対象：この会話で提供された \`1.7test_1111_1933.rws\`、\`1950\`、\`1954\`、\`1956\`、\`2107\`、\`2119\` の計6ファイル。元ファイルの改変・リポジトリへの掲載・識別子の公開は行わなかった。Pythonで既存XMLの \`meta/gameVersion\`、\`game/tickManager/ticksGame\`、埋込 \`numericStatistics\` JSONの**ヘッダーと構造のみ**を確認した。**RimWorld実行およびSaveReader本体の.NETテストは本調査では実施していない**。

| セーブ時刻の呼称 | ゲーム本体のticksGame | embedded checkpointのsaveTick | 差 | 親子関係 |
|---|---:|---:|---:|---|
| 19:33 | 12,059,212 | 12,059,212 | 0 | 供給ファイル中に親が存在しない |
| 19:50 | 12,183,997 | 12,183,997 | 0 | 供給ファイル中に親が存在しない |
| 19:54 | 12,246,497 | 12,246,497 | 0 | **19:50の埋込IDが直接親** |
| 19:56 | 12,308,997 | 12,308,997 | 0 | **19:54が直接親** |
| 21:07 | 12,411,427 | **12,348,927** | **62,500** | **19:56が直接親** |
| 21:19 | 12,520,319 | **12,348,927** | **171,392** | 21:07と**同一ID・同一JSON**の埋込チェックポイント |

- 6件はいずれも \`1.7.5005 rev1082\`、MODリスト191件、埋込済み \`numericStatistics\` あり。5種類のチェックポイントIDがあり、構造上の直接親エッジは3本（19:50→19:54、19:54→19:56、19:56→21:07内のチェックポイント）。19:33と19:50は**ゲーム内時刻が進んでも、親IDの一致がないため直接つながらない**。
- 21:07と21:19の2ファイルに埋込まれたものは、どちらも \`saveTick=12,348,927\`、同一ID、同一親ID、同一JSON。この2ファイルを**独立した2回の統計取得、異なる枝、追加イベント**として扱ってはならない。別の保存ファイルであることと、独立した取得証拠であることは別概念。
- この3本は**IDによる構造上の親子**であり、\`HistoricalCounterIntervals\` が求める正確な記録境界・MOD/DLCメタデータの確証まで含めた「計算可能な差分3本」とは断定しない。特に21:07内のチェックポイントは保存時刻と一致しない。
- 個別のチェックポイントID、campaign ID、保存内容、MOD全一覧、SHA-256原文は**掲載しない**。現物を再確認する際は元セーブの読み取り専用経路を用い、公開レポートには件数と匿名化した時刻差分のみを書く。

### 1.1 現行コードとの照合

- \`Source/RimworldStatistics.Core/Reading/SaveReader.Metrics.cs\` の \`TryBuildCheckpoint\` は \`parsed.Payload.SaveTick == metrics.SaveTick\` かつMODハッシュを持つ時だけ \`CheckpointMetadataEvidence.Confirmed\` とする。21:07/21:19の埋込記録は、この条件によって旧い境界の証拠として慎重に扱われる設計。
- \`Source/RimworldStatistics.Core/Profiles/ProfileMutations.Checkpoints.cs\` の \`UpsertCheckpoint\` は同一ID・同一内容の場合にファイル別名を統合する。したがって再インポートで二重計上しない設計は存在する。ただし今回実行確認はしていない。
- \`Source/RimworldStatistics.Core/Stats/CheckpointSelector.cs\` の代表チェックポイントは、単一コロニーに属する取得済み累積を選ぶ。異なる枝を総和しない。
- \`Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs\` の \`CheckpointChartHistory\` は明示的な親IDを辿るが、\`SaveChartPoints\` は同一キャンペーンIDと数値の存在を接続条件に使用する。この差異をUIで隠さないこと。
- **推奨**：統計ウィンドウに「セーブ時刻」「記録が最後に確定したゲーム時刻」「未確認の区間」を表示する。欠測値をゼロや取得失敗と即断せず、取得設定OFF・MOD欠落・時刻境界不一致・記録のない旧セーブを区別する。

### 1.2 回帰テスト候補

| ケース | 受入条件 |
|---|---|
| 2ファイルが同一のチェックポイントJSONを持つ | 1取得点 + 複数ファイル別名。取得2回と数えない |
| 保存 \`ticksGame\` > チェックポイント \`saveTick\` | 断定的な同時取得扱いにせず、時刻差を表示 |
| 同一campaign、異なる子分岐 | 親子未証明の経路を1本の確定時系列線として結ばない |
| 直接親を持つ真の連続保存 | 成功した記録値は正しい時刻で可視化 |
| 同一tickに矛盾する値 | 恣意的な代表値を選ばず未知・欠測扱い |
| MOD/DLC構成差、古いReader形式、読込エラー | 成立しない差分を除外し、既存値を保持 |

## 2. 今回さらに確認した実装上の改善候補

### F01: 時系列の出典と誤認を防ぐ（最優先・P0）

**既存**：\`MetricHistoryKind\` は \`SaveObservations\`、\`CheckpointLineage\`、\`WealthRecorder\`、\`GameRecorder\` を区別する。  
**改善**：グラフの凡例/ツールチップに「確実な親子」「単独のセーブ観測」「保存内の履歴記録」「Partial/Unknown」を表示。連続性を証明できないセーブ観測は点表示とする。画面内の補間線はデータの存在を保証しない。  
**実装対象**：\`MetricCalculator.ChartHistory.cs\`、\`Dialog_ColonyMetricHistory.cs\`、\`MetricChartHistoryTests.cs\`。  
**受入**：分岐・巻き戻し・重複・アーカイブ復元・空系列で元データを変えずに表示が適切。※最終的な描画不具合はまだ実ゲームで再現していない。

### F02: 取得停止・取り逃しの診断（P1）

保存時刻に対してembedded checkpoint時刻が古いことを利用者が理解できるよう、最新セーブごとに「ゲーム内保存日」「統計の最終取得日」「未観測区間の可能性」を表示。ただし原因を自動的に「MOD不具合」と決めつけない。取得ON/OFFの変更、旧形式のロード、ターゲットMOD未検出、実験的取得の例外を区分し、秘密の保存識別子をサポート出力へ含めない。

### F03: #195の残存ボトルネックを測る（P1）

\`ImportEngine.SaveInterval = TimeSpan.FromSeconds(5)\` のため、Issue記載「5ファイルごとに保存」は過去の実装。とはいえ \`ProfileSession.WriteLatest\` のロック保持中の \`ProfileJson.Serialize\` と \`ProfileStore.ReadVerifiedPrimary\` に残る全内容照合は大容量時のコスト候補。0.2/5/10MB級プロフィールを作り、ロック待ち・I/O・割当量・p95を定量化し、競合上書き禁止・v1保護・バックアップ回転は維持する。更新時刻のみの競合判定への置換は不適切。

### F04: 比較の分母を厳密にした「1日当たり」表示（P1）

件数・総数量など**同じ意味の継続取得区間**を持つ加算可能な累積統計のみ、\`件数 ÷ 取得対象日数\` を表示する案。最高資産、最小値、平均値、世代を跨いだ比較不能の数値、断片的なイベント取り戻しには適用しない。初回取得がゲーム開始後の場合、「ゲーム内総日数」で割ると過小評価になるので、取得区間と対象コロニーごとの分母を明示する。複数コロニーの比率は単純平均しない。

### F05: 3,000件以上を想定したグラフ描画間引き（P2）

表示点数が多い時はビューポート幅ごとに最大/最小を保持して表示する（描画専用）。保存済みの観測数値・時刻・極値・未観測の切れ目を消さず、ズーム時に元データを表示。現時点で「必ず性能問題が発生する」とは証明していないため、5MB/10MBプロフィールで描画時間を測定してから実装判断する。

### F06: CSVに歴史的証拠を追加（P2）

CSV5ファイル＋JSONマニフェストは**既に実装済み**。拡張するとしてもオプションの \`metric_history.csv\` / 構造化JSONを新規出力し、原本の5ファイルの列・欠測・数値単位を保持する。観測点には出典、ゲーム時刻、Partial/Unknown、接続が実証済みかを含める。スプレッドシート式注入の既存保護を維持する。

## 3. 前回の候補登録にないMODを追加調査

本節の候補は既存 \`docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md\`（27件）や \`2026-11-14-popular-race-mod-audit.md\`（6件）とは**別枠**。本調査時のリポジトリ内検索では、以下の名前を既存の候補として確認できなかった。Workshop購読者数は2026年11月のクロール時点の概数であって、実アクティブユーザー数ではない。

### N01：Vanilla Storytellers Expanded - Winston Waves — P0調査

- **ユーザー規模の参考値**：Workshop約19万購読。公式ページの1.7対応表記は取得された時点で揺れているが、[modLang](https://modlang.app/en/rimworld/mods/vanillastorytellersexpanded_winstonwave)は1.7対応と記載。**実際の選択DLLはまだ未確認**。
- **固有ジャンル**：撃退済みウェーブ数、最高到達ウェーブ、ボスウェーブ撃退数、実際に確定した報酬選択数と種類。既存の襲撃撃退数とは違う特別なストーリーテラーの進行記録。
- **成功根拠の要件**：「ウェーブ生成」「襲撃が来た」「報酬UIを開いた」だけで計上しない。**ウェーブが正常終了して次の状態へ確定遷移した時**を観測する。2026年9月の更新履歴ではウェーブ番号停止の不具合修正があり、開始回数と完了回数が異なる可能性も高い。
- **留意点**：ストーリーテラー切替中断、同一番号の再開、報酬の再選択/未受取、MOD無しセーブ、Combat Extended、巻戻し/分岐。まず配布DLLと実際の完了遷移をILで確認。
- 参考：[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3215569151)、[変更履歴](https://steamcommunity.com/sharedfiles/filedetails/changelog/3215569151)。適格性は条件付きであり**現在の正式対応MODではない**。

### N02：Empire Refactored — P0ソース監査 / P1実装候補

- **規模**：Workshop約5万購読。独自の従属入植地と課税・軍事遠征を管理する。Royaltyの「帝国派閥」とは異なるMOD。
- **1.7対応の根拠を更新**：[上流GitHub](https://github.com/matathias/Empire-1_6-Continued)の \`About/About.xml\` は \`supportedVersions: 1.6,1.7\` / \`modVersion=1.7.0\`、\`LoadFolders.xml\` は **v1.7 → root、1.7、互換フォルダー**を明示している。上流READMEの「1.6専用」表示は現在のAbout/LoadFoldersと矛盾する**古い説明**。実配布1.7 DLLのロード確証と実イベントの実行成功は別途必要。
- **ソースで実在を確認したイベント**：\`Source/Core/FactionColonies/Interfaces/Lifecycle/ISettlementListener.cs\` は、従属入植地の \`OnSettlementCreated\` / \`OnSettlementRemoved\` / \`OnSettlementUpgraded\` / \`OnBuildingConstructed\` を定義。\`Util/Registries/LifecycleRegistry.cs\` に公開 \`InvokeOnSettlementCreated/Upgraded/BuildingConstructed\` がある。**Register自体はinternal**なので、他MODから自由にlistenerを登録できるとは主張しない。対象実装後の公開呼出点をHarmonyで監査する案。
- **推奨固有統計**：従属入植地の創設成功数、発展段階到達数、従属施設完成数（種別）、承認済みの軍事作戦勝利数。**元RimWorldの自勢力ホームマップのコロニー数に従属入植地を加算しない**。
- **課税で注意**：\`Settlements/Tax/TaxLedger.cs\` の \`AddTax\` は**請求書を発行するだけ**。これは徴収/着金ではない。\`Interfaces/Economy/ITaxDeliveryInterceptor.cs\` の \`OnTaxEventCreated\` も配達前、\`TryDeliverGoods\` は横取り可能な経路。実際のsilver/物資がプレイヤー所有先へ移動し終わる場所を確認し、請求/納税/到着を混同しない。
- **ロード互換**：同じMOD名でも旧Empire系とリファクタ版は非互換のため、packageId \`Matathias.Empire\` に限定。セーブを遡って推定累積を作らない。
- 参考：[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3701480464)、[1.7フォルダー選択](https://github.com/matathias/Empire-1_6-Continued/blob/master/LoadFolders.xml)、[イベント登録](https://github.com/matathias/Empire-1_6-Continued/blob/master/Source/Core/FactionColonies/Util/Registries/LifecycleRegistry.cs)。

### N03：Caravan Adventures — P1条件付き

- **規模**：Workshop約7.6万購読の参考値。キャラバンの冒険、独自ストーリーライン、賞金クレジットを持つ。
- **有望な独自統計**：確定した賞金ターゲット引渡し、実際に受領した賞金クレジット、ストーリー特有の達成段階、確定した能力習得。ただし**通常のクエスト完了、交易、ドラッグ/移動統計の再集計にはしない**。
- **未解決**：正確な現在ロード対象の1.7 DLL、賞金クレジットの決済メソッド、各ステージの成功確定箇所は未取得。ソースが無い状態では「Ready」と判断しない。
- **DLC**：ストーリーにはRoyaltyが必要だが、他の主要カテゴリは独立する。Anomaly前提は不要。
- 参考：[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2558957509)、[更新履歴](https://steamcommunity.com/sharedfiles/filedetails/changelog/2558957509)。

### N04：World Domination 2.0 — P2条件付き（高い固有性・中小規模）

- **規模**：Workshop 2026-11-13近辺で約1.5万購読。以前の「誰でも人気MOD」優先方針に照らし、初手の対象外。
- **1.7宣言**：[公式ソース](https://github.com/Likewise87/World-Domination-2.0)の \`About/About.xml\` は1.6/1.7を宣言。公式ソースに \`Source/Outposts/Actions/WD_Outpost_EstablishFromCaravan.cs\`、\`Source/Quests/WdWorldDominationVictory.cs\`、\`Source/Quests/QuestPart_WdVictoryHold.cs\` がある。
- **候補**：プレイヤーのアウトポスト創設成功、世界制覇の勝利が正式に確定した回数、実際に領土を獲得した件数。ただし \`Vanilla Outposts Expanded\`、\`RimPacts\`、\`Hegemony\`、汎用クエストと重複しない経路限定。地図上のNPC自動行動数を自勢力の実績として数えない。
- **重複/性能リスク**：大型ワールドシミュレーションMODなので、ポーリング・毎tick全ワールド検索は不可。実際のイベント確定先の最小フックを見つけるまでは実装しない。
- 参考：[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3680501610)。

### N05：Rim War (Continued) — 保留

- **1.7の宣言**：[維持版ソース](https://github.com/myszsoda/RimWar---Threaded)のAbout.xmlは1.7を含み、派閥の世界マップでの戦争・動的戦力を扱う。
- **独自候補**：確認済みの敵勢力征服、決着したワールドマップ戦闘、派閥そのものの勝利条件。ただしAI同士の戦闘をプレイヤーの勝利に含めない。出力確定・所属・再発が証明できるまで不採用。
- **優先度を下げる理由**：維持版の1.7で選択されるDLL、到達ユーザー数、実際の成功フックが未監査。World Domination等と機能重複。

### 明示的に採用を見送るもの

| 候補 | 理由 |
|---|---|
| RimCities元版 | 元Workshopの主な対応バージョンは1.6まで。1.7非公式実験移植は確認できるが、小規模で現在の正式安定版とはみなせない。既存のクエスト成功/訪問記録とも重複し得る |
| More Faction Interaction旧版 | 公式元コードの機能は興味深いが、現行1.7用の動作証拠が足りないため候補追加を見送る |
| Colonial Statistics | **競合MOD研究**には有用。現在のマップでの富/自由入植者の人数の「1人当たり資産」を実装しているが、専用MOD統計アダプターとして扱う必要はない。自MODの同一時点比較計算に参考として利用する |

参考：[RimCities](https://github.com/rvanasa/rimworld-cities)、[RimCities 1.7非公式版](https://steamcommunity.com/sharedfiles/filedetails/?id=3826795564)、[More Faction Interaction](https://github.com/Mehni/MoreFactionInteraction)、[Colonial Statistics](https://github.com/Vectorial1024/ColonialStatistics)。

## 4. 既存の27件＋種族6件との優先順位整理

重複排除後の次期調査順は以下。これらは**実装決定ではない**。

| 調査順 | 新旧 | 項目 | 実装前の最低条件 |
|---:|---|---|---|
| 1 | 既存Issue | セーブ観測とチェックポイントの時系列/欠測区分 | 元の取得値を改変せず分岐テストが通る |
| 2 | 既存Issue | Profile書込最適化 | 現行コードの実測と耐障害回帰テスト |
| 3 | 既存27候補 | HAR/Biotech共通分類の拡張 | #256の加入・死亡を二重計上しない |
| 4 | **今回新規** | Winston Waves | 1.7選択DLLと撃退完了処理の確証 |
| 5 | 既存27候補 | Geological Landforms | 実際に開始したコロニーの地形ID確証 |
| 6 | **今回新規** | Empire Refactored | 1.7イベント呼出点、課税/入植地区分 |
| 7 | 既存27候補 | Puppeteer、Dryads、Vanilla Events | 発生ではなく成功後の確定状態 |
| 8 | **今回新規** | Caravan Adventures | 配布DLLと固有の決済/達成処理 |
| 9 | 既存6候補 | Archon、NewRatkinPlus | 実帰還・実労働の成功確証 |
| 10 | **今回新規・低優先** | World Domination 2.0 / Rim War | 人気、1.7実バイナリ、重複/負荷を再評価 |

## 5. 複数AIの作業追加・統合方法

前回の[AI-A～Fの分担設計](../superpowers/plans/2026-11-14-parallel-ai-research-handoff.md)は維持する。新候補だけを別エージェントとして無制限に増やさず、次のように割り当てる。

- **AI-A：** 6実セーブの時刻不一致・同一チェックポイントの扱いを、既存 #72・時系列表現の受入条件に組み込む。
- **AI-B：** 最新5秒保存間隔と残るlock/I/Oコストをベンチマークで検証。ログの個人情報を保持せず保存。
- **AI-C：** 最終取得日時、実測/Partial、期間正規化の表示を、既存検索/お気に入りを壊さず設計。
- **AI-D：** 以前のHAR/Geological Landforms/Archon/NewRatkinPlusをそのまま担当し、新規MODとは競合しない。
- **AI-E：** 既存のPuppeteer/Vanilla Events/Dryadsに加え、Winston Wavesの成功境界を調べる。イベント系アダプターは統計キー・登録ファイルを変更する前に重複を統合評価。
- **AI-F：** 既存のHospital/Fishing/Farmingに加え、Empire Refactoredの徴税「発行/回収/着金」を分ける。Caravan Adventuresはまずソース/配布DLL所在の調査だけ追加。

**AI同時作業は調査文書の独立ブランチに限定**。実装は1PR=1つの独立した結果でTDD、コードレビュー、CI実行後に統合。公開用の \`docs/supported-mods.md\` は実装が終わるまで一切編集しない。

## 6. 検証水準と制約

- **確認済み**：このリポジトリの現在のコード記述、配布済み6セーブの元データの構造、上流GitHub内のファイル/メソッドの存在、Workshop掲載内容。
- **未確認**：本調査でのRimWorld実行、対象MODの1.7ロード実際のDLLのIL/成功時呼出確認、MOD相互運用、実CPU/TPSの計測。
- **捏造禁止**：「成功したイベント数」をUIクリック/着手/予定出荷/容量/定格電力量から推定しない。例外・旧セーブ・MOD除去はUnknown/Partialを残す。
- **非侵襲**：Anomaly所持を前提とせず、Cloudflareなど本番環境に変更しない。生涯累積の分岐重複を禁止。実ゲーム検証は可能な範囲で \`pengin0503/RimWorld-for-mod-test\` の使用を優先し、1.7の細かなパッチ版差は設計の制約としない。
- **対応MOD一覧の公開境界**：調査候補を正式対応として掲載しない。全候補の確認前にリリースしないという意味ではなく、**証拠のある小さな実装を先に完成させられる**ようにする。

## 主な根拠一覧

### Rimworld Statistics 自身
- \`Source/RimworldStatistics.Core/Reading/SaveReader.Metrics.cs\` — exact-boundary判定
- \`Source/RimworldStatistics.Core/Reading/SaveReader.cs\` — Reader v17、入力不変と読込エラー
- \`Source/RimworldStatistics.Core/Profiles/ProfileMutations.Checkpoints.cs\` — 同じ埋込IDの別名統合
- \`Source/RimworldStatistics.Core/Stats/CheckpointSelector.cs\` — 代表選択と分岐分類
- \`Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs\` — プロット点と線の条件
- \`Source/RimworldStatistics.Core/Import/ImportEngine.cs\` — 5秒保存要求
- \`Source/RimworldStatistics.Core/Profiles/ProfileSession.cs\` / \`ProfileStore.cs\` — 永続化の直列化・検証
- [Issue #72](https://github.com/pengin0503/Rimworld-Statistics/issues/72)、[Issue #195](https://github.com/pengin0503/Rimworld-Statistics/issues/195)

### 外部
- [Vanilla Storytellers Expanded - Winston Waves](https://steamcommunity.com/sharedfiles/filedetails/?id=3215569151) / [変更履歴](https://steamcommunity.com/sharedfiles/filedetails/changelog/3215569151) / [modLangの1.7表示](https://modlang.app/en/rimworld/mods/vanillastorytellersexpanded_winstonwave)
- [Empire Refactored ソース](https://github.com/matathias/Empire-1_6-Continued)、[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3701480464)
- [Caravan Adventures](https://steamcommunity.com/sharedfiles/filedetails/?id=2558957509)
- [World Domination 2.0 ソース](https://github.com/Likewise87/World-Domination-2.0)、[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3680501610)
- [Rim War維持版](https://github.com/myszsoda/RimWar---Threaded)
- [比較MOD Colonial Statistics](https://github.com/Vectorial1024/ColonialStatistics)

**最終提言**：まず履歴の証拠の表示精度を高め、同時に新候補は Winston Waves（完了ウェーブ）→ Empire Refactored（従属入植地イベント）の順でソース・ILを確認する。機能の数を増やすことではなく、既存と重複しない**確定した結果**を持つ統計を増やす。
