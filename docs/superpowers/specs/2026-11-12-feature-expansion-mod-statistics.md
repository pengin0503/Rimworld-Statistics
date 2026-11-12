# 追加機能の設計・別紙：他MOD固有の統計拡張（分野8）

- 対象：`main@59ad1eb0`　作成日：2026-11-12
- 本体の設計：[追加機能の設計](2026-11-12-feature-expansion-design.md)（§2 不変条件、§6.8 共通の受入条件）
- 既存の判断基準：[汎用優先の調査（2026-11-11）](../../research/2026-11-11-content-mod-coverage-and-priority-audit.md)・[段階計画（2026-11-11）](../plans/2026-11-11-generic-first-content-mod-statistics.md)。そこにある Task 1〜3（汎用の釣り・MOD追加インシデント・Outpost 稼働数）は実装済みで、本書は残りの Task 4 と内蔵MODの次段を扱う。

## 1. 方針

1. **本体の共通取得処理に統合する。** 新しい取得は `AcquisitionUnits` の取得単位、`MetricGates` のゲート、`MetricAdapter` による遅延適用のHarmonyパッチ、または `SavedStateMetricReader` の保存時点の読み取りとして作る。別の統計アドオンは作らない。
2. **フレームワーク単位で作る。** VEF Outposts（VOE などが使う）、VEF の能力を使う VPE、Vehicle Framework（VVE などが使う）は、1つの取得単位で同じフレームワークのMOD全体を扱う。
3. **既存の内蔵連携は既存の場所で拡張する。** Hospitality・DBH・SAU は `Source/RimworldStatistics/IntegratedMods/` の既存の提供元（`mod.<owner>.<mod>.*` のキー）に統計を足す。
4. **DLLを参照しない。** 型・メソッドは名前で解決し（`AccessTools.TypeByName` と Harmony の `TargetMethod`）、見つからない・引数が違うときはその取得単位だけを閉じる。
5. **確定点だけで数える。** ジョブ開始・試行・`Tick` では数えない。副作用のある `IEnumerable<Thing>` を2回列挙しない。毎tickの処理を足さない。
6. **既存統計との重複を避ける。** 製作・収穫・採掘・交易・本体の `royalty.psycasts` と同じ出来事を二重に数えない。
7. **実験的・既定OFF。** 有効化した後の出来事と保存時点の状態だけを記録する。
8. **静的確認の方法**：既存の `tools/IntegratedModAudit/check_*_contract.py` と同じく、公開ソースを不変のコミットで取得して形を確かめる。ソースがないものは、R2 の該当版DLLのメタデータ（型・メソッド・引数）とILで確かめる。

## 2. 優先順位

価値（利用者数・本体で取れない仕組みか）と、実現性（ソースの有無・確定点の明確さ）、検証のしやすさで並べた。

| 順 | Issue | 対象 | 最初に作るもの | 根拠の有無 | 主な残課題 |
|---|---|---|---|---|---|
| M1 | #163 | Vanilla Psycasts Expanded | 詠唱回数（能力別）、レベル上昇、サイキャスターの状態 | R2 に 1.7 のソースとDLL | VEF 側の `Cast` が成功時に1回だけ呼ばれることのIL確認 |
| M2 | #164 | VEF Outposts（VOE ほか） | 保存時点の駐在人数・種類別数、生産の配送回数と品目別数量 | R2 に VEF 1.7 の `Outposts.dll`、VOE 1.6 の `VOE.dll` | `Produce`→`Deliver` の経路と各Outpostの上書きのIL確認 |
| M3 | #165 | Hospitality | 訪問の終了（正常に帰った訪問）、客との売買 | R2 に 1.7 DLL、公開ソース（既存の固定コミット） | 売買メソッドの引数と、通常の交易との重複がないことの確認 |
| M4 | #166 | Dubs Bad Hygiene | 設備の種類別数（保存時点）、衛生行動の完了回数 | R2 に 1.7 DLL、既存のIL監査ツール | ジョブの完了条件の確認 |
| M5 | #167 | Vehicle Framework | 所有車両の数（保存時点、種類別） | 公開ソースのみ（R2になし、リポジトリにDLLなし） | 1.7 のDLLの入手と型名の確定 |
| M6 | #168 | Smart Apparel Upgrade | 着替えの成功回数 | 公開ソース（既存の固定コミット） | 成功の判定方法の確定 |
| M7 | #169 | その他（Vanilla Trading Expanded など） | 調査のみ | 一部R2にDLL | 1.7 の版の有無、確定点 |

