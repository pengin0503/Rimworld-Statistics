# Steam Workshop 説明文 / Steam Workshop Description

**言語 / Language:** [日本語](#日本語) | [English](#english)

Steam Workshop（PublishedFileId: `3837802361`）に掲載するための説明文原稿（Steam BBCode形式）です。日本語ページ・英語ページそれぞれの欄にそのまま貼り付けて利用できます。

---

## 日本語

```bbcode
[h1]Rimworld Statistics[/h1]
過去にプレイした全セーブ・全コロニーの記録を集約し、[b]タイトル画面からセーブデータを個別にロードせずに[/b]閲覧できるほか、[b]プレイ中のメニューやショートカットキーからも現在のコロニー総括や通算統計を確認できる[/b] RimWorld 1.7 向け通算統計MODです。

[list]
[*][b]対応バージョン：[/b]RimWorld 1.7
[*][b]必須MOD：[/b][url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]
[*][b]対応言語（12言語）：[/b]日本語、英語、中国語（簡体字・繁体字）、韓国語、ドイツ語、フランス語、スペイン語（スペイン・ラテンアメリカ）、ポルトガル語（ポルトガル・ブラジル）、ロシア語
[/list]

[h2]主な機能[/h2]
[list]
[*][b]全コロニーの通算実績：[/b]ゲーム内日数、実プレイ時間、コロニー数、最高・最終資産、襲撃・大脅威・死亡数、種類別のエンディング到達履歴を一括確認できます。
[*][b]350項目以上の詳細統計：[/b]資産、人口、種族・ゼノタイプ、戦闘、建築、生産、取引、クエスト、医療、全公式DLC（Royalty / Ideology / Biotech / Anomaly / Odyssey）、対応MODの統計を検索・絞り込みできます。累積加算項目では [b]1日あたり変化率（/day）[/b] 表示への切り替えにも対応しています。
[*][b]グループ集計・スマートグループ・比較・ランキング：[/b]任意のコロニーグループ作成に加え、条件別の[b]スマートグループ[/b]（ストーリーテラー別・パーマデスのみ・クリア済み・生存日数N日以上・開始バイオーム別）、最大4列の横並び比較、歴代コロニーランキングに対応しています。
[*][b]コロニー総括レポート・年表・セーブ系統：[/b]各コロニーの主要成績・順位をまとめた総括レポート、文字検索・日数絞り込み付きのコロニー年表、セーブ親子関係を確認できるセーブ系統ビュー、カスタム表示名・検索付きピッカーを備えています。
[*][b]多彩なグラフ可視化：[/b]コロニー別・内訳別の棒グラフ、属性別ドット色切替に対応した2指標散布図、期間拡大（ズーム）・ピーク保持描画・他コロニー重ね合わせ（最大2件追加）・連続2時点間の増減比較に対応した履歴折れ線グラフを利用できます。
[*][b]MOD導入前セーブの取り込み：[/b]導入前に作成された RimWorld 1.5〜1.7 のセーブデータも、対応する範囲で[b]読み取り専用[/b]で解析します。元のセーブデータは書き換えません。
[*][b]CSV / HTML / Markdown出力・匿名診断・バックアップ：[/b]6種類のCSV＋JSONマニフェスト出力、オフライン静的HTMLレポート（年表統合）、年表のMarkdown／HTML出力、個人情報を含まない匿名診断サマリ出力、統計プロフィールの自動世代バックアップおよび手動ZIPバックアップ・復元機能を備えています。
[/list]

[h2]対応MOD（100件以上を本体に内蔵）[/h2]
他MOD固有の統計もすべて本体に統合されており、別の統計アドオンは不要です。対象MODが有効なときだけ初期設定で自動記録し、未導入時は取得処理をスキップします（過去の記録値およびMOD表示名は保持されます）。

[list]
[*][b]代表的な対応MOD：[/b]Hospitality (Continued)、Colonist Recruitment、Dubs Bad Hygiene、Smart Apparel Upgrade、Vanilla Psycasts Expanded、Vanilla Trading Expanded、Vanilla Outposts Expanded / VEF Outposts、Vanilla Fishing Expanded、Combat Extended、Save Our Ship 2、A RimWorld of Magic ほか多数
[*][b]対応MOD一覧：[/b][url=https://github.com/pengin0503/Rimworld-Statistics/blob/main/docs/supported-mods.md]GitHubの対応MOD一覧（Supported Mods）[/url]
[/list]

[h2]導入と基本操作[/h2]
[list]
[*]MOD一覧で [b]Harmony → Rimworld Statistics[/b] の順に有効化し、ゲームを再起動してください。（以前の独立した統計アドオンを使用していた場合は無効化してください）
[*]タイトル画面の[b]「通算統計」[/b]、プレイ中の Esc メニュー[b]「現在のコロニーの統計」[/b]、または [b]オプション → キーボード設定[/b] で割り当てたショートカットキーからいつでも開けます。
[*]画面上部の[b]「全コロニー」[/b]からグループやスマートグループを選択でき、[b]「コロニー」[/b]タブで表示名の変更や[b]「合計から外す」[/b]を設定できます。
[*]ウィンドウの大きさ、自動取り込み、開発者モード中のエンディング集計、各統計単位の記録ON/OFFは [b]オプション → MOD設定 → 通算統計（Rimworld Statistics）[/b] から変更できます（記録対象のON/OFFは再起動不要でプレイ中のコロニーにも即時反映されます）。
[*]同じ画面の「処理に使うPCの資源」で、取り込みで同時に読むセーブの数、統計のバックグラウンド事前計算、キャッシュに使うメモリを選べます。初期値は従来どおりで、記録・表示される内容は変わりません。
[/list]

[h2]統計の記録範囲と注意事項[/h2]
[list]
[*][b]未取得の値と 0 の区別：[/b]記録が残っていない過去の出来事は復元できません。測定根拠のない値は無条件に 0 にせず、[b]「不明（—）」「部分記録」「対象外」[/b]として区別します。
[*][b]セーブ分岐と重複防止：[/b]同じコロニーの複数セーブを別コロニーとして二重計上したり、セーブ＆ロードで分岐した別ルートの記録を無条件に合算したりしません。履歴グラフやセーブ系統ビューでは親子関係が確認できたセーブ間のみを実線で結びます。
[*][b]追加統計・MOD統計の記録期間：[/b]プレイ中のイベント記録やMOD統計は、有効化された時点から記録されます。MOD導入前や無効化していた期間の出来事は後から自動補完されません。
[*][b]導入前セーブのエンディング痕跡：[/b]導入前のセーブに残っている痕跡は種類ごとに「少なくとも1回（1回以上）」として扱います。アルコネクサスなど痕跡が残らないエンディングは導入前セーブからは計上できず、正確な到達回数・日時や旅立った人数も特定できない場合があります。
[*][b]開発者モード設定の範囲：[/b]MOD設定の「開発者モード中に記録したエンディングを表示し、合計に含める」が切り替えるのは[b]エンディングと旅立った人数のみ[/b]です。富・襲撃回数・人口などの他統計は設定に関わらず集計されます。
[*][b]実験的な統計：[/b]「実験的」と表示される項目や一部MOD連携は、対象MODのバージョンや構成によって取得できない場合があります。
[/list]

[h2]よくある質問[/h2]
[list]
[*][b]Q. プレイ途中のセーブに導入できますか？[/b]
A. はい。既存のゲーム内記録や現在状態はそのまま取り込まれ、イベント型の追加統計は導入後のプレイから記録が始まります。
[*][b]Q. セーブデータは安全ですか？途中でMODを外せますか？[/b]
A. タイトル画面での取り込みは読み取り専用で、セーブファイルを一切変更しません。本MOD有効中に保存したセーブから後から本MODを外すと、ロード時にRimWorld標準の未定義コンポーネント通知が1度出ますが、再度セーブすれば消えます。
[*][b]Q. 古いセーブファイル（.rws）を消すと統計も消えますか？[/b]
A. いいえ。取り込み済みの記録はセーブデータフォルダー内の [b]RimworldStatistics/profile.json[/b] に保存されるため、元のセーブを削除しても残ります。
[/list]

[h2]リンク・不具合報告[/h2]
[list]
[*][url=https://github.com/pengin0503/Rimworld-Statistics]GitHub リポジトリ（詳細ドキュメント・CSV出力仕様・外部連携SDK）[/url]
[*][url=https://github.com/pengin0503/Rimworld-Statistics/issues]不具合報告・要望（GitHub Issues）[/url]
[/list]
```

---

## English

```bbcode
[h1]Rimworld Statistics[/h1]
Browse lifetime statistics across every save and colony [b]directly from the RimWorld title screen without loading individual save files[/b], or check your current colony and lifetime records [b]in-game via the pause menu and configurable keyboard shortcuts[/b]. Built for RimWorld 1.7.

[list]
[*][b]Supported Version:[/b] RimWorld 1.7
[*][b]Required Mod:[/b] [url=https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077]Harmony[/url]
[*][b]Supported Languages (12):[/b] English, Japanese, Simplified Chinese, Traditional Chinese, Korean, German, French, Spanish (Spain & Latin America), Portuguese (Portugal & Brazil), Russian
[/list]

[h2]Key Features[/h2]
[list]
[*][b]Cross-Colony Lifetime Totals:[/b] View total in-game days, real play time, colony count, peak and final wealth records, enemy raids, major threats, colonist deaths, and reached endings by type at a glance.
[*][b]350+ Detailed Statistics:[/b] Search and filter metrics across wealth, population, races/xenotypes, combat, construction, production, trade, quests, medical care, all official DLCs (Royalty, Ideology, Biotech, Anomaly, Odyssey), and supported mods, including a [b]Per-day rate (/day)[/b] toggle on additive totals.
[*][b]Colony Groups, Smart Groups, Comparison & Rankings:[/b] Create custom colony groups or rule-based [b]Smart Groups[/b] (by Storyteller, Permadeath only, Cleared only, Survived N+ days, or Starting biome), compare up to 4 columns side by side, and inspect all-time colony leaderboards.
[*][b]Colony Summary Reports, Chronicles & Save Lineage:[/b] Inspect per-colony Summary Reports, searchable & day-filtered Colony Chronicles, and Save Lineage views, with custom colony display names and searchable pickers.
[*][b]Rich Charts & Scatter Plots:[/b] Explore bar charts ([b]By colony[/b] / [b]By category[/b]), 2-Metric Scatter Plots with attribute dot coloring, and History Line Charts with range zoom, peak-preserving rendering, multi-colony overlays (up to 2 extra colonies), and interval comparison across continuous saves.
[*][b]Read-Only Import of Pre-Install Saves:[/b] Saves created in RimWorld 1.5–1.7 before installing this mod are analyzed [b]read-only[/b] to the extent supported by the data in each save. Original .rws files are never modified.
[*][b]CSV / HTML / Markdown Export, Anonymous Diagnostics & Backups:[/b] Export 6 UTF-8 (with BOM) CSV files with a JSON manifest, offline static HTML summary reports (with integrated chronicles), Chronicle Markdown/HTML files, zero-PII Anonymous Diagnostic Summaries, and automatic rolling backups plus one-click manual ZIP backup/restore.
[/list]

[h2]Built-In Support for 100+ Mods[/h2]
Mod-specific statistics are built directly into Rimworld Statistics—no separate add-on mods are needed. Integrations record by default only while the target mod is active; when a mod is not loaded, its hooks stay idle and earlier recorded values (and mod display names) remain preserved.

[list]
[*][b]Examples:[/b] Hospitality (Continued), Colonist Recruitment, Dubs Bad Hygiene, Smart Apparel Upgrade, Vanilla Psycasts Expanded, Vanilla Trading Expanded, Vanilla Outposts Expanded / VEF Outposts, Vanilla Fishing Expanded, Combat Extended, Save Our Ship 2, A RimWorld of Magic, and many more.
[*][b]Full List:[/b] [url=https://github.com/pengin0503/Rimworld-Statistics/blob/main/docs/supported-mods.md]Supported Mods List on GitHub[/url]
[/list]

[h2]Installation & Basic Usage[/h2]
[list]
[*]Enable [b]Harmony[/b] first and [b]Rimworld Statistics[/b] after it in the Mods menu, then restart RimWorld. (If you previously used standalone statistics add-ons, disable those add-ons.)
[*]Open via [b]"Lifetime stats"[/b] on the title screen, [b]"Current colony stats"[/b] in the in-game Esc menu, or custom hotkeys under [b]Options → Keyboard configuration[/b].
[*]Use the [b]"All colonies"[/b] dropdown in the header to select custom groups or Smart Groups, and set custom colony display names or check [b]"Exclude"[/b] in the [b]Colonies[/b] tab.
[*]Open [b]Options → Mod Settings → Rimworld Statistics[/b] to change window size, automatic import, dev-mode ending counting, or individual statistic units (unit toggles take effect immediately without restarting, even mid-session).
[*]In the same screen, [b]PC resources used for processing[/b] sets how many saves an import reads at once, background precomputation of statistics, and how much memory the caches use. The defaults keep the previous behaviour; what is recorded or shown does not change.
[/list]

[h2]Recording Scope & Important Notes[/h2]
[list]
[*][b]Missing Values Are Never Treated as 0:[/b] Unrecorded historical events cannot always be reconstructed. Unmeasured values are shown as [b]Unknown (—)[/b], [b]Partial record[/b], or [b]Not applicable[/b] rather than silently filled with 0.
[*][b]No Colony or Branch Double-Counting:[/b] Multiple saves of the same colony are grouped into one colony, and reloaded/branched timelines are never blindly summed together. History line charts and save-lineage views connect saves only when parent-child continuity is proven.
[*][b]Recording Window for Live & Mod Statistics:[/b] Live gameplay events and mod-specific metrics record from the moment they are active; events before installing the mod or while a unit/mod was disabled are not retroactively backfilled.
[*][b]Pre-Install Ending Traces:[/b] Traces remaining in pre-install saves count as "at least once" (1 or more) per ending type. Endings that leave no trace in the save—such as the Archonexus—cannot be counted from pre-install saves, and exact completion counts, timestamps, or departed counts may not be determinable.
[*][b]Development Mode Setting Scope:[/b] "Show and count endings recorded in development mode" affects [b]only endings and departed colonist counts[/b]. Other statistics (wealth, raids, population, etc.) remain included unless you exclude the colony in the Colonies tab.
[*][b]Experimental Statistics:[/b] Metrics marked [b]Experimental[/b] and third-party mod hooks depend on target mod versions and internals, and may not record in every setup.
[/list]

[h2]FAQ[/h2]
[list]
[*][b]Q. Can I add this mod mid-save?[/b]
A. Yes. Existing native records and current colony snapshots are imported immediately, and live event tracking starts as soon as you continue playing.
[*][b]Q. Is it safe for my save files? Can I remove it later?[/b]
A. Title-screen save importing is strictly read-only and never touches .rws files. Saves written while playing with this mod store lightweight checkpoint data; removing the mod later only produces RimWorld's standard one-time missing-component log notice until you save again.
[*][b]Q. Do I lose my lifetime stats if I delete old .rws save files?[/b]
A. No. Imported records are kept in [b]RimworldStatistics/profile.json[/b] inside your RimWorld save data folder, so deleting old .rws files does not erase your statistics.
[/list]

[h2]Links & Bug Reports[/h2]
[list]
[*][url=https://github.com/pengin0503/Rimworld-Statistics]GitHub Repository (Documentation, CSV Export Spec & External SDK)[/url]
[*][url=https://github.com/pengin0503/Rimworld-Statistics/issues]Bug Reports & Feedback (GitHub Issues)[/url]
[/list]
```
