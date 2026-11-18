# Rimworld Statistics v2.0.0 残作業・引き継ぎ情報

- 作成日: 2026-11-18
- 対象リポジトリ: `https://github.com/pengin0503/Rimworld-Statistics`
- 作業中断時点の `main` 最新コミット: `00657b4` (`PR #360` マージ済み)
- 作業途中コードの保存ブランチ: `feat/v2-colony-chronicle`

---

## 1. 実装状況サマリー

| 優先順 | 機能ID | 機能名 | 状態 | PR / ブランチ |
|---|---|---|---|---|
| 1 (P0) | V2-B | セーブ系統・分岐の可視化 | **完了・`main` マージ済み** | PR `#355` (`847ef57`) |
| 2 (P0) | V2-C | コロニー総括レポート + 静的HTML出力 | **完了・`main` マージ済み** | PR `#360` (`00657b4`) |
| 3 (P1) | V2-A | コロニー記録年表 | **完了（2026-11-18、翻訳12言語・単体テスト・実セーブ受入テスト込み）** | ブランチ `feat/v2-colony-chronicle` の PR |
| 4 (P2) | V2-D | 散布図による統計分析 | **未着手** | V2-A マージ後に `feat/v2-scatter-plot` を作成 |
| 5 (P3) | V2-E | 自己ベスト更新履歴（条件付き） | **未着手（差別化・時刻証明の採否判定から実施）** | 採用時のみ `feat/v2-personal-bests` |

### `main` に合流済みの関連変更（競合・規約確認済み）
- `7d351fb` (PR `#348` / Issue `#307`): `HistoryChanges.Compare`（履歴区間の増減・変化率・連続性検証）
- `847ef57` (PR `#355`): `ColonySaveLineage` / `Dialog_ColonySaveLineage` / `SaveLineageStatistics.xml`
- `45c8289` (PR `#356`): README・CSV仕様・Steam Workshop原稿の改善
- `10f872b` (PR `#359`): 12言語翻訳監査と `TranslationCopyRefinementTests.cs` の追加
- `00657b4` (PR `#360`): `ColonySummaryReport` / `ColonyReportHtmlExport` / `Dialog_ColonySummaryReport` / `ColonySummaryReportStatistics.xml`

---

## 2. V2-A の実装内容（2026-11-18 完了時点）

以下は完了時点の内容である（作業途中に書いた版から、件数上限・API・集計系列・キー数を実装に合わせて訂正済み）。

