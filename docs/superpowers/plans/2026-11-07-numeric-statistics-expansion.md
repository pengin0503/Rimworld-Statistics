# Rimworld-Statistics Numeric Expansion Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans and implement one task at a time after implementation is requested. This document is a handoff. Its creation authorizes documentation only.

**Goal** — Expand the title-screen statistics with numeric history, saved state and confirmed events.

**Architecture** — Retain the v1 profile and grouping behavior, adding a separate collection of compact metric observations. Add a save-scoped checkpoint only for event state. The UI consumes a cached numeric projection.

**Tech stack** — C#/.NET 10, xUnit, RimWorld 1.7/Waylith, Harmony, Scribe and streaming XML.

**Specification** — [design](../specs/2026-11-07-numeric-statistics-expansion-design.md), [323-item registry](../specs/2026-11-07-numeric-statistics-expansion-metrics.md), [evidence and adapter gates](../../research/2026-11-07-numeric-statistics-expansion-evidence.md).

## 共通条件

- グラフなし。公開用v1ワークフローやReleaseを本計画の検証に使用しない。旧テストを改変して既存挙動の変化を正当化しない。新規の回帰テストを独立ファイルに置く。既存エンディングの即時記録とv1集計を維持する。
- 各タスクは合成fixtureまたは手順表の期待値を先に固定 → 新テストの失敗を確認 → 最小実装 → 新規・既存テスト → 同じ変更単位でコミット。DLC/競合MODの実機試験で確認できた出力だけを登録する。環境が揃わなければ関連出力を未登録で終了し、ゲートの根拠・残りの確認を記録する。
- 実装手順中のパスは、`Core/` = `Source/RimworldStatistics.Core/`、`Mod/` = `Source/RimworldStatistics/`、`Tests/` = `Tests/RimworldStatistics.Core.Tests/`。C/Dのファミリー名はファイル名に示す英字を使う。
- すべてのPhaseで下記の自動確認。R2監査資料がある場合は静的参照を確認し、対応ゲームの実体で使用したパッチを改めて照合する。配布・公開は別作業。

```bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -p:UseNuGetRefs=true
dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release --nologo -p:UseNuGetRefs=true
bash tools/check_keys.sh
# 監査資料および対象版のDLLを入手した環境で
python3 tools/fetch_dev_inputs.py audit .devinputs/audit
bash tools/refcheck.sh
bash tools/check_patches.sh
```

## レビュー重点

1. 旧v1 profileでスキーマ省略、保護コピー失敗、v2を旧MODで開く場合はA1。
2. XMLの部分破損、同一ファイルのコピー、上書き後の再読失敗、元ファイル削除はA2。
3. 同tickに異なる枝、未保存、正常書き込み後の後処理失敗、他MODで原処理スキップはC0。
4. 同一事象に複数通知・動的Def・対応DLC/プラグインなしはC1〜C4、D2。
5. 混合分母、異なる範囲、非表示対象、部分観測を含む順位はA3とD1。

---

## Phase A · 履歴から詳細表示まで

### A1 · Profileと数値データ契約

**変更対象** — 新規 `Core/Model/MetricObservation.cs`、`Core/Model/MetricValue.cs`。変更 `Core/Model/Profile.cs`、`Core/Model/ProfileJson.cs`、`Core/Profiles/ProfileStore.cs`。新規 `Tests/NumericProfileTests.cs`。

- [ ] `Profile.MetricObservations` を空の既定値で追加し、設計§2の共通値、来歴、期間、内訳、観測キー、拡張データを定義。集計/描画からは読み取り専用として扱う。
- [ ] スキーマ省略と明示1の旧fixtureをロードし、開始時点でディスク不変、最初の有効変更後だけschema 2になることを固定。v1のエンディング/除外/同定キーを比較する。
- [ ] `ProfileStore` の既存ロック内で移行と1回の保護コピーを実装。v2の未知フィールド往復、未来版の書込禁止、読めないv1のバックアップ復旧、外部差替え、保護コピー書込失敗で一次ファイル不変を検証。
- [ ] v1版のコードが明示v2を読取専用に扱うことを旧タグの既存コード/互換fixtureで確認。破損した一指標が他指標を落とさない。

### A2 · セーブの取得・同一観測の保護

