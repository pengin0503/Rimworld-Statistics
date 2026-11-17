# 実ゲームのイベント計数プローブ（開発専用）

#304 の実ゲーム受け入れ検証で使った開発専用の道具です。RimWorld 本体を実際に動かし、ゲーム自身のAPIで代表的なイベントを起こして、本MODの計数・セーブへの埋め込み・取り込み・集計を確かめます。結果の記録は [docs/testing/2026-11-17-issue304-real-game-acceptance-ja.md](../../docs/testing/2026-11-17-issue304-real-game-acceptance-ja.md) にあります。

- 配布物ではありません。`tools/package.sh` と公開用ワークフローには含まれず、通常CIではビルドできることだけを確かめます。
- プローブは本MODをコンパイル時に参照しません。本MODが読み込まれているときだけ、取り込み処理と取得設定の変更をリフレクションで呼びます。そのため、本MODなしの実行（baseline）と本MODありの実行（target）でゲームへの操作は同じです。
- ゲームのDLL、`Data`、逆コンパイル結果、セーブ、生ログ、私有パスはコミットしないでください。ビルド出力（`Mod/Assemblies/`）は `.gitignore` 済みです。

## 構成

| パス | 内容 |
|---|---|
| `Source/EventProbe.cs` | `GameComponent`。新規ゲームの tick 240 で1回セーブし、tick 300 でイベントを起こしてからセーブ・計時・取り込みを行う |
| `Mod/About/About.xml` | プローブMODの定義（packageId `pengin0503.rimworldstatistics.realgameeventprobe`） |
| `Mod/Defs/RaidStrategies.xml` | 到着方法を持たない襲撃戦略 `RwsProbe_NoArrival`（失敗する襲撃用、出現重み0） |
| `Mod/Defs/Incidents.xml` | 入植者が必要な自前の事件 `RwsProbe_ColonistsRequired`（`baseChance` 0、ワーカーは実行回数を数えるだけ） |
| `Analyzer/` | 実行結果のセーブとプロフィールを本MODの Core（`SaveReader`、`ProfileSession`）で読む解析ツール |
| `request.example.json` | 検証ホストへの要求の例（パスは置き換える） |

## プローブが行うこと

1. tick 240: `rws-probe-0` を保存する。
2. tick 300（すべて同じ tick）:
   - 研究: 前提のない研究1件（定義名の昇順で最初のもの）を完了し、同じ完了をもう一度呼ぶ。
   - 死亡: 敵対派閥の人型ポーンを生成し、最初の入植者がボルトアクションライフルで撃った扱いで `Kill` する。同じ `Kill` をもう一度呼ぶ。
   - 襲撃: 300ポイントの襲撃（`ImmediateAttack`、`EdgeWalkIn`）を実行する。続けて到着方法のない戦略で実行し、失敗させる。
   - 取得のOFF→ON: 本MODの設定画面で「襲撃」の取得を外したときと同じ変更（全取得が選ばれた状態から襲撃だけを除き、ゲームスレッドで即時に反映）を行い、同じ襲撃を実行する。続けて取得を戻し、もう一度実行する。本MODが読み込まれていなければ設定の変更だけを飛ばし、襲撃は同じく実行する。
   - 加入: 派閥なしの村人を生成して `SetFaction(プレイヤー)` し、同じ呼び出しをもう一度行う。
   - MOD事件: 入植者がいる状態で自前の事件を実行する。続けて入植者を一時的に全員マップから外し（`DeSpawn`）、同じ事件を実行してから元の位置に戻す（#352）。
   - `rws-probe-1` を保存し、イベントなしで `rws-probe-t1`〜`t15` を保存して保存時間を測る。
   - 本MODが読み込まれていれば、本MODのセーブフォルダ取り込みを実行し、前後でセーブのSHA-256・サイズ・更新日時を比べる。
3. 各手順の結果を `[rws-event-probe] <手順> <詳細>` の形でログに1行ずつ出す。

2回目の `Kill` と `SetFaction` では、ゲーム自身がエラー2件と警告1件を出します（baseline でも同じ）。検証ホストはエラーを出した tick の後で tick の進行を止めるため、D段階の結果は `incomplete`（tick 300 で終了）になります。そのため、最初のセーブより後の作業はすべて tick 300 の中で行います。

## 使い方

