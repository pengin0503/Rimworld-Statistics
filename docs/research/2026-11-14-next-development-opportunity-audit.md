# Rimworld Statistics：次期開発候補の調査（日本語版）

作成日：2026-11-14  
基準：main \`dc90d3cd99e4c1bdae5240ec24a049c010304b04\` / RimWorld 1.7 / v1.8.0公開後  
性質：**実装を伴わない調査**。コード・公開対応MOD一覧・Steam Workshop・リリースは変更しない。

**追加調査**：[同日第2回の詳細調査と元セーブ分析](2026-11-14-additional-investigation-ja.md)  
**複数AIの担当指示**：[日本語の並列調査引き継ぎ](../superpowers/plans/2026-11-14-parallel-ai-research-handoff.md)

## 1. 最新リポジトリを基準にした現状

- 調査開始時点の未解決Issueは**2件**：[#72](https://github.com/pengin0503/Rimworld-Statistics/issues/72)（過去セーブ・チェックポイントの直接親子証明、実ゲーム確認、55件以上の実セーブ負荷）と [#195](https://github.com/pengin0503/Rimworld-Statistics/issues/195)（プロフィール保存時の直列化/読み戻し負荷）。
- 未マージPRは**0件**（PR #257の作成前）。最新mainの数値統計CIは成功済み。
- [PR #255](https://github.com/pengin0503/Rimworld-Statistics/pull/255) によりコロニー別の統計履歴折れ線表示は**実装済み**。
- [PR #256](https://github.com/pengin0503/Rimworld-Statistics/pull/256) により種族別の加入・自由入植者死亡の内訳は**実装済み**。今後の「HAR対応」は、これを再実装することではなく、在籍ピークやゼノタイプなどの追加次元を検討するもの。
- [PR #237](https://github.com/pengin0503/Rimworld-Statistics/pull/237) のMOD固有統計6件も実装済み。
- **検索/カテゴリ・状態フィルタ、並び替え、お気に入り、比較・ランキング、統計グラフ、CSV出力は存在する**。それらを「新規搭載」と書かない。
- \`docs/supported-mods.md\` には当時**64件**の対応MOD名がある。研究候補をここに混在させない。

## 2. 修正・改善候補の根拠

### C1【P0】セーブ分岐をグラフの連続した歴史として描かない

確認した箇所：\`Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs\` の \`SaveChartPoints\`。

- 同一時刻の観測値が矛盾する場合は欠測扱いにする、という安全な実装が存在する。
- 一方、別時刻の2点については、同じ \`ModCampaignId\` で数値が存在すれば \`ConnectToPrevious\` を真にし得る。
- 同一キャンペーンIDは**同じ親子セーブ枝に属することの証明ではない**。途中巻き戻し/兄弟枝を連続した履歴として表示する設計上のリスクがある。
- \`CheckpointChartHistory\` は別途、明示的な \`ParentKey\` で親子をたどっており、違いがある。

提案：直接親子が確定していないセーブ観測は、まず個別点として可視化する。証拠があるチェックポイント同士だけ線で結ぶ。Unknownをゼロに置き換えず、履歴元（save/checkpoint/recorder）を凡例で示す。実ゲームでの表示再現は未実施なので「確認済み描画バグ」とは扱わない。

受入テスト：同一キャンペーンの別枝、巻き戻し、親欠落、同一tickの矛盾、連続した正当な親子、アーカイブ復元、別MOD/DLC構成。

### C2【P0】Issue #195を「現行実装」に合わせて再測定

- Issueの説明「5ファイルごとに \`QueueSave\`」は古い。現在の \`Source/RimworldStatistics.Core/Import/ImportEngine.cs\` は \`SaveInterval = 5秒\` で保存要求を抑え、処理完了時にも保存する。
- \`Source/RimworldStatistics.Core/Profiles/ProfileSession.cs\` の \`WriteLatest\` は \`_lock\` 内で \`ProfileJson.Serialize\` を行う。
- \`Source/RimworldStatistics.Core/Profiles/ProfileStore.cs\` は既存一次ファイルの内容を確認し、統計差分に応じたバックアップを行う。単純な更新日時比較に変えると、衝突検知が弱まる可能性がある。
- 5/10MB相当で書込のp50/p95、I/O、割当、ゲームスレッドのロック待機を比較し、実測で影響のある箇所だけ最適化する。

受入テスト：別セッションによる上書き拒否、クラッシュ時の原子置換、3世代バックアップ、旧schemaの原本保護、壊れたprofileの復元、取込キャンセル。

### C3【P0】Issue #72の残りの証明は実データに依存

既存の原本による親子確認・.NET読込実績はIssueに残っているが、正の増分が存在する実チェックポイントの連続証拠、55件以上の実セーブ計測、ゲームUIの一部が未確認。親IDのない前後のセーブを「絶対に起きたイベント」として復元しない。同じイベントを複数枝で合計しない。

今回提供された6セーブからは、時刻が異なる2ファイルに同じチェックポイントが保存されている実例が確認された。詳細は[追加調査](2026-11-14-additional-investigation-ja.md)参照。これは欠陥の断定ではなく、欠測表示を整える根拠。

### C4【P1】実測・状態観測・累積・順位の意味を混同させない

\`MetricHistoryKind\`、\`NumericValue.Coverage\`、\`MetricStatus\` はすでに存在する。UI側で「時点の値」「期間の加算量」「史上最高値」「Partial」などが区別できるツールチップを付ける。別コロニーのグラフを重ねる場合も、経過日数・開始日・時点の整列条件を明記。

### C5【P1】インポート失敗理由と復旧を分かりやすくする

\`ImportTab\`、\`CoverageTab\`、\`DataManagementTab\` はすでに実装済み。追加するなら、「スキップ/破損/変化中/未サポート/対象MODなし/取得OFF」を区別する**読み取り専用診断**と、安全なサポート情報のエクスポートを検討。ユーザーのキャンペーンID、チェックポイントID、生セーブ、MOD全一覧を公開Issueへ流出させない。

## 3. 新機能候補の優先順位

| 優先 | 既存との差分がある提案 | 受入条件 |
|---|---|---|
| P0 | 履歴の親子証拠と連続線の意味を一致させる | 未証明の別枝が連続した確定履歴にならない |
| P0 | プロフィール保存を定量的に最適化 | 5/10MBの実測、衝突検知・バックアップ破壊なし |
| P0 | 過去セーブの証拠区分/時刻境界表示 | 古い埋込記録、未知、部分記録を誤認しない |
| P1 | グラフの期間選択/欠測・Partialツールチップ | 表示のみの改善で元の観測値を消さない |
| P1 | 集計可能な件数の1日当たり比較 | 取得期間を分母に使用、極値や断片記録には使わない |
| P1 | 読込診断と個人情報を含まない診断出力 | 壊れたセーブ、変更途中、設定変更を説明可能 |
| P1 | 種族/ゼノタイプ/地形の横断分類 | 汎用加入・死亡総数との二重計上がない |
| P2 | CSV出力への履歴証拠の任意追加 | 既存5CSV＋manifestを破壊しない |
| P2 | 長大な折れ線の描画点数削減 | 元データ、最大・最小・欠測の境界を維持 |

**対象外とする重複案**：検索、お気に入り、基本のグラフ、CSV、既存のデフォルトON録画、PR #255/#256で実装済みの機能の再実装。

## 4. 既存の人気MOD候補の再順位付け

以下の候補はすでに[人気MOD調査登録](../planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md)と[種族MOD調査](../planned-mod-statistics/2026-11-14-popular-race-mod-audit.md)に含まれる。これは**新しく見つけた数ではなく優先順の再評価**。対応済み一覧には追加しない。

| 優先 | MOD | 実装するなら必須の独自結果 | 関門 |
|---|---|---|---|
| P0 | Humanoid Alien Races / Biotech | #256の加入・死亡の次に、在籍ピーク・囚人/奴隷・ゼノタイプ別分類 | 種族とゼノタイプを二重人数にしない |
| P0 | Geological Landforms | 実際に開始/運営したコロニーの地形別記録 | ランダムプレビューを含めない、安定したLandform.Id |
| P0 | Vanilla Events Expanded | 複数段階イベントの実完了 | 取消・失敗・一般incidentとの重複を除外 |
| P0 | Vanilla Psycasts Expanded – Puppeteer | 傀儡成立・意識移動の成功 | 通常のサイキャスト使用回数と別 |
| P0 | Vanilla Ideology Expanded – Dryads | 合成後の実ドライアド生成 | 合成要求や作成途中は不計上 |
| P1 | Hospital | 患者受入、治療後退院、実際の治療費受領 | 単なる退院・支払予定を治療成功/売上としない |
| P1 | Vanilla Fishing Expanded | Odysseyなし環境の釣果・実受取数 | 既存FishingFamilyとの二重計上を防ぐ |
| P1 | Vanilla Furniture Expanded – Farming | 実際に増えた植物成長量 | 毎tickフックの負荷を避ける |
| P1 | Vanilla Races Expanded – Archon | 超越ポーンの実帰還 | 消失予約・死亡だけでは計上しない |
| P1 | NewRatkinPlus | 実際に働いた回し車の生産量 | 作業開始・定格発電を使わない |
| P1 | Alpha Genes | 確定した独自遺伝子操作 | 一般のBiotech遺伝子変更と重複しない |
| P2 | Vanilla Races Expanded – Lycanthrope/Fungoid/Insector | 真に確定した独自変身/進化 | 1.7選択DLLと成功経路を確認 |
| P2 | Vanilla Landmarks Expanded | 最初に訪問したランドマーク | マップ生成回数ではない |
| P2 | Vanilla Furniture Expanded – Power/Spacer | 独自の炉心事故・実際の修理量 | 通常の事故/修理統計との重複監査 |
| 条件付き | Vanilla Traits Expanded / Non-Lethal | 明確に独立した強制退却/損害・特殊気絶 | Combat Extendedとの重複・ホットパス負荷 |
| 共通分類のみ | Gloomy Dragonian、Rabbie、Yuran | HARの共通種族分類 | 個別成果が証明できるまで専用アダプタなし |

Workshopの購読者数は調査日時の概数で、1.7実利用者数を意味しない。1.7タグだけで適合DLLや成功経路が証明されたとは扱わない。

**今回追加した別枠の新候補**：Winston Waves、Empire Refactored、Caravan Adventures、World Domination 2.0、Rim War (Continued)。詳細な選考/除外理由は[第2回調査](2026-11-14-additional-investigation-ja.md)。

## 5. 調査から実装へ移るときの共通規則

1. **重複確認が先**：\`docs/supported-mods.md\`、\`Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs\`、既存 \`MetricRegistry\` と2026-11-13の27件対応状況を照合。既存35＋追加27の決定を尊重。
2. **成功確証が必須**：着手/予定/見込み/瞬間的な所持数から生涯累計を捏造しない。Gastronomy個別支払完了、Prison Expanded暴動全体終結、Rimefeller不明精製経路は既存の見送り決定を維持。
3. **共通HARを優先**：ただの種族専用アイテムや疾患は独立アダプターを作らない。既存の種族別加入・死亡は \`GameFacts.Species\` とパッケージ識別付き \`Pawn.def\` が取得済み。
4. **安全と速度**：毎tick反射、ワールド全域の常時走査、保存値の過大加算は禁止。新規利用者の記録はデフォルトON、既存の明示的OFFは守り、設定変更に再起動を要求しない。
5. **任意DLC/MOD**：未導入や版違いをゼロとしない。必要に応じてPartial/Unknown/NotApplicable。Anomaly不要。
6. **RimWorld 1.7の実DLL**：\`LoadFolders.xml\`、選択される実アセンブリ、ソース/IL、成功後フック、負のテストの順に確証する。
7. **実ゲーム**：使える場合は \`pengin0503/RimWorld-for-mod-test\` による最小構成とセーブの検証。完璧な実環境再現を口実に小さな完成可能な作業を止めない。

## 6. 6担当の並列調査と安全な統合

- **AI-A：履歴・親子関係・過去セーブ**。履歴接続の確定根拠と#72。
- **AI-B：保存・性能・インポート**。#195の現行状態の再計測。
- **AI-C：UI・グラフ・エクスポート**。既存機能の改善。
- **AI-D：種族/ゼノタイプ/地形**。HARとGeological Landforms。
- **AI-E：MOD固有の超能力/イベント/儀式**。Puppeteer、Vanilla Events、Dryads。
- **AI-F：商業/サービス/製造MOD**。Hospital、Fishing、Farming。

調査時は別々のMarkdownファイル・ブランチを用いる。**共有のMetricRegistry・Languages・プロファイル形式を6人が同時編集する実装は行わない**。統括役がレビュー後、独立してテストできる単位でPRを順番に統合。機能追加前に最小設計、TDD、CI、コードレビューを要求する。

## 主なコード参照

- 統計履歴：\`Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs\`、\`MetricChartSeries.cs\`、\`HistoricalCounterIntervals.cs\`
- 保存：\`Source/RimworldStatistics.Core/Profiles/ProfileSession.cs\`、\`ProfileStore.cs\`、\`Import/ImportEngine.cs\`
- UI：\`Source/RimworldStatistics/UI/DetailsTab.cs\`、\`CoverageTab.cs\`、\`OverviewTab.cs\`、\`RankingsTab.cs\`、\`Dialog_ColonyMetricHistory.cs\`
- 種族：\`Source/RimworldStatistics/Patches/Metrics/GameFacts.cs\`、\`MembershipMetricPatches.cs\`、\`DeathMetricPatches.cs\`、\`Tests/RimworldStatistics.Core.Tests/SpeciesBreakdownTests.cs\`
- 既存候補：[人気27件](../planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md)、[種族6件](../planned-mod-statistics/2026-11-14-popular-race-mod-audit.md)、[追加27件の現状](../planned-mod-statistics/2026-11-13-additional-30-implementation.md)

**公開の境界**：調査候補は対応済みではない。小さく完結する機能から実装可能だが、実ゲーム未検証の事項を「確認済み」と表示しない。
