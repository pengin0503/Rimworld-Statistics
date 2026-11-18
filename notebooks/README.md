# Google Colab（ブラウザ版）での開発検証

Rimworld-Statistics の **ゲームを起動しない .NET 10 検証ツール**をブラウザ版Google ColabのCPUランタイムで使うための、独立した3つのノートブックです。

| ノートブック | 用途 | 直接開く |
| --- | --- | --- |
| [01_Save_Analysis.ipynb](01_Save_Analysis.ipynb) | 実セーブ読み取り・取り込み・SHA-256不変性 | [Colab](https://colab.research.google.com/github/pengin0503/Rimworld-Statistics/blob/main/notebooks/01_Save_Analysis.ipynb) |
| [02_Performance_Comparison.ipynb](02_Performance_Comparison.ipynb) | 2コミットの合成セーブ・プロファイルのA/B性能比較 | [Colab](https://colab.research.google.com/github/pengin0503/Rimworld-Statistics/blob/main/notebooks/02_Performance_Comparison.ipynb) |
| [03_Regression_Report.ipynb](03_Regression_Report.ipynb) | xUnit・Pythonテストと性能の回帰レポート | [Colab](https://colab.research.google.com/github/pengin0503/Rimworld-Statistics/blob/main/notebooks/03_Regression_Report.ipynb) |

## 実行手順

1. Colabでノートブックを開き、**ランタイム → ランタイムのタイプを変更 → CPU**を選ぶ。
2. 最初のパラメーターセルを編集する。01の実セーブ利用時だけ、下記手順でデータを投入する。
3. **ランタイム → すべてのセルを実行**。初回は公開GitHubと.NET 10 SDKの取得が必要。
4. Colabのファイルパネルから `/content/rws-colab-output/` のCSV・JSON・PNGを取得する。

3本は互いに独立し、GitHubの公開mainを実行時に取得します。02の比較対象はパラメーターで指定できます。GitHubの作業ブランチからノートブックを開いた場合も、実行対象のコードはmainです。

## 実セーブを渡す場合（01のみ）

- **数本**：Colab左側のファイルパネルから `.rws` / `.rws.old` をアップロードし、`SAVE_SOURCE = "/content"` のまま実行する。
- **多数**：Google Driveに保存したフォルダを指定。`USE_DRIVE = True` にし、`SAVE_SOURCE = "/content/drive/MyDrive/..."` に変更する。元ファイルをColabの一時領域にコピーしてから解析する。
- 指定フォルダ直下のファイルだけを対象にし、上限は1000ファイル。コピー前後のSHA-256検査と、既存C#検証ツールによる入力不変性検査を行う。
- Cloudflare R2は既存方針通り**読み取り専用**。認証情報をノートブックや出力へ記載せず、許可された方法で得たコピーを使用する。

**プライバシー**：セーブをColabのVMやDriveに置くこと自体はデータ転送に当たります。リポジトリやIssueに実セーブを公開しないでください。01のレポートには個別ファイル名・本文・ハッシュ値は含めませんが、集計情報を共有する際も内容を確認してください。終了後はColabランタイムを削除してください。

## 出力

| ノートブック | 生成ファイル |
| --- | --- |
| 01 | `save-analysis.json`, `save-import-measurements.csv`, `save-import-latency.png` |
| 02 | `commit-performance-report.json`, `commit-import-comparison.csv`, `commit-import-summary.csv`, `commit-import-comparison.png`。任意でプロフィール比較CSV/PNG |
| 03 | `regression-report.json`, `regression-benchmark.csv`, `regression-latency.png` |

03が生成した `regression-benchmark.csv` を次回の `REFERENCE_CSV` として使えます。ただし異なるColabセッションの処理時間差を確定的な性能回帰とは判断できません。**02の同一VM・交互A/B比較を優先**してください。

## 実行対象と限界

- 01は `tools/RealSaveImport` と `tools/SaveImportBenchmark --dir` を実行。セーブの取り込み・不変性を検査しますが、すべての統計項目の意味的正確性を独立に証明するものではありません。
- 02は `tools/SaveImportBenchmark` と `tools/ProfileBenchmark` を実行。合成データによるマイクロベンチマークであり、ゲーム内TPS・FPSを測るものではありません。
- 03は `Tests/RimworldStatistics.Core.Tests`、`tools/tests`、`tools/SaveImportBenchmark` を実行。PASSはその範囲のオフラインテスト結果だけを示します。
- 実ゲーム本体・Harmony・MOD互換性・UIの受け入れ検証は [RimWorld-for-mod-test](https://github.com/pengin0503/RimWorld-for-mod-test) で実施してください。
- Colabのリソースや継続実行時間は固定されません。GPUを有効にしても既存のC#処理が自動的に高速化されるわけではありません。

## 静的検査

`python3 -m unittest discover -s tools/tests -p 'test_colab_notebooks.py' -v` でノートブックJSONとPythonコードセルの構文を検査します。実際のColabでのSDK導入、NuGet取得、Driveマウント、.NET実行は別途確認が必要です。