**変更対象** — 新規 `Core/Reading/HistoryMetricReader.cs`、`Core/Reading/RetainedQuestMetricReader.cs`。変更 `Core/Reading/SaveReader.cs`、`Core/Reading/RecordsCodec.cs`、`Core/Reading/RawInflate.cs`、`Core/Import/ImportEngine.cs`、`Core/Profiles/ProfileMutations.cs`。新規 `Tests/NumericSaveReaderTests.cs`、`Tests/NumericImportTests.cs`。

- [ ] `SaveReader.Read` の既存結果にデタッチした拡張観測を渡せるようにし、`ProfileMutations.UpsertMetricObservation(Profile, MetricObservation)` が v1の観測更新と同じロックで受け取る。v1の `Dominates` を変えない。reader=4、同じファイルの旧reader観測は現存分だけ再読。現行の `.rws/.rws.old` は継続し、`.rws.new` は対象外。
- [ ] `HistoryMetricReader` で台帳Aの系列のみ要約。復号上限を拡張の呼び出しだけに適用。既存の `Wealth_Total` はv1のデコード結果を可能な限り再利用する。先頭floatのビット列とv1値を比較。
- [ ] 期待例は `Wealth_Total=[100,150,120]` で最終120、最大150、平均370/3、最大増50、最大減30、減少率20%、連続増0.5日。長さ30/31、120/121の固定窓境界、同値・欠測・非有限値・分母0、異なる長さの系列を試す。 `ColonistMood=[60,0,40,85]` を対象版のtick90000で読み、有効3点、平均185/3、50未満の観測区間0.5日、部分記録。先頭不明の `[0,40,85]` をtick60000で読むと平均62.5は表示可、50未満日数は—。壊れた拡張系列が既存成功を消さない。
- [ ] クエストfixtureは受諾済み成功/失敗各1、未受諾失効1、途中1、無効1、同一IDコピー1。成功率50%、保持済み受諾3、失効1。自動受諾、root欠落、ID矛盾、負の所要期間を別ケースで検証。
- [ ] XMLをEOFまでストリーミングしながらハッシュを取る。コピー/改名、同名別内容、読取り途中差替え、削除済み観測、二度目のスキャン、エラー後再試行を検証。元ファイルが無い値は既知情報を保持し、不明な指標を生成しない。1.5/1.6/1.7について時間付き出力の版ゲートを記録する。

### A3 · コロニー集計と詳細タブ

**変更対象** — 新規 `Core/Stats/MetricRegistry.cs`、`Core/Stats/MetricCalculator.cs`、`Mod/UI/DetailsTab.cs`、`Tests/NumericAggregationTests.cs`。変更 `Core/Profiles/ProfileSession.cs`、`Mod/UI/Dialog_RimworldStatistics.cs`、`Languages/English/Keyed/RimworldStatistics.xml`、`Languages/Japanese/Keyed/RimworldStatistics.xml`。

- [ ] `MetricCalculator.BuildView(Profile, IReadOnlyList<Colony>, StatsOptions)` が immutable 数値ビューを返す。取得元別の代表を1件選び、手動統合・除外をv1の結果から適用。曖昧帰属やv1に存在しないコロニーは集計しない。
- [ ] コロニーAの成功1/2、Bの成功9/10で全体10/12を固定。B除外で1/2、欠損コロニー追加で値を維持し部分表示、DLC非対象は母数外。古い最大値が大きい枝を通算の分子にしない。安定キー、逆順取込、同名別コロニー、手動統合を検証。
- [ ] 詳細タブと日英キーを追加し、Aの対象指標のみ登録する。0/不明/部分/保存時点、分母0、対象数、長いDef名、桁数、並び替えを目視試験。描画からディスクと重い処理を呼ばない。既存4タブをすべて確認。

**A完了** — 旧セーブを読むだけで使用可能。v1概要・コロニーキー・終了計数の回帰と共通の性能基準を満たす。

## Phase B · 保存時点の状態

### B1 · 保存時スナップショット

**変更対象** — 新規 `Mod/SavedStateMetricReader.cs`、`Tests/NumericSnapshotTests.cs`。変更 `Mod/GameComponent_RimworldStatistics.cs`、`Core/Stats/MetricRegistry.cs`、日英キー。A1〜A3の観測・集計基盤を使用。