M1〜M7 は互いに独立で、UI側の機能（分野1〜7）とも独立に進められる。

## 3. 対象ごとの根拠と設計

### M1 Vanilla Psycasts Expanded（`VanillaExpanded.VPsycastsE`）

**確認した根拠（2026-11-12、R2 読み取りのみ）**
- `Workshop/2842502659/About/About.xml`：対応版に 1.7 を含む。依存は Harmony・Royalty・Vanilla Expanded Framework（`OskarPotocki.VanillaFactionsExpanded.Core`）。
- `Workshop/2842502659/1.7/Source/VanillaPsycastsExpanded/AbilityExtension_Psycast.cs`：`public override void Cast(GlobalTargetInfo[] targets, Ability ability)` が `base.Cast` の後に `Hediff_PsycastAbilities.UseAbility(psyfocus, entropy)` を呼ぶ（心霊集中とエントロピーの消費＝詠唱の確定）。`Ability` は `VEF.Abilities.Ability`。
- 同 `Hediff_PsycastAbilities.cs`：`ChangeLevel(int levelOffset)`（上書き）と `ChangeLevel(int, bool sendLetter)`（前者を呼ぶ）、`GainExperience`（経験値でレベルを上げる）、`InitializeFromPsylink`（レベルを直接設定し `ChangeLevel` を通らない）。
- `Workshop/2842502659/1.7/Assemblies/VanillaPsycastsExpanded.dll` のメタデータにも同じメソッドがある（ソースとDLLの一致を名前で確認）。

**候補の統計**（キーは仮。既存の命名に合わせて実装時に確定）

| 仮キー | 種類 | 意味 | 取得点 |
|---|---|---|---|
| `royalty.vpe.casts` | 累計（内訳：能力のDef） | プレイヤー勢力のポーンが完了したVPEサイキャストの回数 | `AbilityExtension_Psycast.Cast` の後処理（postfix） |
| `royalty.vpe.levels_gained` | 累計 | プレイヤー勢力のサイキャスターが上がったレベルの合計 | `Hediff_PsycastAbilities.ChangeLevel(int)` の後処理で `levelOffset > 0` |
| `royalty.vpe.psycasters` | 保存時点 | VPE のサイキャスターであるプレイヤーの入植者数 | セーブ時、入植者の `Hediff_PsycastAbilities` の有無 |
| `royalty.vpe.level.max`／`.sum` | 保存時点 | サイキャスターの最高レベル・合計レベル | 同上 |

- 取得単位は2つ（累計とセーブ時点）。必要DLCは Royalty。VPE の型が見つからなければ閉じる。
- 本体の `royalty.psycasts`（`RimWorld.Psycast.Activate`）とは別の能力の仕組み（VEF の能力）なので、同じ詠唱を二重に数えないはず。実装時に、VPE 導入時にバニラのサイキャストが残るか、残る場合に同じ操作で両方が呼ばれないかを確かめる。
- 確認が必要：VEF 1.7 の `VEF.Abilities.Ability.Cast`（R2 `Workshop/2023507013/1.7/Assemblies/VEF.dll`）が、各拡張の `Cast` を詠唱成功時に1回だけ呼ぶか。中断・失敗時に呼ばないか。
- 静的確認：公開リポジトリ `Vanilla-Expanded/VanillaPsycastsExpanded` の固定コミットで型・メソッドを確かめるスクリプトを追加する（`check_*_contract.py` と同じ形）。

### M2 VEF Outposts（`OskarPotocki.VanillaFactionsExpanded.Core` の Outposts、VOE `vanillaexpanded.outposts` など）

