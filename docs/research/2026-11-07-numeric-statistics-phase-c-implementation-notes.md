# 数値統計拡張 Phase C0〜C4 の実装記録と開発者向け知見

[確定設計](../superpowers/specs/2026-11-07-numeric-statistics-expansion-design.md) · [確定台帳](../superpowers/specs/2026-11-07-numeric-statistics-expansion-metrics.md) · [取得根拠](2026-11-07-numeric-statistics-expansion-evidence.md) · [実装計画](../superpowers/plans/2026-11-07-numeric-statistics-expansion.md) · [Phase A・D1 の検証記録](2026-11-07-numeric-statistics-expansion-verification.md)

この文書は、Phase C（チェックポイント基盤 C0 と確定イベント C1〜C4）を実装した時点の状態、設計の要点、ゲーム本体の調査で分かった事実、使った手順、残作業をまとめたものです。次に作業する人が、同じ調査をやり直さずに続きから進められることを目的にしています。ゲームを起動できない環境（Linux コンテナ）で実装したため、**実機での検証は一切行っていません**。

## 1. 現在の状態

### 1.1 まとめ

| 範囲 | 状態 |
|---|---|
| C0 チェックポイント基盤 | 実装済み・自動テスト済み・実機未検証 |
| C1〜C4 の 16 取得単位 | 実装済み・自動テスト済み・実機未検証（台帳 C の 195 候補中 190 候補、188 出力） |
| C の保留 | 5 候補（N2-F-010、N1-A-008、N3-H-005、N3-H-011、Q-014。理由は §1.3） |
| Phase B（保存時点の 35 候補） | 未着手（取得単位の定義と翻訳カテゴリのみ） |
| Phase D2（Royalty・Ideology・Biotech・Odyssey） | 未着手（取得単位の定義のみ。Anomaly は対象外） |
| B/C に依存する順位（R-001/2/4/5/6/12、N1-F-001/002） | 未着手 |
| 開発専用の検証ハーネス | 未着手（設計は §7.3） |
| 通常利用への影響 | なし。すべての取得単位は `Unverified` で、ゲート閉・パッチ未適用・出力未公開。既存の Phase A/D1 の表示と集計は変化なし |

**実機で検証済み・通常取得が有効な取得単位はひとつもありません。** `AcquisitionUnits.All` の `Status` はすべて `Unverified` で、`NumericCatalogTests.NoUnitIsVerifiedWithoutAnInGameRun` がそれを固定しています。

### 1.2 取得単位ごとの状態（すべて「実装済み・自動テスト済み・実機未検証」）

| 単位 | 出力数 | 主なテスト | パッチしたゲームのメソッド（1.7.4985） |
|---|---|---|---|
| research | 5 | `NumericEventTests` | `ResearchManager.ResearchPerformed / AddProgress / ApplyKnowledge(ResearchProjectDef,float,out float) / FinishProject` |
| mental | 4 | `NumericEventTests` | `MentalBreaker.MentalBreakerTickInterval / TryDoRandomMoodCausedMentalBreak / TryDoMentalBreak`、`InspirationHandler.TryStartInspiration` |
| health_death | 22 | `NumericCombatPopulationTests` | `Pawn.Kill` |
| down | 4 | 同上 | `Pawn_HealthTracker.MakeDowned`（private） |
| membership | 14 | 同上 | `Pawn.SetFaction`、`GenGuest.EmancipateSlave / PrisonerRelease`、`PregnancyUtility.ApplyBirthOutcome`、`KidnappedPawnsTracker.Kidnap / RemoveKidnappedPawn`、`Pawn_GuestTracker.CapturedBy` |
| raid | 22 | 同上 | `IncidentWorker.TryExecute`、`IncidentWorker_Raid.TryGenerateRaidInfo`、`Lord.Notify_PawnLost`、`Game.DeinitAndRemoveMap` |
| lifetime | 4 | 同上 | `SkillRecord.Learn`（＋死亡・加入は上の共有パッチ） |
| social | 8 | 同上 | `Pawn_RelationsTracker.AddDirectRelation`、`SpouseRelationUtility.DoDivorce`、`Pawn_InteractionsTracker.StartSocialFight`、`PrisonBreakUtility.StartPrisonBreak`（5 引数版）、`RelationsUtility.TryDevelopBondRelation` |
| animals | 4 | 同上 | `InteractionWorker_RecruitAttempt.DoRecruit`（6 引数版）、`PawnUtility.TrySpawnHatchedOrBornPawn` |
| work | 32 | `NumericEconomyTests` | `QuestManager.Notify_PlantHarvested`、`Plant.PlantCollected / MakeLeafless`、`Pawn_RecordsTracker.Increment`、`RecordsUtility.Notify_BillDone`、`Frame.CompleteConstruction / FailConstruction`、`JobDriver_Deconstruct.FinishedRemoving`、`ListerBuildingsRepairable.Notify_BuildingRepaired`、`Mineable.TrySpawnYield`（private 3 引数版）＋`GenPlace.TryPlaceThing`（out なし 8 引数版）、`CompLaunchable.TryLaunch` |
| food | 2 | 同上 | `Thing.Ingested`、`CompRottable.TickInterval`（private） |
| trade | 15 | 同上 | `TradeDeal.TryExecute` |
| diplomacy | 5 | 同上 | `TradeDeal.TryExecute`（贈答）、`Faction.Notify_RelationKindChanged`、`FactionDialogMaker.CallForAid`（private）、`IncidentWorker.TryExecute` |
| medicine | 25 | `NumericWorldEventsTests` | `TendUtility.DoTend`、`Bill_Medical.Notify_IterationCompleted`、`Recipe_Surgery.CheckSurgeryFail`（protected）、`RestUtility.TuckIntoBed`、`ResurrectionUtility.TryResurrect`、`Pawn_HealthTracker.AddHediff`（Hediff 版）`/ RemoveHediff`、`HediffComp_GetsPermanent.PreFinalizeInjury / CompPostInjuryHeal` |
| disaster | 12 | 同上 | `GameConditionManager.RegisterCondition`、`GameCondition.End`、`IncidentWorker.TryExecute`、`Fire.TrySpread`、`FireUtility.TryStartFireIn`、`Thing.Kill` |
| caravan | 11 | 同上 | `CaravanExitMapUtility.ExitMapAndCreateCaravan`（PlanetTile 3 個の版）、`Caravan_PathFollower.TryEnterNextPathTile`（private）、`CaravanEnterMapUtility.Enter`（Func 版）、`Caravan.Notify_MemberDied`、`WorldObjectsHolder.Add / Remove`、`IncidentWorker.TryExecute`、`SettlementDefeatUtility.CheckDefeated`、`SettlementAbandonUtility.Abandon`（private）、`SettleInEmptyTileUtility.Settle`、`SettleInExistingMapUtility.Settle` |
| checkpoint（C0） | — | `NumericCheckpointTests` | `SafeSaver.Save`（Prefix・Finalizer）。既存 `SaveCompletedPatch` の Postfix で確定 |

