# READMEから移動した開発・検証情報（2026-11-11）

この文書は、利用者向けのREADMEから開発者・保守者向けの情報を移動した記録です。元の記述を保持しており、設計・検証・移行に関するリンク、ビルド手順、当時の検証状況を参照できます。

**注意:** 以下のPR番号、未マージ表記、項目数、リリース状態、ゲーム検証結果は、移動前のREADMEにあった**当時の記録**です。現在の状態を保証するものではありません。最新版のソースコード・CI・リリースと照合してください。開発資料の起点は[仕様の履歴・状態索引](../superpowers/README-current-and-historical.md)です。

---

**新規統計の採用原則:** 全コロニー横断の通算実績・歴代記録・比較に加え、**各コロニーの代表保存時点の人数・物資・設備等を横断して合計し、最多・最少コロニーを求める統計**も対象です。単一セーブの「最新値」だけを表示する重複項目は増やしません。**ただし2026-11-11にユーザーが削除を明示した10項目（旧発電機・収納内在庫は別途廃止）は例外です。** 現行の正式採否は[19項目の存廃確定記録](../research/2026-11-11-approved-metric-retention-removal.md)に従います。[横断性の監査](../research/2026-11-10-cross-colony-metric-scope-audit.md)・[集計/ランキング候補の追加方針](../research/2026-11-10-cross-colony-aggregation-candidates.md)を参照。

## 状況

**基本統計・取り込み・外部MOD統計基盤は実装済み。** Core自動テスト、RimWorld 1.7参照APIの静的照合、SDK互換性検査、配布ZIP検査をGitHub Actionsで実行しています。検証用RimWorld 1.7では限定的な自動ケースを実行し、通常環境で再スキャンとDPAのGame Component Tickを[限定的に確認](../performance/2026-11-09-standard-game-dpa-partial.md)しました。ただし実験的取得項目を含む全登録統計の数値正確性、保存処理の追加時間、長時間TPS悪化率、全MOD構成での動作は証明されていません。未検証項目は実験的なまま扱います。

- [設計仕様 v0.2（2026-11-06）](../superpowers/specs/2026-11-06-rimworld-statistics-design.md)
- [RimWorld 1.7 実現可能性調査（2026-11-06）](../research/2026-11-06-rimworld-1.7-feasibility.md)
- [実装計画（2026-11-06）](../superpowers/plans/2026-11-06-rimworld-statistics.md)
- [手動確認の手順書](../manual-test-checklist.md)
- 数値統計拡張: [確定設計](../superpowers/specs/2026-11-07-numeric-statistics-expansion-design.md)・[台帳](../superpowers/specs/2026-11-07-numeric-statistics-expansion-metrics.md)・[計画](../superpowers/plans/2026-11-07-numeric-statistics-expansion.md)・[実装・検証記録](../research/2026-11-07-numeric-statistics-expansion-verification.md)（Phase A/D1に加え、Phase B/C/D2の取得コードを実装。Phase B/C/D2は未検証の実験的機能として既定OFF）

**開発版PR #102の確定スコープ（未マージ）：** [残す13群／削除6群＋従来の2件](../research/2026-11-11-approved-metric-retention-removal.md)。SaveReader v15で404定義（321基本＋83派生）。旧版v1.4.0は変更せず、削除した統計の旧プロファイル保存値を他のキーへ転用しません。 

### 開発・検証を引き継ぐ場合

過去の調査を最初から繰り返さず、**[現行仕様と旧仕様の区別](../superpowers/README-current-and-historical.md)**を起点にしてください。参照する資料は、[R2のバージョン別ゲーム本体・Defs調査](../research/2026-11-10-r2-private-reference-inventory.md)、[R2実セーブ49件・12ビルドの匿名台帳](../research/2026-11-10-r2-save-compatibility-census.md)、[49件の本番.NET読込検証結果](../testing/2026-11-10-private-r2-49-original-net-acceptance.md)、[残りの実装と受入条件](../superpowers/plans/2026-11-10-post-v1.4-five-save-improvements.md)です。これらには再現可能な取得手順・検証済み範囲・未検証の理由・CIの参照番号を記録してあります。元の非公開セーブ、全MOD一覧、ゲームバイナリは公開リポジトリへ保存していません。