- [ ] `SavedStateMetricReader.Read(Game, int, DateTime)` で保存時の値をデタッチ。 `RecordSave` が正常保存後に1回呼ぶ。Profileへ反映する前にGame参照を外す。Aの履歴と同じ観測を装わない。失敗/非保存時にBだけ記録しない。
- [ ] 最初の独立パッチ単位は `research-state` と `factions`。完了2/対象4で50%、研究途中と開始時に完了済みの研究を区別。Ally/Hostile各1、Neutral・一時勢力・恒久敵対のgoodwillを別に扱う。
- [ ] 次の独立単位は `roster` と `facilities`。通常ホーム1、遠征マップ1でホーム1、グラヴシップ条件でホーム増を確認。輸送中の自由入植者、ロッジャー、囚人、動物、死者を別に数える。P-019は未治癒負傷を持つ人数の最大保存観測。
- [ ] `stock` は独立単位。Silver 100、Steel 10、別の商人在庫のSteel 5ならSteel 10。同一武器の保管→装備で同じ合計。低頻度に一巡だけ行い、独自コンテナ未確認時は部分記録か行無効。 `evidence` の各カテゴリの陽性・陰性ペアをすべて通す。

**B完了** — Cのフックやチェックポイントがなくても正常保存で数値が更新。旧セーブには未記録を表示。ゲーム側の失敗や対応外Defは他の数値に影響しない。

## Phase C · 成功セーブに結び付く確定イベント

### C0 · チェックポイント基盤

**変更対象** — 新規 `Core/Model/MetricCheckpoint.cs`、`Core/Stats/CheckpointSelector.cs`、`Mod/MetricSaveScope.cs`、`Tests/NumericCheckpointTests.cs`。変更 `Mod/Patches/SaveCompletedPatch.cs`、`Mod/GameComponent_RimworldStatistics.cs`、`Core/Reading/SaveReader.cs`、`Core/Profiles/ProfileMutations.cs`。

- [ ] `MetricCheckpoint` は checkpointId、parentCheckpointId、tick、UTC、フォーマット、ファミリーごとの集計と計測範囲。 `CheckpointSelector.Select` はコロニーの代表・系譜状態を返す。v1の代表計算は維持。reader=5、Scribe `dataVersion=2` と文字列 `numericStatistics` で拡張を読み、未知内部版は不透明文字列のまま保つ。
- [ ] 親Pの件数3→子Aで+2、Pを再ロード→子Bで+1。Aがtick大なら5、Bがtick大なら4、同tickは保存UTC→キーで決定。9も6も出ない。子を再インポート/コピーしても増えない。未保存進行と失敗保存はプロファイルに出ない。
- [ ] 設計§3のPrefix/ExposeData/Postfix/Finalizerの順序を実装。ダミーの計数源で、保存失敗→再試行、v1セーブ初回、正常書込み後の後処理失敗→再読、ファイル上書き、ゲーム終了、パーマデス、自動保存、RimSaves併用を確認。P→置換後に例外のF→再ロードせず別名SのケースではF/Sとも親P。Fを再読すると保存枝が2つになり、累計は1枝のみ。Fを実ロードしてからTを保存すれば、ロード時にFを同ロックで冪等回収しTの親はF。
- [ ] MODなし保存→再導入、親欠落、ID衝突、循環、未知形式、連続セーブ、再入、original skip、プロファイル書込失敗を検証。本体と本MODの成功後に後続他MOD Postfixだけが例外を出すfixtureでは確定済み親・観測は維持し、例外は元どおり返す。新規データなしで既存セーブ/読込みが従来どおり成立することを確認。

### C1 · 研究と心情の最初のアダプター

**変更対象** — 新規 `Mod/Patches/ResearchMetricPatches.cs`、`Mod/Patches/MentalMetricPatches.cs`、`Core/Stats/LiveMetricAccumulator.cs`、`Tests/NumericEventTests.cs`。変更 `Core/Stats/MetricRegistry.cs`、日英キー。

