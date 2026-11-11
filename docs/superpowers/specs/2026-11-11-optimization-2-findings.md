# 最適化レビュー 2/7：設計問題と最適化候補の一覧

- 対象：`main@2cd7a2d`　作成日：2026-11-11
- 根拠の表記（実測／静的確認／未検証）は [1 アーキテクチャ分析](2026-11-11-optimization-1-architecture-analysis.md) §0 に従う。
- 採否は [3 優先順位](2026-11-11-optimization-3-prioritization.md) に書く。ここでは事実だけを並べる。

## 一覧

| ID | 問題 | 種別 | 根拠 | 既存issue |
|---|---|---|---|---|
| F-01 | チェックポイント観測が保存回数に比例して際限なく増える | メモリ・I/O・設計 | 実測（合成） | — |
| F-02 | 指紋ハッシュを呼び出しのたびにSHA-256で再計算する | CPU・割当 | 実測＋試作 | — |
| F-03 | 過去セーブの初回取込が1スレッドで、時間の約9割がXML字句解析 | CPU | 実測 | — |
| F-04 | 同梱MODのフックが取得OFFでも動く | ゲームスレッド負荷・設計 | 静的確認 | #117 #125 |
| F-05 | 取得可否の確認が診断カウンタを書き換える | 診断の正確性・CPU | 実測（微小）＋静的確認 | #132 |
| F-06 | SDK定義の一覧をアクセスのたびに作り直す | UI負荷 | 静的確認 | #124 |
| F-07 | コロニー一覧が毎描画で全件を並べ替え、全行を描く | UI負荷 | 静的確認 | #123 |
| F-08 | 詳細統計が全行のツールチップ文字列を先に作る | UI負荷 | 静的確認 | — |
| F-09 | 変更のたびにプロファイル全体を直列化・読み戻し・比較・書込みする | I/O・ロック | 実測 | — |
| F-10 | 証拠種別の結合がHashSetを確保する | 割当 | 実測 | #122 |
| F-11 | 同じ選択規則が複数箇所に重複している | 保守性 | 静的確認 | #112 #113 #119 #120 |
| F-12 | 実行されないコードが残っている | 保守性 | 静的確認 | #125 #127 |
| F-13 | 1ファイルに責務が集まりすぎている | 保守性 | 静的確認 | — |
| F-14 | 保存時にゲームスレッドで行う処理が多い | ゲームスレッド負荷 | Core部分は実測、ゲーム部分は未検証 | #63 |
| F-15 | SaveReaderの一時割当 | 割当 | 実測（影響小） | #129 #131 #133 |
| F-16 | 取込中に5ファイルごとにプロファイル全体を書く | I/O | 机上計算 | — |
| F-17 | ビュー構築がコロニー数×定義数の行を毎回すべて作る | CPU・割当 | 実測（影響小） | — |
| F-18 | `MetricGates.IsOpen` がDef全走査とロックを繰り返す | CPU | 静的確認（影響小） | — |
| F-19 | タイトル画面の描画ごとに翻訳2回とリスト走査 | UI負荷 | 静的確認（影響小） | — |
| F-20 | 高頻度のHarmonyフック | ゲームスレッド負荷 | 静的確認、実ゲームは未検証 | #63 |
| F-21 | CI・検証基盤の重複と不足 | 保守性 | 静的確認 | #126 |
| F-22 | スキップ用索引の作り直し、祖先圧縮の総当たり | CPU | 実測（影響小） | #120 #121 |
| F-23 | 集計の正しさに関わる未解決issue | 正確性（範囲外） | — | #112〜#116 #118 #119 #128 |

---

## F-01 チェックポイント観測が際限なく増える

**原因**　`ProfileMutations.UpsertCheckpoint`（`Profiles/ProfileMutations.cs:409`）は保存1回につき `Checkpoint` 観測を1件追加する。`CompressAncestors`（同 :444）は祖先に `Compressed = true` を付けるだけで、`Metrics` は捨てない（「後で子が競合扱いになったとき、元のセーブがなくても祖先の値を復元できるようにする」ため。テスト `ConflictedChild_RestoresAncestorMetricsEvenWhenOriginalSaveCannotBeRead` がこの契約を固定している）。`MetricObservation.Compressed` のコメント（`Model/MetricObservation.cs:99`）は「値を落とした」と書いており、実装と食い違っている。

