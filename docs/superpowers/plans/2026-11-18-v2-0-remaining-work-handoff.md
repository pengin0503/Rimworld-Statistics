# Rimworld Statistics v2.0.0 残作業・引き継ぎ情報

- 作成日: 2026-11-18（同日、V2-A マージ・V2-D 実装・V2-E 判定の結果で更新）
- 対象リポジトリ: `https://github.com/pengin0503/Rimworld-Statistics`
- V2-D 着手時点の `main` 最新コミット: `1d7ff87` (`PR #363` マージ済み)
- V2-D の作業ブランチ: `feat/v2-scatter-plot`（PR `#364`）

---

## 1. 実装状況サマリー

| 優先順 | 機能ID | 機能名 | 状態 | PR / ブランチ |
|---|---|---|---|---|
| 1 (P0) | V2-B | セーブ系統・分岐の可視化 | **完了・`main` マージ済み** | PR `#355` (`847ef57`) |
| 2 (P0) | V2-C | コロニー総括レポート + 静的HTML出力 | **完了・`main` マージ済み** | PR `#360` (`00657b4`) |
| 3 (P1) | V2-A | コロニー記録年表 | **完了・`main` マージ済み**（翻訳12言語・単体テスト・実セーブ受入テスト込み） | PR `#363` (`1d7ff87`) |
| 4 (P2) | V2-D | 散布図による統計分析 | **完了（2026-11-18、翻訳12言語・単体テスト・実セーブ受入テスト・性能計測込み）** | PR `#364`（ブランチ `feat/v2-scatter-plot`） |
| 5 (P3) | V2-E | 自己ベスト更新履歴（条件付き） | **不採用（2026-11-18、仕様 §6 のゲート不通過。根拠は Step 3）** | ブランチなし |

### `main` に合流済みの関連変更（競合・規約確認済み）
- `7d351fb` (PR `#348` / Issue `#307`): `HistoryChanges.Compare`（履歴区間の増減・変化率・連続性検証）
- `847ef57` (PR `#355`): `ColonySaveLineage` / `Dialog_ColonySaveLineage` / `SaveLineageStatistics.xml`
- `45c8289` (PR `#356`): README・CSV仕様・Steam Workshop原稿の改善
- `10f872b` (PR `#359`): 12言語翻訳監査と `TranslationCopyRefinementTests.cs` の追加
- `00657b4` (PR `#360`): `ColonySummaryReport` / `ColonyReportHtmlExport` / `Dialog_ColonySummaryReport` / `ColonySummaryReportStatistics.xml`
- `1d7ff87` (PR `#363`): `ColonyChronicle` / `ProfileSession.ReadColonyChronicle` / `Dialog_ColonyChronicle` / `ColonyChronicleStatistics.xml` / `ColonyChronicleTests.cs`

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

### Step 2: V2-D（散布図による統計分析）— 完了（2026-11-18）

- **Core (`Source/RimworldStatistics.Core/Stats/MetricScatterPlot.cs`)**: 読み取り専用。`MetricScatterPlot.Build(view, xKey, yKey)` が `view.Stats.Colonies` をコロニーキー順に1回走査し、各軸の値を `BasicMetrics.Value` と `NumericColony.Rows`（ランキング・コロニー比較グラフと同じ経路）から取る。独自の再計算はしない。
  - 描ける統計 (`CanPlot`): 基本統計、または `Overall != Ranking` かつ派生統計（`cross.*`）でない数値統計。実セーブでは `LeaderboardBuilder.RankableKeys` の全キーが該当した。
  - 点にしないコロニーは理由ごとに1回だけ数える（`ScatterExclusionReason`、優先順）: `Excluded` → `OutOfScope` → `NotApplicable` → `NotReached`（閾値未到達。既知の結果だが軸上の数値ではない） → `NoSingleValue`（0÷0 の率・内訳だけの値） → `Missing`（未記録・不明・非有限値）。両軸に問題があれば先の理由を採り、該当軸 (`ScatterAxes.X` / `Y` / `Both`) を保持する。0 埋めはしない。
  - 注意フラグ: `HasPartial`（どちらかが `Partial`）と `ObservedAtDifferentTicks`（両方の値が対象ティックを持ち、それが異なる）。基本統計は `SnapshotOnly` として扱い、対象ティックは観測済みの最終ティック。
  - 軸 (`ScatterAxis`): 値の種類 (`ColonySummaryReport.ClassifyValueKind`)、目盛り（点の範囲に `MetricChartTicks.Generate` を適用、0 を含む）、`Position` は先に縮尺してから差を取り 0〜1 に収める。全点同値・1点・負値・0・`±1e15` でも NaN やゼロ除算がない。点が0件なら目盛りは [0, 1]。
  - 重なり (`Cluster(width, height, spacing)`): 描画サイズの格子に分け、先頭点からのチェビシェフ距離が間隔以下の点を1つの印にまとめる。
  - 相関係数・回帰線は計算も表示もしない（仕様 §5）。点が3件未満なら傾向を読めないことを明示する。
