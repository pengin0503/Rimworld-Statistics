# 検証ハーネス（開発専用）

Rimworld Statistics の未検証の取得単位（[実装記録](../../docs/research/2026-11-07-numeric-statistics-phase-c-implementation-notes.md) §1）を、テスト用のゲームでだけ開いて、実機で確かめるための別 MOD です。配布物（`tools/package.sh` の zip、公開ワークフロー）には入りません。

- `packageId` は `pengin0503.rimworldstatistics.verifyharness`。本体（`pengin0503.rimworldstatistics`）と Harmony の後にロードします。
- 起動時に `-savedatafolder=` が無ければ、**何も開きません**（エラーログを出すだけ）。未検証のコードが記録した値が通常のプロファイルに入ると、その単位を `Verified` にした後で利用者に表示されてしまうためです。
- 開いた単位の値はセーブとテスト用プロファイルに記録されますが、詳細タブには出ません（公開は `AcquisitionUnits` で `Verified` にした単位だけ）。

## 最短の検証手順（Windows）

1. [GitHub Actions「Numeric statistics build, tests and development packages」](https://github.com/pengin0503/Rimworld-Statistics/actions/workflows/numeric-verification.yml) の、**同じコミットの成功した実行**から Artifacts を取得する。ZIP 内の `RimworldStatistics-unverified.zip` と `RimworldStatisticsVerifyHarness.zip` をそれぞれ展開し、両MODのフォルダを RimWorld の `Mods` に置く。実際のリリース版ではなく**同じコミットの本体とハーネス**を使う。
2. リポジトリのルートから、以下のコマンドを実行する。ゲームのパスは自分の環境に置き換える（Steam → RimWorld → 管理 → ローカルファイルを閲覧で確認）。
3. 使い捨てのコロニーを開始して、開発モード → Debug actions → `RWS Verify` → **1. Run guided full audit (MODIFIES TEST WORLD)** を1回実行する。
4. **テスト専用のセーブ**を1回正常に保存した後、**2. Read-only rescan after save (no game changes)** を選ぶ。保存時スナップショットの未観測項目を再確認できる。
5. `RimworldStatistics-verify-guide.md` を開いて優先順に残作業を確認する。前提を揃えたケースだけ再実行したい場合は **3. Retry only failed or skipped cases (MODIFIES TEST WORLD)** を選ぶ。

```powershell
powershell -NoProfile -File ".\tools\VerifyHarness\launch-windows.ps1" -GameExe "D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe" -Minimal
```

`-Minimal` は新規の使い捨てテストコロニー向け。元のMOD設定を**別フォルダにコピー**し、Vanilla・有効なDLC・Harmony・Rimworld-Statistics・VerifyHarness 以外をテスト環境のMODリストから除外して軽量化する。Hospitality / Recruitment / 他MODとの互換検証をするとき、または元のMOD構成を必要とする既存セーブを使用するときは **`-Minimal` を付けない**。

`launch-windows.ps1` は通常の `ModsConfig.xml`、通常セーブ、Steam起動オプション、ゲーム本体のMODファイルを書き換えない。独立した `%LOCALAPPDATA%\RimworldStatistics-Verify` 以下に専用データと構成を作成する。既存の無関係なフォルダを指定した場合は上書きを拒否する。MODのZIP展開・インストール自体は行わない。

通常のセーブを使う場合は、元のセーブを**コピー**して専用データフォルダの `Saves` へ入れ、コピーした方で試験する。自動ケースは研究・襲撃・敵の撃破などを発生させるため、**実際のセーブを直接使わないこと**。

PowerShell の実行ポリシーにより拒否された場合は、信頼できるこのリポジトリのスクリプトであることを確認した上で、一度のコマンドだけ `powershell -NoProfile -ExecutionPolicy Bypass -File ...` として起動する方法もある。

### 自動生成レポート

出力先は専用データフォルダ `%LOCALAPPDATA%\RimworldStatistics-Verify`（`-TestDataFolder` で変更可）。

| ファイル | 内容 |
|---|---|
| `RimworldStatistics-verify-guide.md` | **取得単位別に優先順**で原因・次の操作を示すレポート。自動ケースの結果、パッチ一覧、プレビュー値も含む |
| `RimworldStatistics-verify-metrics.csv` | 全264項目を1行ずつ列挙。取得状態、対応操作、「未検証」を明示 |
| `RimworldStatistics-verify-report.json` | 前回レポートと比較するための機械可読ファイル。ゲーム・キャンペーン・バージョンが一致した場合だけ新規観測/消失を比較 |
| `RimworldStatistics-verify.log` | 既存のケースログと今回の概要を追記 |

`guide.md` で優先度の高い順に処理するだけで、毎回264個のログを手動検索する必要がなくなる。`Observed` は数値を見つけた意味であり**数値の正確性を保証しない**。`NotObserved` は未観測を意味し、不具合の断定ではない。`DlcUnavailable` はDLC不在による適用外であり失敗ではない。

## ビルドと導入

本体と同じ参照（`.refs/` か `GameDir`・`HarmonyDll`）を使います。

```bash
bash tools/VerifyHarness/package.sh artifacts/RimworldStatisticsVerifyHarness.zip
```

zip を展開した `RimworldStatisticsVerifyHarness` フォルダを、本体と同じ `Mods` フォルダに置き、MOD 一覧で本体の後に有効にします。本体は `tools/package.sh` で作った同じコミットのものを使ってください。

## 起動

テスト用のフォルダを `-savedatafolder` に指定し、開く単位を `-rwsverify` で選びます（省略か `all` で全単位）。

```bash
./RimWorldLinux -savedatafolder=/path/to/rws-verify -rwsverify=research,mental
```

Windows では `RimWorldWin64.exe -savedatafolder=C:\rws-verify -rwsverify=all`。Steam では起動オプションに同じ引数を書きます。パスに `=` を含めないでください（ゲームの引数解析が `=` で分割します）。

起動後、`Player.log` に本体の警告 `RWS Verify harness opened unverified acquisition units for verification: …` が出ていれば開いています。開発モードを有効にし、デバッグアクションの「RWS Verify」を使います。

## デバッグアクション（RWS Verify）

| 項目 | 内容 |
|---|---|
| Status | ゲームの版、`Assembly-CSharp.dll` の SHA-256、Harmony の版、有効な DLC、単位ごとのゲート（recording / open, not applied / closed (DLC off) / closed (failed this session) / closed）と受け付けたイベント数、記録中のファミリーの範囲（since・through・continuous）、本体の Harmony が当てたメソッドの一覧 |
| Benchmark pollution totals | Biotech有効時、全ホームマップで `PollutionGrid.TotalPollution` とセル全走査の結果を比較。取得時間・セル数・`MATCH/MISMATCH` をファイルへ記録（マップへの変更なし） | 
| Checkpoint lineage | テスト用プロファイルにある、このゲームの campaignId のチェックポイント（ID・親・tick・UTC・衝突・圧縮、値はファイルだけ）と、詳細タブが使う代表・系譜 |
| Dump values | 次の保存で記録される値の一覧（保存と同じ PrepareSave → ToObservation を、確定せずに実行したもの） |
| Mark / Diff since mark | 今の値を覚え、その後に変わった値と、受け付けたイベント数の増分を出す。手動の確認（下記）に使う |
| Run all automated cases / Run one case | 下表の自動ケース |

結果はゲームのログと、テスト用フォルダの `RimworldStatistics-verify.log` に書きます。長い報告（Status、Dump、Diff）の全文はファイルだけに書きます。Status はタイトル画面とプレイ中の両方で表示される（各状態に個別の DebugAction を登録）。

**注意:** `medicine.hospital_bed` は同期呼出しから入院開始を確定できないため、開始しない場合は SKIP とします。これは本体の入院記録が正常だと検証できた意味ではありません。

## 一括検証（264項目のカバレッジ監査）

最も簡単な手順は、Steam の起動オプションへ `-savedatafolder=C:\\rws-verify -rwsverify=all` を指定し、テスト専用のセーブをロードしてから **開発モード → Debug actions → RWS Verify → 1. Run guided full audit (MODIFIES TEST WORLD)** を1回押す方法です。

この操作は次を順番に行います（その後、保存して「2. Read-only rescan」を実行）。

1. ゲームのバージョン、DLLハッシュ、Harmonyの適用先、全取得ゲートを記録する。
2. 従来の自動ケース18件を一括実行する。研究・襲撃・Pawn操作などで **テスト用コロニーが変更される** ため、普段のセーブでは実行しない。
3. 全264項目のカタログについて `OBSERVED`（記録済み数値あり）、`NOT_OBSERVED`（未観測）、`NOT_RECORDING`（取得ゲートなし）、`DLC_UNAVAILABLE`（DLC不在）、`DERIVED_ONLY`（ランキング等）を1行ずつ出力する。
4. ケース別PASS/FAIL/SKIP/ERRORと264項目の状態別件数をログへ記録し、優先順レポート・CSV・JSONを生成する。

**重要:** OBSERVED は取得データの存在確認であり、正しい値かどうかは保証しません。イベントを一括で発生させても未観測になる項目や、数日経過・保存・再読み込みが必要な項目があります。実機の数値正確性・セーブ/ロード・TPS は別に検証する必要があり、全項目が自動でVerifiedになる機能ではありません。セーブ時スナップショットの項目を観測したい場合、一度**テスト用データへ正常に保存**してから再実行してください。ログはテストフォルダ内だけに出力され、公開用MODにはこの機能を入れません。

## 自動ケース（18件）

すべて**使い捨てのテスト用コロニー**で実行してください（研究を完了させ、ひらめきと精神崩壊を起こし、敵を出して倒し、襲撃を起こします）。前提を満たさないケース（未完了の研究がない、敵対勢力がない など）は SKIP になります。

| ケース | 操作（1.7.4985 のゲームの経路） | 期待 |
|---|---|---|
| research.direct | 前提済みの未完了研究に `FinishProject` | 完了 +1 |
| research.prerequisites | 前提が未完了の研究に `FinishProject`（ゲームが前提を再帰的に完了） | 完了 + ゲームが完了させた件数 |
| research.repeat | 完了済みの研究に `FinishProject` | 完了 +0 |
| research.threshold | `AddProgress` で費用を超える（完了ダイアログが出る） | 完了 +1、所要日数の標本 +1 |
| mental.inspiration_debug | 理由 `"Debug gain"` で `TryStartInspiration` | ひらめき +0 |
| mental.inspiration | 別の理由で `TryStartInspiration` | ひらめき +1 |
| mental.forced_break | `TryDoMentalBreak` を直接呼ぶ（心情判定の外） | 精神崩壊 +0 |
| health_death.player_kill | 敵を出し、入植者を加害者・遠距離武器の `DamageInfo` で `Kill` | 撃破 +1、遠距離 +1 |
| health_death.enemy_kill | 別の敵を加害者にして `Kill` | 撃破 +0 |
| health_death.unattributed | `DamageInfo` なしで `Kill` | 撃破 +0 |
| down.enemy | 敵を出し `HealthUtility.DamageUntilDowned`（出血なし） | 敵ダウン +1、入植者ダウン +0 |
| raid.enemy | `RaidEnemy` を即時攻撃・端から徒歩で実行 | 襲撃 +1、EdgeWalkIn +1、人数の標本 +1 |
| raid.last_kill | 新しい襲撃を発生させ、出現した襲撃者をすべて実際の `Pawn.Kill` 経路で撃破 | 最後の撃破を含む `raid.kills.max` と、襲撃終了標本 +1 |
| medicine.withdrawal | 入植者に依存症を付け、化学的欲求の水準を 0 → 0.005 → 1 → 0 にする（実際の `Need_Chemical.CurLevel` → `Hediff_Addiction.Notify_NeedCategoryChanged`） | 禁断症状 +2。使った依存症は最後に取り除く |
| medicine.hospital_bed | 医療ベッドと自由入植者の存在を確認。同期デバッグアクションでは実際の `LayDown` toil を進められないため、ジョブの変更は行わない | SKIP（誤った FAIL や意図しないジョブ変更を防止）。実際の入院時間は手動で確認 |
| food.stock_recount | 現在のホームマップで `ResourceCounter.UpdateResourceCounts` | 食料の在庫フックが発火し、ほかの値は変わらない |
| food.growing_cells | 新しい栽培ゾーンに 1 セル足して取り除く（`Zone.AddCell / RemoveCell`） | 追跡中の栽培セル数が +1 してから元に戻る |
| caravan.ancient_danger | 祠の外のセル、次に監視中の封印された祠の中心を 2 回 `FogGrid.Unfog` | 古代の脅威の開封 +1（2 回目と外のセルは +0）。監視中の祠が現在のマップに無ければ SKIP |

陽性ケースはその単位のフックがイベントを受け付けたこと（`hooks` が 1 以上）も条件です。陰性ケースもフックが発火したうえで数えないことを確かめるため、同じ条件にしています。

ログの形式:

```text
[RWS-VERIFY] case=research.direct unit=research game=1.7.4985_rev1269 expected=research.live.completed:+1 actual=research.live.completed:+1 hooks=3 result=PASS note="Electricity"
```

`result` は PASS・FAIL・SKIP・ERROR。ERROR はケース自体が例外を出したもので、MOD の判定の正否は分かりません。

## 手動の確認

自動ケースにない確認（実装記録 §6 の表）は、Mark → ゲーム内で操作 → Diff since mark で値の変化を読み、表の期待値と比べます。例:

- **心情起因の精神崩壊**: 心情を下げて自然な崩壊を待つ → `mental.breaks.count` +1。開発ツール「(natural mood break)」では +0。
- **C0（保存・ロード・分岐）**: 研究完了 3 の時点で P を保存 → 2 件完了して A を保存 → P をロード → 1 件完了して B を保存 → Checkpoint lineage で、代表が tick の大きい方で、その `research.live.completed` が A なら 5、B なら 4（9 も 6 も出ない）、系譜が Branched であること（各チェックポイントの値はログファイルに出る。未検証の出力は詳細タブに出ない）。
- **性能**: 単位を 1 つだけ開いて（`-rwsverify=lifetime` など）、受け入れ計測 3（60 秒×5 回の中央値で TPS 低下 2% 以下）を測る。毎tick級の `SkillRecord.Learn`、`MentalBreakerTickInterval`、`CompRottable.TickInterval`、`GenPlace.TryPlaceThing`、`Pawn_RecordsTracker.Increment`、`Thing.Kill` は個別に測る。2026-11-08 追加分では、運搬のたびに呼ばれる `ResourceCounter.UpdateResourceCounts`（food）と、霧の解除でセルごとに呼ばれる `FogGrid.Unfog`（caravan）を測る。
- **2026-11-08 追加分（時間経過が要るもの）**: 入院期間（医療ベッドで回復させて起床 → 1 秒以上後に Diff で `medicine.hospital.stay_mean` の標本 +1）、食料不足のない最長期間（備蓄を食べ尽くす → 再び備蓄、Diff で値が区切れること）、栽培セル日あたりの収穫量（作物を収穫 → 分子 +収穫数、分母が時間とともに増えること）、帝国関係クエスト（成功後に保存し、テスト用プロファイルの保存時点の観測で `royalty.quest_success` を確認）、最少人数エンディング（開発モードの宇宙船の強制カウントダウンでは値が付かないこと、実際の宇宙船では使用中のカプセル数）。詳細は実装記録 §6 の追記行。

## 検証が通ったら

1. Status の版・SHA-256・パッチ一覧と、ケースのログを検証記録に残す。
2. `Source/RimworldStatistics.Core/Stats/AcquisitionUnits.cs` のその単位を `Verified` にする（ゲートが既定で開き、出力が公開される）。
3. 意図して失敗するテスト `NumericCatalogTests.NoUnitIsVerifiedWithoutAnInGameRun` を、検証記録に合わせて更新する。

## 静的な確認

```bash
bash tools/refcheck.sh tools/VerifyHarness/Mod/1.7/Assemblies/RimworldStatistics.VerifyHarness.dll
```

ゲームに依存しない部分（`Logic/`）は Core のテスト（`VerifyHarnessLogicTests`）でコンパイル・テストしています。ゲーム側のケースは実機でしか動かせず、この README の期待値は 1.7.4985 の逆コンパイル結果と本体の判定から決めたものです。

## 本体の実験的取得との違い（2026-11-08）

本体は通常のMOD設定から個々の未検証単位を選び、次回起動から取得を有効化できる。初期OFFで、取得時点の信頼性をプロファイルに保持する。通常取得のオプトインは、実機による検証や `AcquisitionStatus.Verified` への昇格を意味しない。

この開発専用VerifyHarnessは別MODのまま維持し、`-savedatafolder` と `-rwsverify` のテスト手順で使う。本体の製品ZIPに含まれない。