**影響**　`profile.json` と、それを読み込んだメモリ上のオブジェクトが保存回数に比例して増える。プロファイルは変更のたびに全体を直列化・書込み・読み戻し・比較するので（F-09）、増えた分がそのまま毎回のコストになる。

**実測（合成）**　全24ファミリーを取得中にし、種類別内訳を各150種類にして保存を繰り返した。

| 保存回数 | profile.json | プロファイル直列化 | 数値観測の件数 |
|---:|---:|---:|---:|
| 10 | 0.70MB | 19.5ms | 12 |
| 100 | 6.96MB | 77ms | 103 |
| 300 | 21.0MB | 344ms | 303 |
| 1000 | 70.2MB | 655ms | 1003 |

内訳を各1500種類にすると、300回で95.9MB、直列化499ms。1000回後の読み込み（直列化＋復元の往復）は2.6秒だった。

**頻度**　ファミリー取得を有効にしたユーザーの、すべての保存（オートセーブを含む）。既定OFFなので、既定のユーザーには起きない。

**注意（未検証）**　実際のチェックポイント1件の大きさは測れていない。R2の実セーブ49件にチェックポイントを含むものがなかったためである。増え方が保存回数に比例することは構造上確実だが、傾きは実プロファイルで確認する必要がある（[6 検証手順](../plans/2026-11-11-optimization-6-verification.md) の `profile-report`）。

**対象コード**　`ProfileMutations.cs:409-456`、`MetricCalculator.cs:104-142`（圧縮済み祖先を読む経路）、`ProfileSession.cs:223-255`、`ProfileStore.cs:208-257`。

## F-02 指紋ハッシュを毎回再計算する

**原因**　`ColonyIdentity.FingerprintHash()`（`Model/ColonyIdentity.cs:35,50`）は呼ばれるたびに文字列を組み立て、SHA-256を計算し、16進文字列を作る。結果を保持しない。

**呼び出し箇所**　`CampaignMatcher`（`SameColony`、`GroupById`、`AssignKeys`）、`ProfileMutations`（`CanPrune`、`CompactSaveSnapshots`、`Supersedes`）、`MetricCalculator.Find`、`HistoricalCounterIntervals`。多くがループの中にある。`UpsertMetricObservation` は新しい `SaveFile` 観測1件につき既存の全観測に対して `Supersedes` を2回評価するので、取込全体では件数の2乗になる。

**実測**　1回あたり1,410ns、割当314バイト。

**試作での効果（実測）**　指紋の5フィールドが変わらない限り前回の結果を返すようにした版を、リポジトリ外のコピーで作って比べた。

| 条件（セーブ数／コロニー数） | 更新処理の合計（現行） | 同（試作） | 倍率 |
|---|---:|---:|---:|
| 2000／200、MOD構成が毎回異なる | 3,506ms | 361ms | 9.7倍 |
| 5000／500、同上 | 19,230ms | 2,178ms | 8.8倍 |
| 2000／200、MOD構成が同じ | 662ms | 107ms | 6.2倍 |

出力は同一（実セーブ49件の取込後プロファイルは同じ319,175バイト）で、試作に対して既存テスト797件はすべて同じ結果（成功788、スキップ9）だった。

**影響の大きさ**　実データの規模（49件）では更新処理は読取時間に比べて無視できる。効くのは数千件の初回取込と、ロック保持時間の短縮である。

## F-03 初回取込が1スレッド

**原因**　`ImportEngine.Run`（`Import/ImportEngine.cs:49`）はファイルを1件ずつ読む。

**実測**　実セーブ49件（824.6MB）。

| 処理 | 合計時間 |
|---|---:|
| ファイル読取のみ | 151ms |
| 順次SHA-256 | 676ms |
| XmlReaderで全体をSkip（字句解析のみ） | 3,112ms |
| SaveReader（現行） | 3,506ms |

つまり読取時間の約89%は .NET の XmlReader がファイル全体を字句解析する時間で、このMOD固有の処理は約11%である。1.7系のセーブは1MBあたり9.2ms、1.5／1.6系は3.3〜3.8msで、差の大半も字句解析（1.7系は1MBあたりの要素数が多い）による。

**並列読取の実測**　同じ49件を `Parallel.For` で読んだ。

| 並列数 | 時間 |
|---:|---:|
| 1 | 2,471〜3,869ms |
| 2 | 1,320〜1,371ms |
| 3 | 1,138ms |
| 4 | 863〜963ms |

割当の合計は並列数によらず約53MBだった。

