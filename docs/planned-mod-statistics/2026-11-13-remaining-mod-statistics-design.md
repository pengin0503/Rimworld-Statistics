# MOD固有統計の残作業設計

基準は [main 44e0a40](https://github.com/pengin0503/Rimworld-Statistics/commit/44e0a40ee8b6994a169420b1c93bb57dd5f455b9)。対象は RimWorld 1.7。本書は設計案であり、上流の公開ソースと配布候補の DLL の所在を確認した段階を含む。追加候補の IL 照合・実行時確認は今後の作業とする。

## 計画と現行実装の照合

[35件の計画](2026-11-13-candidate-disposition.md)を [現行の登録一覧](../../Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs)、各アダプターおよび [追跡 Issue #211](https://github.com/pengin0503/Rimworld-Statistics/issues/211) と照合した。A01–A11、B01–B07、C01–C06、C08、D01–D09 の **34 MOD** に少なくとも一つの取得経路が登録されている。B07とD02は VEFのフックを共有する。C07 Vanilla Books Expanded の新聞制作は既存のクラフト統計を使用する。読書終了用のアクションは中断でも呼ばれるため、固有の読了数には独立した成功根拠が要る。登録済みとはコード上の状態を指し、各配布版での動作確認を含まない。

第二波 Tasks 26、30–34 はそれぞれ弾薬消費・被制圧、海賊雇用・支払、専門技能上昇・最高値、砲撃到達、ハイブリッド誕生・メカ変換、ドローン孵化の取得経路がある。Tasks 27–29 は[確定済みの取得範囲と見送り](2026-11-13-evidence-gated-outcome-disposition.md)を適用する。ARCアップグレード、中継器スキャン、RimFactoryの地面への配置、手配引き渡しと受取銀貨、軌道到達、治癒成功、Desertersの情報支払は追加済み。

## 設計を詰めた追加統計

| 優先 | MOD／単位 | 成功の境界と除外 | 残る確認 |
|---|---|---|---|
| 1 | **Vanilla Races Expanded - Android**。製造完了1体。PawnKindDef別 | `JobDriver_CreateAndroid` の作業達成分岐から呼ばれる `Building_AndroidCreationStation.FinishAndroidProject()` に限定。実際に生成された個体が同一マップで Spawned、自勢力で、製造プロジェクトが消費されたことを確認する。通常の作業者と自勢力の設備を要求。開発用の直接呼び出し、途中の未完成品、改造、休眠解除、クエスト加入は別経路。 | `VREAndroids.dll` の通常の生成デリゲートと Spawn 呼び出しを IL で特定。失敗・作業再開・通常完成・開発用直接呼び出しを確認。 |
| 2 | **Vanilla Quests Expanded - Cryptoforge**。作業を完了して再起動した発電機1基 | `JobDriver_RestartGenerator` の finish action にスコープを限定。作業者が自勢力、対象が `VQE_FrozenCryptogenerator_Off`、`totalTimer > totalTime`。その呼び出し内で `Restartable.Notify_Restarted()` が `VQE_FrozenCryptogenerator` を実際に配置し、旧設備を despawn したことを確認する。サイトの設備は自勢力所有を前提にせず、作業者で帰属を決める。 | finish action が中断時に呼ばれる場合も想定して、タイマー未達・配置失敗・別設備・再呼出しで0を確認。既存の中継器スキャン、別MODのARCアップグレードとは別のイベント。 |
| 3 | **Vanilla Social Interactions Expanded**。実際に学習効果があった指導1回。SkillDef別 | `InteractionWorker_Teaching.Interacted` の対象生徒に対する最初の `SkillRecord.Learn` の前後で、技能レベル上昇、または同一レベルでの技能XPの正の増加を確認。自勢力の生徒を対象とし、教師に入るXPは件数に含めない。授業回数が対象なので、既存の技能上昇・子供の学習統計と別に扱う。 | 1.7 DLLで対象生徒側の呼び出しを固定。レベル跨ぎ、XP上限、対象不一致、効果なし、他MODで呼出しが抑止された場合を検証。 |
| 4 | **Vanilla Quests Expanded - Ancients**。遺伝子が実際に付与された成功注入1回。追加された遺伝子の内訳は別単位 | `HandleSuccessOutcome` の即時経路と `Window_GeneChoice` の確定デリゲートの両方で、対象者の新規 xenogene を開始前と正常終了後で比較。確認画面を開くだけでは0。画面を経る場合は確定操作の直前を基準にし、処置ごとに最大1回を記録する。両経路が合流する `PostGeneSelectionSuccess` に入る時点では初回の遺伝子付与が済んでいるため、単独では全差分を測れない。 | 生存・プレイヤー所属と処置の帰属を確認。付与なし、効果が残らない、確認未完了、複数遺伝子、遺伝子付与と既存の拒絶・変異の排他性を検証。パッケージID付き GeneDef 内訳を加える場合は処置件数と別に集計する。 |
| 5 | **Vanilla Factions Expanded - Classical**。通常プレイで新規に獲得したパーク1件。PerkDef別 | 通常クエスト／会話経由の `GainFavorOf` 内の `AddPerk` で、`ActivePerks` の非所属から所属への遷移を確認。1回の後援獲得が議員・全議員・共和国統合の複数パークを付与できる。シナリオの初期付与、開発用、ロード時の再初期化、所持中の再付与を除外。解除後に通常経路で得たパークは新たな獲得として扱う案。 | 議員後援の既存フックが扱う通常の呼出元を再利用できるか確認。複数パーク、同一Def再付与、初期付与、セーブ時に解除された後の再取得を検証。 |
| 6 | **Save Our Ship 2**。確認された自勢力の艦船戦勝利1回 | 1.7開発版の `ShipMapComp.EndBattle(Map loser, bool fled, bool hack, int burnTimeElapsed)`。開始時に双方の戦闘マップを固定し、自勢力の起点・相手側の敗北・`fled == false` を要求。終了後は戦闘状態の離脱と接続解除を確認。ハッキングによる同じ勝利分岐を含める。敵の退却や自勢力の撤退は0。 | 出航・再ロード・再呼出し、敵の撤退、自勢力の敗北、残骸マップの二次戦闘を検証。最初の対応は現在の公開1.7開発版の明示的な勝利分岐に限定。 |

根拠の上流リビジョンとパッケージ内の DLL は[参照ロック](2026-11-13-reference-lock.md)を参照。Save Our Ship 2 は [63209ef9](https://github.com/Bqr1s/SaveOurShip2/blob/63209ef9a9d86c5d69ea1c70200b891a96263af8/Source/1.7/Comp/ShipMapComp.cs)。1～6は実装順の提案であり、1・2は判定が個別に完結するため最初の作業単位にできる。

## 残りの提案と確認事項

| 計画ID | 統計候補 | 次に確かめること |
|---|---|---|
| A01 / B07 / D02 / D05 | VFE Factoryの独立した加工、Brewing/Cookingの出来高、Chemfuelの地上採掘・精製 | [PipeSystemの固定参照](https://github.com/Vanilla-Expanded/VanillaExpandedFramework/tree/597579c666226a1e3b80bae586a47fb26fa46cb5/Source/PipeSystem/PipeSystem)。地上の実配置・既存スタックへの追加と、配管受入量を別単位にする。同じ機械の配置リトライは、最初に確定した数量を再加算しない。処理完了回数と数量は別。工場ホッパー経由は所有権移転と再搬送を要調査。 |
| A02 / A05 / A09 | 巣の発展、全願望達成者、Androidの在籍記録 | 巣の新規マイルストーンに出生と独立した成功条件を求める。全願望達成は同一人物の最後の未完了願望から達成への遷移とし、再抽選・再設定時の扱いを確認。Android在籍は比較用のコロニー別ピーク候補。累積製造と独立し、採用するなら観測時点と全体Maxを定義する。 |
| A07 / A08 / C01 / C03 / D04 | Desertersの情報獲得・作戦、Generator固有クエスト、Tribalsのコーナーストーン点、Settlersの襲撃、Empireの儀式 | Desertersの情報獲得は自勢力の `CompIntelExtract.Extract(Pawn)` 等で確認できた実配置分から検討。取引、物資の移動、見積りは含めない。作戦・クエストの成功自体は既存の `quest.retained.success_by_root` との重複を調べ、独立した完了があれば追加。Tribalsでは初期値や時代昇格時の減算と、行動による獲得点を区別する。 |
| B02 | Rimatomicsの実発電電力量 | 定格や単発出力値から通算値を推定せず、経過時間を含む実出力、停止・負出力・電網への供給の区別、既存の発電統計との重複を確認。監視を高頻度に増やさない方法が条件。 |
| B04 / B05 | Save Our Ship 2の艦損傷、A RimWorld of Magicの他呪文・職業進行 | 艦損傷は修理・既存ダメージ・一般の戦闘計測との分離が未確定。魔法は [HUGINN版 30f60c1](https://github.com/kelnmaari/RWoM/tree/30f60c1d3b947fb19565ab19648bc2efd5678f9f) の `TM_Action.TransmutateFrom` の原材料変換を候補にする。出力の実配置かつ元材料の消費が完了した分岐を使う。他分岐は戻り値だけで判定できない。職業進行は初期化・ロード・デバッグを除いた恒久的な遷移を要調査。 |
| C05 / D06 / D08 | Ancientsの保管庫実績、Medieval 2の養蜂実績、Giddy-Upの降車 | 保管庫は固有目標の達成を汎用クエストと照合。養蜂は既存の蜂蜜収穫から導出できる実績を別に記録しない。降車は乗馬済みからの通常の離脱と強制解除を分けられ、既存の乗馬開始件数とは別の意味を持つ場合に設計する。 |
| C07 / C08 | Booksの読了、RimFactoryの設備間直接転送 | Booksは読書中断と区別できる終端が確認できるまで保留。RimFactoryは受取側の実増加、返却・途中失敗、後で同じ出力を地面に置く場合の二重計上を切り分ける。 |

[Gastronomyの個別注文完済、Prison Expandedの暴動終結、Rimefellerの精製品・未証明の投入量](2026-11-13-evidence-gated-outcome-disposition.md)には既存の見送り決定を適用する。

## 未導入MODの調査候補

[別枠の30候補](README.md)のうち F01 Vehicle Framework は [#221](https://github.com/pengin0503/Rimworld-Statistics/pull/221) で所有台数を取得済み。以下の3件は35件の計画とは別の追加候補。公開1.7候補のソースと DLL があるため、設計時に先行して成功条件を調べられる。

| ID | MOD | 設計案と確認事項 |
|---|---|---|
| G04 | Prisoner Arena (Continued) | 自勢力の会場で `Building_Bell.EndBrawl` が有効な勝者を確定し、`winners` が1件増加した試合を記録。勝者が両参加者のいずれかであること、中断でないことを要件とする。釈放は自由になった実状態が確認できる場合のみ別候補。勝者名は保存しない。[上流 04566ecd](https://github.com/emipa606/PrisonerArena/blob/04566ecd63e11f699d3979a6331b9010e79378f5/Source/RimWorldExampleProjectDLL/Building_Bell.cs) |
| H05 | Biomes! Bees | 自勢力の巣由来のハチが実行する `JobDriver_Pollinate` の最終処理で、同じ生存・配置済み植物の `Growth` が正に増えた受粉を植物Def別に記録する案。巣の帰属を上流の関連データで確認。成熟上限・失敗・待機中断は0。花蜜や蜂蜜の数は別の事象。[上流 c539dad8](https://github.com/biomes-team/BiomesBees/blob/c539dad8b0265626c9c5db958417f640a559d851/Source/JobDriver_Pollinate.cs) |
| E01 | Vanilla Vehicles Expanded | 残骸由来の Frame を通常作業で完成させ、新規の自勢力車両が実配置されたときに1台。上流の `frameWrecks` 消費は完成処理の prefix なので、単独では成功条件に使えない。Vehicle Framework側の最終生成・配置を確認し、既存の所有台数とは別の累積値にする。[上流 66452f35](https://github.com/Vanilla-Expanded/VanillaVehiclesExpanded/blob/66452f358f511943be720d652ab42043dbb99f73/1.7/Source/Harmony/Construction_CompleteConstructionVehicle_Patch.cs) |

## 集計と検証

- 既存の `IntegratedTransitionAdapters`、取得設定、チェックポイント集計を使う。候補のキーは成功経路を確定した時点で決める。通常の件数はコロニーごとの代表チェックポイントを使い独立コロニー間で合算し、同一コロニーの巻き戻し分岐を合算しない。過去の実行回数をセーブ内の現存状態から逆算しない。
- 既定で取得し、明示的なOFFと設定変更時の再起動不要を維持する。アンインストール後も以前の記録を表示し、未取得区間には既存の欠測処理を使う。配布 DLL のシグネチャ・呼出箇所が一致しない場合は、その取得だけ利用不可にする。
- 汎用APIへの常設フックより対象MOD内の確定呼出箇所を優先する。共通APIの観察が必要な場合は、呼出元と対象インスタンスを限定し、例外時にも解除する。候補ごとに実際の1.7選択 DLL と IL を照合し、既存統計の重複も確認する。
- 受入ケースは成功、失敗、中断、開発操作、同一経路の二重呼出し、自勢力以外、保存と再ロード、巻き戻し分岐、別コロニー、対象MODを外して再導入。実装時にCoreテスト、ビルド、配布ZIP検査を行い、実ゲームを使える場合は RimWorld-for-mod-test で成功例と失敗例を追加確認する。

## 入力

Android、Cryptoforge、VSIE、Ancients、Classicalは公開参照で設計・IL確認を進められる。Save Our Ship 2 は #226 の公開1.7開発版、A RimWorld of Magic は公開HUGINN 1.7版を使用できるため、同じ版を対象にする限り追加アップロードは不要。別の1.7配布版も対応範囲に含める場合はその版の `About`、`LoadFolders.xml`（配布物にある場合）、有効版フォルダーの `Assemblies` と関連 `Defs` が必要。JecsLiteだけでは `TorannMagic.dll` の照合はできない。

手元の6セーブは全てVSIEが有効とヘッダーで確認した。既存環境の読み込み確認に利用できる。実ゲームで効果まで検証する段階では、作業完了直前のAndroid製造台、または再起動前のCryptoforge発電機と作業者がいるセーブがあれば、再現準備を短縮できる。