すべてのパッチ対象（既存 10 件を含め 84 件）は `tools/check_patches.sh` で 1.7.4985 の監査索引と一致（型・オーバーロード・注入する引数名・`___field`）。`tools/refcheck.sh` も `type-not-found` 0・`signature-not-found` 0。**静的な一致であり、Harmony が実際に発火することの確認ではありません。**

### 1.3 保留した候補（実装不能または保留）

| 候補 | 技術的根拠 | 残作業 |
|---|---|---|
| N2-F-010 食料不足が起きなかった最長日数 | ホームマップの食用栄養の総量を連続して知る必要がある。確定通知だけでは保管間移動・腐敗・摂食・取引・建物内容物の全変化を拾えず、取得根拠の「セーブ間の補間や定期全Thing走査が必要なら無効」に該当 | 低コストの在庫変化通知が見つかれば再検討。B の保存時点の在庫（S-009/N1-A-005）とは別物 |
| N1-A-008 栽培面積あたりの平均収穫量 | 栽培ゾーンのセル数の時間積分と「ゾーン由来」の収穫の区別が必要。ゾーンの追加・削除・セル変更・マップ消滅をすべて確定通知で追う仕組みが未確立 | `Zone.AddCell/RemoveCell`・ゾーン登録解除を追う設計を検討。水耕槽（Building_PlantGrower）との区別も要る |
| N3-H-005 禁断症状の新規発症 | 1.7.4985 の禁断症状は依存症 Hediff の段階（stage）で、新しい Hediff が加わらない。段階変化の確定通知が無い | `Hediff_Addiction` の段階遷移を低コストに取れる経路を調べる |
| N3-H-011 平均入院期間 | 医療指定ベッドへの入床・退床の確定通知が見つからない（毎tickの `CurrentBed` 監視は禁止） | 入床は `RestUtility.TuckIntoBed` と就寝ジョブの開始、退床はジョブ終了で組めるかを調べる |
| Q-014 古代の脅威の開封 | 「封印解除の確定処理」に当たるゲームの通知が 1.7.4985 で特定できない（一般の壁破壊・敵出現では数えない条件） | 古代の祠・冷凍睡眠棺の開封経路の調査 |

## 2. C0 の設計（実装済みの形）

### 2.1 データの流れ

```text
ゲームのイベント（Harmony の Prefix/Postfix）
  └ GameFacts でゲームの状態を平易な値に読む（Pawn ID、所属、Def 名…）
      └ Core の XxxFamily.Method(FamilyState, 事実…) が判定して累計に足す（重複排除・状態遷移もここ）
SafeSaver.Save Prefix  → CheckpointSaveSession.Enter → LiveMetricAccumulator.PrepareSave（累計を深いコピー → JSON）
GameComponent.ExposeData（Saving）→ TextToWrite → Scribe 文字列 "numericStatistics" と dataVersion=2
SaveCompletedPatch Postfix → Complete（本体が走った・このセーブが書いた・ファイルがある時だけ確定）
  └ RecordSave: v1 の ApplyLiveObservation と CheckpointMetrics.ToObservation を同じ ProfileSession.Mutate で反映
SafeSaver.Save Finalizer → Exit（何が起きても範囲を閉じる。例外はそのまま返す）
ロード: ExposeData（LoadingVars）で文字列を読む → LoadedGame で LiveMetricAccumulator.Resume
  └ 読めたチェックポイントは同じ Mutate でプロファイルに冪等回収（確定し損ねたセーブも拾える）
取り込み: SaveReader 5 が numericStatistics を読み、同じ CheckpointMetrics.ToObservation で観測にする
```

主なファイル（Core）