**頻度**　初回導入時と、`SaveReader.Version` を上げた版への更新時（全ファイルを読み直す。現在v15）。バックグラウンドなので操作は止めないが、ゲームを始めると取込は中断される。

**未検証**　HDDなど実ディスクでの並列読取。ページキャッシュが効いた状態でしか測っていない。

## F-04 同梱MODのフックが取得OFFでも動く（#117、#125）

**原因**　`IntegratedModStartup.Register`（`Metrics/IntegratedModStartup.cs:23`）は MOD コンストラクタの中で、対象MODがあれば設定を見ずに Harmony パッチを当てる（`SauOrderHook.TryInstall`、`HospitalityArrival.TryInstall`、`HospitalityRecruitHook`、`RecruitmentSource.InstallEventHook`）。取得単位が開いているかどうかは、イベントがSDKの末端（`ExternalSdkHost.RecordEvent` → `MetricGates.IsRecording`）に届いてから初めて判定される。

一方、SDK経由の登録が持つ `InstallEventHook` の枠は `_ => { }` の空処理、`ValidateBinding` はどこからも値が渡されない（`ExternalSdkHost.cs:94`、`ModProviderAdapters.cs:17,88`）。本来フックを「開いていて適格な単位だけ」に付けるための枠が使われていない。

**影響**　実験的取得が既定OFFのユーザーでも、対象MODを入れていればフックが走る。Smart Apparel Upgrade では `OrderIdleUpgrades` のたびにスタンプを全列挙してリフレクションで読む（`SauOrderHook.cs:75`）。既定のユーザーが負荷を受ける唯一の経路である。

**未検証**　実ゲームでの呼び出し頻度と所要時間。

## F-05 取得可否の確認が診断カウンタを書き換える（#132）

**原因**　`LiveMetrics.Family`（`Metrics/LiveMetrics.cs:62`）は状態を返すたびに `Hits` を加算する。`LiveMetrics.Family(X) is null` のように「取得中かどうか」を確かめるだけの呼び出しが38箇所あり（全47箇所中）、記録しなかった処理も回数に入る。`GameFacts.AnyRecording` も同じ。

**影響**　検証ハーネスが報告するフック回数が実際の受理イベント数より多くなる。通常利用でも、確認のたびに辞書を2回引く。

**実測（同じ形の処理を模したマイクロベンチ）**　取得していないファミリーに対する1回：クロージャ＋回数加算の形で57ns・96バイト、辞書を直接引く形で21ns・0バイト。取得中：313ns対189ns。

**判断材料**　クロージャが作られるのは実際にイベントが起きたときがほとんどで、高頻度フックは先に安い条件で抜けている（静的確認）。95箇所のラムダをすべて書き換えても、1イベントあたり約96バイトと数十nsの節約にとどまる。

## F-06 SDK定義の一覧を毎回作り直す（#124）

**原因**　`ModProviderAdapters.KnownUnits`／`KnownDescriptors`／`ActiveDescriptors`／`HasSnapshots`（`Metrics/ModProviderAdapters.cs:40-53`）はプロパティを読むたびに LINQ で配列を作る。設定画面（`RimworldStatisticsMod.cs:48,80`）は描画1回につき全取得単位を連結し、各行で `KnownDescriptors.FirstOrDefault` を呼ぶ。登録は起動時に確定するので、内容は変わらない。

**影響**　設定画面を開いている間、描画ごとに「単位数×定義数」の走査と割当。`ActiveDescriptors` は保存ごとにも呼ばれる。

## F-07 コロニー一覧（#123）

**原因**　`ColoniesTab.Draw`（`UI/ColoniesTab.cs:49`）は描画ごとに全コロニーを並べ替え、スクロール範囲外も含めて全行×16列の文字列生成と `Text.CalcSize` を行う。

**影響**　一覧タブを開いている間だけ。コロニー33件なら1回の描画で約530個の文字列と同数の幅計測。OnGUIは1フレームに複数回呼ばれる。

## F-08 詳細統計のツールチップ

**原因**　`DetailsTab.Build`（`UI/DetailsTab.cs:224`）は入力が変わるたびに全行の `Line` を作り、`MakeLine`（:298）が各行のツールチップ文字列を翻訳約10回と連結で先に作る。検索欄に1文字入れるたびに全行分を作り直す。ツールチップが実際に表示されるのはマウスを載せた1行だけである。

