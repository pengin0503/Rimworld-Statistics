# CSV export

Use **Data management → Export CSV** on the statistics window. The scope printed on the button is captured with the current view. Each export writes five UTF-8 CSV files (with BOM and CRLF row endings) and a JSON manifest to a new timestamped folder.

| File | Contents |
|---|---|
| `summary.csv` | Overall metrics and their breakdowns. The `value` column is numeric and culture-independent. `value_display` and `label` use the game language. Holder keys refer to the matching row in `colonies.csv`. |
| `colony_metrics.csv` | Metrics and breakdowns for colonies that contribute to the selected scope. Join by `colony_key` and `metric_key`. |
| `colonies.csv` | Every colony. `in_scope` shows group membership and `excluded` shows the player's independent exclusion setting. Basic values with missing evidence are empty. |
| `rankings.csv` | Every measured rank in both directions, with ties shared. It includes all ranked colonies regardless of the tab's top-ten display setting. |
| `endings.csv` | Endings from contributing colonies with the current dev-mode setting. A trace can have no known ordinal, departure count, or time. |
| `manifest.json` | UTC creation time, MOD version, scope, language, and the number of data rows in each CSV. |

Columns ending in `_day` use 60,000 ticks per RimWorld day. Missing numeric values are blank. Coverage describes the recorded interval, evidence describes the acquisition evidence, and availability describes how many target colonies supplied data. Text that spreadsheet programs may interpret as a formula is prefixed with a single quote.