- [ ] 集計器には確認済みの有限数値・安定キー・tickを渡す。計測開始/終了と集計辞書上限をC0へ渡す。ゲームオブジェクト参照をプロファイルに持ち込まない。
- [ ] 通常作業が完了閾値を超える場合、直接完了、重複、前提の再帰完了を試験。進捗変更前の状態を外側で持つ。完了2、開始を観測できたもの1だけなら所要時間の標本1。最初の完了tickと研究期間の単位を混同しない。
- [ ] 通常の心情起因の成功1、失敗1、強制1なら精神崩壊1。ひらめき成功1と失敗1なら1。署名不一致のファミリーだけ未登録にして C0 の保存と他方が動くことを確認。

### C2 · 戦闘・人口の観測

**変更対象** — 新規 `Mod/Patches/DeathMetricPatches.cs`、`Mod/Patches/DownMetricPatches.cs`、`Mod/Patches/MembershipMetricPatches.cs`、`Mod/Patches/RaidMetricPatches.cs`、`Mod/Patches/LifetimeMetricPatches.cs`、`Mod/Patches/SocialMetricPatches.cs`、`Mod/Patches/AnimalMetricPatches.cs`、`Tests/NumericCombatPopulationTests.cs`。集計器、Registry、日英キーへ接続。

- [ ] 以下は表の1行を独立実装単位とする。各単位で台帳・根拠の個別ゲートを照合し、最初の成功出力を登録。その後同じ証拠を再利用できる内訳を追加。前提が無効ならその依存出力も無効。

| 単位 | 数値fixtureと実機の判定 |
|---|---|
| health-death | 自軍武器で敵1、敵同士1、帰属不明1で自軍撃破1。蘇生後に再撃破は2。タレット・トラップ・環境の両分類軸を照合 |
| down | 1人がダウン→復帰→ダウンで2。ダウンを経ない即死は0。無力化と敵ダウンの別名は同値 |
| membership | 初期3→新規1→離脱/再加入1で新規1・ユニーク4。勧誘、誘拐、出生の同じIDを一度だけ分類 |
| raid | 成功2・失敗1で発生2。到着がtick60000,180000なら完結間隔2日。2つの襲撃の重複と別起因死を混ぜない |
| lifetime | スキル19→20→19→20で初到達1。完結在籍2日と未完了5日は平均2日 |
| social | 1ペアの両側通知で成立1。死別で離婚0。捕獲、釈放、脱獄は別の状態遷移 |
| animals | 調教1と購入1で調教1。屠殺1と戦闘死1で家畜死亡2・屠殺1 |

### C3 · 経済・仕事の観測

**変更対象** — 新規 `Mod/Patches/WorkMetricPatches.cs`、`Mod/Patches/FoodMetricPatches.cs`、`Mod/Patches/TradeMetricPatches.cs`、`Mod/Patches/DiplomacyMetricPatches.cs`、`Tests/NumericEconomyTests.cs`。集計器、Registry、日英キーへ接続。

- [ ] 以下は1行ごと独立。表示の承認前に、Core＋Harmonyと対象MODで通常経路/代替経路を測る。

| 単位 | 数値fixtureと実機の判定 |
|---|---|
| work | 成果10+2個、別の中止1で12。名品2個は品質内訳2。単発/一括の主収穫、未回収、採掘失敗、実建築/キャンセルを区別 |
| food | 栄養0.2を3回食べて0.6。腐敗2個と焼失3個で腐敗損失2。連続在庫は境界を証明できない場合は無効 |
| trade | 10個を単価3で売却、2個を単価20で購入、贈答、空取引、失敗で1取引・売30/買40・販売単価最大3。通貨は物品個数外 |
| diplomacy | 贈答による実増加+2、上限の変化なし、敵対→中立の各経路で+2と和解1。援軍の予約のみでは到着0 |

### C4 · 医療・環境・遠征の観測

**変更対象** — 新規 `Mod/Patches/MedicalMetricPatches.cs`、`Mod/Patches/ConditionMetricPatches.cs`、`Mod/Patches/CaravanMetricPatches.cs`、`Tests/NumericWorldEventsTests.cs`。集計器、Registry、日英キーへ接続。

- [ ] 以下は1行ごと独立。イベントが途中で消えた場合は未完了として破棄し、完結標本に足さない。

