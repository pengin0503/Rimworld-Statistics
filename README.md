# Rimworld-Statistics

RimWorld 1.7 向けの MOD です。全セーブ・全コロニーを通した、通算のプレイ統計を表示します。

タイトル画面の「通算統計」ボタンから、どのセーブもロードせずに、次のような値を確認できます。
- 通算ゲーム内日数
- エンディング到達回数（種類別）
- 最高資産の合計
- コロニー数、実プレイ時間、襲撃・死亡の累計など

「詳細統計」タブでは、取り込んだセーブの履歴グラフ・ゲームの累計カウンター・セーブに残ったクエストから計算した数値（資産・人口と心情・襲撃・クエスト・コロニーの順位など）を、全コロニーまたはコロニー別に一覧できます。グラフは表示しません。

MOD 導入前のセーブ（1.5〜1.7）も読み取り専用で取り込みます。セーブには書き込みません。

前提 MOD: Harmony

## 状況

**基本統計・取り込み・外部MOD統計基盤は実装済み。** Core自動テスト、RimWorld 1.7参照APIの静的照合、SDK互換性検査、配布ZIP検査をGitHub Actionsで実行しています。検証用RimWorld 1.7では限定的な自動ケースを実行しましたが、264項目の正確性、長時間TPS、全MOD構成での動作は証明されていません。追加の実機検証は実施しない方針のため、未検証項目は実験的なまま扱います。

- [設計仕様 v0.2（2026-11-06）](docs/superpowers/specs/2026-11-06-rimworld-statistics-design.md)
- [RimWorld 1.7 実現可能性調査（2026-11-06）](docs/research/2026-11-06-rimworld-1.7-feasibility.md)
- [実装計画（2026-11-06）](docs/superpowers/plans/2026-11-06-rimworld-statistics.md)
- [手動確認の手順書](docs/manual-test-checklist.md)
- 数値統計拡張: [確定設計](docs/superpowers/specs/2026-11-07-numeric-statistics-expansion-design.md)・[台帳](docs/superpowers/specs/2026-11-07-numeric-statistics-expansion-metrics.md)・[計画](docs/superpowers/plans/2026-11-07-numeric-statistics-expansion.md)・[実装・検証記録](docs/research/2026-11-07-numeric-statistics-expansion-verification.md)（Phase A/D1に加え、Phase B/C/D2の取得コードを実装。Phase B/C/D2は未検証の実験的機能として既定OFF）

## 実験的な統計取得と他MOD対応（v1.2.0）

**264項目の拡張統計は取得コードを実装済みですが、全項目の正確性は未確認です。** 内蔵のPhase B/C/D2取得単位は既定OFFです。「MOD設定」で実験的な統計取得を許可し、必要な項目を個別に選んで**ゲームを再起動**してください。過去に保存された実験統計の表示は、取得と独立してON/OFFできます。取得開始前の履歴はさかのぼって補完されません。`Unverified` の取得を `Verified` と表示しません。

一方、ソースを監査した**組み込みアダプター**（対応するHospitality／Rimworld-Recruitment）は、対象MODと必要なAPIが一致した場合に例外的に自動で記録を開始します。外部SDK経由の任意のMODは自動で許可されず、明示的な有効化が必要です。自動記録でも信頼性はExperimentalのままです。

RimWorld 1.7 用の **Colonist Recruitment 1.1.2**（`pengin0503.recruitment`）が存在し、バージョンと実装APIが一致する場合のみ、次の2項目を取得できます。

- **募集依頼の開始回数**：募集依頼が実際に作成されたときの累積イベント数。同じ依頼IDを重複計上しません。導入・有効化前の依頼は復元しません。
- **進行中の募集依頼数**：ゲームのセーブに成功した時点で進行中だった依頼の件数。履歴の累積数ではありません。

対象MODがない場合やAPIが変わった場合は取得せず、存在しない値を0と見なしません。過去に記録したデータ・取得時のバージョン・信頼性は残ります。取得内容の正確性、MOD競合、ゲーム内UI、TPS負荷には未検証の範囲があります。これらは通常機能と区別した**実験的な機能**として配布物に含まれます。

**Hospitality / Hospitality (Continued)** の組み込みアダプターは、宿泊客の現在人数および訪問グループの現在数を保存時点で記録します。*これまで宿泊した客の累積人数*は取得しません。詳細は[組み込みアダプター](docs/sdk/automatic-built-in-adapters.md)を参照してください。

**外部MOD統計SDK v1.1**（実験的）は、MOD作者がイベント数値や保存時スナップショットを登録できます。独立したサンプルMODとABI確認を用意していますが、公開APIとしての完全な互換性保証はありません。[SDK仕様](docs/sdk/external-provider-api-v1.md)を参照してください。Anomaly専用の統計は未実装です。

- [P0実装・検証の範囲](docs/research/2026-11-08-experimental-statistics-p0-validation.md)
- [P1 Provider基盤](docs/research/2026-11-09-mod-metric-providers-p1-validation.md)
- [P2 実MOD監査と検証項目](docs/research/2026-11-09-recruitment-provider-pilot-audit.md)

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