### 開発版の1.7過去研究統計（PR #102・未公開）

[ゲーム本体の実DLL・ILと公式研究Defの照合記録](../research/2026-11-10-r2-game-source-1-7-research-recovery.md)を根拠として、1.7の旧セーブに保存されている研究進捗から**研究完了候補数・完了率・到達技術段階**を再構築する実験的な処理を追加しています。1.7.4985と1.7.5000は開発上同等と仮定しますが、実ゲームに導入されたMODが研究コストを書き換えている可能性があるため、復元値は**常にPartial（暫定）**です。旧セーブを変更せず、1.5/1.6からはこの方法で推定しません。正式版v1.4.0には未導入で、実装は[PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102)に保持しています。

## 実験的な数値統計と汎用SDK

本体はバニラ・対応DLCの全セーブ統計、264項目の実験的拡張統計、数値表示、チェックポイント、汎用の**外部MOD統計SDK v1.1**を担当します。**「詳細統計」には実装済みの全項目を表示**し、実験的／不明（未観測）の値も隠しません。未観測の値は「—」と「不明」で示し、**0として扱いません**。個別コロニーにのみ適用される項目はコロニー別表示で確認できます。外部MOD統計は、プロファイルに記録された提供元の定義がある場合のみ表示されます。

表示と取得は別です。264項目の正確性・長時間TPSは未検証のため、**実験的な取得処理は既定OFFのまま**です。新しく記録するには必要な取得単位をMOD設定で明示的に選び、ゲームを再起動してください。未確認の数値はVerifiedに昇格しません。導入前のイベントは遡及できません。

[外部MOD SDKの仕様](../sdk/external-provider-api-v1.md) · [P0取得ゲートの検証記録](../research/2026-11-08-experimental-statistics-p0-validation.md)

### 過去セーブからの復元可能性（調査中）

過去の `.rws` に**保存されている**履歴・カウンター・クエスト結果は、読み取り専用で集計できる範囲があります。一方、記録が残っていないイベントの日時・内訳を、実験的な統計取得をONにするだけで復元することはできません。

[264項目の復元可能性一覧](../research/2026-11-09-historical-recovery-inventory.csv)は、**イベント記録が必要214件／セーブ時点のデータを検証すべき41件／元データに依存するランキング9件**というソース上の分類です。**復元可能性の候補**であり、全項目が復元できるという意味ではありません。**v1.3.0以降では、保存済みのバニラ派閥との関係2項目と、確認できた自拠点マップ数1項目を実験的・部分的に復元できます**（いずれも保存時点の限定的な証拠であり、ゲーム全期間の完全な外交履歴や最大値を証明するものではありません）。分岐セーブの差分を無条件に合算せず、未取得値を0で埋めません。実験的な区間差分は直接のチェックポイント親子関係を証明できる場合に限る内部APIで、統計合計には加算しません。[実装済みの部分復元と制約](../research/2026-11-09-historical-recovery-stage-b-c.md)／[判定基準・実装計画](../superpowers/specs/2026-11-09-historical-save-recovery-design.md)を参照してください。

## 本体内蔵の外部MOD統計（開発版PR #103）

**別の統計アドオンは不要です。** この開発版ではRimworld-Statistics本体に4つの取得処理を内蔵し、Harmony・本体・統計を取得したいゲームプレイMODだけで利用できます。各取得処理は独立しており、対象MODがない場合に他の統計を阻害しません。