1. `Source/RimworldStatistics.Core/Stats/ColonyChronicle.cs`
   - 読み取り専用・単一分岐限定の年表モデル・ビルダー（`RimworldStatistics.Core` Release ビルド成功確認済み）。
   - `Profile` / セーブデータ / チェックポイント形式への変更はゼロ、Harmony パッチの追加もゼロ。
   - 収集する確定根拠:
     - **結末 (`ChronicleEntryKind.Ending`)**: `SaveObservation.Lineage` (`LineageEntry(Type, Ordinal, TicksGame)`) と `Colony.Endings` (`EndingSource.Live`) を照合し、選択中の分岐に属する結末のみを厳密時刻 (`ExactTick`) で表示。別分岐に属する結末は `OtherBranchEndingCount`、分岐後で所属分岐を特定できない結末は `AmbiguousBranchEndingCount` として除外し件数を通知。プロファイル欠損時のセーブ復元結末は `RecoveredLineage`（開発者モード不明）として区別。
     - **発生日不明の痕跡 (`UndatedTraces`)**: `EndingSource.SeededTrace`、`TicksGame <= 0`、未説明の `Colony.Traces` (`SaveTrace`) は仮の日付を付けず `UndatedTraces` に分離。
     - **完了研究 (`ChronicleEntryKind.ResearchCompleted`)**: 選択分岐の最新チェックポイントの `ResearchFamily.CompletedTicks` (`"research.live.completed_ticks"`) 内訳から各研究の完了ティック (`Math.Round(day * TicksPerDay)`) を復元。4,096件上限到達 (`Weakened == true`) 時は `ChronicleGapKind.ResearchKeyCapReached` を明示。
     - **初達成・閾値マイルストーン (`ChronicleEntryKind.Milestone`)**:
       - チェックポイント厳密値 (`ExactTick`): `MembershipFamily.FirstJoinDay`, `RaidFamily.FirstDay`, `HealthDeathFamily.FirstDeathDay`, `ResearchFamily.FirstDay`
       - セーブ内履歴グラフ観測 (`HistorySampleDay`, 0.5日刻み): `MetricKeys.WealthTotalFirst100K`, `MetricKeys.WealthTotalFirst1M`, `MetricKeys.FreeColonistsFirst10`, `MetricKeys.WealthTotalPeakDay`
     - **検証済みセーブ区間差分 (`ChronicleEntryKind.VerifiedInterval`, `ProvenInterval`)**: 証明済み親子チェックポイント列 `prev -> next` ごとに `ColonySaveLineage.CompareMetric`（内部で `HistoryChanges.Compare` を使用）で初回加入・囚人勧誘・誕生・襲撃死・襲撃（到着方法別）・敵撃破・取引・拠点制圧の累計増加と総資産の増減を集約（`IntervalCounterMetricKeys`、すべて `HistoryChanges.IsRunningTotal` を満たす系列のみ）。死亡・襲撃のライブ比較がない区間だけ `VerifiedParentInterval`（ゲーム側累計）にフォールバック。決して単一日の出来事として表示しない。比較不能（`CounterGap`／`CounterDecrease`）や連続記録系列の途絶がある区間は `IntervalNotCounted` の記録上の注意としてまとめて表示し、増減を作らない。
     - **記録欠損・環境変化 (`ChronicleEntryKind.RecordingGap`)**: 親チェックポイント欠損 (`MissingParentCheckpoint`)、未証明セーブ (`UnprovenSaveWithoutCheckpoint`)、チェックポイント未更新区間 (`CarriedCheckpointUnrecordedSpan`)、途中からのライブ記録開始/再開 (`LiveRecordingStartedLateOrReset`)、アーカイブ読込不可 (`ArchivedMetricsUnavailable`)、バージョン/MOD構成/DLC変化 (`EnvironmentChanged`) を明示。
     - **件数上限 (`DefaultMaxEntries = 4096`)**: 結末・欠損・節目を最優先で保持し、次に新しい区間差分、最後に研究完了を完了順で保持する（超過時は新しい研究から省略）。省略数は `OmittedEntryCount` に記録。
     - **分岐の指定 (`ChronicleBranchKey`)**: 系統ノードの添字は記録の増減で変わるため、分岐は種類・チェックポイントID・セーブ時刻・代表ファイル名で指定する。見つからない場合は既定の分岐を表示し `RequestedBranchMissing` を立てる。
     - **結末の所属判定**: 分岐上のセーブが結末を自身の過去（`Lineage` の種類・序数・ティック一致）として持てば確定。同一キャンペーンの後続セーブが持たない場合は別分岐。どのセーブも持たない結末は、他のすべての系統がそれぞれのセーブで否定される場合に限りこの分岐に置き、それ以外は件数のみ通知する。
2. `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`
   - `ReadColonyChronicle(StatsView view, string colonyKey, ChronicleBranchKey? branch = null, bool includeDevMode = false)`。系統入力と結末の根拠をセッションのロック内で同時に複製してから構築する。
3. `Source/RimworldStatistics/UI/Dialog_ColonyChronicle.cs`
   - コロニー切替、分岐切替 (`Branches.Count > 1`)、区分フィルタ（すべて／結末・節目／区間・欠損／研究）、並び順切替（古い順／新しい順）、仮想スクロール年表テーブル、関連統計グラフ (`Dialog_MetricChart`) への遷移、発生日不明痕跡セクション、未導入MOD研究名の安全なフォールバックを実装済み。
4. 導線ボタン追加済みファイル
   - `Source/RimworldStatistics/UI/ColoniesTab.cs`
   - `Source/RimworldStatistics/UI/Dialog_ColonySummaryReport.cs`
   - `Source/RimworldStatistics/UI/Dialog_ColonySaveLineage.cs`（選択ノードがあれば初期分岐として引き渡す）

---

## 3. 残りの作業手順（優先順位順）

