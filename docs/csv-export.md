# CSV出力仕様 / CSV Export

**言語 / Language:** [日本語](#日本語) | [English](#english)

---

## 日本語

統計ウィンドウの **「データ管理」 → 「CSV出力」** から、現在選択している集計範囲（全コロニーまたは選択中のコロニーグループ）のデータを書き出せます。

実行するたびに、RimWorldセーブデータフォルダー配下の `RimworldStatistics/exports/<UTC日時-ID>/` に新しいフォルダーが作成され、5つのCSVファイル（UTF-8 BOM付き・CRLF改行）と1つのJSONマニフェストが出力されます。

### 出力ファイル一覧

| ファイル名 | 内容と主な列 |
| --- | --- |
| `summary.csv` | 選択範囲全体の集計値と内訳。<br>列：`scope`, `metric_key`, `breakdown`, `label`, `category`, `unit`, `aggregation`, `value`, `value_display`, `coverage`, `evidence`, `availability`, `verification`, `provenance`, `acquisition_unit`, `owner_mod`, `colonies_measured`, `colonies_target`, `holder_colony_key`, `holder_colony_name`, `since_day`, `through_day`<br>`value` は言語設定に依存しない数値、`label` と `value_display` は現在のゲーム言語の表示文字列です。記録保持コロニー（`holder_colony_key`）は `colonies.csv` と対応します。 |
| `colony_metrics.csv` | 選択範囲に含まれ、かつ除外されていない各コロニーの統計値と内訳。<br>列：`colony_key`, `colony_name`, `metric_key`, `breakdown`, `label`, `unit`, `value`, `coverage`, `evidence`, `since_day`, `through_day`<br>`colony_key` と `metric_key` で他ファイルと結合できます。 |
| `colonies.csv` | 登録されている全コロニーの基本情報。<br>列：`colony_key`, `name`, `world`, `scenario`, `storyteller`, `difficulty`, `permadeath`, `excluded`, `in_scope`, `days`, `real_play_hours`, `peak_wealth`, `last_wealth`, `raids`, `threats`, `killed`, `departed`, `max_population`, `endings`, `saves`, `last_played_utc`, `game_versions`<br>`in_scope` は現在のグループ範囲内か、`excluded` はプレイヤーによる個別除外（`合計から外す`）を示します。 |
| `rankings.csv` | 順位付け可能な全統計項目の昇順（`ascending`）・降順（`descending`）ランキング（同順位は同順扱い）。画面上の「上位10件」表示設定に関わらず全順位を出力します。<br>列：`metric_key`, `label`, `direction`, `rank`, `colony_key`, `colony_name`, `value`, `coverage`, `evidence` |
| `endings.csv` | 集計対象コロニーのエンディング一覧（現在の開発者モード表示設定を反映）。導入前セーブや痕跡由来の記録では、到達順（`ordinal`）・旅立った人数（`departed`）・日時（`time_utc`）が空欄になる場合があります。<br>列：`time_utc`, `colony_key`, `colony_name`, `ending_type`, `ordinal`, `departed`, `source` |
| `manifest.json` | 出力日時（UTC）、MODバージョン、選択範囲名、出力時のゲーム言語、各CSVのデータ行数、本ドキュメントへの参照URLを記録します。 |

### 値の形式と注意事項

- **欠測値**：未取得・測定根拠のない数値は `0` ではなく**空欄**になります。
- **日数の換算**：`_day` で終わる列（`since_day`, `through_day`）は、RimWorldの1日＝60,000ティックで換算した実数です。
- **記録状態の列**：`coverage` は記録区間（全体／部分／未取得など）、`evidence` は取得根拠、`availability` は対象コロニーのうち何件で値が得られたかを表します。
- **表計算ソフト対策**：表計算ソフトが数式として誤解釈する先頭文字を持つ文字列には、先頭にシングルクォート（`'`）が付与されます。

---

## English

Use **Data management → Export CSV** in the statistics window to export data for the currently selected scope (**All colonies** or the selected colony group).

Each export creates a new timestamped folder under `<RimWorld SaveDataFolder>/RimworldStatistics/exports/<UTC-timestamp-ID>/` containing five UTF-8 CSV files (with BOM and CRLF line endings) and one JSON manifest.

### Output Files

| File | Contents & Columns |
| --- | --- |
| `summary.csv` | Overall metrics and breakdowns for the selected scope.<br>Columns: `scope`, `metric_key`, `breakdown`, `label`, `category`, `unit`, `aggregation`, `value`, `value_display`, `coverage`, `evidence`, `availability`, `verification`, `provenance`, `acquisition_unit`, `owner_mod`, `colonies_measured`, `colonies_target`, `holder_colony_key`, `holder_colony_name`, `since_day`, `through_day`<br>`value` is culture-independent numeric data; `label` and `value_display` use the current game language. `holder_colony_key` matches `colonies.csv`. |
| `colony_metrics.csv` | Metrics and breakdowns for non-excluded colonies within the selected scope.<br>Columns: `colony_key`, `colony_name`, `metric_key`, `breakdown`, `label`, `unit`, `value`, `coverage`, `evidence`, `since_day`, `through_day`<br>Join with other files using `colony_key` and `metric_key`. |
| `colonies.csv` | Every known colony.<br>Columns: `colony_key`, `name`, `world`, `scenario`, `storyteller`, `difficulty`, `permadeath`, `excluded`, `in_scope`, `days`, `real_play_hours`, `peak_wealth`, `last_wealth`, `raids`, `threats`, `killed`, `departed`, `max_population`, `endings`, `saves`, `last_played_utc`, `game_versions`<br>`in_scope` indicates membership in the active group, and `excluded` indicates the player's per-colony exclusion setting. |
| `rankings.csv` | All measured ranks in both directions (`descending` and `ascending`), with ties shared. Includes all ranked colonies regardless of the UI's top-10 display toggle.<br>Columns: `metric_key`, `label`, `direction`, `rank`, `colony_key`, `colony_name`, `value`, `coverage`, `evidence` |
| `endings.csv` | Endings from contributing colonies under the current development-mode ending setting. Trace-based records may have blank `ordinal`, `departed`, or `time_utc` fields.<br>Columns: `time_utc`, `colony_key`, `colony_name`, `ending_type`, `ordinal`, `departed`, `source` |
| `manifest.json` | Records UTC creation timestamp, mod version, active scope name, game language, data row counts per CSV, and the URL to this specification. |

### Value Formatting & Notes

- **Missing Values**: Unmeasured or unknown numeric values are **blank**, never `0`.
- **Day Conversion**: Columns ending in `_day` (`since_day`, `through_day`) use `60,000` ticks per RimWorld in-game day.
- **Metadata Columns**: `coverage` describes the recorded span, `evidence` describes the acquisition evidence, and `availability` describes how many target colonies supplied measured data.
- **Spreadsheet Safety**: Text strings that spreadsheet programs could interpret as formulas are prefixed with a single quote (`'`).
