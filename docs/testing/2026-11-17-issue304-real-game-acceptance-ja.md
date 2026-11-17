# RimWorld 1.7 実ゲーム受け入れ検証の記録（#304、2026-11-17）

日付と時刻はUTCです。[#304](https://github.com/pengin0503/Rimworld-Statistics/issues/304) の実施項目のうち、ゲームを画面なしで動かせる検証ホストで確かめられた範囲と、確かめられなかった範囲を記録します。

## 結論

- **確かめたこと**: RimWorld 1.7 の実ゲームで、本MODの読み込み、Harmonyパッチの登録、ワールドと地図の生成、59,000 tick の進行、代表的なイベントの計数（成功で+1、失敗で+0、同じ通知の繰り返しで+0）、取得OFF→ONでの部分的カバレッジ、セーブへのチェックポイント埋め込みと直接の親子関係、本MODの取り込み、プロフィールの集計、バニラ累積カウンターの区間、セーブ元の不変、保存時間とメモリ。
- **見つけた不具合**: 2件。どちらも個別Issueにして修正し、同じ検証で直ったことを確かめた。
  - [#350](https://github.com/pengin0503/Rimworld-Statistics/issues/350)（ワールド生成中に「Could not find player faction.」エラー）→ [#351](https://github.com/pengin0503/Rimworld-Statistics/pull/351)
  - [#352](https://github.com/pengin0503/Rimworld-Statistics/issues/352)（入植者不在で実行されなかった事件を数える）→ [#353](https://github.com/pengin0503/Rimworld-Statistics/pull/353)
- **確かめていないこと**: セーブの読み込み、タイトル画面への復帰、セーブの分岐、コロニーの切り替え、対応MODと多MOD構成、MODの除去と再導入、P1〜P5の画面と12言語の表示、利用者のPCでのTPS・FPS・UIフレーム時間、Windows。理由と代わりの証拠は「[未確認の項目](#未確認の項目)」に書いた。これらを「合格」とは扱わない。

## 環境

| 項目 | 内容 |
|---|---|
| 検証ホスト | [RimWorld-for-mod-test](https://github.com/pengin0503/RimWorld-for-mod-test) `9aa2fdf` の `tools/validation_host.py run`。本MODなし（baseline）と本MODあり（target）を同じ要求で1回ずつ動かして比べる |
| ゲーム | RimWorld **1.7.4985 rev1269**（Linux x86_64、非公開のR2から取得）。ホスト同梱の WaylithClr ランタイムと UnityStubs で、画面を描画せずに実行 |
| 公式コンテンツ | Core＋Royalty・Ideology・Biotech・Odyssey（r2/r8 は Core のみ）。Anomaly は所有していないため使っていない |
| MOD | Harmony（`brrainz.harmony`、Workshop 2009463077、modVersion 3.0.1.0）、Rimworld Statistics。e 系の実行では開発専用の [イベント計数プローブ](../../tools/RealGameEventProbe/README.md) を Harmony と本MODの間に置いた（baseline と target で共通） |
| 本MODの版 | `c6f4509`（検証開始時の main）、`abdefc1`（#351 の後）、`6edef99`（#353 の後、最終の main。PR の `c90b3aa` と同じツリー） |
| ビルド参照 | `Krafs.Rimworld.Ref 1.7.4985-unstable`（NuGet）。Windows 版 1.7.5011 の `Assembly-CSharp.dll` は、`IncidentWorker.TryExecute` と `Verse.Log.Warning` の実装の比較だけに使った |
| 計測機 | 1台の Linux コンテナ（Intel Xeon 2.10GHz、4 vCPU、メモリ 15 GiB）。ほかの実行と並行させていない |

## 実行一覧

| ID | 本MOD | 段階 | 言語・公式 | ホストの結果 | 要点 |
|---|---|---|---|---|---|
| r1 | `c6f4509` | A＋B | 日本語・5 | passed（qualified） | 新規エラー0。翻訳キー3件（値の一致1件を含む）と指定パッチ3件が一致。登録パッチは計196件 |
| r2 | `c6f4509` | A＋B | 英語・Core | passed（qualified） | 新規エラー0。指定パッチ3件が一致、計145件 |
| r3 | `c6f4509` | A＋地図 | 英語・5 | **failed** | 地図生成（種 `rws-304`、温帯林）で新規エラー2件 → #350 |
| r3c | `abdefc1` | A＋地図 | 英語・5 | passed（qualified） | 新規エラー0 |
| r4 | `abdefc1` | A＋D 600 tick | 英語・5 | incomplete（experimental） | D は両側とも600 tickまで完了、新規エラー0 |
| r5 | `abdefc1` | A＋D 60,000 tick | 英語・5 | incomplete | 両側とも tick 60,000 の自動保存（長いイベント）でホストが停止。ホストの制約 |
| r6 | `abdefc1` | A＋D 59,000 tick | 英語・5 | incomplete（experimental） | D は両側とも完了、新規エラー0 |
| r7 | `abdefc1` | A＋B | 日本語・5 | passed（qualified） | r1 と同じ（計196件） |
| r8 | `abdefc1` | A＋B | 英語・Core | passed（qualified） | r2 と同じ（計145件） |
| e2〜e7 | `abdefc1` | A＋D＋プローブ | 英語・5 | incomplete | 計数・保存・取り込み。e7 で #352 を発見 |
| e8、e9 | `c90b3aa` | A＋D＋プローブ | 英語・5 | incomplete | #352 の修正を確認。e9 はリポジトリに入れた版のプローブ |
| r9 | `c90b3aa` | A＋B | 日本語・5 | passed（qualified） | 新規エラー0。計198件（#353 で前処理2件が増えた） |
| e11 | `c90b3aa` | A＋D＋プローブ | 英語・5 | incomplete | 最初のセーブを tick 240 にして、親子セーブ間に tick 差を付けた |
| e12 | `c90b3aa` | A＋D＋プローブ | 英語・5 | incomplete | e11 に取得OFF→ONを追加 |

- 「incomplete」の理由: D段階（有限tick）はこのホストでは独立に適格化されていない（`experimental`、`dNotQualified`）。プローブを使う実行では、さらに、プローブが意図的に起こすゲーム自身のエラー（2回目の `Kill`）の後でホストが tick の進行を止める（tick 300 で終了、両側とも同じ）。プローブはすべての作業をその tick までに行う。
- 新規の診断: プローブの実行では、2回目の `Kill` と `SetFaction` によるエラー2件・警告1件が target だけの新規として報告される。ポーンの名前が実行ごとに違うためで、baseline でも同じ手順で同じ文面が出ている。プローブの実行で、これ以外の新規の診断は0件だった。表にある、プローブを使わない実行の新規の診断は、r3 の2件（#350）だけだった（#350 の原因は、表にない診断用の非公開MODでスタックトレースを付けて特定した）。どちらも、ホストが既知のノイズとする終了時のメモリ解放の警告を除いた数。

## 1. 起動・パッチ・地図・tick（実施項目1、2の一部）

- A段階: 本MODあり・なしで起動し、本MOD由来の新規のエラー・警告がないことを確かめた（r1、r2、r7〜r9）。日本語の翻訳エラー28件は baseline と同数で、本MOD由来ではない。
- B段階: 要求に書いた3件のパッチ（`Verse.SafeSaver.Save` の後処理、`RimWorld.RecordsUtility.Notify_BillDone` の後処理、`RimWorld.Frame.CompleteConstruction` の前処理）が、本MODの所有で登録されていることを確かめた。登録パッチの総数は、全DLCで196件（#353 の後は198件）、Coreのみで145件。ほかのパッチの対象が正しいかは、ホストでは個別に照合していない（CIの静的照合で確認している）。
- 地図生成: #350 の修正後、新しいワールドと地図の生成で本MOD由来の新規エラーは0件（r3c）。
- tick: 59,000 tick（1日弱）を両側とも完了（r6）。60,000 tick では日付の自動保存（長いイベント）でホストが止まる（baseline も同じ）。

## 2. イベントの計数（実施項目3）

プローブは新規ゲームの tick 300 に、ゲーム自身のAPIで次のイベントを起こす。値は、最初のセーブ `rws-probe-0`（e11 以降は tick 240、それより前は tick 300 のイベント前）とイベント後のセーブ `rws-probe-1` に埋め込まれたチェックポイントを、本MODの `SaveReader` で読んだもの。研究・死亡・襲撃・加入は e2〜e9、e11、e12 の全10回で同じ結果だった（e12 の襲撃は取得OFF→ONを含むため下の3に示す）。MOD事件の手順は e7 で追加した。

| イベント | 1回目 | 同じ通知の繰り返し／失敗 | 指標と結果 |
|---|---|---|---|
| 研究の完了（`ResearchManager.FinishProject`、前提なしの研究） | +1 | 同じ研究の完了をもう一度: +0 | `research.live.completed` 0→1 |
| 敵対人型ポーンの死亡（入植者がライフルで撃った扱いの `Pawn.Kill`） | +1 | 死体へもう一度 `Kill`: +0 | `combat.hostile_kills`、`combat.kills.ranged`、`combat.kills.victim_kind[humanlike]` 各 0→1 |
| 襲撃（300ポイント、`ImmediateAttack`、`EdgeWalkIn`） | +1 | 到着方法のない戦略で `TryExecute` が失敗: +0 | `raid.arrival_mode[EdgeWalkIn]`、`raid.factions[<派閥>]` 各 0→1、`raid.points.max` 300 |
| 加入（派閥なしの村人を `SetFaction(プレイヤー)`） | +1 | 同じ `SetFaction` をもう一度: +0 | `membership.route[other]`、`membership.first_join`、種族別・ゼノタイプ別の加入 各 0→1 |
| MOD事件（入植者が必要な自前の事件） | +1 | 入植者がマップにいない状態で実行（ゲームはワーカーを呼ばずに `true` を返す）: +0 | `environment.modded_incidents.by_def[<packageId>:<defName>]` 0→1（修正前の e7 では 0→2、#352） |

- 自由な入植者の死亡だけを数える `population.death.causes` は、敵の死亡では0のまま。
- 未観測と0の区別: 最大値・平均は、観測がなければ `NotApplicable`（例: `raid.pawns.max` は襲撃前に null）。最初の日は、まだ起きていなければ null（`Complete`）。件数は0（`Complete`）。

## 3. 取得OFF→ONと Unknown / Partial / 0（実施項目4の一部）

e12 では、最初の襲撃の後で、本MODの設定画面で「襲撃」の取得を外したときと同じ変更（全取得が選ばれた状態から襲撃だけを除き、`ApplyLiveSelectionIfChanged` でゲームスレッドで即時に反映）を行った。そのまま同じ襲撃を実行し、取得を戻してからもう一度実行した。

| 指標 | e11（OFFなし） | e12（OFF→ON） | 読み方 |
|---|---|---|---|
| `raid.arrival_mode[EdgeWalkIn]`、`raid.factions[<派閥>]` | 1（`Complete`） | 2（`Partial`） | 1回目と、取得を戻した後の襲撃。OFF中の襲撃は数えない |
| `raid.wiped` などの襲撃の件数 | 0（`Complete`） | 0（`Partial`） | 測定済みの0ではなく、OFFの間を含む下限の0 |
| `raid.points.max` | 300（`Complete`） | 300（`Partial`） | 数えた襲撃の中での最大 |
| `raid.first_day` | 0.005日（`Complete`） | null（`Unknown`） | 記録が開始時から途切れていないときだけ値を出す（`LiveFamilies.PutFirstDay`） |
| 襲撃以外の指標 | `Complete` | `Complete` | OFFにしたのは襲撃だけ |
| バニラの襲撃カウンターの区間差（下記4） | +1 | +3 | OFF中の襲撃もゲームは数える。本MODの記録とは別の値として `Partial` で表示 |

- `raid.first_day` は、OFFより前に最初の襲撃を記録していても `Unknown` になる。誤った値ではなく、情報を控えめに扱う設計どおりの動作である（途切れた範囲の最初の開始時刻を保存していないため、OFFより前の記録かどうかを区別できない）。
- 「取得OFF→ON」は、同じゲームの中で設定を切り替えた場合だけを確かめた。設定を保存してゲームを再起動する場合は確かめていない。

## 4. セーブ・チェックポイント・取り込み・集計（実施項目3、6）

- チェックポイント: 17件のセーブすべてで、本MODの `numericStatistics`（e11 で 4.3〜5.8 KB）がJSONとして読める（`CheckpointJson.Parse` が Ok、`SaveReader` の観測が Ok、メタデータ Confirmed）。親子は `rws-probe-0` → `rws-probe-1` → `t1` → … の一直線。
- 直接の親子とイベントの関係: `rws-probe-0`（tick 240）と `rws-probe-1`（tick 300）は、ゲームが保存したままの2セーブで、`rws-probe-1` の親が `rws-probe-0` のチェックポイントIDである。間のイベントの増分は上の2の表のとおり。イベントのない `rws-probe-1` から `t15` までの15回の保存の間の変化は、すべて0件。
- 取り込み: 本MODの取り込み（`ImportRunner.StartIfIdle`、プールスレッドで実行）は17件すべてを読み、失敗0件、所要 0.61〜0.77 秒（e6〜e9、e11、e12）。プロフィールの観測は SaveFile 17、Checkpoint 17、SaveSnapshot 3（#318 の圧縮で残るもの）、植民地は1つ（観測51）、系譜は `Linear`。各指標の集計値はチェックポイントの値と一致した。
- バニラ累積カウンターの区間（実施項目6）: プロフィールの `ReadHistoricalCounterIntervals` は、直接の親子 `rws-probe-0` → `rws-probe-1`（tick 240..300）について、襲撃 +1（e12 では +3）、入植者の死亡 +0 の区間を `Partial` で返した。差が0の区間（`t1` 以降）は返さない。親子のセーブが同じ tick の場合（e2〜e9）は、区間を作らない（`HistoricalCounterIntervals` の条件 `child.SaveTick > parent.SaveTick`）。
- 人工的な改変: 区間の2セーブはゲームが保存したままで、内容を変えていない。ただし、イベントは通常のプレイではなく、プローブがゲームのAPIで起こしたものである。死亡の正の増加（自由な入植者の死亡）は起こしていない。
- 分岐の表示: このホストではセーブを読み込めないため、分岐（同じ親から2つの子）を作れず、確かめていない。

## 5. 性能（実施項目7）

同じ要求（e2〜e9、e11、e12 の10回）、同じコンテナで、各実行の中で baseline と target を順に1回ずつ動かした。地図は種を固定しても実行ごとに少し異なり、セーブは約8.7〜9.0 MB。

| 計測 | baseline | target | 備考 |
|---|---|---|---|
| 保存時間（各実行の7〜15回目の保存、各90回） | 中央値 235.2 ms、平均 243.9 ms、標準偏差 33.2 | 中央値 252.0 ms、平均 257.6 ms、標準偏差 38.8 | 中央値で約7%長い。同じ実行の中の標本は独立ではないため、厳密な差の推定ではない |
| 最初の保存（`rws-probe-0`、各10回） | 597〜747 ms（中央値 661） | 699〜937 ms（中央値 735） | |
| ワーカー全体の時間 | 25.8〜27.8 秒 | 28.7〜30.7 秒 | 実行ごとの差 +1.6〜+3.8 秒（中央値 +2.9）。起動・地図生成で約+1秒、tick 中（17回の保存と取り込み 0.6〜0.8 秒を含む）で約+1.7秒（ホストの段階時刻の中央値、0.1秒単位） |
| 最大RSS | 1131〜1144 MiB | 1136〜1172 MiB | 中央値 1141 と 1169 |
| 59,000 tick（r6） | 48.4 秒、1276 MiB | 49.4 秒、1202 MiB | 1回だけ |

- 測っていないもの: TPS・FPS、UIのフレーム時間、プロフィール書き込み単体の時間、大量のセーブの初回・再走査。プロフィール書き込みと走査は、合成データのベンチマーク（[ProfileBenchmark](../../tools/ProfileBenchmark/README.md)、SaveImportBenchmark）で別に測っている。利用者のPCの代わりにはならない。

## 6. バックグラウンドスレッドとセーブ元（受け入れ条件2）

- 静的な確認（main `6edef99`）: 本MODのゲーム側のソース（`Source/RimworldStatistics`）で別スレッドを使うのは `Task.Run` の14か所だけ（取り込み、グラフの履歴、統計の表示の計算、セーブ一覧、バックアップ一覧と削除、比較、CSV出力、履歴区間、プロフィールを開く・バックアップ・復元）。`Parallel`、タイマー、`async` はない。どれも Core（ゲームを参照しない）の処理か `File` の操作を呼び、Core に渡す進捗の通知先も Core の `ImportRunStatus` である。パスや翻訳済みの文字列は、ゲームスレッドで取り出してから渡す。
- 例外1: 取り込みの読み込み問題の報告は `Verse.Log.Warning` をプールスレッドから呼ぶ。1.7.4985 と 1.7.5011 の `Verse.Log` は、ロック（`logLock`）と、ゲームスレッド以外から来たメッセージ用のキュー（`offThreadMessages`）を持ち、別スレッドから呼ばれることを想定した実装である。
- 例外2: `ProfileService.Session` の初回アクセスは、どのスレッドでも `GenFilePaths` のフォルダパス（ゲームがキャッシュする文字列）を評価しうる。ゲーム自身もこのパスを別スレッドで使う。
- 実行時の確認: 本MODの live 計数は、ゲームスレッド以外からのイベントを適用せず、その系列を「欠落あり」にする（`LiveMetrics.Resolve`）。実ゲームの取り込み（プールスレッド）で、エラー・警告は出なかった。
- セーブ元: 取り込みの前後で、17件すべてのセーブの SHA-256・サイズ・更新日時が一致した（e6 以降の全実行、`allUnchanged=True`）。`SaveReader` はファイルを `FileAccess.Read` で開く。過去の非公開の実セーブ49件でも、読み取り前後の一致を確かめている（[2026-11-10 の記録](2026-11-10-r2-49-original-reader-v13-acceptance.md)）。

## 7. 取り違えがないこと（受け入れ条件3）

- 0と欠測: 観測前の最大値は `NotApplicable`、取得OFFを挟んだ0は `Partial`、開始時から途切れない記録がない最初の日は `Unknown`、測定済みの0は `Complete`。上の2、3で実ゲームの値として確かめた。
- 分岐と直接親子: 区間は直接の親子チェックポイントだけから作られ、差が0の区間や同じ tick の親子からは作られなかった。分岐そのものは作れなかった（未確認）。
- スナップショットと通算: 襲撃の live 計数（本MODがイベントを数えた値）と、バニラの累積カウンターの区間差（ゲームの値の差）は、e12 で 2 と +3 の別の値になり、どちらも `Partial` で別々に示された。

## 8. 見つけた不具合（受け入れ条件4）

| Issue | 内容 | 修正 | 修正後の確認 |
|---|---|---|---|
| [#350](https://github.com/pengin0503/Rimworld-Statistics/issues/350) | ワールド生成中、プレイヤー勢力ができる前に動くフックが `Faction.OfPlayer` を呼び、「Could not find player faction.」を2件記録 | [#351](https://github.com/pengin0503/Rimworld-Statistics/pull/351): `GameFacts.PlayerFaction`／`IsPlayerFaction` に置き換え、CIで再混入を拒否 | r3c（地図生成 passed）、r7、r8 |
| [#352](https://github.com/pengin0503/Rimworld-Statistics/issues/352) | 入植者が必要な事件を、入植者不在のマップで「実行されずに `true`」の呼び出しまで数える | [#353](https://github.com/pengin0503/Rimworld-Statistics/pull/353): 実行前に判定し、事件・襲撃・援軍の後処理で数えない | e8、e9、e11、e12（0→1）、r9 |

## 未確認の項目

| 項目 | 理由 | 代わりの証拠 |
|---|---|---|
| セーブの読み込みと、読み込み後の二重記録 | ホストでは読み込み（長いイベント）を実行できない | Core の単体テスト `NumericCheckpointTests`（`Resume_ContinuesAFamilyThatRanUpToTheSave`、`Resume_AfterAGap_KeepsTheTotalsButIsPartial`、`StoppedScope_*`）。e 系では、読み込みなしの連続保存で変化0 |
| タイトル画面への復帰 | ホストはゲームを終了させるだけ | なし |
| セーブの分岐の表示 | 読み込みができないため分岐を作れない | Core の単体テスト `NumericCheckpointTests.Branches_ShowOneLineageNeverASum`、`MetricChartLineageTests.SiblingBranchesAreNotJoinedAndEachContinuesItsOwnParent` |
| コロニーの切り替え | 同上 | Core の単体テスト `NumericCheckpointTests.AnotherGamesSave_WritesItsLoadedTextWhatever_TheScope`、`CampaignMatcherTests` |
| 代表的な対応MOD・多MOD構成 | 対応MODの1.7版を実ゲームに入れて動かしていない | CI の静的パッチ対象照合（VPE・VEF・Outposts・VOE の参照メタデータを含む134件、失敗0）と IL 監査 |
| 対象MODの除去と再導入、取得設定を保存しての再起動 | 読み込みと再起動が必要 | Core の単体テスト `CheckpointArchiveTests.OptionalMod_RemovalReadditionAndRewind_MatchOrdinaryProfile`、`ModdedIncidentFamilyTests.RemovedEventsModKeepsHistoricalCheckpointValues`。同じゲーム内の取得OFF→ONは上の3で確認 |
| P1〜P5 の画面と12言語の表示 | ホストは画面を描画しない。英語・日本語は翻訳の読み込みだけ確認（r1、r2 の翻訳キー） | `tools/check_keys.sh` と翻訳形式の検査（CI） |
| TPS・FPS・UIフレーム時間 | 画面なしのホストでは意味のある値にならない | 上の5の保存時間・ワーカー時間・メモリ（ヘッドレス） |
| Windows | Linux 版のゲームだけで実行 | Windows 版 1.7.5011 の `IncidentWorker.TryExecute` と `Log.Warning` を静的に比べただけ。CI の Windows ジョブは検証用ランチャーの設定処理を確かめるもので、ゲームは動かさない |
| VerifyHarness の誘導監査 | 隔離の条件（`-savedatafolder` と目印ファイル）をホストのパス設定が満たさず、条件を迂回しなかった | なし |
| 自由な入植者の死亡の正の増加を含む2セーブ | プローブは敵の死亡だけを起こした | 襲撃の正の増加（+1、+3）で区間を確認 |

## 再現手順

1. [イベント計数プローブの README](../../tools/RealGameEventProbe/README.md) に従い、プローブと解析ツールをビルドし、検証ホストの要求を用意する（`request.example.json`）。
2. A＋B（日本語・全DLC、英語・Coreのみ）、A＋地図（種 `rws-304`、温帯林）、A＋D（600 tick は種 `rws-304-tick`、59,000 tick は種 `rws-304-day`）、A＋D＋プローブ（900 tick、種 `rws-304-events`）をそれぞれ `validation_host.py run` で実行する。
3. プローブの実行では、`events.jsonl` の `[rws-event-probe]` の行と、解析ツールの出力を、README の「期待される結果」と比べる。
4. セーブ、ログ、プロフィール、地図画像は非公開の場所に置き、Issue や CI の成果物に載せない。

## CI（受け入れ条件6）

通常CI（`numeric-verification.yml`）のテスト・ビルド・翻訳検査は変えていない。プローブと解析ツールはビルドできることだけを確かめるステップを追加した（配布物には含めない）。