### Step 1: V2-A（コロニー記録年表）— 完了（2026-11-18）

- **翻訳**: `Languages/<12言語>/Keyed/ColonyChronicleStatistics.xml`（全言語同順の **87キー**）。作業途中版の79キーに、UI が参照する `RWS_Chronicle_BranchMissingNotice`, `RWS_Chronicle_Fact_ColonistDeaths`, `RWS_Chronicle_Fact_Raids`, `RWS_Chronicle_Fact_VanillaDeaths`, `RWS_Chronicle_Fact_VanillaRaids`, `RWS_Chronicle_Fact_Wealth`, `RWS_Chronicle_Gap_NotCounted`（`{0}`,`{1}`）, `RWS_Chronicle_TipDepartedMax` を追加。用語は各言語の `SaveLineageStatistics.xml` に合わせた。`check_keys.sh`（書式・MOD表示名・提供元名の検査を含む）と `TranslationCopyRefinementTests` に合格。
- **単体テスト**: `Tests/RimworldStatistics.Core.Tests/ColonyChronicleTests.cs`（15件）。§4.5 の受入条件1〜6に加え、分岐キーの再解決、開発者モード結末、持ち越しセーブ末尾の未記録区間、連続記録系列の途絶、1,500チェックポイントの構築速度（約90 ms、上限5秒）を検証。
- **実セーブ受入テスト**: `PrivateSixSaveChartLineageAcceptanceTests.SuppliedSixOriginals_ChronicleKeepsEachBranchToItsOwnProvenChain`（`RWS_PRIVATE_SIX_SAVES_DIR` 指定時のみ実行。ファイル名・ID・内容は検証も出力もしない）。4分岐それぞれが自分の証明済み連鎖だけから作られ、区間が連続チェックポイント間だけに限られ、結末・痕跡を捏造せず、再構築で同一結果になり、元ファイルの長さ・更新時刻・SHA-256 が不変であることを確認。
- **計測**（クラウド Linux、1.7.5005 の6セーブ）: 取込 約1.2秒、既定分岐の初回構築 80〜204 ms（JIT込み）、分岐ごと 10〜14 ms。
- **未実施**: 実ゲーム画面での目視確認（ヘッドレス環境のため）。

### Step 2: V2-D（散布図による統計分析）の実装・PR・マージ

必読設計書 `docs/superpowers/specs/2026-11-17-v2-0-distinctive-features-design.md` §5 に従い、最新 `main` から `feat/v2-scatter-plot` ブランチを作成する。

1. **Core 集計モデル (`Source/RimworldStatistics.Core/Stats/MetricScatterPlot.cs`)**
   - `LeaderboardBuilder.RankableKeys(view)`（基本統計 `BasicMetrics` + 数値統計 `NumericView`）から任意の2指標 `(X, Y)` を選択し、`view.Stats.Colonies` を分類する。
   - **厳守する不変条件**:
     - 値は `BasicMetrics.Value` / `NumericColony.Rows`（`ColonySummaryReport`・`LeaderboardBuilder` と同一経路）から取得し、独自再計算しない。
     - `Unknown`・`NotApplicable`・非有限値・`Excluded`・`OutOfScope` を **絶対に `0` としてプロットしない**。除外されたコロニー数を理由別（`MissingX`, `MissingY`, `MissingBoth`, `NotApplicable`, `Excluded`, `OutOfScope`）に集計して返す。
     - 軸目盛りは既存の `MetricChartTicks.Generate(min, max)` を再利用し、全点同値・1件のみ・負値（資産純増減など）・0・極端な大値（`1e15`）でも `NaN` やゼロ除算を起こさない。
     - 累計値とスナップショット値など異なる `ColonyReportValueKind` を組み合わせた場合や、`MetricCoverage.Partial` の点が含まれる場合の注意フラグを持たせる。
     - v2.0 初期版では相関係数・回帰直線の自動解釈は出さない（仕様 §5.2）。
