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

**基本統計・取り込み・外部MOD統計基盤は実装済み。** Core自動テスト、RimWorld 1.7参照APIの静的照合、SDK互換性検査、配布ZIP検査をGitHub Actionsで実行しています。検証用RimWorld 1.7では限定的な自動ケースを実行し、通常環境で再スキャンとDPAのGame Component Tickを[限定的に確認](docs/performance/2026-11-09-standard-game-dpa-partial.md)しました。ただし264項目の数値正確性、保存処理の追加時間、長時間TPS悪化率、全MOD構成での動作は証明されていません。未検証項目は実験的なまま扱います。

- [設計仕様 v0.2（2026-11-06）](docs/superpowers/specs/2026-11-06-rimworld-statistics-design.md)
- [RimWorld 1.7 実現可能性調査（2026-11-06）](docs/research/2026-11-06-rimworld-1.7-feasibility.md)
- [実装計画（2026-11-06）](docs/superpowers/plans/2026-11-06-rimworld-statistics.md)
- [手動確認の手順書](docs/manual-test-checklist.md)
- 数値統計拡張: [確定設計](docs/superpowers/specs/2026-11-07-numeric-statistics-expansion-design.md)・[台帳](docs/superpowers/specs/2026-11-07-numeric-statistics-expansion-metrics.md)・[計画](docs/superpowers/plans/2026-11-07-numeric-statistics-expansion.md)・[実装・検証記録](docs/research/2026-11-07-numeric-statistics-expansion-verification.md)（Phase A/D1に加え、Phase B/C/D2の取得コードを実装。Phase B/C/D2は未検証の実験的機能として既定OFF）

## 実験的な数値統計と汎用SDK

本体はバニラ・対応DLCの全セーブ統計、264項目の実験的拡張統計、数値表示、チェックポイント、汎用の**外部MOD統計SDK v1.1**を担当します。**「詳細統計」には実装済みの全項目を表示**し、実験的／不明（未観測）の値も隠しません。未観測の値は「—」と「不明」で示し、**0として扱いません**。個別コロニーにのみ適用される項目はコロニー別表示で確認できます。外部MOD統計は、プロファイルに記録された提供元の定義がある場合のみ表示されます。

表示と取得は別です。264項目の正確性・長時間TPSは未検証のため、**実験的な取得処理は既定OFFのまま**です。新しく記録するには必要な取得単位をMOD設定で明示的に選び、ゲームを再起動してください。未確認の数値はVerifiedに昇格しません。導入前のイベントは遡及できません。

[外部MOD SDKの仕様](docs/sdk/external-provider-api-v1.md) · [P0取得ゲートの検証記録](docs/research/2026-11-08-experimental-statistics-p0-validation.md)

### 過去セーブからの復元可能性（調査中）

過去の `.rws` に**保存されている**履歴・カウンター・クエスト結果は、読み取り専用で集計できる範囲があります。一方、記録が残っていないイベントの日時・内訳を、実験的な統計取得をONにするだけで復元することはできません。

[264項目の復元可能性一覧](docs/research/2026-11-09-historical-recovery-inventory.csv)は、**イベント記録が必要214件／セーブ時点のデータを検証すべき41件／元データに依存するランキング9件**というソース上の分類です。**復元可能性の候補**であり、全項目が復元できるという意味ではありません。**v1.3.0以降では、保存済みのバニラ派閥との関係2項目と、確認できた自拠点マップ数1項目を実験的・部分的に復元できます**（いずれも保存時点の限定的な証拠であり、ゲーム全期間の完全な外交履歴や最大値を証明するものではありません）。分岐セーブの差分を無条件に合算せず、未取得値を0で埋めません。実験的な区間差分は直接のチェックポイント親子関係を証明できる場合に限る内部APIで、統計合計には加算しません。[実装済みの部分復元と制約](docs/research/2026-11-09-historical-recovery-stage-b-c.md)／[判定基準・実装計画](docs/superpowers/specs/2026-11-09-historical-save-recovery-design.md)を参照してください。

## 外部MOD統計アドオン（本体とは別配布）

**v1.2.1以降、他MOD固有の取得コードは本体に同梱されません。** 統計表示・記録・SDKはRimworld-Statistics本体が担当し、他MODの統計取得は独立したアドオンが担当します。アドオンのソース、テスト、CI、配布ZIPは専用リポジトリで管理します。

- **配布先：** [Rimworld-Statistics-mod-addon のリリース](https://github.com/pengin0503/Rimworld-Statistics-mod-addon/releases)
- **対応状況・詳細：** [アドオンリポジトリのREADME](https://github.com/pengin0503/Rimworld-Statistics-mod-addon)

| アドオン | 対象MOD | 取得する値・注意点 |
| --- | --- | --- |
| Smart Apparel Upgrade | Smart Apparel Upgrade **1.0.1** | 自動着替え用のWearジョブを**発行した回数**。実際の着替え完了回数ではありません |
| Hospitality | Hospitality / Hospitality (Continued)、監査済みの構成 | **保存時点**のゲスト人数・ゲストグループ数。歴代の宿泊客総数ではありません |
| Recruitment | Rimworld-Recruitment **1.1.2** | 保存時点の進行中募集依頼数、アドオン導入後に確定した新規募集依頼の開始件数 |

導入は、(1) Harmonyと本体を有効化、(2) 利用している対象MODに対応するアドオンだけを別途追加、(3) StatisticsのMOD設定で対応する実験的取得単位（`mod.<owner>.<family>`）を選択、(4) RimWorldを再起動、の順です。アドオンは**実験的・既定OFF**です。対象MODや本体の不在、対応外のバージョンでは新たな値を記録しません。不明な値を0と偽装しません。

**旧版からの引き継ぎ：** v1.2.0まで本体が取得したHospitalityとRecruitmentの値・来歴は保持します。Hospitalityの保存時スナップショットとRecruitmentの進行中募集数は既存の数値IDを維持します。一方、募集開始イベントは重複排除方式が変わったため、従来の`mod.pengin0503.recruitment.requests.started`と新アドオンの`mod.pengin0503.recruitment.requestsaddon.started`を**別系列**として保存し、勝手に合算しません。

セーブごとに導入MODが異なっても、過去に保存した統計と提供元情報は保持されます。アドオンを削除した期間や導入前のイベントは補完できず、取得できなかった期間はPartial/Unknownとして扱います。古い実測値を消去したり、欠測を0に置き換えたりしません。

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
