# RimWorld 1.7 数値統計の取得根拠と検証条件

[確定台帳](../superpowers/specs/2026-11-07-numeric-statistics-expansion-metrics.md) · [設計](../superpowers/specs/2026-11-07-numeric-statistics-expansion-design.md) · [従来の実セーブ調査](2026-11-06-rimworld-1.7-feasibility.md)

- 対象MODの基準は `f8de0ebdb26b53eaeb3fef85610603b1b075ff6c`。ゲームの静的根拠は 1.7.4985 rev1269 のローカル監査済み Def と [同版の逆コンパイル結果 d5cbe118](https://github.com/codexvn/RimWorldDecompiled/tree/d5cbe1186bbb9962b7bd4913c900441c6fe4a4ff/Assembly-CSharp)。既存調査の49件は新規の全系列・拡張フックの動作試験には使われていない。今回は元の49ファイルを取得できなかったため、既存調査と静的照合の結果を分ける。
- [RimRef 1.7.4995](https://www.nuget.org/packages/Krafs.Rimworld.Ref/1.7.4995-unstable) と [1.7.5000](https://www.nuget.org/packages/Krafs.Rimworld.Ref/1.7.5000-unstable) を1.7.4985の同パッケージと比較した。下記36メソッド名について全オーバーロードのECMA-335署名（型トークンを名前に正規化）、パラメーター名・フラグが3版で一致。参照パッケージはメソッド本体や Waylith 上の Harmony 発火を確認できない。対応判定は対象PCの `Version.txt` と実体 `Assembly-CSharp.dll` の SHA-256 を記録し、パッチの型・引数・戻り型の照合、発火確認、下記陽性・陰性試験を行う。
- **Bの既定取得は稼働中の正規状態を正常セーブ時に要約する方法。** 下記に旧セーブの対応を明示した項目以外、旧データは未記録。XMLに一部のフィールドがあるだけで完全な状態と判定しない。
- **C/Dのライブ項目は追加計測前のイベントを復元しない。** Tale、Archive、HistoryEvent、個人 Records の断片を合算しない。個人Recordsは所属期間・削除Pawn・MODの加算経路が統一されておらず、全コロニー累計の裏づけにならない。
- 「候補」は静的入口または探索範囲。実体が未確認の入口にパッチを当てない。合否はファミリー一括でなく出力ごと。説明どおりの観測値を得られない出力は取得を無効のままにし、同ファミリーで確認できた出力を独立に進める。既定の陽性・陰性ペアに加え、各出力に対象外イベント、二重通知、ロード直後、対応しないDLC/Defを試す。試験には新規の合成コロニーを使用する。

## 版差の静的比較

ローカル監査のゲーム実体 SHA-256（1.7.4985 rev1269）は `0be7a7ea4b3111d4430f45b36c234a5e43287880b0514b23915f2ef9a0629e16`。署名比較は各RimRefの `ref/net10.0/Assembly-CSharp.dll`。オーバーロード数は `Pawn_HealthTracker.AddHediff`、`ResearchManager.ApplyKnowledge` が各版2、その他は各版1。

| 分野 | 署名一致を確認した型とメソッド名 |
|---|---|
| 保存・クエスト | SafeSaver.Save、Quest.ExposeData / End / Accept |
| 人口・医療 | Pawn.Kill / SetFaction、Pawn_HealthTracker.AddHediff / MakeDowned、TendUtility.DoTend、ResurrectionUtility.TryResurrect、Thing.Ingested |
| 心情・技能 | MentalBreaker.TryDoMentalBreak、MentalStateHandler.TryStartMentalState、InspirationHandler.TryStartInspiration、Pawn_InteractionsTracker.StartSocialFight、SkillRecord.Learn |
| 事件・災害 | IncidentWorker.TryExecute、IncidentWorker_Raid.TryGenerateRaidInfo、GameConditionManager.RegisterCondition、GameCondition.End |
| 研究・仕事 | ResearchManager.FinishProject / ResearchPerformed / AddProgress / ApplyKnowledge、QuestManager.Notify_PlantHarvested、RecordsUtility.Notify_BillDone、Frame.CompleteConstruction |
| 交易・勢力 | TradeDeal.TryExecute、Tradeable.ResolveTrade、Faction.TryAffectGoodwillWith |
| 遠征 | CaravanMaker.MakeCaravan、Caravan_PathFollower.TryEnterNextPathTile、WorldObjectsHolder.Add / Remove |
| DLC | Pawn_RoyaltyTracker.SetTitle、Pawn_IdeoTracker.SetIdeo |

比較に用いた参照DLLの SHA-256 は次のとおり。比較方法は Python dnfile 0.18.0 でTypeDef/MethodDefを探索し、署名中のTypeDefOrRefを完全型名へ正規化して、戻り型・引数型・呼出規約・ジェネリック引数数・パラメーター名/フラグとオーバーロード集合を比較する。結果は36/36一致。

| RimRef版 | ref/net10.0/Assembly-CSharp.dll のSHA-256 |
|---|---|
| 1.7.4985-unstable | `b407feeb4d81209db785a8bd17b8baaa3af30537dfaac7a2428019600d4b475a` |
| 1.7.4995-unstable | `8906dddf20b73f8adb7b1186da95d8c861cc63ad2889c6f85ba644c9029cd3ce` |
| 1.7.5000-unstable | `b991e9d85ced61cb15c08937938fdd42349a227b0f0789fe54f61ca535918ff8` |

同名メソッドの存在だけで機能を有効化しない。特に非公開の MakeDowned / TryEnterNextPathTile、仮想の ResolveTrade、保存後のパッチ順を実機で確認する。この表の外の候補の版差は未確認。

## wealth

`game/history/autoRecorderGroups/li/recorders/li` の `def` と `records` または `recordsDeflate`。既存 `RecordsCodec` のLE float32とraw deflateを継承。1.7.4985の `Data/Core/Defs/Misc/HistoryAutoRecording/HistoryAutoRecorders.xml` で `Wealth_Total / Items / Buildings / Pawns` は全て30000 tick間隔。Workerはプレイヤーホームを合算する。Wealth_Pawns は wealthWatcher の生物資産分類。統計の取得に地面の全Thingを再集計しない。

`HistoryAutoRecorder.Tick` は初回または頻度の倍数で記録し、ホームのないときは直前値を複製する。XMLに点ごとの時刻・補完フラグはない。インデックスの時間は `HistoryAutoRecorderGroup.DrawGraph` と同じ名目上の観測日。詳細設計の開始整合規則を満たさない系列で「初回日」「連続日数」を出さない。旧1.5/1.6と1.7各版で raw/deflate の両方、空、NaN、同名Def重複、異常な点数を確認。Def変更や無拠点区間の実際の価値は復元不可。実装時の確認: 実セーブ49件（1.5系22・1.6系20・1.7系7）で7系列すべての点数が `floor(ticksGame/30000)+1` に一致し、1.5/1.6の間隔の条件を満たした（[実装・検証記録](2026-11-07-numeric-statistics-expansion-verification.md)）。

## population

上記の Core Def で `FreeColonists / Prisoners / ColonistMood` を確認。すべて30000 tick。Workerの自由入植者は全マップ・キャラバン・移動中輸送の生存者（ロッジャー除外）、囚人は同じ範囲の生存するコロニー囚人。心情は全マップの自由入植者の mood need を持つPawnに限定し 100 倍する。対象が存在しないと0になり得るので心情0は欠測扱い。旧版の単位・欠測状況は実セーブで確認。心情サンプルは人日を表さず、人口で重み付けしない。

## vanilla

`game/tickManager/ticksGame`、`game/storyWatcher/statsRecord/{numRaidsEnemy,colonistsKilled}`、`meta/modIds/li`。正しい親要素の存在と明示値の有効性を確認し、確認済みの版の省略時デフォルトだけ適用する（実装時の確認: 1.7は `StatsRecord.ExposeData`、1.5/1.6は実セーブ42件で明示値が0・`greatestPopulation` が3になる例がなく、既定値0の省略と整合）。MOD数は保存メタデータに載ったID数（Core/DLCも含む）。リスト欠落は不明、明示空リストは0。台帳H-031/032はv1の分岐横断最大値を流用せず同じセーブの分子・分母を使う。

## quests

`game/questManager/quests/li` の `id, root, acceptanceTick, acceptanceExpireTick, initiallyAccepted, ended, endOutcome, cleanupTick`。1.7.4985の `Quest.ExposeData` と `Quest.State` を参照。既定で tick は -1、ended は false、outcome は Unknown。受諾済みは `acceptanceTick>=0 || initiallyAccepted` とし、所要時間には実際の acceptanceTick が必須。期限切れは `acceptanceTick<0 && acceptanceExpireTick>=0 && acceptanceExpireTick<=saveTick` のうち initiallyAccepted でないもの。成功/失敗の母数から無効・未受諾・期限切れ・Unknownを外す。正のIDの同一内容は1件、同じIDで矛盾する行はそのIDを除外し部分扱い。root不明は合計を有効にできるが種類別は不明枠。 `QuestManager.Remove` と `QuestPart_SubquestGenerator` に削除経路がある。 `cleanupTick` は cleanup 開始の時刻であり、正確な完了所要時間と同義であることを実セーブで確認するまで「保存内の受諾から終了処理まで」と表示。古い版の試験は成功/失敗/受諾前無効/削除/保持/自動受諾/期限切れを含める。既存の `ReadQuests` のエンディング痕跡ルールとは独立。

## roster

`Verse.Pawn.ExposeData` と `WorldPawns.ExposeData` ではマップPawn、世界Pawn、死者、装備や年齢が別ノード。参照と深い定義を単純に数えない。既定は保存用スナップショットに `PawnsFinder` の対象リスト、`Pawn.Faction/RaceProps/Dead/IsColonist/IsPrisonerOfColony`、`ageTracker.AgeBiologicalTicks`、`skills`、`health.hediffSet` を用い、ThingIDでユニーク化。義肢は実Defの追加部位型、負傷は未治癒の `Hediff_Injury`。利用版でマップと輸送に同Pawnがある例、ロッジャー・奴隷・囚人・冷凍睡眠・死者を比較し、ゲーム側の対象範囲をログの人数で照合する。技能不能とMOD追加情熱を試す。旧セーブで全保有者コンテナを確定できないので初期の旧XML対応なし。

## stock

`Verse.Map.ExposeData` は `maps/li/things/thing` に非圧縮オブジェクトだけを書き、`MapFileCompressor` に別途圧縮対象を保存。ThingDefやスタック情報だけでは栄養、食品・武器分類、Quality/Compの値を保証しない。ホームマップのプレイヤー保有、非禁制、非死体の地上Thingと自軍Pawnの装備・装着・インベントリ、バニラ保管設備の中身を対象。商人在庫、非プレイヤー所有者、遠征・輸送中は除外。ThingIDを1回、MinifiedThingは内部を1回、スタック個数を数える。ゲーム側分類と栄養、品質Comp、RoyaltyのバインドCompを参照する。Deep Storage系MODの独自保持がバニラ列挙に現れない場合はその環境で該当指標を部分扱い。1つの品を床→保管→装備へ移して不変、禁制変更・食料劣化・商人在庫で境界を確認。

## facilities

`Map.IsPlayerHome` はマップ親の勢力・Defだけでなくグラヴシップ着陸フラグ・グラヴエンジンも見る。 `mapInfo` の親参照のみからホームと断定しない。正常セーブで `Find.Maps`、ゾーン、部屋、`Building_Bed` 医療指定、タレット型、電力Compの定格と運転状態、`map.mapTemperature.OutdoorTemp` を読む。ゼロマップ時の設備0は範囲が確認できた場合のみ、温度は不明。非ロード拠点、屋外Room、MOD発電Comp、通電なし、グラヴシップ離着陸を試す。Bの各出力は保存時に数値1回、設備全体の継続監視は不要。

## research-state

`game/researchManager/{progress,techprints,knowledge}` は進捗と補助状態。 `Verse.ResearchProjectDef.IsFinished` は `ProgressReal>=Cost`。セーブの進捗非ゼロやキー数は完了数ではない。Bでは稼働中の `DefDatabase<ResearchProjectDef>.AllDefsListForReading` から、明示的にデバッグ専用と分かるDefを除いた有効な登録Defを母数にする。DLC/非表示研究も有効な登録Defなら含む。知識研究は対象版のゲーム側完了述語が正しく対応することを検証して採用。未着手/半分/完了/初期付与/非表示/知識研究を用意し既知の正解と比較。旧セーブ用の全MOD Def再構築は範囲外。

## factions

`game/world/factionManager/allFactions/li/{loadID,def,relations/li/{other,kind,goodwill},defeated,hidden,temporary,deactivated}` を静的確認。プレイヤー識別に表示名を使わない。稼働中のプレイヤー勢力から見た関係を読み、非プレイヤーの有効・公開・非一時勢力に限定。Goodwill非対応の恒久敵対勢力は数だけに含める。旧XML対応は player の明示参照または既存同定に基づく一意のFaction、`Faction_<loadID>` の完全な解決、有効勢力とgoodwill対応の検証が全て揃った版に限る。初期Bはライブのみ。2勢力が同じ表示名、非対称関係、Neutral、hidden、deactivated を含め比較。

## raid

候補は `IncidentWorker.TryExecute(IncidentParms) -> bool` の成功、`IncidentWorker_RaidEnemy` と親 `IncidentWorker_Raid` の実際の襲撃生成・到着箇所。成功時の最終parmsを使う。実Pawn集合が要る行は、親処理の `TryGenerateRaidInfo(IncidentParms,out List<Pawn>,bool)` 成功で受け取った集合を、外側のincident成功まで仮保持する。単独のデバッグ生成は数えない。 `RaidEnemy`、`Infestation`、`ManhunterPack` はCore、`MechCluster` はRoyaltyのIncidentDef。DLC由来は該当DLCゲート。1.7.4985の固定集合は包囲 `Siege`、壁破壊 `ImmediateAttackBreaching / ImmediateAttackBreachingSmart`、降下 `EdgeDrop / EdgeDropGroups / CenterDrop / RandomDrop`。Sappers は別扱い。対象版のDefと包囲、壁破壊、降下、通常襲撃を各1回以上照合。RaidStrategy・PawnsArrivalModeを別軸にする。発生数/間隔は成功1回から実装可。帰結/死者/時間は、到着Pawnと複数Lordの対応を保持し、`Lord.Notify_PawnLost` 等の離脱理由と消滅を照合できる場合のみ。重複襲撃、増援、負傷捕虜、退却、全滅、マップ消滅で検証。マップ消滅・途中導入は終了理由不明。全敵走査の毎tick実行は禁止。

## health-death

`Verse.Pawn.Kill(DamageInfo?, Hediff) -> void`。Prefixで死亡前の所属・敵対・原因・所属キャラバン・襲撃ID、Postfixで `!wasDead && pawn.Dead` を確認。既存 `DoKillSideEffects` は `SetDead` 前なので単独では確定点に使わない。同じ呼出し中の再入は1遷移。蘇生後の別の死亡は別件。 `DamageInfo.Instigator` 等に帰属があれば自軍、帰属が切れた場合は不明。加害主体と手段の分類軸を分け、手段は爆発→明示環境→武器射撃/近接/素手→不明の順で排他。死因は明示HediffまたはDamageInfoを使い、病気が併存しただけでは病死にしない。自軍タレット・トラップ・家畜・メカ・無所属環境、復活/即死/多重呼出し、Combat Extended（利用できる版）で試験。証拠がない原因分類は無効にできる。

## down

`Verse.Pawn_HealthTracker.MakeDowned(DamageInfo?, Hediff)` は非公開。Prefix/Postfixで実状態遷移、死亡で終了したケースは除外。敗退や意識低下からダウンを推定しない。非公開パッチがインライン化等で発火しない版は無効。Hediffと社交喧嘩がダウン直因の証拠になるケースと、併存のみの陰性ケースを分ける。

## membership

`Verse.Pawn.SetFaction(Faction, Pawn)` の前後と `RecruitUtility.Recruit(Pawn, Faction, Pawn)` / `InteractionWorker_RecruitAttempt.DoRecruit` の勧誘経路。陣営が変わらずゲスト・奴隷の身分のみ変わる経路を補完できるまでは、結果を「検証済み経路の記録」に限定し完全扱いしない。初期ロスターは `StartedNewGame`、旧セーブ再開では既知ロスターから開始し過去の初加入は不明。出生は `PregnancyUtility.ApplyBirthOutcome` の戻りと `PawnUtility.TrySpawnHatchedOrBornPawn` の成功の共通終点で一方を選ぶ。Biotechなし、死産、双子、捕虜→勧誘、奴隷→自由、加入→離脱→再加入、MOD直接加入を試す。誘拐・解放はFactionの kidnapped tracker の実遷移と救出経路の同一Pawn照合が必須。

## lifetime

死亡時生物年齢は `health-death` を共有。在籍期間は `membership` の開始/終了が揃う場合のみ。技能は `SkillRecord.Learn(float,bool,bool)` の前後の有効レベルを候補とし、人物IDとDefをキーにする。毎tick監視禁止。19→20→19→20は1、既に20の途中導入は0件と断言しない、無技能とMOD独自成長は対象範囲を明示。直接レベル変更が混ざる環境では監視済み経路のみ。

## mental

`Verse.AI.MentalBreaker.TryDoMentalBreak(string,MentalBreakDef) -> bool` の成功のみを数える。引数の MentalBreakDef で分類。各Workerの `TryStart(...,causedByMood:true)` に到達するため、特殊Workerも同じ入口で扱える。内側の `MentalStateHandler.TryStartMentalState` を併用して二重加算しない。 `RimWorld.InspirationHandler.TryStartInspiration(InspirationDef,string,bool) -> bool` は戻り値に加え状態の実変化を確認。失敗、同じ状態、強制デバッグ、襲撃者・奴隷・囚人を陰性試験。

## social

`Pawn_RelationsTracker.AddDirectRelation(PawnRelationDef,Pawn)` の対称呼出しはペア正規化と実遷移で1件。離婚は汎用 RemoveDirectRelation で区別できないので離婚の呼出し元確定が必須。 `PrisonBreakUtility.StartPrisonBreak(Pawn,out string,out string,out LetterDef,out List<Pawn>)` は成功リストの非空を確認。捕獲・釈放はゲストTrackerの拘束状態と該当ジョブの成功両方で確定。社交喧嘩は `Pawn_InteractionsTracker.StartSocialFight(Pawn,string)` 前後の成立を1件、絆はBond関係の実成立。死別、失敗捕獲、逃走、自然帰還を陰性試験に含める。

## medicine

`TendUtility.DoTend(Pawn,Pawn,Medicine)`、`Hediff.Tended(float,float,int)`、`Pawn_HealthTracker.AddHediff(Hediff,BodyPartRecord,DamageInfo?,DamageWorker.DamageResult)`、`ResurrectionUtility.TryResurrect(Pawn,ResurrectionParams) -> bool`。治療は医師・患者・適用対象の実変化を確認し、Hediffごとの品質を平均の分母にだけ使う。薬品は治療中の実消費差、スタック分割は不変。蘇生は死→生。病気/薬物はHediffDef分類と実追加、消滅理由は治癒経路の証拠を要求。手術は `Recipe_Surgery` 系の実行と失敗判定を同一試行で結合し、派生Recipeにも陽性例が必要。救助は運搬ジョブ完了とベッド収容、入院期間は入退床成功を別途追跡。手術中止、失敗、子部位連鎖、自然治癒、死亡時除去、普通の運搬、MOD手術を試す。全 `TakeDamage` や全ヘルスtickパッチを前提とする出力は無効。

## disaster

`GameConditionManager.RegisterCondition(GameCondition)` 成功と `GameCondition.End()` で状態。 `SolarFlare, ColdSnap, HeatWave, VolcanicWinter, ToxicFallout, Eclipse` のCore Defを確認済み。 `IncidentWorker.TryExecute` 成功で `CropBlight, MeteoriteImpact, ShortCircuit` を集計。開始済み条件を旧セーブで読み込んだ場合は開始回数に足さず、期間は確認できた区間のみ。重複効果は区間の和集合。火災は `FireUtility.TryStartFireIn(IntVec3,Map,float,Thing,SimpleCurve)` と延焼経路を照合、設備破壊は破壊前所属とダメージによる直接原因が必須。炎を1つずつtick監視しない。マップ2枚同時、同一条件再ロード、延焼、自然終了と強制解除を試す。

## work

製作の一次候補は `RecordsUtility.Notify_BillDone(Pawn,List<Thing>)`。 `Verse.AI.Toils_Recipe` が成果と素材消費確定後に呼ぶ。後続 `QuestManager.Notify_ThingsProduced` と二重登録しない。数えるのは製品の生成であり保管の成功ではない。収穫の `QuestManager.Notify_PlantHarvested(Pawn,Thing)` は通常/一括作業の主収穫を渡すが作物Def・追加収穫物を渡さない。W-001は主収穫のみ、作物別はジョブ側の元植物と結果の対応が確定するまで無効。採掘の `Mineable.TrySpawnYield(Map,bool,Pawn)` は生成されたThingを返さない。採掘1回や定格yieldで代用せず、産出確定のフックが高頻度パッチなしに取れる場合のみ。建築 `Frame.CompleteConstruction(Pawn)` は実際の完成Thingと完成前素材を確認。植付/枯死/修理/輸送は該当ジョブや発射処理の確定時に限定。品質なし、スタック10、複数成果、中止/生成失敗、一括収穫、寒害/刈り取り、ポッド編隊を試す。株コホートは上限超過時に指標を部分/無効化。定期全植物走査はしない。

## food

`Thing.Ingested(Pawn,float) -> float` の実栄養、`CompRottable` 等の実消失が候補。腐敗ステージ変更のみ、焼失、普通の摂食の重複を陰性試験。継続在庫と面積積分は境界の変化を低コストの確定通知から全て観測できる場合に限る。セーブ間の補間や定期全Thing走査が必要なら N2-F-010/N1-A-008 は無効。個別荷物移動で食料ゼロになった区間と、単に別の保管場所に移したケースを判別する。

## animals

`membership` の所属遷移と出生成功経路を共有。調教は成功経路が確認できる分のみ。 `PawnUtility.TrySpawnHatchedOrBornPawn` と `CompHatcher` の二重通知を数えない。死亡は `health-death` を共有し、屠殺/狩猟の直接証拠を追加。狩猟指定だけ、野生化、商人から購入、敵対動物との偶発戦闘、卵/多胎、蘇生を陰性/境界試験にする。

## trade

`TradeDeal.TryExecute(out bool actuallyTraded) -> bool` は成功・実決済を両方確認するが、Postfixでは `Reset()` 済み。価格や個数は実際の各 `Tradeable.ResolveTrade()` に入る時点の行（通貨を除く）を `TryExecute` 呼出しスコープに仮置きし、成功で反映。Tradeable overrideを含む対象版で明細の一回性を確認し、未収集の成立明細がある場合は金額指標を無効化。 `giftMode`、資金制限後の丸め、購入と売却の同時成立、銀/代替通貨、空取引、キャンセル、商船と訪問キャラバンと訪問先拠点、MOD取引を分ける。資産の前後差と在庫差では代用しない。

## diplomacy

`Faction.TryAffectGoodwillWith(Faction,int,bool,bool,HistoryEventDef,GlobalTargetInfo?) -> bool` の前後関係と実友好度。贈答は `trade` の gift 分岐。軍事支援は通話オプションの成功・援軍 Incident成功を独立採取。勢力の対称通知、クールダウン、予約後に来ない、実際に到着、友好度上限で変化なしを試す。

## research

`ResearchManager.ResearchPerformed(float,Pawn)`、`AddProgress(ResearchProjectDef,float,Pawn)`、`ApplyKnowledge(ResearchProjectDef,float,out float)` は進捗を更新してから `FinishProject(ResearchProjectDef,bool,Pawn,bool)` を呼ぶ。後者だけのPrefixでは通常研究が既に `IsFinished` になっている。通常は外側3入口の更新前状態をプロジェクト単位の同期スコープで保持し、完了後の状態差で確定する。内側の `FinishProject` は同じDefならこのスコープに成功を知らせるだけ、別の前提Defや直接完了なら自身の前後差を使う。計測期間で最初に完了したDefだけを数え、初期研究は `StartedNewGame` 前で除外する。最初の実作業は `ResearchPerformed` で正の実進捗を確認し、最初の作業で同時に完了した場合もそのtickを使用。開始証拠がない完了は期間標本から外す。既完了への再呼出し、しきい値をまたぐ通常研究、直接完了、前提自動完了、初期研究、切替・再開、知識研究を試す。

## caravan

`CaravanMaker.MakeCaravan(IEnumerable<Pawn>,Faction,PlanetTile,bool)` は出発以外も呼ぶ。実出発の呼出し元でのみ起点を作る。 `Caravan_PathFollower.TryEnterNextPathTile()` 前後の実タイルと隣接性を確認。 `WorldObjectsHolder.Add/Remove(WorldObject)` は所属確認と同時数には使えるが帰還・全滅の証拠にしない。死亡時 `Caravan.Notify_MemberDied`、任意放棄 `SettlementAbandonUtility` の `Abandon(wasGravshipLaunch:false)`、新設拠点はプレイヤーの拠点生成完了、敵攻略は勝利文脈とWorldObject消滅の両方を確認。古代の脅威は封印解除の確定処理のみ、一般の壁破壊・敵出現では数えない。往復、休憩、分割/合流、待ち伏せ、転送、タイルレイヤー、全滅、未完遠征を試す。遠征の分割/合流後は期間/個別距離の同一性が証明できない場合は打ち切る。

## royalty

`Pawn_RoyaltyTracker.SetTitle(Faction,RoyalTitleDef,bool,bool,bool)` の上昇、`GetFavor/GainFavor/TryRemoveFavor` の確定差分。許可証と超能力は成功消費/発動、UI入口は不可。`Ability.Activate` には複数オーバーロードがあり、プレイヤーの実キャスト判定を確認。サイリンクはBと同様セーブ時、帝国クエストは保存rootの固定許可表が確定した分のみ。爵位継承・初期付与・同一値再設定・失効と購入の違い、DLCオフを試す。

## ideology

`Pawn_IdeoTracker.SetIdeo(Ideo)` の実遷移、`LordJob_Ritual.ApplyOutcome(float,bool,bool,bool)` と実際の結果通知、`Precept_Role.Assign(Pawn,bool)` の具体実装が候補。生成時の設定と成立した改宗を区別。改宗期間は最初の明示行為と成功が一意対応できない場合は無効。DLCオフ、初期割当、キャンセル、結果と通知の二重発火、MOD儀式を試す。

## biotech

出生と出産者は `membership` と共用、メカ孵化はレシピ産物リストではなく個体成立。廃棄物は生成・処分の完了に限定し、`CompAtomizer.Notify_HauledTo` は投入の根拠だけ。セル汚染・帯域のピークは保存観測のみ。成長段階通知と再ロード、メカ損傷の部分修理と全修理、廃棄物輸出/自然崩壊/無害化を試す。処分量・除染は確定差分が取れない環境で無効。

## anomaly

今回のローカル監査には Anomaly の Def と実機セーブがない。 `Building_HoldingPlatform` / `CompEntityHolder` の収容遷移、`CompStudiable.Study(Pawn,float,float)` と研究完了の対応、該当儀式の終了経路は静的な入口候補。 `Study` の呼出し回数で研究完了を代用しない。明示的に失敗した収容操作の境界が見つからない場合 L-021 は無効。収容→研究→脱走→再収容、手動解放、複数存在、DLC無しを別々に試す。実ゲームでDLCが有効な場合だけ、この節の個別ゲートを評価する。

## odyssey

`Gravship` のワールド側状態、`CompPilotConsole` / `Building_GravEngine`、`GravshipUtility.TryGetPathFuelCost(PlanetTile,PlanetTile,out float,out int,float,float)` を照合。プレビュー経路と消費燃料は距離確定の証拠にしない。離陸→着陸完了の同一フライトと新規生成フラグが分かる接点を実版で確認。キャンセル、着陸再試行、同じ場所の再訪、異なるレイヤー、着陸失敗、ロード中、DLCオフを試す。

## ranking

旧Profile・旧セーブの v1 採用済みコロニー/エンディングと A を利用。 `StatsCalculator` のエンディング採否を共通化し、そのフィルター結果を読む。 `EndingSource.Live` の有効tickのみで R-007。難易度、シナリオ、ストーリーテラーは代表観測時のラベル。過去の途中変更を再構成しない。除外コロニー、手動統合、Devモード表示設定、同点、痕跡しかない記録を試す。

## ranking-dependency

基礎指標が未記録なら派生指標も未記録。R-009はAの同期規則を満たす2系列のみ。N1-F-001/002はゲーム中 `Find.WorldGrid` の開始PlanetTileからBiomeDefを取得し、初回の正常セーブ時に保存。古いworldGridの全展開なしに同じ情報を取れた版のみ旧セーブを拡張。R-008/N1-F-003はv1 `EndingRuntime` の確定時にその時点の `colonistsKilled` を追加メタデータとして記録。N1-F-004はエンディング種別ごとに参加者の確定定義がある場合のみ。既存 `EndingEvent.Departed` の0を「参加0」の証拠にしない。敗戦・Dev・痕跡・参加者不明・対象除外を試す。

## unsupported

R-011の因果と評価額はセーブの前後差から復元できない。同時襲撃・交易・自然劣化を排除する直接証拠がなく、被ダメージ単位の監視は今回の負荷条件外。独立した監査済みの確定損失源が得られた場合にのみ再検討する。

## 受け入れ用の記録

ゲーム版・OS・Harmony版・DLC・競合MOD・フック署名・発火カウント・保存前後の期待値/実測値・処理時間を匿名の集計で記録する。49セーブの回帰は `tools/fetch_dev_inputs.py saves .devinputs/saves` と `RWSTATS_REAL_SAVES_DIR` を使うか、同等の匿名合成データを別に用意する。実セーブの名前・Pawn名・ゲーム本体・逆コンパイル結果はコミットしない。実機または旧実セーブが用意できない場合、対応範囲を検証できた版と出力に限定する。