2. **UI (`Source/RimworldStatistics/UI/Dialog_MetricScatterPlot.cs`) と導線**
   - `RankingsTab.cs` および `DetailsTab.cs` の上部コントロールに散布図ボタンを追加（初期軸は選択中の統計を Y 軸、生存日数 `BasicMetrics.Days` などを X 軸にすると自然）。
   - X軸・Y軸の検索付き統計ピッカー、X↔Y 軸入替ボタン、目盛り線・軸ラベル付き 2D プロット領域を実装。
   - `Complete`/`SnapshotOnly` と `Partial` を視覚的に区別（塗りつぶし点 vs 中抜きリング等）し、同座標に複数コロニーが重なる場合は重なりバッジ表示＋ツールチップ全件列挙＋クリック時の対象選択メニューで対応。
   - 点クリックで `Dialog_ColonySummaryReport` または `Dialog_MetricChart` を開けるようにする。
3. **12言語翻訳 (`Languages/*/Keyed/ScatterPlotStatistics.xml`) と単体テスト (`Tests/RimworldStatistics.Core.Tests/MetricScatterPlotTests.cs`)**
   - 値一致、欠測/`NotApplicable` の非ゼロ埋め除外、負値・0・同値・`1e15` 境界、グループ・除外設定連動、500コロニー規模での構築速度テストを追加。
4. **ローカル検証・コードレビュー・PR作成・CI確認・マージ**

### Step 3: V2-E（自己ベスト更新履歴）の採否判定（条件付き実装）

必読設計書 `docs/superpowers/specs/2026-11-17-v2-0-distinctive-features-design.md` §6 の実装ゲートを適用する。

- **確認済みの技術的制約**:
  - `SaveObservation.LastWriteUtc` はファイルの最終更新日時であり、ファイルのコピー・バックアップ復元・再配置で書き換わるため、過去セーブ間で「現実時間のいつ自己ベストを更新したか」の順序証明には使えない。
  - ゲーム内日数での初到達速度（`wealth.total.first_100k`, `wealth.total.first_1m`, `population.free.first_10`, `raid.first_day` 等）は既に `RankingsTab` の昇順ランキングおよび V2-C / V2-A で表示可能。
- **判断基準**:
  - 既存の `RankingsTab`・V2-B・V2-C・V2-A・V2-D と明確に差別化でき、かつ更新順序を厳密に証明できる範囲が成立する場合のみ実装する（その場合も実績通知ポップアップ・ポイント・報酬は絶対に入れない）。
  - 証明可能な時刻軸では既存ランキング／年表／総括レポートと実質重複する場合は、仕様 §6（「正確な更新時刻を証明できず、現状の最高値/順位表示と実質同じなら不採用のまま終了してよい」）に従い**スキップとし、検証した技術的根拠を最終報告に明記**する。

### Step 4: 最終確認と日本語での完了報告

1. 本引き継ぎファイル `docs/superpowers/plans/2026-11-18-v2-0-remaining-work-handoff.md` を完了状態に合わせて更新（または整理）する。
2. `.devinputs/saves/` が存在する環境では、テスト前後で全セーブファイルの SHA-256 ハッシュが不変（完全読み取り専用）であることを検証しつつ、V2-B / V2-C / V2-A / V2-D の実データ実行時間を計測する。
3. 全テスト・CIスクリプト通過を確認し、変更内容・PR/コミット一覧・テスト/性能計測結果・V2-E の採否根拠・未完了事項（Linux ヘッドレス環境のため実ゲーム画面の目視確認が未実施である点など）を日本語で簡潔に報告する。
4. **GitHub Release と Steam Workshop は更新しない。**

---

## 4. 開発・検証上の重要ルールと注意点

### ローカル検証コマンド（PR作成前に毎回すべて実行）
```bash
dotnet test RimworldStatistics.sln -c Release
bash tools/check_keys.sh
python3 tools/check_translation_formats.py
python3 tools/check_player_faction_access.py
bash tools/package.sh /tmp/rws-pkg.zip && python3 tools/check_sdk_package.py /tmp/rws-pkg.zip && rm -f /tmp/rws-pkg.zip
```
※ `GameDir` を設定できない環境では、先に `dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release -p:UseNuGetRefs=true` でゲーム側 DLL を作り、`bash tools/package.sh --no-build <zip>` で包装する。
※ `.NET SDK 10` が `/home/antigravity/.dotnet` にある環境では `export PATH="/home/antigravity/.dotnet:$PATH" DOTNET_ROOT="/home/antigravity/.dotnet"` を設定する。

