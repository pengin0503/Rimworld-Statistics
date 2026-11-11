# 最適化レビュー 1/7：現行アーキテクチャの分析

- 対象：`main@2cd7a2d`（v1.5.0 以降、SaveReader v15、登録定義 404）
- 作成日：2026-11-11
- 位置づけ：調査資料。本番コードは変更していない。
- 関連資料：[2 問題一覧](2026-11-11-optimization-2-findings.md)／[3 優先順位](2026-11-11-optimization-3-prioritization.md)／[4 確定設計](2026-11-11-optimization-4-adopted-design.md)／[5 実装計画](../plans/2026-11-11-optimization-5-implementation-plan.md)／[6 検証手順](../plans/2026-11-11-optimization-6-verification.md)／[7 引き継ぎプロンプト](../plans/2026-11-11-optimization-7-handoff-prompt.md)

## 0. 調査の範囲と根拠の種類

本資料群では、根拠を次の3種類に分けて書く。混同しないこと。

| 表記 | 意味 |
|---|---|
| **実測** | この調査でコマンドを実行して得た数値。環境は Linux コンテナ（4 vCPU）、.NET SDK 10.0.404、Release ビルド、ページキャッシュが温まった状態。RimWorld 本体は起動していない。 |
| **静的確認** | ソースコード、または RimWorld 1.7.4985 rev1269 の参照DLLをローカルで逆コンパイルして読んだ結果。実行はしていない。 |
| **未検証** | 実ゲームのTPS、保存時間、Harmony適用後の挙動、実ディスクでのI/Oなど。この調査では測定していない。 |

確認した入力：

- ソース全体（`Source/` 約2.2万行、`Tests/`、`tools/`、`.github/workflows/`）
- 公開中の issue 27件（#62〜#133）。オープンなPRは0件。
- R2 バケット `rimworld-17` の `research/vanilla-1.7.4985-audited/START-HERE.md` と `systems/` 3資料（tick・保存・UI/Harmony）。索引の対象版は **1.7.4985 rev1269**。実セーブで使われている 1.7.5000 との完全一致は証明されていない（既存の方針どおり暫定的に同等とみなす）。
- R2 の実セーブ49件（合計824.6MB。1.5系22件、1.6系20件、1.7系7件）。読み取りのみ。**49件にチェックポイント（`numericStatistics`）を含むものは0件**だった（実測）。
- ベースライン：`dotnet test` は合計797件、成功788、スキップ9、失敗0（実測、約4秒）。

## 1. プロジェクト構成と依存関係

```
RimworldStatistics.Api      （78行）  公開SDKの契約。RimWorld/Harmony/Core の型を含まない。
        ▲
RimworldStatistics.Core     （約13,300行）ゲーム非依存。読取・照合・集計・保存・SDKの方針。
        ▲                              テストはここだけを対象にする。
RimworldStatistics          （約8,500行）ゲーム側。Harmonyパッチ、GameComponent、UI、同梱MOD連携。
                                       Assembly-CSharp / UnityStubs / 0Harmony を参照。
```

- `Tests/RimworldStatistics.Core.Tests` は Core に加えて `tools/VerifyHarness/Logic/*.cs` と `Source/RimworldStatistics/IntegratedMods/*/Logic/*.cs` をリンクしてテストする。ゲーム側アセンブリの単体テストはない（ゲームDLLが必要なため）。
- `tools/` に単独実行のベンチマーク5本、実セーブ検証、パッチ対象の静的検査、同梱MODの契約検査がある。
- 依存の向きは一方向で、循環はない。Core の境界は明確で、設計上の大きな強みである。

### Core の内部構成

| フォルダ | 役割 | 主なファイル（行数） |
|---|---|---|
| `Model/` | 保存データの型とJSON | `MetricObservation`(236) `MetricCheckpoint`(192) `ProfileJson`(125) `ColonyIdentity`(72) |
| `Reading/` | `.rws` の読取 | `SaveReader`(1378) `HistoryMetricReader`(379) `RawInflate`(275) ほか過去復元3本 |
| `Import/` | フォルダ走査 | `ImportEngine`(136) `AutoImportGate`(60) |
| `Matching/` | コロニー照合 | `CampaignMatcher`(254) |
| `Profiles/` | 単一プロファイルの更新・保存 | `ProfileMutations`(629) `ProfileStore`(330) `ProfileSession`(256) |
| `Stats/` | 定義・集計・ランキング・取得ファミリー | `MetricCalculator`(550) `MetricRankings`(326) `LiveCatalog`(306) `MetricRegistry`(282) `CrossColonyAggregator`(250) `Families/`25本 |