**確認した根拠**
- 既存：`exploration.outposts.active`（`Stats/OutpostSnapshot.cs`、保存時点のプレイヤーのOutpost数）。型は基底名 `Outposts.Outpost` で判定し、生産メソッドは呼ばない（`SavedStateMetricReader.ReadOutposts`）。
- `Workshop/2023507013/1.7/Assemblies/Outposts.dll`（VEF 1.7）のメタデータ：`Outposts.Outpost` に `Tick`・`TickInterval`・`ProducedThings`・`Produce`・`Deliver`・`Deliver_Pods`・`Deliver_PackAnimal`・`AddItem`・`TakeItem(s)`・`AllPawns`・`PawnCount`・`TicksPerProduction`・`TimeTillProduction`・`ConvertToCaravan` がある。
- `Workshop/2688941031/loadFolders.xml`：VOE は 1.7 で `1.6/` フォルダ（1.6向けの `VOE.dll`）を読み込む。1.7 の実行時互換は未確認。
- 以前の調査：Vehicle Framework の About.xml が `vanillaexpanded.outposts` を非互換と宣言していた（版ごとに再確認）。

**候補の統計**

| 仮キー | 種類 | 意味 | 取得点 |
|---|---|---|---|
| `exploration.outposts.pawns` | 保存時点 | プレイヤーのOutpostに駐在するポーン数 | 既存の `ReadOutposts` に追加（`AllPawns` または `PawnCount`） |
| `exploration.outposts.by_type` | 保存時点（内訳：WorldObjectDef） | 種類別のOutpost数 | 同上 |
| `exploration.outposts.production_deliveries` | 累計 | 生産によって配送処理に渡された回数 | `Produce` の実行中に呼ばれた `Deliver` |
| `exploration.outposts.produced_units_by_def` | 累計（内訳：ThingDef） | 生産によって配送処理に渡された品目別の数量 | 同上 |

- 保存時点の2件は既存の取得単位（`outpost_state`）に出力を足す。既存の値は変わらない。
- 累計は新しい取得単位にする。`Produce` の前処理で「生産中」の印を立て、終了処理（finalizer）で外す。その間に呼ばれた `Deliver(IEnumerable<Thing>)` の前処理で、引数を**一度だけ**リストに変換して引数を置き換え、そのリストを数える（乱数で中身を作る列挙を2回実行しない）。
- 意味の注意：「配送処理に渡した数」であり「到着した数」ではない。キャラバン化や保管品の引き出し（`TakeItems`・`ConvertToCaravan`）は数えない。
- 確認が必要：`Produce` が `Deliver(ProducedThings())` を呼ぶ経路、`Deliver` の配送方法ごとの分岐、VOE 各Outpostの `Produce`・`ProducedThings` の上書き（VOE 1.6 DLL のIL）。
- 製作（`Notify_BillDone`）・収穫の既存統計とは経路が違うため重複しない（マップ外の生産）。

### M3 Hospitality（`Orion.Hospitality`）

**確認した根拠**
- 既存の内蔵連携：`mod.orion.hospitality.visits.*`（到着した客・グループ）、`mod.orion.hospitality.recruits.*`（加入）、滞在客・割り当てベッドの保存時点の値。CI は公開ソース `Zaljerem/Hospitality@d465bdc0` で形を確認している。
- `Workshop/3509486825/1.7/Assemblies/Hospitality.dll` のメタデータ：`Hospitality.LordJob_VisitColony` に `OnLeaveTriggered`・`Notify_LordDestroyed`・`Notify_PawnLost`、`Hospitality.LordToil_VisitPoint` に `Leave`・`OnPlayerBoughtItem`・`OnPlayerSoldItem`・`BoughtOrSoldByPlayer`・`GetVisitScore`・`PlanRevisit`、`Hospitality.Utilities.GuestUtility` に `Arrive`・`Leave`。

**候補の統計**

| 仮キー | 種類 | 意味 | 取得点（候補） |
|---|---|---|---|
| `mod.orion.hospitality.visits.completed` | 累計 | 正常に帰った訪問グループの数 | `LordJob_VisitColony.OnLeaveTriggered` または `LordToil_VisitPoint.Leave` |
| `mod.orion.hospitality.trade.guest_purchases` | 累計 | 客がコロニーから買った品の数（と銀額） | `LordToil_VisitPoint.OnPlayerSoldItem` |
| `mod.orion.hospitality.trade.guest_sales` | 累計 | 客からコロニーが買った品の数（と銀額） | `LordToil_VisitPoint.OnPlayerBoughtItem` |