| ファイル | 役割 |
|---|---|
| `Model/MetricCheckpoint.cs` | `CheckpointPayload`（formatVersion=1, checkpointId, parentCheckpointId, saveTick, savedUtc, families）、`FamilyState`（revision, sinceTick, throughTick, continuous, metrics, active）、`Accumulator`（N/Sum/Min/Max/MinKey/MaxKey/By/Overflow/First/Last）。ファミリー単位で読めないものは原文のまま保持 |
| `Stats/CheckpointJson.cs` | 圧縮 JSON の読み書きと内容ハッシュ（UTF-8 の SHA-256）。formatVersion 1 以外のオブジェクトは `UnknownFormat`、それ以外の壊れ方は `Invalid` |
| `Stats/FamilyOps.cs` | 累計の操作（Count / CountBy / Observe / Distinct / Mark / Gap / Track / Untrack）。キー付き集合は 4096 キーで頭打ちにし Overflow を立てる |
| `Stats/LiveMetricAccumulator.cs` | 1 ゲームの累計。新規開始・再開（連続性と改訂番号の規則）・停止・欠落（MarkGap）・保存草稿・確定 |
| `Stats/CheckpointSaveSession.cs` | 保存プロトコルの状態機械（Harmony から切り離してテスト可能にしたもの） |
| `Stats/CheckpointSelector.cs` | 代表チェックポイント（tick 降順 → 保存 UTC 降順 → ID 昇順）と系譜（None/Linear/Branched/Unknown） |
| `Stats/LiveFamilies.cs` | ファミリーの登録、`FamilyOutputContext`（出力の形の共通部品）、`CheckpointMetrics.ToObservation` |
| `Stats/LiveCatalog.cs` | B/C/D2 の出力定義（キー・台帳 ID・単位・集計・翻訳キー・取得単位） |
| `Stats/AcquisitionUnits.cs` | 取得単位の一覧と検証状態。**ここで `Verified` にした単位だけが公開され、ゲートが既定で開く** |
| `Stats/Families/*.cs` | 各ファミリーの判定と出力 |

主なファイル（Mod）

| ファイル | 役割 |
|---|---|
| `Metrics/MetricGates.cs` | ゲート。開く条件は「Verified または検証ハーネスが開いた」「DLC が有効」「パッチ適用済み」「この起動中に失敗していない」 |
| `Metrics/MetricAdapters.cs` | `[MetricAdapter("単位"...)]` の付いたパッチクラスを、開いている単位のものだけ `[StaticConstructorOnStartup]` で適用。どの単位も開いていなければ何もパッチしない。家族がひとつでも記録する時だけ `CheckpointSavePatch` を適用 |
| `Metrics/LiveMetrics.cs` | 現在のゲームの累計への入口。別のゲーム・セットアップ中・ゲームスレッド外のイベントは受け付けない（スレッド外は欠落として continuous=false） |
| `Metrics/FamilyStartup.cs` | 記録開始時の基準化（加入の既知ロスター、新規ゲームでの在籍・技能の基準、進行中のゲーム条件） |
| `Patches/CheckpointSavePatch.cs` | SafeSaver の Prefix/Finalizer |
| `Patches/Metrics/*.cs` | 取得アダプター。`GameFacts.Record(family, handler)` で例外を捕まえ、その単位だけ閉じる |

### 2.2 守っている不変条件（テストで固定）

- P→A（+2）／P 再ロード→B（+1）で、代表は tick の大きい方の 5 または 4。9（A+B）も 6 も出ない（`Branches_ShowOneLineageNeverASum`）。
- 同じ checkpointId・同じ内容（ハッシュ）は冪等。コピーや再取り込みは別名ファイルが増えるだけ。同じ ID で内容が違えば `Conflicted` にして使わず、系譜は Unknown。
- 後の tick の確認済みの子を持つ祖先は `Compressed`（値を捨てて系譜のノードだけ残す）。同じ tick の子では圧縮しない。順不同の取り込みでも同じ結果。
- 保存失敗・原処理スキップ・ファイルなし・ExposeData 未到達は確定しない。後続の他 MOD の Postfix が例外を出しても確定済みは戻さない。入れ子の保存や別スレッドの保存は、前回の確定済み文字列をそのまま書き、確定しない。
- 記録するファミリーがないときは何も書かない。ロードしたデータがあれば、そのまま書き戻す（データを失わない）。未知の formatVersion・壊れた JSON も原文のまま書き戻し、その上に新しく記録しない。
- 再開時、ファミリーが「ロードしたセーブの tick まで記録していた」場合だけ連続。そうでなければ continuous=false（以後ずっと部分）にし、開いていた区間（Active）は捨てる。累計は残すが、記録していない期間をまたぐ完結標本は作らない。取りこぼし（MarkGap）も同じ。改訂番号が古ければ作り直し、新しければ触らずに保持。
- DLC の出力で、観測の DLC 一覧にその DLC がなく値が何も記録されていない（0、0÷0、対象外）ものは対象外として母数から外す。DLC が有効だった間に記録した値は表示する。
- キー上限（4096）に達した集合に依存する出力は部分（「標本なし」も部分）にする（加入ファミリーでは `MarkPartial`）。
- 完全（Complete）の条件は「sinceTick=0（ゲーム開始から）、continuous、throughTick=このセーブの tick」。それ以外は部分（Partial）。「最初の○○までの日数」は開始から記録した場合だけ値を持つ。
- 区間がない平均・極値（標本 0）は NotApplicable、0÷0 の率は分子・分母 0 の既知値（Phase A の規約と同じ）。