- **UI (`Source/RimworldStatistics/UI/Dialog_MetricScatterPlot.cs`)**:
  - X/Y の検索付き統計ピッカーと X↔Y 入替。ピッカーは `UI/Widgets/MetricPickerDialog.cs` として新設し、`Dialog_ColonySaveLineage` の内部ピッカーもこれに置き換えた。
  - 両軸の値の種類（累計・最高値・時点値・平均/率）、点の数／範囲内のコロニー数／範囲名、理由別の非表示件数（ツールチップに理由ごと最大12件のコロニー名と該当軸）、点なし・3件未満・注意点の件数を表示する。
  - 注意点は中抜きの橙色の印、重なりは件数バッジで示す。ツールチップは各コロニーの X/Y の値と記録状態（ランキングの値・状態列と同じ書式）を出し、異なる日の値ならそれぞれの日付を出す。
  - 点をクリックするとメニューから総括レポート (`Dialog_ColonySummaryReport`) または各軸のコロニー履歴グラフ (`Dialog_ColonyMetricHistory`。基本統計は `BasicMetrics.HasSaveHistory` のときだけ) を開く。重なった点ではまずコロニー一覧（最大30件）を出す。
  - 導線: `RankingsTab`（選択中の統計を Y、生存日数を X にし、選択中のグループ名を引き継ぐ）と `DetailsTab` の上部コントロール。
- **翻訳**: `Languages/<12言語>/Keyed/ScatterPlotStatistics.xml`（全言語同順の33キー。値の種類・記録状態の用語は既存の `RWS_Report_Kind_*` などの訳に合わせた）。`check_keys.sh` 合格（全1,706キーが12言語に存在）。
- **単体テスト**: `Tests/RimworldStatistics.Core.Tests/MetricScatterPlotTests.cs`（17件）。ランキング・比較グラフとの値一致、理由別の除外と非ゼロ埋め、閾値未到達、終了時だけ観測された基本統計の除外、部分値・異なる日の注意、ランキング系・派生統計の除外、軸の境界（全点同値・0のみ・負値・`-2e15..1e15`・1点・僅差）、0点の軸、重なり、グループ範囲・除外設定、決定性と入力の不変、500コロニー×302行の構築（約4.6 ms）と重なり計算（約6.1 ms）。
- **実セーブ受入テスト**: `PrivateSaveFolderScatterAcceptanceTests`（`RWS_PRIVATE_SAVES_DIR` 指定時のみ実行。ファイル名・ID・値は検証も出力もしない）。R2 の実セーブ55件（1.5系22・1.6系20・1.7系13、計約902 MB、34コロニー。読み取り専用で取得し SHA-256 を記録）で、X＝生存日数／最大資産 × 全333統計の666枚すべてについて、点の集合が両軸のランキング対象の積集合と一致し、各点の値がランキングの値と一致し、目盛りが有限で位置が0〜1に収まり、元ファイルの長さ・更新時刻・SHA-256 が不変であることを確認した。
  - 計測（クラウド Linux、2回）: 取込 約4.3秒、散布図1枚の構築 中央値0.21〜0.24 ms・最大5.5〜8.0 ms。点3,838件（部分値を含む504件、異なる日の値408件。後者はすべて前者にも該当）。非表示は `NotApplicable` 176・`NotReached` 160・`NoSingleValue` 48・`Missing` 18,422。