### ゲーム側の内部構成

| フォルダ | 役割 |
|---|---|
| ルート | `RimworldStatisticsMod`（起動・設定画面）、`GameComponent_RimworldStatistics`（保存・ロードの入口）、`ProfileService`、`ImportRunner`、`SavedStateMetricReader`（保存時スナップショット）、`LiveStateReader` |
| `Patches/` | 常時適用の5クラス（保存完了、タイトルメニュー、エンディング等） |
| `Patches/Metrics/` | 取得ユニットが開いているときだけ適用する27ファイル・約115対象 |
| `Metrics/` | `LiveMetrics`（イベント受付）、`MetricGates`（取得可否）、`MetricAdapters`（適用）、`ExternalSdkHost`、`ModProviderAdapters`、`IntegratedModStartup` |
| `IntegratedMods/` | Hospitality／Recruitment／Dubs Bad Hygiene／Smart Apparel Upgrade の同梱連携 |
| `UI/` | `Dialog_RimworldStatistics` と5タブ |

## 2. 主要なデータフロー

### 2.1 過去セーブの取込（バックグラウンド）

```
タイトル画面の描画（MainMenuButtonPatch.Prefix、毎描画）
  └ ImportRunner.AutoStartOnce → Task.Run
      └ ImportEngine.Run（1スレッド、ファイル名順）
          ├ スキップ判定：session.ReadWithRevision（プロファイル改訂が変わるたび索引を作り直す）
          ├ SaveReader.ReadDetailed(path)
          │    ├ HashingStream：64KiB×4ブロックを循環、SHA-256は専用スレッド
          │    ├ XmlReader で先頭から末尾まで字句解析（不要部分は Skip）
          │    └ BuildMetrics / TryBuildCheckpoint
          ├ session.Mutate（ロック内）：UpsertMetricObservation ×2、AddSaveObservation
          └ 5ファイル読むごとに SaveMode.Background（プロファイル全体を書き直す）
```

### 2.2 ゲーム中の保存（ゲームスレッド）

```
SafeSaver.Save
  ├ CheckpointSavePatch.Prefix   …ファミリー取得中のみ適用
  │    └ CheckpointSaveSession.Enter → LiveMetricAccumulator.PrepareSave
  │         （全ファミリー状態のディープコピー＋JSON直列化）
  ├ GameComponent.ExposeData     …campaignId と numericStatistics を書く
  ├ SaveCompletedPatch.Postfix   …常時適用
  │    └ GameComponent.RecordSave
  │         ├ LiveStateReader（v1の値）
  │         ├ CheckpointMetrics.ToObservation（全ファミリーの出力を構築、SHA-256）
  │         ├ SavedStateMetricReader.Read（スナップショット単位。全Thing走査を含む）
  │         └ session.Mutate(…, SaveMode.Background)   ←ゲームスレッドがロックを取る
  │              └ UpsertCheckpoint / CompactSaveSnapshots
  └ CheckpointSavePatch.Finalizer
バックグラウンド：ProfileSession.WriteLatest
  ├ ロック内で ProfileJson.Serialize（プロファイル全体）
  └ ProfileStore.SaveSerialized：既存ファイル全体の読み戻し・文字列比較・
     JsonDocument 2回解析（バックアップ回転の要否）・tmp書込み・fsync・置換
```

### 2.3 ゲーム中のイベント取得（ゲームスレッド、取得ユニットを開いた場合のみ）

```
Harmony の Prefix/Postfix（約115対象）
  └ GameFacts.Record(family, s => Family.Xxx(s, …))
       └ LiveMetrics.Family(name)：現在のゲームか／メインスレッドか／欠測印／
            _recording 辞書の検索／Hits 辞書の加算
            └ FamilyOps（Count / CountBy / Observe / Track …）が FamilyState を更新
```

既定では実験的取得はOFFで、その場合 `Patches/Metrics/` のパッチは1つも適用されない（`MetricAdapters.ApplyOpen`、静的確認）。例外は同梱MOD連携で、対象MODがあれば設定にかかわらずフックを付ける（[問題一覧 F-04](2026-11-11-optimization-2-findings.md)）。

### 2.4 表示