### 2.3 公開とゲートの仕組み

- `MetricRegistry.Catalog` は実装済みの全出力、`MetricRegistry.All` は公開分だけ（`Published = AcquisitionUnits.IsVerified(Acquisition)`）。詳細タブは `All` だけを使うので、未検証の出力は表示されない。
- 単位を公開する手順: 実機で取得根拠の陽性・陰性試験を通す → `AcquisitionUnits.All` のその単位を `Verified` にする → それだけでゲートが既定で開き、出力が公開される。テスト `NoUnitIsVerifiedWithoutAnInGameRun` は意図して失敗するので、検証記録を残したうえで更新する。
- 開発用に開く: 検証ハーネス MOD がコンストラクタで `MetricGates.OpenForVerification(ids, caller)` を呼ぶ（`[StaticConstructorOnStartup]` より前に走る）。警告ログが必ず出る。`-savedatafolder` で通常の記録と分けること。
- DLC: `AcquisitionUnit.RequiredDlc` が無効ならゲートは閉。出力定義の `RequiredDlc` は「値がなく、観測の DLC 一覧にその DLC がない」時だけ対象外にする（過去に記録した値は DLC が今無効でも表示する設計）。

### 2.4 新しいファミリー・出力を足す手順

1. 台帳の節名を ID として `AcquisitionUnits.All` に単位を追加（Family / Snapshot / Derived、必要な DLC）。
2. `Core/Stats/Families/` に `XxxFamily`（`Name`、`Revision`、判定メソッド、`Build`）を作り、`LiveFamilies.All` に登録。判定はできるだけ Core に置き、ゲーム側は事実を読むだけにする。
3. テストを先に書く（同じ形のテストファイルが `Numeric*Tests.cs` に 4 つある）。重複通知・失敗・対象外・ロード後・上限超過を陰性ケースに入れる。変異テスト（条件を壊して失敗するか）で効き目を確かめる。
4. `LiveCatalog.Definitions` に出力定義を追加し、`RWS_Metric_*`/`RWS_MetricDesc_*` を日英の `Languages/*/Keyed/RimworldStatistics.xml` に追加（**公開ワークフローがこのファイル名を確認しているので、別ファイルに分けない**）。
5. `Source/RimworldStatistics/Patches/Metrics/` にアダプターを書く。クラスに `[MetricAdapter("単位", ...)]` と `[HarmonyPatch(typeof(T), "名前"[, 引数型[, ArgumentType]])]`。本体は `GameFacts.Record` か try/catch→`LiveMetrics.Fail`。毎tick呼ばれるメソッドは Prefix をフィールド読みだけにする。
6. `tools/check_patches.sh`・`tools/refcheck.sh`・`tools/check_keys.sh`・両方のビルド・テストを通す。

## 3. ゲーム本体（1.7.4985 rev1269）の調査で分かったこと

逆コンパイルは手元の `Assembly-CSharp.dll`（SHA-256 `0be7a7ea…`、監査済み索引と一致）を ilspycmd 11.2 で展開したもので、リポジトリには入れていません（§5）。以下は実装の根拠にした事実です。

### 3.1 保存・ロード・開始

- すべての通常保存（手動・オートセーブ・パーマデスの保存して終了）は `LongEventHandler.QueueLongEvent(..., doAsynchronously: false)` から `GameDataSaveLoader.SaveGame` → `SafeSaver.Save(path, "savegame", action, leaveOldFile: permadeathMode)`。**メインスレッド**で走る。パーマデスでは `.old` を残す。
- `SafeSaver.Save` は新規なら直接、既存なら `.new` に書いて `.old` へ退避→置換→（leaveOldFile でなければ）`.old` を削除。最後の削除に失敗すると例外が出るが、ファイルは置換済み（Postfix は走らない）。この場合のチェックポイントは取り込みかロードで回収される。
- 「保存して終了」は `SaveGame` の直後に `MemoryUtility.ClearAllMapsAndWorld` するが、Postfix はその前に走る。
- `Game.InitNewGame` の順序: マップ生成 → `FinalizeInit`（GameComponent.FinalizeInit）→ `Scenario.PostGameStart` → `history.FinalizeInit` → `ResearchUtility.ApplyPlayerStartingResearch` → `GameComponentUtility.StartedNewGame`。**初期研究・シナリオの初期処理は StartedNewGame より前**なので、StartedNewGame から受け付ければ自然に除外できる。
- ロード: `FinalizeInit` → `GameComponentUtility.LoadedGame`（各コンポーネントを個別に try/catch）→ `BackCompatibility.PostLoadSavegame`。LoadedGame は長時間イベントの裏スレッド。
- `Scribe_Values.Look<string>` は値も既定値も null なら何も書かない。dataVersion は既定値 1 と同じなら書かれない。

### 3.2 研究

- `ResearchPerformed` と `AddProgress` は進捗を更新してから `IsFinished` を見て `FinishProject` を呼ぶ。`FinishProject` だけでは「完了前」が分からない（既に完了扱い）。
- `FinishProject` は未完了の前提を再帰的に `FinishProject` する。最後に `currentProj == proj` なら `currentProj = null` にするので、外側の Postfix で対象を知るには Prefix で保持する必要がある。
- `GetProgress` は辞書に無い研究を 0 で**追加する副作用**がある。アダプターは `___progress` / `___anomalyKnowledge` を直接読む。
- `ApplyKnowledge` は 2 オーバーロード（カテゴリ版がプロジェクト版を呼ぶ）。プロジェクト版は `out float remainder` で、パッチには `ArgumentType.Out` の指定が要る。

