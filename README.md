# Rimworld Statistics

全セーブ・全コロニー横断のプレイ統計をタイトル画面から閲覧できる RimWorld 1.7 向けMOD  
Cross-save, cross-colony lifetime statistics viewer on the title screen for RimWorld 1.7

**言語 / Language:** [日本語](#日本語) | [English](#english)

---

## 日本語

[主な機能](#主な機能) · [導入と更新](#導入と更新) · [基本操作](#基本操作) · [対応MOD](#対応mod) · [統計の記録範囲と注意事項](#統計の記録範囲と注意事項) · [よくある質問](#よくある質問) · [関連資料](#関連資料)

過去にプレイしたコロニーの記録を集約し、RimWorldの**タイトル画面から、セーブデータを個別にロードせずに**閲覧できるMODです。セーブフォルダ内のセーブデータと、本MODが保存する統計プロフィールをもとに全コロニーの実績を集計します。

- **対応バージョン**：RimWorld 1.7
- **必須MOD**：[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- **対応言語（12言語）**：日本語、英語、中国語（簡体字・繁体字）、韓国語、ドイツ語、フランス語、スペイン語（スペイン・ラテンアメリカ）、ポルトガル語（ポルトガル・ブラジル）、ロシア語（RimWorld本体の言語設定に従います）

### 主な機能

- **全コロニーの通算実績**：ゲーム内日数、実プレイ時間、コロニー数、資産の最高・最終記録、襲撃・大脅威・死亡数、種類別のエンディング到達履歴などを一括確認できます。
- **340項目以上の詳細統計**：資産、人口、種族・ゼノタイプ、戦闘、建築、生産、取引、クエスト、医療、各DLC（Royalty / Ideology / Biotech / Odyssey）、対応MODの統計を一覧・検索・絞り込みできます。対応項目では1日あたりの平均値も表示します。
- **グループ集計・横並び比較・ランキング**：任意のコロニーグループ作成、全コロニー・グループ・個別コロニーを最大4列で並べる比較表示、項目別の歴代コロニーランキングに対応しています。
- **グラフと推移の比較**：統計項目をクリックするとコロニー別・内訳別の棒グラフを表示し、さらにコロニー別の履歴折れ線グラフへ進めます。履歴グラフでは他コロニーとの重ね合わせ（最大2件追加）や、連続性が証明された2時点間の増減比較が可能です。
- **MOD導入前セーブの取り込み**：MOD導入前に作成された RimWorld 1.5〜1.7 のセーブデータも、対応する範囲で**読み取り専用**で解析します。元のセーブファイルは一切書き換えません。
- **CSV出力とバックアップ**：集計結果のCSV書き出し（UTF-8 BOM付き）および統計プロフィールの自動世代バックアップ・手動ZIPバックアップ／復元機能を備えています。

### 導入と更新

#### Steam Workshopから導入する場合（推奨）

1. Steam Workshopで [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) と [Rimworld Statistics](https://steamcommunity.com/sharedfiles/filedetails/?id=3837802361) をサブスクライブします。
2. ゲーム内のMOD一覧で **Harmony → Rimworld Statistics** の順に有効化し、ゲームを再起動します。
3. 更新はSteam側で自動適用されます。

#### 手動で導入・更新する場合（GitHub Releases）

1. [Releases](https://github.com/pengin0503/Rimworld-Statistics/releases) から最新版のアーカイブを取得し、展開した `RimworldStatistics` フォルダーを RimWorld の `Mods` フォルダーに配置します。
2. ゲーム内のMOD一覧で **Harmony → Rimworld Statistics** の順に有効化し、ゲームを再起動します。
3. **手動更新時の注意**：古いファイルが残るのを防ぐため、上書き展開はせず、既存の `Mods/RimworldStatistics` フォルダーを削除してから新しいフォルダーを配置してください（統計プロフィールはセーブデータ側の別フォルダーに保存されるため削除されません）。

#### 旧統計アドオンからの移行

以前の独立した統計アドオン（Hospitality / Recruitment / Dubs Bad Hygiene / Smart Apparel Upgrade 向け）を導入していた場合は、現在すべて本MOD本体に統合されているため無効化してください。対象のゲームプレイMOD本体を削除する必要はありません。

### 基本操作

1. **統計画面を開く**  
   タイトル画面の **「通算統計」**（「オプション」の上）をクリックすると統計ウィンドウが開きます。初期設定ではタイトル画面を開いた際にセーブデータの自動取り込みが行われます。
2. **集計対象の絞り込みと除外**  
   - ウィンドウ上部の **「全コロニー」** ドロップダウンから **「グループを編集…」** を選ぶと、条件やプレイスタイル別のコロニーグループを作成して集計範囲を切り替えられます（`概要`・`詳細統計`・`ランキング`・`取得状況`・CSV出力に反映）。
   - 特定のコロニーを通算合計やランキングから外したい場合は、**「コロニー」** タブの **「合計から外す」** にチェックを入れます。
3. **7つのタブの使い分け**

| タブ | 主な内容 |
| --- | --- |
| **概要** | 通算サマリーカードと「お気に入りの統計」を表示。`★` でお気に入り登録・並び替えが可能。 |
| **エンディング** | 到達したエンディングの日時、コロニー、種類、何回目の到達か、旅立った人数、記録元を一覧表示。 |
| **コロニー** | 全コロニーの基本成績一覧と並び替え、`合計から外す` の切り替え。行をクリックすると取り込み済みセーブの内訳を確認可能。 |
| **詳細統計** | 全統計の一覧・検索・カテゴリ／状態フィルター。`全コロニー`・`コロニー別`・`比較`（最大4列）を切り替え可能。 |
| **ランキング** | 統計項目ごとに全コロニーを大きい順／小さい順で順位表示（上位10件／全順位）。 |
| **取得状況** | 各統計が現在記録中か、DLC・対応MOD未導入か、取得済み／部分記録／未取得／実験的かを確認可能。 |
| **データ管理** | セーブの `再スキャン`・`強制再検査`、過去セーブの区間差分確認、プロフィールのバックアップ／復元、`CSV出力` を実行。 |

4. **グラフと履歴の比較**  
   `概要` や `詳細統計` の項目をクリックすると棒グラフ（`コロニー別`／`内訳別`）が開きます。`コロニー別` でコロニーを選択すると日数ごとの履歴折れ線グラフが開き、**「コロニーを追加…」** で別コロニーの推移を重ねたり、グラフ上の2点クリック（または **「最初と最後」**）で期間内の増減を確認したりできます。
5. **MOD設定**  
   `オプション` → `MOD設定` → **「通算統計（Rimworld Statistics）」** から、ウィンドウの大きさ（標準／大／特大／画面に合わせる）、タイトル画面での自動取り込み、開発者モード中のエンディング集計、各統計単位の記録ON/OFFを変更できます。統計単位のON/OFFは**ゲームを再起動せず、プレイ中のコロニーにも即時反映**されます。

### 対応MOD

他MOD固有の統計も Rimworld Statistics **本体に内蔵**しています。別の統計アドオンは不要です。対象のゲームプレイMODが有効なときのみ初期設定で自動記録し、未導入のMODについては取得処理を行いません（過去に記録した数値はそのまま保持されます）。

- **[対応MOD一覧（100件以上）](docs/supported-mods.md)**

| 対応MODの例 | 記録される統計の例 |
| --- | --- |
| **Hospitality (Continued)** | 宿泊客・グループの来訪数、ゲストベッド数、勧誘成功数、買い物、平穏な滞在終了 |
| **Colonist Recruitment** | 募集開始回数、支払った銀、予定応募人数 |
| **Dubs Bad Hygiene** | 貯水量・タンク容量、給水能力、衛生設備数、衛生行動の完了回数 |
| **Smart Apparel Upgrade** | 自動着替えジョブの発行回数、着替え完了回数 |
| **Vanilla Psycasts Expanded** | サイキャスターレベル上昇、能力別の詠唱回数、サイキャスター状態 |
| **Vanilla Trading Expanded** | 銀行の入出金・融資、契約の納品・購入 |
| **Vanilla Outposts Expanded / VEF Outposts** | 前哨基地の稼働数、駐在人数・種類別内訳、生産物の配送 |
| **Vanilla Fishing Expanded / Odyssey** | 釣りの成功回数、獲得した漁獲物の種類別数量 |
| **その他多数のコンテンツMOD** | 各MODが追加するインシデント・設備・活動の実績（詳細は[対応MOD一覧](docs/supported-mods.md)を参照） |

### 統計の記録範囲と注意事項

本MODは統計項目ごとに以下の情報を組み合わせて集計します。正しい解釈のため、以下の仕様にご留意ください。

- **取得元の違い**：
  1. **ゲーム本体の記録・履歴グラフ**：プレイ時間、襲撃・大脅威の記録、資産推移など、セーブデータ内に元々保存されている値（導入前セーブからも取得可能）。
  2. **セーブ時点の状態スナップショット**：入植者・動物・設備・備蓄など、各セーブ時点のマップやワールドの状態から読み取る値。
  3. **プレイ中のイベント記録（チェックポイント）**：本MOD有効中に発生した出来事（建築・生産・戦闘・MOD固有イベントなど）を記録し、セーブ時にそのセーブ系統へ紐づけて保存する値。
- **未取得の値と `0` の区別**：記録が存在しない出来事は過去セーブから復元できません。測定根拠がない項目は **「不明（`—`）」**・**「部分記録」**・**「対象外」** として区別し、無条件に `0` として扱うことはありません。
- **同一コロニーの重複防止とセーブ分岐の扱い**：同じコロニーの複数セーブを別コロニーとして二重計上することはありません。また、セーブ＆ロードで分岐した別ルートの記録を無条件に合算することもありません。履歴折れ線グラフでは親子関係（連続性）が確認できたセーブ間のみを実線で結び、確認できない点は白抜きで表示します。
- **追加統計・MOD統計の記録期間**：プレイ中のイベント記録やMOD固有統計は、**その統計が有効化された時点から**記録されます。本MOD導入前や、統計設定をOFFにしていた期間、対象DLC／MODを外していた期間の出来事は後から自動補完されません（一時的に無効化して再度有効化した場合は、以前の累計を保持したまま「部分記録」として扱います）。
- **導入前セーブのエンディング痕跡**：MOD導入前のセーブに残っているエンディング痕跡も可能な範囲で検出し、種類ごとに「少なくとも1回（`1回以上`）」の到達として扱います。ただし、アルコネクサスなどセーブ内に痕跡が残らないエンディングは導入前セーブからは計上できません（宇宙船発射と王室昇叙の両方の痕跡がある場合は「宇宙船または王室昇叙」として扱われます）。また、正確な到達回数・日時や旅立った人数は特定できない場合があります。
- **開発者モード設定の適用範囲**：初期設定では開発者モード中に記録したエンディング（および旅立った人数）も表示・集計します。除外したい場合はMOD設定の **「開発者モード中に記録したエンディングを表示し、合計に含める」** をOFFにしてください。この設定が切り替えるのは**エンディングと旅立った人数のみ**であり、富・襲撃回数・人口などの他統計は設定に関係なく集計されます（コロニー単位で外したい場合は `コロニー` タブの `合計から外す` を使用してください）。
- **実験的な統計について**：`実験的` と表示される統計や一部MOD連携は、対象MODのバージョンや内部実装の変更により取得できない場合があります。

### よくある質問

**Q. すでにプレイ途中のセーブデータがある状態で導入しても大丈夫ですか？**  
A. はい、そのまま導入できます。セーブ内に残っているゲーム標準の記録や現在の状態は取り込まれ、イベント型の追加統計は導入後のプレイから記録が始まります。

**Q. セーブデータは書き換えられますか？あとからMODを外すことはできますか？**  
A. タイトル画面からのセーブ取り込みはすべて**読み取り専用**で行われ、`.rws` セーブファイルを書き換えることはありません。本MODを有効にしてプレイ・セーブしたデータには進行記録用の軽量なチェックポイントが含まれるため、後から本MODを外してそのセーブをロードするとRimWorld標準の未定義コンポーネント通知が1度表示されますが、そのままバニラ等で上書きセーブすれば以降は表示されなくなります。

**Q. 古いセーブファイル（`.rws`）を削除すると、これまでの通算統計も消えますか？**  
A. いいえ。一度取り込まれた統計は専用プロフィール（`profile.json`）に保持されるため、元の `.rws` セーブファイルを整理・削除しても記録は残ります。

**Q. 数値が `0` ではなく「`—`（不明）」や「部分記録」と表示されるのはなぜですか？**  
A. MOD導入前の期間や対象MOD・記録設定が無効だった期間など、本当に `0` だったのか記録が存在しないのかを判別できない区間があるためです。根拠のない `0` で平均やランキングが歪むのを防いでいます。

**Q. セーブ＆ロードを繰り返した場合、襲撃や生産数が二重にカウントされますか？**  
A. 二重計上されません。プレイ中の記録は各セーブのチェックポイント系統に紐づいて管理されるため、ロードしてやり直した試行分のカウントが別ルートへ混ざることはありません。

**Q. 統計データやバックアップ、CSV出力ファイルはどこに保存されますか？**  
A. RimWorldのセーブデータフォルダー配下の `RimworldStatistics/` フォルダーに保存されます（統計本体：`profile.json` および自動バックアップ `.bak1`〜`.bak3`、手動バックアップ：`backups/`、CSV出力：`exports/`）。`データ管理` タブに `profile.json` の保存先パスが表示されるほか、`パスをコピー` や、手動バックアップ作成後・CSV出力後に利用できる **「フォルダーを開く」** から保存先へ移動できます。

### 関連資料

- [対応MOD一覧](docs/supported-mods.md)
- [CSV出力仕様](docs/csv-export.md)

---

## English

[Key Features](#key-features) · [Installation and Updating](#installation-and-updating) · [Basic Operations](#basic-operations) · [Supported Mods](#supported-mods) · [Recording Scope and Notes](#recording-scope-and-notes) · [Frequently Asked Questions](#frequently-asked-questions) · [Related Documents](#related-documents)

**Rimworld Statistics** aggregates records across all of your colonies and lets you browse them **directly from the RimWorld title screen without loading individual save files**. It combines read-only analysis of saves in your save folder with a persistent local statistics profile maintained while you play.

- **Supported Game Version**: RimWorld 1.7
- **Required Mod**: [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- **Supported Languages (12)**: Japanese, English, Simplified Chinese, Traditional Chinese, Korean, German, French, Spanish (Spain & Latin America), Portuguese (Portugal & Brazil), Russian (follows RimWorld's language setting)

### Key Features

- **Cross-Colony Lifetime Totals**: View total in-game days, real play time, colony count, peak and final wealth records, enemy raids, major threats, colonist deaths, and reached endings by type at a glance.
- **340+ Detailed Statistics**: Search and filter metrics across wealth, population, races/xenotypes, combat, construction, production, trade, quests, medical care, official DLCs (Royalty, Ideology, Biotech, Odyssey), and supported mods, including per-in-game-day rates where applicable.
- **Colony Groups, Side-by-Side Comparison & Leaderboards**: Create custom colony groups, compare up to 4 columns side by side (All colonies, Groups, or individual Colonies), and view all-time colony rankings in ascending or descending order.
- **Charts & Lineage-Aware History**: Click any statistic to open bar charts (**By colony** / **By category**), then select a colony to inspect its day-by-day history line chart, overlay up to 2 additional colonies, or compare exact changes between any two proven continuous saves.
- **Read-Only Import of Pre-Install Saves**: Saves created in RimWorld 1.5–1.7 before installing this mod are analyzed **read-only** to the extent supported by the data inside each save. Original `.rws` files are never modified.
- **CSV Export & Profile Backups**: Export structured UTF-8 (with BOM) CSV files with a JSON manifest, and protect your records with automatic rolling backups plus one-click manual ZIP backup and restore.

### Installation and Updating

#### Steam Workshop (Recommended)

1. Subscribe to [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) and [Rimworld Statistics](https://steamcommunity.com/sharedfiles/filedetails/?id=3837802361) on Steam Workshop.
2. Enable **Harmony** first and **Rimworld Statistics** after it in the in-game Mods menu, then restart RimWorld.
3. Updates are delivered automatically by Steam.

#### Manual Installation & Updating (GitHub Releases)

1. Download the latest release archive from [Releases](https://github.com/pengin0503/Rimworld-Statistics/releases) and extract the `RimworldStatistics` folder into RimWorld's `Mods` directory.
2. Enable **Harmony** and **Rimworld Statistics** (after Harmony) in the Mods menu and restart RimWorld.
3. **When updating manually**: Do not extract over an existing folder. Delete the old `Mods/RimworldStatistics` folder first, then place the new folder (your statistics profile is stored separately in the RimWorld save data directory and will not be lost).

#### Migrating from Legacy Statistics Add-ons

If you previously used standalone statistics add-ons (for Hospitality, Recruitment, Dubs Bad Hygiene, or Smart Apparel Upgrade), disable those add-ons after updating—all integrations are now built directly into Rimworld Statistics. Keep the gameplay mods themselves enabled as usual.

### Basic Operations

1. **Opening the Statistics Window**  
   Click **"Lifetime stats"** on the RimWorld title screen (just above "Options"). By default, saves are scanned and imported automatically when the title screen opens.
2. **Filtering by Group or Excluding Colonies**  
   - Use the **"All colonies"** dropdown in the top header and select **"Edit groups…"** to create custom colony groups and filter the active scope (`Overview`, `Details`, `Rankings`, `Acquisition status`, and CSV export).
   - To exclude a specific colony (such as a test world) from overall totals and rankings, check **"Exclude"** on its row in the **Colonies** tab.
3. **Overview of the 7 Tabs**

| Tab | Purpose |
| --- | --- |
| **Overview** | Displays lifetime summary cards and **Favorite statistics** (`★` to pin, reorder, or click to chart). |
| **Endings** | Lists every recorded ending across colonies with date, colony, ending type, ordinal, departed colonists, and data source. |
| **Colonies** | Sortable table of all colonies, per-colony **Exclude** toggle, and per-colony list of imported `.rws` save files. |
| **Details** | Searchable catalog of all statistics with category/status filters and **All colonies** / **One colony** / **Compare** (up to 4 columns) views. |
| **Rankings** | Colony leaderboards for any rankable statistic, sorted highest-first or lowest-first (top 10 or all ranks). |
| **Acquisition status** | Shows whether each unit/statistic is currently recording, inactive (missing DLC/mod or disabled), full, partial, unknown, or experimental. |
| **Data management** | Run **Rescan** or **Full re-check** on saves, inspect historical save intervals, manage profile backups/restores, and run **Export CSV**. |

4. **Using Charts & History Comparison**  
   Click a statistic in **Overview** or **Details** to open its bar chart (**By colony** / **By category**). Select a colony bar to open its day-by-day history line chart, where you can click **"Add colony…"** to overlay up to 2 other colonies, or click any two points (or **"First and last"**) to inspect exact interval changes across continuous saves.
5. **Mod Settings**  
   Open `Options` → `Mod Settings` → **Rimworld Statistics** to configure **Statistics window size** (Standard, Large, Extra large, Fit screen), automatic title-screen save import, dev-mode ending counting, and per-unit recording toggles. Toggling individual statistic units takes effect **immediately without restarting the game**, including in an active colony.

### Supported Mods

Support for over 100 gameplay mods is **built directly into Rimworld Statistics**—no separate compatibility or add-on mods are required. Mod-specific statistics are enabled by default only while the corresponding mod is active; when a mod is not loaded, its hooks stay idle and any previously recorded values remain preserved.

- **[Full List of Supported Mods (100+)](docs/supported-mods.md)**

| Example Mod | Examples of Recorded Statistics |
| --- | --- |
| **Hospitality (Continued)** | Guest and group visits, guest beds, successful recruits, guest purchases, peaceful visit completions |
| **Colonist Recruitment** | Recruitment drives started, silver spent, scheduled applicants |
| **Dubs Bad Hygiene** | Stored water & tank capacity, water supply capacity, fixture counts by type, completed hygiene actions |
| **Smart Apparel Upgrade** | Automatic apparel-swap jobs issued and completed |
| **Vanilla Psycasts Expanded** | Psycaster level gains, psycasts by ability Def, psycaster snapshot state |
| **Vanilla Trading Expanded** | Bank deposits, withdrawals, loans, and contract deliveries/purchases |
| **Vanilla Outposts Expanded / VEF Outposts** | Active outposts, stationed pawns by outpost type, produced item deliveries |
| **Vanilla Fishing Expanded / Odyssey** | Successful catches and caught items by Def |
| **Many Other Content Mods** | Mod-added incidents, facilities, and gameplay outcomes (see the [full list](docs/supported-mods.md)) |

### Recording Scope and Notes

Rimworld Statistics combines multiple data sources while strictly avoiding fabricated numbers or double counting:

- **How Data Is Collected**:
  1. **Built-in Game Records & History Graphs**: Play time, raid/threat counts, wealth history, and other counters natively stored in RimWorld saves (available even from pre-install saves).
  2. **Save-Time State Snapshots**: Colonists, animals, buildings, stockpiles, and world state read at each save.
  3. **Live Playtime Checkpoints**: Gameplay events (construction, crafting, combat, mod-specific actions, etc.) recorded while this mod is active and saved along each save's checkpoint lineage.
- **Missing Values Are Never Treated as `0`**: Events without recorded evidence cannot always be reconstructed from older saves. Unmeasured values are explicitly marked as **Unknown (`—`)**, **Partial record**, or **Not applicable** rather than silently filled with `0`.
- **No Colony Duplication or Branch Double-Counting**: Multiple saves belonging to the same colony are grouped into a single colony rather than summed as separate colonies, and branched timelines (from reloading earlier saves) are never blindly added together. On history line charts, only saves with proven parent-child continuity are connected by solid lines; saves whose continuity cannot be proven are drawn as hollow points.
- **Recording Window for Live & Mod Statistics**: Live event statistics and mod integrations record **only from the moment they are active**. Events that occurred before installing Rimworld Statistics, while a statistic unit was turned off, or while a target DLC/mod was disabled are not retroactively backfilled. Disabling and re-enabling a unit preserves earlier totals and marks the interrupted span as a partial record.
- **Pre-Install Ending Traces**: Ending traces remaining inside pre-install saves are detected where possible and counted as "at least once" (`1 or more`) per ending type. Endings that leave no trace in the save—such as the Archonexus—cannot be counted from pre-install saves (and saves holding traces of both Ship launch and Royal ascent appear as "Ship or Royal Ascent"), nor can exact completion counts, timestamps, or departed colonist counts always be determined.
- **Scope of the Development Mode Setting**: By default, endings (and departed colonist counts) recorded in Development Mode are shown and counted. Turn off **"Show and count endings recorded in development mode"** in Mod Settings to hide them. This setting affects **only endings and departed colonist counts**—all other statistics (wealth, raids, population, etc.) for colonies played with Development Mode remain included unless you check **Exclude** for that colony in the **Colonies** tab.
- **Experimental Statistics**: Metrics labeled **Experimental** and third-party mod hooks depend on the target mod's version and internal methods, and may not record in every mod configuration.

### Frequently Asked Questions

**Q. Can I install this mod mid-playthrough?**  
A. Yes. Native counters, history graphs, and current snapshots from your existing saves are imported right away, and live event tracking begins from the moment you continue playing with the mod enabled.

**Q. Does importing saves modify my `.rws` save files? Can I remove the mod later?**  
A. Title-screen save importing is strictly **read-only** and never modifies `.rws` files. Saves written *while playing with this mod enabled* store lightweight checkpoint data in a `GameComponent`; if you later remove the mod and load that save, RimWorld will show its standard one-time missing-component log notice, which disappears once you save again.

**Q. If I delete old `.rws` save files, will I lose my lifetime statistics?**  
A. No. Imported observations and records are stored in your local statistics profile (`profile.json`), so deleting old `.rws` save files will not erase already-recorded statistics.

**Q. Why do some metrics show `—` (Unknown) or "Partial record" instead of `0`?**  
A. When a colony has play periods before the mod was installed or while a unit/mod was inactive, there is no way to prove whether the true count during that period was zero or simply unrecorded. Keeping missing values distinct prevents false zeros from distorting totals and rankings.

**Q. If I reload an earlier save and replay a battle or crafting session, are events counted twice?**  
A. No. Live event counters follow checkpoint lineage per save, so reloaded attempts on discarded branches are not merged into your active branch's totals.

**Q. Where are my profile data, backups, and CSV exports stored?**  
A. Everything is stored under `<RimWorld SaveDataFolder>/RimworldStatistics/` (`profile.json` with rolling `.bak1`–`.bak3` files, manual ZIP backups in `backups/`, and CSV exports in `exports/`). The **Data management** tab shows the full path to `profile.json`, lets you click **Copy path** for the backup directory, and provides **"Open folder"** buttons once a manual backup or CSV export has been created.

### Related Documents

- [Supported Mods](docs/supported-mods.md)
- [CSV Export Specification](docs/csv-export.md)
