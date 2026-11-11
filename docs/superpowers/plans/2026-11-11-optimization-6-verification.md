# 最適化レビュー 6/7：回帰テスト・性能比較・検証手順

- 作成日：2026-11-11
- [5 実装計画](2026-11-11-optimization-5-implementation-plan.md) の各タスクがここを参照する。

## 1. 何を、どこまで確かめられるか

| 確かめること | 手段 | この計画で実施できるか |
|---|---|---|
| 集計結果・保存データが変わらない | Core のテスト、合成セーブでの順次／並列の一致、実セーブ取込後プロファイルの一致 | できる |
| Core の処理時間・割当 | 計測ハーネス | できる（実行した機械での値） |
| パッチ対象のメソッド名・引数名が 1.7.4985 に存在する | `PatchTargetCheck` | できる |
| ゲームが起動し、パッチが当たる | `RimWorld-for-mod-test` の実行、または手元の実ゲーム | 所有者が行う |
| ゲーム内のTPS、保存時間、UIの描画時間 | 実ゲームでの計測 | **できない**。この計画のどのタスクも、ゲーム内の速度が上がったとは主張しない |

PR 本文に書いてよいのは、実際に実行して得た数値だけである。実行していない検査を「通った」と書かない。

## 2. 共通検査（すべてのタスクの最後に行う）

```bash
dotnet build -c Release --nologo
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo
bash tools/check_keys.sh
bash tools/refcheck.sh
bash tools/check_patches.sh
python3 tools/IntegratedModAudit/check_embedded_integrations.py
bash tools/package.sh /tmp/rws-check.zip && python3 tools/check_sdk_package.py /tmp/rws-check.zip
git status --short
```

期待：

- ビルドは警告0・エラー0（警告はエラーになる設定）。
- テストは `failed: 0`、`skipped: 9`。`total` は「797＋そのタスクまでに追加した件数」。既存テストのファイルに差分がないこと（`git diff --stat main -- Tests` に出るのは新規ファイルだけ）。
- 調査時点（`main@2cd7a2d`）の出力：`translation keys: 904 used, all present in every language`、`patch targets checked: 127, failures: 0`、`Embedded optional MOD integration guard: PASS`、`Host ZIP SDK ownership and optional-addon isolation: PASS`。
- `refcheck.sh`、`check_patches.sh`、`package.sh` は `.refs`（R2 から取得）が必要。取得できない環境では省き、PR 本文に「未実行」と書く。CI の `numeric-verification` が同じ検査を NuGet の参照パッケージで行う。
- `git status --short` に、意図しないファイル（`1.7/Assemblies/`、`bin/`、`obj/` は無視設定済み）が出ないこと。

1件だけ実行する場合：

```bash
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -- --filter-class "*クラス名"
```

## 3. 性能比較

### 3.1 手順

1. 比較は必ず同じ機械・同じ状態で、変更前（`main` または直前のタスク）と変更後を続けて測る。別の日・別の機械の数値と比べない。
2. 各モードを3回実行し、中央の値を使う。1回目は捨ててよい。
3. 出力の全行を PR 本文に貼る。良い行だけを抜き出さない。
4. 「何倍」と書く場合は、変更前後の同じ行の比で書く。

```bash
B="dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile --"
$B hash
$B scale
$B savepath 150 1000
$B real .devinputs/saves skip        # R2 の実セーブがある場合
$B parallel .devinputs/saves         # 同上
```

既存のベンチマーク（`tools/ProfileBenchmark`、`SaveImportBenchmark`、`CrossColonyBenchmark`、`SdkEventBenchmark`、`BreakdownLookupBenchmark`）は CI が実行する。手元でも変更前後で1回ずつ実行し、悪化（2倍を超える増加）がないことを見る。

### 3.2 調査時点の基準値（2026-11-11、`main@2cd7a2d`）

環境：Linux コンテナ、4 vCPU、.NET SDK 10.0.404、Release、ページキャッシュあり。別の機械では値が変わる。目安として使い、合否の閾値にはしない。

| モード | 項目 | 値 |
|---|---|---|
| `hash` | 指紋ハッシュ | 1,410 ns／回、314 バイト／回 |
| `hash` | 証拠種別の結合（2要素） | 382 ns／回、200 バイト／回 |
| `hash` | 記録呼び出し（取得していないファミリー） | クロージャ形 57 ns・96 バイト／直接 21 ns・0 バイト |
| `scale` | 500件／50コロニー、MOD構成同一 | 更新合計 86 ms、ビュー 23 ms |
| `scale` | 2000件／200、同一 | 更新合計 662 ms、ビュー 129 ms、JSON 3.23 MB |
| `scale` | 500件／50、MOD構成が毎回異なる | 更新合計 172 ms |
| `scale` | 2000件／200、異なる | 更新合計 3,506 ms、末尾1件 3.65 ms、JSON 10.8 MB |
| `scale` | 5000件／500、異なる | 更新合計 19,230 ms、末尾1件 6.44 ms、JSON 27.1 MB、ビュー 176 ms |
| `savepath 150 1000` | 1000回目 | `PrepareSave` 0.74 ms、プロファイル 70.2 MB、直列化 655 ms |
| `savepath 1500 300` | 300回目 | `PrepareSave` 4.5 ms、プロファイル 95.9 MB、直列化 499 ms |
| `real`（49件、824.6 MB） | 読取内訳 | 読取のみ 151 ms／SHA 676 ms／XML字句解析 3,112 ms／SaveReader 3,506 ms |
| `real` | 取込 | 初回 2,958〜3,114 ms、再走査 16〜20 ms、プロファイル 319,175 バイト、v1観測 49、数値観測 42、コロニー 33 |
| `real` | ビュー（定常） | 14.6 ms、4.4 MB |
| `parallel` | 並列数 1／2／3／4 | 2,471〜3,869／1,320〜1,371／1,138／863〜963 ms |
| `store` | 319 KB | 書込み 19〜22 ms（読み戻し 0.9 ms、JSON比較 5.5 ms） |