```
Dialog_RimworldStatistics.DoWindowContents（OnGUI。1フレームに複数回）
  ├ session.TryGetCachedView（ロックを試行。取れなければ前回の表示を使う）
  └ キャッシュがなければ Task.Run(session.GetView)
        ロック内：CampaignMatcher.BuildColonies → StatsCalculator → MetricCalculator.BuildView
                  （コロニー×定義404の行を全部作る）→ MetricRankings → CrossColonyAggregator
各タブは不変の StatsView を描く。DetailsTab は入力が変わったときだけ行を作り直し、可視行だけ描く。
ColoniesTab と OverviewTab は毎回すべての文字列を作り直す。
```

## 3. スレッドとロックのモデル

| スレッド | 行うこと |
|---|---|
| ゲーム（メイン） | Harmonyフック、保存時の `RecordSave`、UI描画、`StartedNewGame` |
| ロード用ワーカー | `LoadedGame`、`FinalizeInit`（`ProfileService.Session` の完了待ちで同期ブロックし得る） |
| スレッドプール | 取込（`ImportEngine`）、プロファイル書込み、ビュー構築、ファイル一覧 |
| 専用スレッド | セーブ1件の読取ごとにSHA-256用の `LongRunning` タスク1本 |

- プロファイルは `ProfileSession._lock` 1本で保護する。直列化はこのロック内、ファイル書込みはロック外（`_ioLock`）。
- ゲームスレッドがロックを取る箇所：`RecordSave`（保存ごと）、`StartedNewGame`、エンディング記録、コロニー一覧の除外チェック操作。
- ロックを長く持つ処理：ビュー構築、プロファイル全体の直列化。これらが走っている間はゲームスレッドの `Mutate` が待つ。

## 4. 永続化モデル

- `profile.json` 1ファイル（スキーマ2）。変更のたびに全体を書き直す。バックアップ3世代と `.previous`、v1 の控え。
- 中身は5種類のレコード：v1観測（`SaveObservations`）、ライブ記録、エンディング、ユーザー決定、数値観測（`MetricObservations`）。
- 数値観測の出所は3種類。増え方が異なる（[問題一覧 F-01](2026-11-11-optimization-2-findings.md)）。

| 出所 | 1件が増える契機 | 圧縮の仕組み | 上限 |
|---|---|---|---|
| `SaveFile` | 取り込んだ `.rws` 1件 | 同一コロニー・同一MOD構成で後のtickがあれば古い行を削除 | ファイル数に比例 |
| `SaveSnapshot` | ゲーム保存1回 | `CompactSaveSnapshots` が定義ごとの証人だけ残す | キャンペーンごとにほぼ一定 |
| `Checkpoint` | ゲーム保存1回（ファミリー取得中） | 祖先に `Compressed` 印を付けるが**数値は保持する** | **なし（保存回数に比例）** |

- セーブ内には `GameComponent_RimworldStatistics` の `campaignId`、`lineageEndings`、`numericStatistics`（チェックポイントJSON）を書く。

## 5. 現行設計の強み（維持するもの）

1. **Core とゲーム側の分離**。集計規則がすべてテスト可能で、797件のテストが4秒で回る。
2. **取得ゲート**。未検証の取得単位は既定で1つもパッチを当てない。
3. **失敗の局所化**。パッチ・ファミリー・スナップショット・SDK提供元がそれぞれ単独で停止し、ゲームの保存やロードを止めない。
4. **不明をゼロにしない規則**と、MOD削除後も記録を残す規則が型（`MetricCoverage`、隔離、`ExtensionData`）で表現されている。
5. **`.rws` を1回の前方走査で読む設計**。マップ本体を木構造に展開しない。
6. `DetailsTab` の「入力が変わったときだけ再構築、可視行だけ描画」。

## 6. 規模の実測（設計判断の前提）

| 項目 | 実測値 | 備考 |
|---|---|---|
| 実セーブ49件の読取合計 | 3.5秒（1件の中央値60ms、95%点154ms） | うちXMLの字句解析だけで3.1秒 |
| 同、順次SHA-256のみ | 0.68秒 | 現行は別スレッドで並行処理 |
| 同、ファイル読取のみ | 0.15秒 | ページキャッシュあり |
| 取込後のプロファイル | 319KB、v1観測49、数値観測42、コロニー33 | |
| ビュー構築（定常） | 14.6ms、割当4.4MB | 初回はJITで390〜545ms |
| プロファイル直列化／復元 | 2.2ms／11.9ms | |
| 再走査（全件スキップ） | 16〜20ms | |
| 保存の書込み1回 | 22ms（うち読み戻し0.9ms、JSON比較5.5ms） | fsyncが支配的 |

実データの規模では、集計・直列化・再走査はいずれも十分に速い。問題が出るのは「数が増えたとき」と「ゲームスレッド上」であり、以降の資料はそこに絞る。