| ゲームプレイMOD | 本体に内蔵した統計 | 記録上の意味 |
| --- | --- | --- |
| **Hospitality (Continued)** | 滞在客数・グループ数・割当ゲストベッド数、累計来訪客・グループ、加入成功数（強制／任意） | 保存時点の数値と、統合後のイベント累計を別系列で保持 |
| **Rimworld-Recruitment 1.1.2** | 進行中募集数、募集開始回数、支払銀の累計、抽選した予定応募人数の累計 | 予定応募人数は実際の加入者数と異なる |
| **Dubs Bad Hygiene 3.2.3260** | 配管貯水量・タンク容量（L）、ポンプ能力・水源供給能力（L/ゲーム内日） | 保存時点の容量・能力であり、実際の累計生産・消費量ではない |
| **Smart Apparel Upgrade 1.0.1** | 自動Wearジョブの発行回数 | 着替え成功回数ではない |

実装済みの項目は**実験的・取得既定OFF**です。本体のMOD設定で対象の `mod.<owner>.<family>` 取得単位を有効化し、RimWorldを再起動してください。対象MODと互換ソースが見つからないときは、取得を行わず「不明」として扱います。

**既存アドオンからの移行：** `pengin0503.rws.hospitalityaddon`、`pengin0503.rws.recruitmentaddon`、`pengin0503.rws.dubsbadhygieneaddon`、`pengin0503.rws.smartappareladdon` は、本体統合版が利用できるようになったら削除してください。同じアドオンをまだ導入している間は、その対象について本体内蔵版のフックを無効化して二重登録を防ぎます。削除して再起動した後に、**同じ統計ID・改訂番号**で本体版が取得を引き継ぎます。旧統計は新系列へ勝手に足し合わせません。

**対象MODを削除しても既に保存した統計は残ります。** 本体のプロファイルは取得元MODのインストール状況とは独立して数値・取得元・改訂番号を保存します。外した期間の新イベントは取得できず、過去記録を0に書き換えることもありません。翻訳文字列も本体に含めるため、旧アドオンを削除しても項目名が失われません。

**v1.5.0から4種類のMOD統計を本体に同梱しています。** [統合PR #103](https://github.com/pengin0503/Rimworld-Statistics/pull/103)で実装済みです。実ゲームでの各統計の数値照合・TPS長時間検証は未完了のため、取得機能は実験的・既定OFFです。外部開発者向け汎用SDK v1.1は引き続き利用できます。

## ビルド（開発者向け）

.NET 10 SDK が必要です。参照するゲームの DLL と Harmony は、環境変数 `GameDir`（`Assembly-CSharp.dll` と `UnityStubs.dll` のあるフォルダ）と `HarmonyDll`（Harmony の `1.7/Assemblies/0Harmony.dll`）で指定します。指定しなければ `.refs/game` と `.refs/harmony/0Harmony.dll` を使います。

```bash
dotnet test
```

```bash
dotnet build -c Release
```

```bash
bash tools/package.sh artifacts/RimworldStatistics.zip
```

開発用の入力（参照 DLL、実セーブ、監査済み索引）は、R2 の環境変数（`R2_ENDPOINT`、`R2_BUCKET`、`AWS_ACCESS_KEY_ID`、`AWS_SECRET_ACCESS_KEY`）があれば `tools/fetch_dev_inputs.py` で取得できます。実セーブはリポジトリに入れません。

```bash
python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install -q boto3
```

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py refs .refs
```

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py audit .devinputs/audit
```

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py saves .devinputs/saves
```

```bash
RWSTATS_REAL_SAVES_DIR=$PWD/.devinputs/saves dotnet test
```

```bash
bash tools/refcheck.sh && bash tools/check_patches.sh && bash tools/check_keys.sh
```

| ツール | 内容 |
|---|---|
| `tools/refcheck.sh` | 作った DLL が参照するゲームの型とメンバーが、1.7.4985 に存在するか |
| `tools/check_patches.sh` | Harmony のパッチ対象と、注入する引数名が 1.7.4985 に存在するか |
| `tools/check_keys.sh` | コードで使う翻訳キーが、日本語と英語の両方にあるか |