### 翻訳品質ゲート（`TranslationCopyRefinementTests.cs`）
- 12言語すべてでキー集合および `{0}`, `{1}` 等のプレースホルダ番号集合が `English` と完全一致していること。
- 空文字、`\ufffd`、`▁` が含まれないこと。
- **内部識別子の混入禁止**: `ThingDef`, `PawnKindDef`, `AmmoDef`, `ExpertiseDef`, `ProcessDef`, `FactionDef`, `MentalBreakDef`, `InspirationDef`, `PawnsArrivalModeDef`, `PawnKind`, `(Def)`, `(DefName)`, `(hediff)`, `（hediff）`, `（Def名）`, `CurrentCap`, `FuelAvailable`, `Pawn.ExitMap`, `HaulToCell`, `ButcherCorpseFlesh`, 台帳ID（`P-010`, `B-002`, `N2-C-002` 等）を翻訳文字列内に絶対に入れないこと。

### 後方互換性・データ安全性
- `Profile` スキーマ、既存統計キー・Revision、`SaveReader.Version`、CSV出力列、外部公開 SDK 契約を破壊しない。
- R2（`r2_credentials.env`）および `.devinputs/saves/` は完全読み取り専用。認証情報・エンドポイント・バケット名等をログ、コミット、PR、チャットに出力しない。
- 他MODのコード・文章・UIデザインをコピーしない。「RimWorld史上初」等の未検証の宣伝文句を使わない。

---

## 5. 次セッション再開用プロンプト

```text
Rimworld Statistics v2.0.0 新機能実装の続きを進めてください。
対象リポジトリ: https://github.com/pengin0503/Rimworld-Statistics

まず最新 main とブランチ `origin/feat/v2-colony-chronicle` を確認し、以下の必読資料をこの順で読んでから作業を再開してください。
1. `docs/superpowers/plans/2026-11-18-v2-0-remaining-work-handoff.md`（ブランチ `feat/v2-colony-chronicle` 上の引き継ぎ書：完了済みの V2-B PR #355・V2-C PR #360、保存済みの V2-A 途中実装、残作業手順、翻訳・CI注意点を記載）
2. `docs/superpowers/specs/2026-11-17-v2-0-distinctive-features-design.md`（§4 V2-A コロニー記録年表、§5 V2-D 散布図による統計分析、§6 V2-E 自己ベスト更新履歴）
3. 未解決 Issue・未マージ PR・最新 main の直近コミット

実施内容（優先順位順）:
1. V2-A（コロニー記録年表）：`feat/v2-colony-chronicle` ブランチの保存済み実装（`ColonyChronicle.cs`, `ProfileSession.cs`, `Dialog_ColonyChronicle.cs`, 導線ボタン）を引き継ぎ、12言語翻訳 `ColonyChronicleStatistics.xml` と単体テスト `ColonyChronicleTests.cs` を追加し、ローカル検証・レビュー・PR作成・CI確認・squashマージまで完了させる。
2. V2-D（散布図による統計分析）：最新 main から `feat/v2-scatter-plot` を作成し、仕様 §5 に従って Core モデル・UI・導線・12言語翻訳・単体テスト・性能確認を実装し、PR作成・CI確認・squashマージまで完了させる。
3. V2-E（自己ベスト更新履歴）：仕様 §6 の採否ゲートに従い、既存ランキングや V2-A〜D と明確に差別化でき更新順序を厳密に証明できる場合のみ実装する（同質・時刻証明不可なら仕様通りスキップし技術的根拠を記録する）。
4. 後方互換性、欠測の非ゼロ扱い、セーブ分岐の非合算、12言語対応、R2/セーブデータの読み取り専用、秘密情報の非公開を厳守する。GitHub Release と Steam Workshop は更新しない。
5. 最後に、全機能（V2-B, V2-C, V2-A, V2-D, V2-E）の実装状況、PR・コミット、テスト・性能確認結果、未完了事項を日本語で簡潔に報告する。
```