### 3.3 精神・ひらめき

- `MentalBreaker.TryDoMentalBreak` の呼び出し元は心情（`TryDoRandomMoodCausedMentalBreak`）以外に、サイロシビン、書籍、キューブ、不自然な死体、血の怒り・狂乱誘発（`AlwaysBerserkUtility`）がある。心情起因だけを数えるには呼出しの範囲を見る必要がある。
- 開発ツール「(natural mood break)」は `TryDoRandomMoodCausedMentalBreak` を直接呼ぶ。通常は `MentalBreakerTickInterval` から呼ばれるので、その範囲内に限ると開発ツールを除外できる。
- ひらめきの開発ツールは理由文字列 `"Debug gain"` を渡す（`DebugToolsPawns`）。

### 3.4 死亡・ダウン・襲撃

- `Pawn.Kill` は内部で `DoKillSideEffects` → `PreDeathPawnModifications` → `DropBeforeDying` → `health.SetDead()` の順。死亡前の状態は Prefix で読む。狩猟指定（`DesignationDefOf.Hunt`）・屠殺指定（`ShouldBeSlaughtered()`）もここで分かる。
- `Pawn_HealthTracker.MakeDowned` は private。中で `Lord.Notify_PawnLost(Incapped)` を呼ぶ。
- `Lord.Notify_PawnLost` は `curJob.ShouldRemovePawn` が false なら**何もしない（ポーンはロードに残る）**。Postfix で `ownedPawns` に残っていないことを確認する必要がある。条件は `PawnLostCondition`（Killed・ExitedMap・LeftVoluntarily・MadePrisoner・MadeSlave・Incapped ほか）。
- 1.7.4985 の Def: 包囲 `Siege`、壁破壊 `ImmediateAttackBreaching`・`ImmediateAttackBreachingSmart`、工兵は `ImmediateAttackSappers`（別扱い）、降下 `EdgeDrop`・`EdgeDropGroups`・`CenterDrop`・`RandomDrop`、虫 `Infestation`・`DeepDrillInfestation`（`Infestation_Jelly` は虫ではない。Biotech の `WastepackInfestation` は休眠繭で除外した）、メカクラスター `MechCluster`（Royalty）、人狩り `ManhunterPack`（`ManhunterAmbush` はキャラバン待ち伏せ）。
- 友軍は `IncidentWorker_RaidFriendly`。軍事支援の要請は `FactionDialogMaker.CallForAid`（private static）が友好度を下げてから友軍の incident を実行する。

### 3.5 加入・誘拐・関係

- 誘拐された入植者は**プレイヤー勢力のまま**、誘拐した勢力の `kidnapped` リストに入る。リストから外れるのは身代金の手紙（`ChoiceLetter_RansomDemand` → `RemoveKidnappedPawn`）、MTB で誘拐側に寝返る（`SetFaction` してから除去）、消滅のどれか。救出経路として確認できたのは身代金だけ。
- 奴隷の解放は `GenGuest.EmancipateSlave`（勢力は変わらない）。`SlaveRelease` は釈放（去る）。`PrisonerRelease` は奴隷勢力の調整をしてから `GuestRelease`。
- 捕縛は `Pawn_GuestTracker.CapturedBy` → `SetGuestStatus(Prisoner)`。
- `Pawn_RelationsTracker.AddDirectRelation` は既存なら警告して何もしない。反射的な関係（配偶者・恋人）は 1 回の呼出しで両側に追加する。
- `SpouseRelationUtility.DoDivorce` は配偶者を外して元配偶者を足す（離婚専用の経路）。
- 出生は `PregnancyUtility.ApplyBirthOutcome` が赤子を生成してプレイヤー側の勢力で生む（死産は `forceDead`）。動物の出生・孵化は `PawnUtility.TrySpawnHatchedOrBornPawn`。
- `IsColonist` は「プレイヤー勢力・人型・（奴隷でないか奴隷が安定）」。`IsFreeColonist` は IsColonist かつ HostFaction なし（安定した奴隷を含む）。`IsFreeNonSlaveColonist` で奴隷を除く。本実装は「自由入植者」に IsFreeColonist（Phase A の FreeColonists 系列と同じ）、「加入者」に IsFreeNonSlaveColonist を使った。

### 3.6 生産・食料・交易