- 既存の提供元に、新しい取得単位（例：`mod.orion.hospitality.departures`、`mod.orion.hospitality.trade`）として足す。既存のキーは変えない。
- 確認が必要：`Leave`・`OnLeaveTriggered` が敵対化・逮捕・全滅のときにも呼ばれるか（呼ばれるなら正常な帰還だけを数える条件）。売買メソッドの引数（品物・数量・価格）。客との売買がバニラの `TradeDeal.TryExecute` を通らないこと（通るなら `trade.*` と重複するため数えない）。
- 訪問の評価（`GetVisitScore`）の平均は、確定点が明確になった場合の追加候補とする。

### M4 Dubs Bad Hygiene（`Dubwise.DubsBadHygiene`）

**確認した根拠**
- 既存の内蔵連携：配管の貯水量・タンク容量・ポンプ能力・地下水能力（保存時点）。CI は `Dubwise56/Dubs-Bad-Hygiene@96ec4b51` の 1.7 DLL を `BadHygieneIlAudit` で確認している。
- `Workshop/836308268/1.7/Assemblies/BadHygiene.dll` のメタデータ：`JobDriver_UseToilet`・`JobDriver_takeShower`・`JobDriver_takeBath`・`JobDriver_UseHotTub`・`JobDriver_UseSauna`・`JobDriver_GoSwimming`・`JobDriver_washHands`・`JobDriver_useWashBucket`・`JobDriver_washAtCell` などがある。

**候補の統計**

| 仮キー | 種類 | 意味 | 取得点（候補） |
|---|---|---|---|
| `mod.dubwise.dubsbadhygiene.fixtures.by_def` | 保存時点（内訳：ThingDef） | プレイヤーの拠点にあるDBHの設備の種類別数 | セーブ時、DBH の packageId を持つ建物を数える |
| `mod.dubwise.dubsbadhygiene.activities.by_job` | 累計（内訳：JobDef） | 完了したトイレ・シャワー・入浴などの回数 | ジョブ終了時に `JobCondition.Succeeded` で、対象のジョブドライバーのとき |

- ジョブ終了の取得点の候補は `Verse.AI.Pawn_JobTracker.EndCurrentJob(JobCondition, bool, bool)`（1.7.4985 の監査済み索引で存在を確認）。呼ばれる回数が多いので、対象のJobDefを起動時に集合にしておき、参照の比較だけで外れを捨てる。ILで各ドライバーが正常完了時に `Succeeded` で終わることを確かめる。
- 実際の水の消費・生産量（`PlumbingNet.PullWater` など）は、呼び出しが多く、周期的な集計の設計が別に要るため、この計画では作らない（[既存の調査](../../research/2026-11-11-next-modded-statistics-backlog.md) P1 のまま）。

### M5 Vehicle Framework（`SmashPhil.VehicleFramework`）

**確認した根拠（公開GitHub、読み取りのみ）**
- `SmashPhil/Vehicle-Framework` の既定ブランチ `develop`、コミット `41e51adc160f1b91c9bf7df89db38122f91c0778`：About.xml の版 `1.7.2503 rev7834`、対応版 1.4〜1.7。
- `Source/Vehicles/World/Caravan/VehicleCaravan.cs`：`namespace Vehicles.World;`、`public class VehicleCaravan : Caravan, …`。`vehiclePather`（`VehicleCaravan_PathFollower`）、`Launch(…)`、`LaunchAerialVehicle()`、`Notify_MemberDied(Pawn)`、`Destroy()` がある。到着時の処理は `CaravanArrivalAction_StashedVehicle` などの到着アクション。
- 車両ポーンの型は `Source/Vehicles/Components/Vehicles/VehiclePawn/` にある（名前空間・基底型は未確認）。
- リポジトリにビルド済みDLLはない（README：リリース・配布物以外に含めない）。R2 の Workshop 索引にもない。

**候補の統計**

| 仮キー | 種類 | 意味 | 取得点 |
|---|---|---|---|
| `exploration.vehicles.owned` | 保存時点（内訳：VehicleDef） | プレイヤーの車両の数（拠点マップと車両キャラバン） | セーブ時、車両ポーンの型（名前で判定）を数える |
| （後段）`exploration.vehicles.trips` など | 累計 | 車両キャラバンの出発・到着、航空機の発進 | 経路・到着アクションの確定点（要調査） |