**静的確認**　1.7.4985 の `Verse.TooltipHandler` には `TipRegion(Rect, Func<string> textGetter, int uniqueId)` があり、表示時にだけ文字列を作る書き方ができる。表示中のツールチップは毎フレーム `textGetter` を差し替える実装である。

## F-09 変更のたびにプロファイル全体を書く

**原因**　`ProfileSession.WriteLatest`（`Profiles/ProfileSession.cs:223`）はロック内で全体を直列化する。`ProfileStore.SaveSerialized`（`Profiles/ProfileStore.cs:208`）は、既存ファイル全体の読み戻しと文字列比較（他セッションの変更検出）、`SameStatistics`（:314、新旧JSONを両方解析して比較し、バックアップを回すか決める）、tmp書込み、fsync、置換を行う。

**実測**

| プロファイル | 書込み1回 | うち読み戻し | うちJSON比較 |
|---|---:|---:|---:|
| 319KB（実データ） | 19〜22ms | 0.9ms | 5.5ms |
| 2.6MB（同じ内容を10倍） | 50〜75ms | 6.5ms | 24ms |

**影響**　現在の実データ規模ではバックグラウンドの20ms程度で、問題にならない。プロファイルが大きくなったとき（F-01）に、ロック保持（直列化）とLOHへの大きな一時割当が効いてくる。

## F-10 証拠種別の結合（#122）

**実測**　`MetricEvidencePolicy.Combine`（`Stats/MetricEvidence.cs:14`）は2要素で1回382ns・200バイト。`new[] { a, b }` を渡す呼び出しが5箇所ある。集計の各行と内訳で呼ばれる。

## F-11 同じ規則の重複

| 規則 | 重複箇所 |
|---|---|
| 代表観測の順序（tick降順→時刻降順→キー昇順） | `MetricCalculator.cs:262`、同 :286、`ProfileMutations.cs:219`、同 :260、`CheckpointSelector.cs:35` |
| 「最後に取得できた値」へ戻す対象（SDK提供元と `factions`） | `MetricCalculator.cs:283` と `ProfileMutations.cs:253`。片方だけ直すと、圧縮で消した行を集計が探すことになる（#112 はこの形の不具合） |
| 再読取が要るかの条件 | `ProfileMutations.NeedsRead`（:38、テストからのみ使用）と `ImportEngine.cs:72` の索引（#120） |
| 数値として採用する条件 | `MetricCalculator.Aggregate`、`NumericValue.Known`、`CrossColonyAggregator`（#119） |

## F-12 実行されないコード

- `CheckpointSaveSession._foreign`（`Stats/CheckpointSaveSession.cs:14`）：増減するだけで何にも使われない（#127）。
- `ModMetricAdapterRegistration.ValidateBinding`：値を渡す箇所がない。`InstallEventHook` は空処理だけ（#125、F-04 と同じ設計の穴）。
- `MetricObservation.Compressed` のコメントが実装と違う（F-01）。

## F-13 大きすぎるファイル

`SaveReader.cs` 1,378行（ドライバ、マップ、ワールド、履歴、クエスト、指標の組み立て、ハッシュ用ストリームが1クラス）、`ProfileMutations.cs` 629行（v1観測、数値観測、スナップショット圧縮、チェックポイント、ライブ記録、決定）、`MetricCalculator.cs` 550行、`DetailsTab.cs` 504行、`WorkMetricPatches.cs` 497行。未解決issueの多くが `SaveReader` と `ProfileMutations` に集中しており（#112〜#116、#121、#128、#129、#131、#133）、変更が同じファイルで衝突しやすい。

## F-14 保存時のゲームスレッド処理

**Core部分の実測（合成、内訳150種類）**　`PrepareSave`（全ファミリー状態のコピー＋JSON）0.7〜2.1ms・割当約260〜310KB、`ToObservation` 0.2〜0.7ms、`Mutate` 0.3〜0.7ms。内訳1500種類では `PrepareSave` 4.5〜12.4ms・割当1.2〜2.0MB。

**ゲーム部分（未検証）**　`SavedStateMetricReader.ReadStock`／`ReadFacilities`（`SavedStateMetricReader.cs:202,249`）はホームマップの全Thingを走査し、在庫では1個ごとに `GetUniqueLoadID` の文字列を作って `HashSet<string>` に入れる。所要時間は実ゲームでしか測れない。

**ロック競合**　バックグラウンドの直列化やビュー構築がロックを持っている間、ゲームスレッドの `RecordSave` は待つ。待ち時間の上限はプロファイルの大きさで決まる（F-01、F-09）。