| 単位 | 数値fixtureと実機の判定 |
|---|---|
| medicine | 1回の治療で2患部へ適用したら治療1・品質標本2。成功手術1、失敗1、中止1で実施2・成功率50%。死によるHediff除去は治癒0 |
| disaster | 2マップの同効果区間 [0,60000] / [30000,90000] で1.5日。延焼を着火数に加えない。発生の成功と終了を別に観測 |
| caravan | 徒歩A→B→A＋転送Cで距離2、到着ユニーク2。キャラバンの合流と全滅、帰還と任意放棄を区別 |

**C完了** — C0と少なくともC1の確認済み経路が動作。C2〜C4の未通過指標は個別に閉じたまま、通過指標は分岐/巻き戻しと性能を実機で確認。Bを入れなくても機能する。

## Phase D · 派生比較とDLC

### D1 · 数値ランキング

**変更対象** — 新規 `Core/Stats/MetricRankings.cs`、`Tests/NumericRankingTests.cs`。変更 `Core/Stats/StatsCalculator.cs`、`Core/Stats/MetricRegistry.cs`、`Mod/UI/DetailsTab.cs`、日英キー。終了時メタデータが必要な出力だけ `Mod/EndingRuntime.cs` と `Core/Model/EndingEvent.cs` を拡張。

- [ ] v1の終了採否、代表ラベルと除外を共通に参照する。変更前後で既存の件数・出発数・終了種類を完全一致させる。
- [ ] 終了実測tick600000と痕跡tick300000で最速は10日。Dev設定、痕跡のみ、同点、除外コロニー、選択された難易度の帰属を試験。
- [ ] 依存項目は個別に有効化。途中導入値と完全値を別順位。同一時点の分子/分母のみ使い、終了時死亡数不明は0にしない。R-011に行や記録を作らない。

### D2 · DLCアダプター

**変更対象** — 新規 `Mod/Patches/RoyaltyMetricPatches.cs`、`Mod/Patches/IdeologyMetricPatches.cs`、`Mod/Patches/BiotechMetricPatches.cs`、`Mod/Patches/AnomalyMetricPatches.cs`、`Mod/Patches/OdysseyMetricPatches.cs`、`Tests/NumericDlcTests.cs`。Registry、集計器、日英キー。保存状態を使う指標はB1の読取りを共有。

- [ ] 1 DLCごとに独立コミット。適切な `ModsConfig.*Active` ゲートとDef/メソッド検出で、新規取得だけを有効化する。現在Royaltyが無効でも、旧観測の既知の爵位は表示。旧セーブで明示DLC無しは対象外、modIds欠落は不明。無効・未購入・対象なしで v1〜Cに影響0。
- [ ] Royaltyの昇爵成功1・同一爵位再設定1で1。Ideologyの成功1・失敗1・中止1は有効分母2。Biotechの出生はP-003と共用し、双子なら2。Anomalyの収容→解放は捕獲1・脱走0。Odysseyの同じ着陸場所2回は移動2・場所1。各DLCの全出力は根拠資料の対応版ゲートと個別fixtureを通過した場合のみ登録。

**D完了** — D1の基礎値だけで成立する順位が使える。DLCが無効でもエラーなし。追加できたDLC行の発火と非発火を実機で記録する。

## 受け入れ計測

1. Aは v1 と同じ匿名化データセットを使用。実49件を使える場合は `RWSTATS_REAL_SAVES_DIR` を設定し、匿名集計だけ保存。なければ各版と同等サイズの合成XMLで構造・負荷を測り、実セーブ互換判定とは区別する。各々3回の中央値で初回取込がv1の1.25倍以下。未変更再走査は0件再読。ファイルのbefore/afterハッシュ一致。
2. Bは同じ大型セーブで各10回、ウォームアップ後の追加保存時間中央値50 ms以下またはv1中央値の5%以下の大きい方。拡張なしとありで正常保存/失敗の結果一致。
3. C/Dは同じ大型コロニー・MOD順で非描画負荷を揃え、開始状態を戻して同じ速度・戦闘で60秒×5回。中央値TPS低下2%以下を目標。イベント無し時の拡張ログとファイル書込0。大容量fixtureでは拡張由来の追加作業メモリを64 MiB以下とする（プロファイルのバックグラウンド処理を含む）。目標超過は原因を特定し、該当単位を有効化しない。
4. UIで5タブの遷移、除外、履歴欠損、保存時点、言語切替、MODを多数載せた1000行のスクロールを確認。実機試験未実施の版・MOD構成は実績に含めない。