- 先に保存時点の数だけを作る。累計は、1.7 のDLLとソースの対応を確かめてから別のPRで判断する。
- **着手の前提**：所有者が 1.7 のビルド済みDLLを用意する（R2 の `Workshop/` に追加する、または GitHub のリリースから取得してよいと指示する）。DLLなしでは型名を確定できない。

### M6 Smart Apparel Upgrade（`pengin0503.smartapparelupgrade`）

**確認した根拠**
- 既存の内蔵連携：`mod.pengin0503.smartapparelupgrade.orders.issued`（自動着替えジョブの発行数。成功数ではない）。
- 公開ソース `pengin0503/rimworld-Smart-Apparel-Upgrade@5e910b82`（CI の固定コミット）の `Source/SmartApparelUpgrade.cs`：`ApparelScanner : MapComponent` が `OrderIdleUpgrades()` で `JobMaker.MakeJob(JobDefOf.Wear, choice.Better)` を発行し、`SwapStamp{PawnId, TargetId, RemovedId(s), LastIssued}` を `stamps`（セーブに保存）に記録する。着替え自体はバニラの `JobDefOf.Wear`。

**候補の統計**

| 仮キー | 種類 | 意味 | 取得点（候補） |
|---|---|---|---|
| `mod.pengin0503.smartapparelupgrade.swaps.completed` | 累計 | SAU が発行した着替えが実際に着用まで完了した回数 | バニラの着用処理 `RimWorld.Pawn_ApparelTracker.Wear(Apparel, bool, bool)`（1.7.4985 の監査済み索引で存在を確認）の後処理で、同じポーン・同じ服の新しい `SwapStamp` があるとき |

- 同じ指示を二重に数えないよう、イベントIDを「ポーンID＋服ID＋発行tick」にする（SDK の重複排除を使う）。
- 確認が必要：`stamps` の読み取り方法（反射）が版の違いで壊れたときに閉じること、手動の着替えを数えないこと。SAU は所有者のMODなので、必要ならSAU側に成功通知を足す選択肢もある（その場合は本計画の範囲外の変更として所有者が判断する）。

### M7 その他の候補（調査のみ）

| 対象 | 状況 | 次の一歩 |
|---|---|---|
| Vanilla Trading Expanded（`VanillaExpanded.VanillaTradingExpanded`） | R2 の DLL は 1.3〜1.6（1.7 フォルダなし）。銀行・融資は `TradeDeal` を通らない独自処理 | 1.7 での読み込みフォルダを確認し、預金・引き出し・融資の確定点を調べる |
| Rimefeller（`Dubwise.Rimefeller`） | R2 にDLL（ソースなし） | 1.7 版の有無とIL。実際の採掘量と容量の区別 |
| Vanilla Genetics Expanded・Rimatomics など | R2 になし | 必要になった時点で調査 |
| Vanilla Events Expanded・Vanilla Fishing Expanded | 汎用の MOD追加インシデント・釣りの統計で既に対象 | 追加の実装は不要。実ゲームで値が入るかだけ確認 |

## 4. 実装の共通手順（各Mの PR で行う）

1. 対象の版を固定する（公開ソースのコミット、または R2 のDLLのパスとサイズ）。調査結果を PR 本文に残す。
2. Core に純粋な集計ロジック（イベントの重複排除、内訳の上限、正の値だけの判定など）を書き、先にテストを書く。
3. ゲーム側に取得点（パッチまたは保存時の読み取り）を追加する。`MetricGates.IsOpen` に「対象の型があること」の条件を足す（既存の `ModdedIncidentFamily`・`OutpostSnapshot` と同じ形）。
4. 翻訳キーを日英で追加し、`tools/check_keys.sh` を通す。
5. 静的確認：公開ソースがあれば `tools/IntegratedModAudit/` に契約確認スクリプトを足し、CI で固定コミットを取得して実行する。DLLだけの場合は型・メソッド・引数をメタデータで確認する手順を PR に記録する。
6. RWMT（`pengin0503/RimWorld-for-mod-test`）の A+B：対象MODあり・なしで、読み込みの新しいエラーがないこと、ONのときだけ想定したパッチが登録されることを確かめる（R2 に対象MODがあるもの：VPE・VEF・VOE・Hospitality・DBH）。
7. 数値の正しさは所有者の実ゲーム確認（VerifyHarness と手動手順書）で行い、確認できるまで実験的のままにする。