前提: [RimWorld-for-mod-test](https://github.com/pengin0503/RimWorld-for-mod-test) の検証ホストと、そのゲームスナップショット（RimWorld 1.7、Linux）。以下の `/private/...` は手元の非公開の場所に置き換えてください。

1. ビルドする。

   ```bash
   dotnet build tools/RealGameEventProbe/RealGameEventProbe.csproj -c Release -p:UseNuGetRefs=true
   # またはスナップショットの Assembly-CSharp.dll と UnityStubs.dll を参照する場合
   dotnet build tools/RealGameEventProbe/RealGameEventProbe.csproj -c Release -p:GameDir=/private/rw-snapshot/game
   ```

2. `tools/RealGameEventProbe/Mod` を、ビルド後の `Assemblies/` ごと非公開の MOD フォルダ（例: `/private/mods/RealGameEventProbe`）へコピーする。本MODも `About`・`1.7`・`Languages` を含むフォルダとして用意する。
3. `request.example.json` のパスを置き換え、プローブを Harmony と本MODの間に、`"target": false` で置く。本MODだけを `"target": true` にする。D段階（有限tick）で 900 tick を指定する。
4. 検証ホストを実行する。

   ```bash
   python3 tools/validation_host.py run --snapshot /private/rw-snapshot --request /private/request.json \
     --out /private/results/run-001 --baseline-cache /private/results/baseline-cache
   ```

5. ログを確認する。`/private/results/run-001/{baseline,target}/events.jsonl` に `[rws-event-probe]` の行がある。target では `statisticsLoaded=True`、`import` の行に `allUnchanged=True` が出る。
6. 解析ツールで結果を読む（.NET 10）。

   ```bash
   dotnet run -c Release --project tools/RealGameEventProbe/Analyzer/RealGameEventAnalyzer.csproj -- \
     /private/results/run-001/target/userdata/Saves /private/results/run-001/target/userdata/RimworldStatistics
   ```

   セーブごとにSHA-256の先頭、サイズ、チェックポイントの親子、前のセーブからの指標の変化を出します。続けてプロフィールの写しを開き、観測の件数、植民地の系譜、変化した指標の集計値、直接の親子チェックポイントの間のバニラ累積カウンターの差（襲撃・入植者の死亡）を出します。実行結果のファイルは読むだけで、プロフィールは一時フォルダへの写しを開きます。

## 期待される結果

`rws-probe-0` → `rws-probe-1` の変化（本MODの live 計数）:

| 指標 | 変化 | 根拠 |
|---|---|---|
| `research.live.completed` | +1 | 同じ研究の2回目の完了は数えない |
| `combat.hostile_kills`、`combat.kills.ranged`、`combat.kills.victim_kind[humanlike]` | 各 +1 | 2回目の `Kill` は数えない |
| `raid.arrival_mode[EdgeWalkIn]`、`raid.factions[<派閥>]` | 各 0 → 2（`Partial`） | 最初の襲撃と、取得を戻した後の襲撃。失敗した襲撃と、取得OFF中の襲撃は数えない。OFFの間があるので部分的 |
| `raid.pawns.max`、`raid.points.max` | 未観測（`NotApplicable`）→ 襲撃の人数、300（`Partial`） | 数えた襲撃の中での最大（下限） |
| `raid.wiped` など襲撃系の残りの件数 | 0（`Complete`）→ 0（`Partial`） | 測定済みの0ではなく、OFFの間を含む下限としての0 |
| `raid.first_day` | 未到達の null（`Complete`）→ null（`Unknown`） | 記録が開始時から途切れていないときだけ最初の日を示す（`LiveFamilies.PutFirstDay`）。OFF前に最初の襲撃を記録していても、OFFを挟むと不明になる |
| `membership.route[other]`、`membership.first_join`、種族・ゼノタイプ別の加入 | 各 +1 | 2回目の `SetFaction` は数えない |
| `environment.modded_incidents.by_def[pengin0503.rimworldstatistics.realgameeventprobe:RwsProbe_ColonistsRequired]` | +1 | 入植者不在で実行されなかった回は数えない（#352） |
| `population.death.causes` | 0のまま | 自由な入植者の死亡だけを数える |

襲撃以外の指標は `Complete` のままです。ほかに、`rws-probe-1` から `t15` までの各セーブの間は変化0、チェックポイントの親子は一直線、取り込みは17件すべて成功、プロフィールの植民地の系譜は `Linear` になります。直接の親子 `rws-probe-0` → `rws-probe-1`（tick 240..300）には、バニラの襲撃カウンターの差 +3（取得OFF中の襲撃も含む）、入植者の死亡 +0 の区間が `Partial` で出ます。差が0の区間（`t1` 以降）は表示されません。

## 限界

- 新規ゲームだけを扱います。検証ホストではセーブの読み込み（長いイベント）ができないため、読み込み後の二重計上、タイトル画面への復帰、セーブの分岐は確かめられません。
- 60,000 tick に達すると日付の自動保存（長いイベント）で検証ホストが止まります。D段階はそれより短くしてください。
- 地図はシード固定でも実行ごとに少し異なり、ポーンの名前は毎回変わります。検証ホストは、2回目の `Kill` と `SetFaction` のエラーと警告を名前の違いから target だけの新規として報告します。baseline の同じ手順で同じ文面が出ていることで確認してください。
- 保存時間は1台のコンテナ・ヘッドレスの実測です。利用者のPCの TPS/FPS や UI のフレーム時間の代わりにはなりません。