## F-15 SaveReaderの一時割当（#129、#131、#133）

**実測**　49件で割当53.3MB（1件あたり1.1MB、1.7系は2.85MB、最大3.6MB）。SaveReader固有の時間は全体の約11%。専用スレッドでのハッシュ（#133 が問題にしている点）は、順次なら676ms かかる処理を並行させており、実サイズのセーブでは有効に働いている。

## F-16 取込中の定期保存

`ImportEngine.cs:118` は5ファイル読むごとにプロファイル全体を書く。書く量は「ファイル数÷5×プロファイルの大きさ」。49件・319KBでは合計約3MBで問題ない。2000件・10.8MB（F-02 の合成条件）なら計算上約4.3GBになる。実測はしていない。

## F-17 ビュー構築

**実測**　実データ（33コロニー）で14.6ms・4.4MB。合成では200コロニーで51〜129ms・約30MB、500コロニーで176ms・77MB。バックグラウンドのタスクで行い、結果はキャッシュされる。`MetricRankings.RowOf`（`Stats/MetricRankings.cs:266`）は行を線形検索する。

## F-18〜F-19 影響が小さいもの

- `MetricGates.IsOpen`（`Metrics/MetricGates.cs:77`）：ロックを2〜3回取り、`modded_incidents` と前哨基地の単位では全Defを走査する。呼ばれるのは起動時・ゲーム開始時・保存時・SDKイベント時。
- `MainMenuButtonPatch.Prefix`（`Patches/MainMenuButtonPatch.cs:14`）：タイトル画面の描画ごとに翻訳2回、リスト走査2回、ロック1回。

## F-20 高頻度のHarmonyフック

静的に確認した高頻度の対象：`SkillRecord.Learn`、`CompRottable.TickInterval`、`Pawn_AgeTracker.TickBiologicalAge`、`MentalBreaker.MentalBreakerTickInterval`、`Pawn_RecordsTracker.Increment`、`Pawn_JobTracker.EndCurrentJob`、`Pawn_HealthTracker.AddHediff`／`RemoveHediff`、`GenPlace.TryPlaceThing`（2クラス）、`ResearchManager.ResearchPerformed`／`AddProgress`、`MechRepairUtility.RepairTick`、`Thing.Kill`、`Fire.TrySpread`。

読んだ範囲では、いずれもフィールドの比較や型判定で先に抜けており、明らかな無駄は少ない。気づいた点：

- `ResearchPerformed` は研究tickごとに `Scope` と `Frame` を確保する（`ResearchMetricPatches.cs:44`）。
- `MechRepairMetricFacts.WasInjured` は修理tickごとに LINQ の `Any` を使う。
- `TickBiologicalAge` の Prefix は最初に `LiveMetrics.Family` を呼ぶ（F-05 の回数加算が毎回走る）。

**未検証**　実ゲームでの呼び出し回数と時間。#63 が実測を求めており、まだ行われていない。測らずに書き換える根拠はない。

## F-21 CI・検証基盤

- 版ごとの公開ワークフローが6本・533行あり、ほぼ同じ内容（#126）。
- `numeric-verification.yml` は1ジョブで静的検査、テスト、ベンチマーク5本、パッケージ、パッチ対象検査を順に行う。ベンチマークは閾値も基準値との比較も持たず、結果はログに出るだけである。
- 取込結果が変わっていないことを確かめる自動検査がない（実セーブは非公開なのでCIには置けない。合成セーブでの等価性検査は書ける）。
- ゲーム側アセンブリ（パッチ、UI）には自動テストがなく、`PatchTargetCheck` による対象名・引数名の静的検査と、手動の確認手順書に頼っている。

## F-22 影響が小さいと実測で分かったもの

- スキップ用索引の作り直し（#120）：全件スキップの再走査は49件で16〜20ms。
- 祖先圧縮の総当たり（#121）：通常の流れでは未圧縮なのは末端1件だけで、合成1000回保存でも `Mutate` は0.3ms。問題になるのは未圧縮の行が多い特殊な形だけである。

## F-23 範囲外：集計の正しさに関わるissue

#112、#113、#114、#115、#116、#118、#119、#128 は統計値・観測の保持・取込の成否を変える修正である。今回の制約（集計結果の意味を変えない）の外なので、この計画では扱わない。ただし F-11 の重複解消と F-13 のファイル分割は、これらの修正を小さく安全にするための下準備になる。