- 収穫は `JobDriver_PlantWork` の作業トイル（ラムダ）内で、成功時に `Find.QuestManager.Notify_PlantHarvested(actor, product)`。元の植物はそのジョブの `targetA`。失敗時はモート `TextMote_HarvestFailed` を出すだけで呼び出しがない。その後どちらも `Plant.PlantCollected` に進む。
- 植え付けは種まきトイルの完了時に `actor.records.Increment(RecordDefOf.PlantsSown)`。
- 製作は `Toils_Recipe` から `RecordsUtility.Notify_BillDone(actor, products)` の 1 か所。
- 採掘の産出は private `Mineable.TrySpawnYield(Map, bool, Pawn)` が `GenPlace.TryPlaceThing(thing, ..., ForbidIfNecessary)` で置く（戻り値がない）。置く直前の `stackCount` が産出量。
- 作物の寒害死は `Plant.MakeLeafless(LeaflessCause.Cold)`（他に Poison・Pollution・NoPollution）。`dieIfLeafless` の植物はここで壊れる。
- 腐敗の消失は `CompRottable` の private `TickInterval`（`CompTickRare` と `CompTickInterval` の両方から）で `parent.Destroy()`。
- `TradeDeal.TryExecute` は明細を処理した後 `Reset()` するので、Postfix では明細が消えている。贈答モードは別分岐（`FactionGiftUtility.GiveGift`）。
- 関係の種類の変化は `Faction.Notify_RelationKindChanged` が**どちら側の勢力にも**呼ばれ得る（直接設定では両側）。
- `CompLaunchable.TransportersInGroup` と `Transporter` は protected。外からは `parent.TryGetComp<CompTransporter>().TransportersInGroup(map)`。

### 3.7 医療・災害・遠征

- 手術の成否は protected の `Recipe_Surgery.CheckSurgeryFail`（true が失敗）。派生レシピの `ApplyOnPawn` から呼ばれる。手術の範囲は `Bill_Medical.Notify_IterationCompleted`。
- 救助は `JobDriver_TakeToBed` → `Toils_Bed.TuckIntoBed` → `RestUtility.TuckIntoBed(bed, taker, takee, rescued)`。
- 傷の永久化は `HediffComp_GetsPermanent.PreFinalizeInjury`（繊細な部位は即時）と `CompPostInjuryHeal`（閾値到達）の 2 か所で `IsPermanent = true`。
- 延焼は `Fire.TrySpread` → `FireUtility.TryStartFireIn(…, 0.1f, instigator)`。
- `SettleUtility.AddNewHome` の呼び出し元にはグラヴシップ（`GravshipUtility` 2 か所）と移住（`MoveColonyUtility`）がある。新規の拠点設立はキャラバンからの `SettleInEmptyTileUtility.Settle` と `SettleInExistingMapUtility.Settle` に限定した。
- 拠点の任意放棄は private `SettlementAbandonUtility.Abandon(MapParent)` が `Abandon(wasGravshipLaunch: false)` を呼ぶ。
- キャラバンの出発は `CaravanExitMapUtility.ExitMapAndCreateCaravan`（呼び出し元: 隊列作成ダイアログ、キャラバン編成ロード、1 人での退出、一時マップの強制退出、開発ツール）。`CaravanMaker.MakeCaravan` は分割・会合・瞬間移動・輸送ポッド到着でも呼ばれる。

## 4. 実装上の判断（Ruling）

すべて進捗台帳にも記録した判断です。「誤りだった場合の損失」を併記します。

1. B/C/D2 の全取得単位を未検証として閉じ、出力を公開しない（利用者の指示が計画の「最初の成功出力を登録」に優先）— 損失: 検証まで利用者には何も見えない。
2. 既存テスト `ReaderVersion_IsFour` を `ReaderVersion_IsAtLeastFour` に変更（計画が reader=5 を指定。5 は新テストで固定）— 損失: なし。
3. チェックポイント用の SafeSaver パッチ自体もアダプターにし、記録するファミリーがある時だけ適用 — 損失: なし（閉じている間）。
4. 後の確認済みの子を持つ祖先チェックポイントの値を捨てる（圧縮）。子が後で衝突扱いになったら祖先の圧縮を解き、その内容を次に読んだとき（取り込み・ロード時の回収）に値を戻す（Codex レビューで修正）— 損失: 祖先のファイルが失われていると値は戻らない。
5. 標本のない期間・極値は NotApplicable（Phase A のクエスト所要日数と同じ）— 損失: 表示の言い方だけ。
6. 心情起因の精神崩壊は `MentalBreakerTickInterval` の範囲内に限る（開発ツール除外のため、ポーンのティック間隔ごとに Prefix/Finalizer 1 組）— 損失: 単位を開いた時のわずかな負荷。
7. C2 以降のファミリーは「型が無いことによるコンパイル失敗」を RED とし、実装後に条件を壊す変異テスト（C2 で 11、C3 で 7、C4 で 5）で検出力を確認。生き残った変異は新ケースを足して潰した — 損失: 変異を当てていない分岐に誤りが残る可能性。
8. 加入・在籍は IsFreeNonSlaveColonist、死亡・ダウンの「自由入植者」は IsFreeColonist — 損失: 一部の出力で安定した奴隷の扱いが期待と違う可能性。
9. 1 襲撃の撃破数（B-016）は襲撃の継続中だけ数え、ダウンした襲撃者は戦闘離脱扱い（結末が「その他」になり得る）— 損失: 襲撃後の処刑が数えられない。
10. 誘拐からの帰還は身代金（`RemoveKidnappedPawn`）だけを確認済み経路として記録 — 損失: 他の経路の救出が数えられない。
11. W-001 の収穫は作物（`Plant.IsCrop`）だけ。栽培ゾーン外の伐採は含めない — 損失: 野生の木の伐採が合計に入らない。
12. W-008 の解体は建て替えのための解体（`JobDriver_DeconstructForBlueprint`）を除く — 損失: 建て替えが数えられない。
13. 保留 5 候補（§1.3）。

## 5. 開発環境と手順

### 5.1 環境の準備（Linux コンテナで確認した手順）