### 3.3 タスクごとの確認点

| タスク | 見る行 | 期待 |
|---|---|---|
| T1 | `hash` の `FingerprintHash`、`scale` の `mutate_total_ms` | ハッシュは10分の1以下・0バイト。2000件／異なる構成の更新合計は3分の1以下（試作では約10分の1）。`saveObs`／`metricObs`／`json_MB` は不変 |
| T2 | `hash` の `EvidenceCombine` | バイト数が減る |
| T3 | `scale` の件数とJSONサイズ | 不変 |
| T7 | `real` の `IMPORT` 行の `cold_ms` と件数 | `cold_ms` が減る。`profile_bytes`／`saveObs`／`metricObs`／`colonies` は不変 |
| T10 | `real` の `IMPORT` 行 | 件数とバイト数が不変 |
| T11 | `savepath-session 150 1000` の最終 `profile_MB` | 変更前の `savepath 150 1000` の10分の1以下 |

T4〜T6、T8、T9 はゲーム側の変更で、ハーネスでは測れない。効果の数値は主張しない。

## 4. 実ゲームでの確認（所有者が行う）

実験用の `-savedatafolder` を使い、普段のセーブと設定を使わない（`tools/VerifyHarness/README.md` の手順）。結果は PR に追記する。

### 4.1 同梱MODのフック（T6）

対象MOD（Hospitality／Recruitment／Smart Apparel Upgrade のいずれか）を入れた状態で確認する。

1. 実験的取得がOFF：ゲームを起動し、開発者モードの Harmony パッチ一覧（またはログ）で、このMODが対象MODのメソッドに付けたパッチが0件であること。
2. 対象の取得単位だけON→再起動：該当パッチが1件あり、イベントを起こすと詳細統計の値が増えること。
3. 旧アドオン（`pengin0503.rws.*addon`）を併用：本体側のパッチが0件で、二重に数えないこと。
4. 対象MODのバージョンが対応範囲外：パッチが0件で、設定画面に単位は表示され、有効にしても値が増えず、エラーログが1回だけ出ること。

### 4.2 コロニー一覧（T8）

1. 各列のヘッダを押すと並びが変わり、もう一度押すと逆順になる。日本語・英語の両方で名前順が変更前と同じ。
2. 縦・横にスクロールしても行と列がずれない。画面外だった行にも正しい値が出る。
3. 除外のチェックを切り替えると、その行が薄くなり、概要の数値が変わる。
4. 行を押すと下段に元のセーブ一覧が出る。もう一度押すと閉じる。
5. 取込の実行中に一覧を開いたままにしても、取込が進むと表示が更新される。

### 4.3 詳細統計のツールチップ（T9）

1. 行にマウスを載せると、変更前と同じ内容のツールチップが出る（全コロニー／コロニー別、内訳の行、ランキングの行それぞれで1つずつ見比べる）。
2. 検索欄に文字を入れて行が入れ替わった後、同じ位置の別の行に、前の行のツールチップが出ない。
3. 内訳を開閉した後も同様。

### 4.4 チェックポイントの退避（T11）

1. 実験的なイベント系の取得単位をONにして新規ゲームを始め、5回以上保存する。`RimworldStatistics/checkpoint-archive/` にファイルができ、`profile.json` の該当観測に `archivedMetrics` が付く。
2. 詳細統計の値が、退避の前後（同じセーブを旧版と新版で読ませる）で同じ。
3. ゲームを終了して起動し直しても値が同じ。
4. `checkpoint-archive` フォルダを別の場所へ移して起動する：エラーで止まらず、通常の表示は変わらない。

### 4.5 起動とパッチ（全タスク）

`RimWorld-for-mod-test` の起動確認（新しい起動エラーが0件、タイトルメニューのパッチ2件が登録されること）を、T4〜T6・T8・T9 を含むビルドで1回行う。これは起動と Harmony 適用の確認であり、統計値やTPSの確認ではない。

## 5. 採用しなかった項目のための計測手順（参考）

高頻度フック（[2 問題一覧](../specs/2026-11-11-optimization-2-findings.md) F-20）と保存時のスナップショット読取（F-14）は、実ゲームで測ってからでないと変更しない。測る場合の手順：

1. 同じセーブ、同じMOD構成、同じ速度設定、同じカメラ位置で、実験的取得を「すべてOFF」と「すべてON」にして、それぞれ5回、安定した区間のTPSを記録する（#63 の受入条件）。
2. 差が中央値で2%を超えた場合にだけ、取得単位を半分ずつ切り替えて原因の単位を絞る。
3. 保存時間は、各条件で10回の保存を測る。差が50msまたは5%を超えた場合にだけ、スナップショット単位を1つずつ切り替えて絞る。
4. 原因が特定できたら、その1点について issue を立て、再現手順と数値を書く。変更はその後に計画する。

## 6. 完了の定義

各タスクは、次をすべて満たしたときに完了とする。

1. 実装計画のそのタスクの Step がすべて済んでいる。
2. §2 の共通検査を実行し、結果を PR 本文に貼った（省いたものは「未実行」と理由を書いた）。
3. §3.3 に行があるタスクは、変更前後の数値を PR 本文に貼った。
4. 実ゲームでの確認が要るタスク（T6、T8、T9、T11）は、PR 本文に「実ゲームでは未確認」と書き、§4 の該当項目を列挙した。
5. PR はレビュー可能な状態で作成し、マージはしない。
