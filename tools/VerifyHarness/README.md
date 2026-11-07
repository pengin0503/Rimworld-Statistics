# 検証ハーネス（開発専用）

Rimworld Statistics の未検証の取得単位（[実装記録](../../docs/research/2026-11-07-numeric-statistics-phase-c-implementation-notes.md) §1）を、テスト用のゲームでだけ開いて、実機で確かめるための別 MOD です。配布物（`tools/package.sh` の zip、公開ワークフロー）には入りません。

- `packageId` は `pengin0503.rimworldstatistics.verifyharness`。本体（`pengin0503.rimworldstatistics`）と Harmony の後にロードします。
- 起動時に `-savedatafolder=` が無ければ、**何も開きません**（エラーログを出すだけ）。未検証のコードが記録した値が通常のプロファイルに入ると、その単位を `Verified` にした後で利用者に表示されてしまうためです。
- 開いた単位の値はセーブとテスト用プロファイルに記録されますが、詳細タブには出ません（公開は `AcquisitionUnits` で `Verified` にした単位だけ）。

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
| Checkpoint lineage | テスト用プロファイルにある、このゲームの campaignId のチェックポイント（ID・親・tick・UTC・衝突・圧縮、値はファイルだけ）と、詳細タブが使う代表・系譜 |
| Dump values | 次の保存で記録される値の一覧（保存と同じ PrepareSave → ToObservation を、確定せずに実行したもの） |
| Mark / Diff since mark | 今の値を覚え、その後に変わった値と、受け付けたイベント数の増分を出す。手動の確認（下記）に使う |
| Run all automated cases / Run one case | 下表の自動ケース |

結果はゲームのログと、テスト用フォルダの `RimworldStatistics-verify.log` に書きます。長い報告（Status、Dump、Diff）の全文はファイルだけに書きます。

## 自動ケース

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
- **性能**: 単位を 1 つだけ開いて（`-rwsverify=lifetime` など）、受け入れ計測 3（60 秒×5 回の中央値で TPS 低下 2% 以下）を測る。毎tick級の `SkillRecord.Learn`、`MentalBreakerTickInterval`、`CompRottable.TickInterval`、`GenPlace.TryPlaceThing`、`Pawn_RecordsTracker.Increment`、`Thing.Kill` は個別に測る。

## 検証が通ったら

1. Status の版・SHA-256・パッチ一覧と、ケースのログを検証記録に残す。
2. `Source/RimworldStatistics.Core/Stats/AcquisitionUnits.cs` のその単位を `Verified` にする（ゲートが既定で開き、出力が公開される）。
3. 意図して失敗するテスト `NumericCatalogTests.NoUnitIsVerifiedWithoutAnInGameRun` を、検証記録に合わせて更新する。

## 静的な確認

```bash
bash tools/refcheck.sh tools/VerifyHarness/Mod/1.7/Assemblies/RimworldStatistics.VerifyHarness.dll
```

ゲームに依存しない部分（`Logic/`）は Core のテスト（`VerifyHarnessLogicTests`）でコンパイル・テストしています。ゲーム側のケースは実機でしか動かせず、この README の期待値は 1.7.4985 の逆コンパイル結果と本体の判定から決めたものです。