- **未実施**: 実ゲーム画面での目視確認（ヘッドレス環境のため）。

### Step 3: V2-E（自己ベスト更新履歴）— 不採用（2026-11-18 判定）

仕様 §6 の条件（既存表示と明確に差別化でき、かつ「いつ確定した新記録か」の順序を厳密に証明できる）を満たさないため実装しない。ブランチも作っていない。

- **時刻軸**:
  - MOD が書く現実時刻はチェックポイントの `CheckpointPayload.SavedUtc` だけで、保存した PC の時計に依存する（複数PC・時計変更で前後し得る）。厳密な順序の証明にはならない。
  - `SaveObservation.LastWriteUtc` とセーブファイル観測の `ObservedUtc` はファイルの更新日時で、コピー・バックアップ復元・同期で変わる。
  - ゲーム内ティックはコロニーごとの時計で、別コロニー間の前後を決められない。全コロニーの記録が更新された順はティックからは作れない。
- **記録値**:
  - 新記録と言うには、それまでの最高値が `Complete` でなければならない。`Partial` の値は下限にすぎず、途中導入や記録の中断があるコロニーでは更新の有無を断定できない。
  - アーカイブを読めないチェックポイントは時刻の範囲しか与えない。
  - 分岐（巻き戻し・別枝）では、ある枝で更新した記録が代表系統には存在しないことがあり、ランキングの代表値と食い違う履歴になる。
- **実データ**（上記の実セーブ55件、34コロニー）:
  - 観測の出典は SaveFile 51・Checkpoint 5。チェックポイントは1コロニー分（5件）だけで、33コロニーにはチェックポイント系統がない。
  - 順位付け可能な数値統計322件のうちライブ集計は228件。`Complete` のライブ値を1つでも持つコロニーは0（チェックポイントのある1コロニーも途中導入のため `Partial`）。
  - したがって証明可能な順序で並べられる新記録は存在せず、作れる履歴は空か、既存ランキングの最高値・初到達日の言い換えになる。
- **既存表示との重複**: ゲーム内日数での初到達（`wealth.total.first_100k` 等）は `RankingsTab` の昇順ランキング・V2-C 総括レポート・V2-A 年表で表示済みで、最高値と順位はランキングで表示済み。
- 再検討するなら、MOD 自身が単調な順序情報と `Complete` の値を十分な期間記録できていることが前提になる。称号・通知・ポイント・報酬は引き続き作らない。

### Step 4: 最終確認と日本語での完了報告

1. 本引き継ぎファイルを完了状態に更新する — 済（V2-D の PR に含める）。
2. 実データの読み取り専用確認 — V2-A は6セーブ、V2-D は55セーブで実施し、前後で SHA-256 が不変であることを確認済み。
3. 全テスト・CIスクリプトの通過を PR 作成前に確認し、結果は PR 本文に記載する。
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
Rimworld Statistics v2.0.0 の残作業を進めてください。
対象リポジトリ: https://github.com/pengin0503/Rimworld-Statistics

V2-B（PR #355）・V2-C（PR #360）・V2-A（PR #363）・V2-D（PR #364）は実装済み、V2-E は仕様 §6 により不採用です。
1. `docs/superpowers/plans/2026-11-18-v2-0-remaining-work-handoff.md` と `docs/superpowers/specs/2026-11-17-v2-0-distinctive-features-design.md` を読み、PR #364 がマージ済みか確認する。
2. 実ゲーム（1.7。可能なら Windows も）で V2-B 系統画面・V2-C 総括レポートと HTML 出力・V2-A 年表・V2-D 散布図を開き、狭いウィンドウ・12言語・長い MOD 統計名・多数コロニーで表示が崩れないかを目視確認し、問題があれば修正する。
3. 後方互換性、欠測の非ゼロ扱い、セーブ分岐の非合算、R2/セーブデータの読み取り専用、秘密情報の非公開を厳守する。GitHub Release と Steam Workshop は明示的な依頼があるまで更新しない。
```