```bash
bash dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet   # 10.0.403 が入る
python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install boto3
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py refs .refs          # 1.7.4985 rev1269 の DLL と Harmony
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py audit .devinputs/audit
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py saves .devinputs/saves   # 実セーブ 49 件（約 825 MB）
```

`R2_ENDPOINT`・`R2_BUCKET`・`AWS_ACCESS_KEY_ID`・`AWS_SECRET_ACCESS_KEY` が環境にある前提。`.refs/`・`.devinputs/` は git の対象外。

### 5.2 ゲーム本体のコードを読む（コミットしない）

```bash
dotnet tool install -g ilspycmd          # 11.2.0 で確認。DOTNET_ROLL_FORWARD=Major が要る場合がある
ilspycmd -p -o <作業用の外部フォルダ> -r .refs/game .refs/game/Assembly-CSharp.dll   # 約 9,255 ファイル、数分
```

逆コンパイル結果は**リポジトリの外**（作業用のスクラッチ領域）に置き、コミットしない。監査資料 `research/vanilla-1.7.4985-audited/evidence/decompiled/` には 60 型分しかないので、それ以外はこの手順で読む。

Def（XML）は R2 の `Data/<Core|Royalty|Ideology|Biotech|Odyssey>/Defs/` にある（約 1,364 ファイル、16 MB。Anomaly は無い）。IncidentDef・RaidStrategyDef・PawnsArrivalModeDef などの固定表はここから確定した。

監査索引でメソッドの完全署名を引くには、`.devinputs/audit/assembly/Assembly-CSharp/chunk-*.json.gz` の `entries[].methods[]`（`name`、`signature.parameters`、`parameters[].name`、`attributes`）を読む。by-ref は `float32&`、ジェネリックは `[System.Collections]System.Collections.Generic.List`1<[Assembly-CSharp]Verse.Pawn>` のように書かれている。

### 5.3 検査ツールの変更点

- `tools/PatchTargetCheck`: `[HarmonyPatch(typeof(T), "m", Type[], ArgumentType[])]` の 4 番目の引数（Ref/Out を `&` に）、ジェネリック引数（多引数・入れ子を含む）、BCL の参照アセンブリの違い（`[System.Runtime]` と `[System.Collections]` 等）を同一視するようにした。`MethodType.Getter/Setter` には対応していない（プロパティの setter を狙う代わりに、値を変える側のメソッドをパッチした）。
- `tools/check_keys.sh`: 言語ごとに `Keyed/*.xml` をまとめて読み、重複キーも報告する。
- `tools/package.sh` と公開ワークフロー（`.github/workflows/publish-v1.0.0.yml`）は**変更していない**。ワークフローは zip 内の `Languages/*/Keyed/RimworldStatistics.xml` の名前を確認しているので、翻訳キーはこのファイルに足すこと。

### 5.4 確認コマンド（この PR の時点の結果）

| コマンド | 結果 |
|---|---|
| `RWSTATS_REAL_SAVES_DIR=$PWD/.devinputs/saves dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release -p:UseNuGetRefs=true` | 377 件すべて成功（既存 289 ＋ 新規 88。実セーブ 49 件の照合 2 件を含む。Codex レビューの指摘 4 件の回帰テストを含む） |
| `dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release -p:UseNuGetRefs=true` と実 DLL 参照のビルド | 警告 0、エラー 0 |
| `bash tools/check_patches.sh` | パッチ対象 84 件、失敗 0 |
| `bash tools/refcheck.sh` | `type-not-found` 0、`signature-not-found` 0 |
| `bash tools/check_keys.sh` | 翻訳キー 655 件、日英とも揃っている |

## 6. 実機で確かめること（単位ごと）

共通: 対象 PC の `Version.txt` と `Assembly-CSharp.dll` の SHA-256 を記録し、`check_patches.sh`・`refcheck.sh` をその DLL でやり直す。新しい合成コロニーで、陽性・陰性に加えて対象外イベント・二重通知・ロード直後・対応しない DLC/Def を試す。`Player.log` に `[RimworldStatistics]` のエラーが無いこと、閉じた単位は何もパッチされないこと（Dubs Performance Analyzer などで確認）。

| 単位 | 最低限の陽性・陰性 |
|---|---|
| C0 | 保存→成功で親が進む。保存失敗→次の保存に全部入る。P をロード→A と B で 5/4。MOD なしのセーブの初回、パーマデス、オートセーブ、保存して終了、RimSaves 併用、他 MOD の後続 Postfix だけが例外を出す場合 |
| research | しきい値をまたぐ通常研究、直接完了（開発ツール）、前提の自動完了、初期研究（数えない）、切替・再開 |
| mental | 心情起因の成功・失敗、開発ツール「(natural mood break)」（数えない）、サイロシビン（数えない）、ひらめきの成功・失敗・"Debug gain"、奴隷・囚人・襲撃者 |
| health_death / down | 自軍の銃・近接・素手・爆発・火、タレット・罠・家畜・メカ、敵同士、出血死（帰属不明）、蘇生後の再死亡、ダウン→回復→ダウン、即死 |
| membership / lifetime | 囚人の勧誘、放浪者の加入、離脱→再加入、奴隷の解放、出生と死産、誘拐→身代金、スキル 19→20→19→20 |
| raid | 通常・包囲・壁破壊・降下・同時 2 件、退却・全滅・捕虜あり、マップ消滅、虫・メカクラスター・人狩り |
| social / animals | 結婚（両側通知で 1）、離婚と死別、喧嘩、脱獄、釈放、捕縛、絆、調教と購入、屠殺と戦闘死、狩猟指定と偶発 |
| work / food | スタック 10 と複数成果、品質、一括収穫、収穫失敗、寒害、採掘、建築と中止、解体、修理、ポッド、腐敗と焼失、摂食 |
| trade / diplomacy | 売買同時、空取引、贈答と上限、商船・訪問商隊・訪問先拠点、敵対→中立、同盟→敵対、支援要請と到着 |
| medicine | 1 回の治療で 2 患部、手術の成功・失敗・中止、義肢装着、救助、蘇生、病気の治癒と死亡時の除去、切断と子部位、傷痕 |
| disaster | 2 マップで重なる死の灰、太陽フレア、ロード時に進行中の条件（数えない）、延焼（数えない）、火災による建物の破壊 |
| caravan | 往復、休憩、分割・合流、待ち伏せ、瞬間移動、全滅、拠点の攻略・放棄・設立、グラヴシップ（数えない） |

性能: 受け入れ計測 3（60 秒×5 回の中央値で TPS 低下 2% 以下）。特に毎tick級の `SkillRecord.Learn`、`MentalBreakerTickInterval`、`CompRottable.TickInterval`、`GenPlace.TryPlaceThing`、`Pawn_RecordsTracker.Increment`、`Thing.Kill` は、その単位を開いたときの負荷を測る。

## 7. 残作業（推奨順）

### 7.1 Phase B（保存時点の 35 候補）

- 取得単位は `AcquisitionUnits` に定義済み（research_state・factions・roster・facilities・stock・colony_start、DLC の royalty_state・biotech_state）。
- 入口: `SaveCompletedPatch` → `RecordSave` の同じ Mutate に `SaveSnapshot` 観測（キーは保存ごとの GUID）を足す。ゲーム状態の読み取りは保存成功直後のメインスレッドで 1 回、必要なリストを一巡するだけにする（取得根拠 #roster・#stock・#facilities・#research-state・#factions）。
- **プロファイルの肥大化に注意**: 「最大観測」系（S-016、S-021…）を保存ごとに 1 観測ずつ残すと際限なく増える。同じ campaignId の既存観測の極値を新しい観測に引き継ぎ（観測横断の最大・最小は不変）、tick が後の観測が代表になる古い観測を枝刈りする設計を推奨（`ProfileMutations.Supersedes` の SaveFile 版と同じ考え方）。
- 屋外気温の最小・最大（N4-C-001/002）は季節で上下するので、引き継ぎがないと枝刈りできない。

### 7.2 Phase D2 と依存順位

- Royalty: `Pawn_RoyaltyTracker.SetTitle` の上昇、名誉の増減、許可証の実使用、超能力の発動、最高爵位・サイリンクは保存時点、帝国クエストは保存クエストの root の固定許可表（R2 の `Data/Royalty/Defs/QuestScriptDefs` から作れる）。
- Ideology: `Pawn_IdeoTracker.SetIdeo`、`LordJob_Ritual.ApplyOutcome`、`Precept_Role.Assign`。
- Biotech: 出生（P-003 と共用、L-014 は出産者）、メカ製造の個体成立、修理の全快、廃棄物の生成・無害化、汚染セル・帯域は保存時点。
- Odyssey: グラヴシップの離陸→着陸の同一飛行、距離、場所、新マップ着陸。`GravshipUtility`（`AddNewHome` を呼ぶ 2 か所がある）から調べる。
- 依存順位（R-001/2/4/5/6/12）は、入力の単位が公開されたときに `MetricRankings` に足す。N1-F-001/002 は開始タイルのバイオームを `colony_start` スナップショットで取る設計。

### 7.3 開発専用の検証ハーネス（未着手、設計案）

- 通常の配布物（`tools/package.sh` の zip）に入らない別プロジェクト（例: `tools/VerifyHarness/`）で、`packageId` も別の MOD にする。本体の後にロード。
- コンストラクタで `MetricGates.OpenForVerification(<検証する単位>, "RWS Verify")` を呼ぶ。
- 開発モードの DebugAction から、テスト用コロニーでイベントを再現（研究の完了、精神崩壊、襲撃の発生など）し、`LiveMetrics.Accumulator` の家族の累計と `LiveMetrics.HitCounts()` を読み、期待値と比べる。
- ログ形式の案: `[RWS-VERIFY] case=<ID> unit=<単位> game=<VersionControl.CurrentVersionStringWithRev> expected=<…> actual=<…> hooks=<発火数> result=PASS|FAIL`。
- 保存→ロードをまたぐケース（P→A/B）は、テスト用の `-savedatafolder` に状態ファイルを置いてロード後に続きを実行する。
- 起動例: `RimWorldLinux -savedatafolder=<テスト用フォルダ>`（Windows は `RimWorldWin64.exe -savedatafolder=…`）。

### 7.4 その他

- 詳細タブで、コロニー別にチェックポイントの系譜（Branched/Unknown）を表示する（`NumericColony.Lineage` は用意済み、UI 未対応）。
- 最終の全体コードレビュー（この PR では C0〜C4 の各段階で自己確認したのみ。独立したレビューは未実施）。
- 実機検証の結果に応じて `AcquisitionUnits` を `Verified` にし、`docs/manual-test-checklist.md` と検証記録を更新する。
