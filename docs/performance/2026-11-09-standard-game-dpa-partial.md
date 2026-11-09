# 通常プレイ環境での限定的な DPA 検証（2026-11-09）

> 判定：**限定的な負荷確認のみ。実ゲームでのTPS悪化率・セーブ時間・264項目の数値正確性は未検証。** 元CSV/JSONとスクリーンショットにはローカル環境情報が含まれるため公開リポジトリには保存しない。

## 確認できた事実

- RimWorld: **1.7.5000 rev1275**。Dubs Performance Analyzer: **1.7.21**。Rimworld-Statisticsを有効にした通常環境での測定。MOD本体の実際のビルドハッシュと、未導入時の比較条件は取得していない。
- DPA CSV: `Tick → Game Component Tick`、**2,000サイクル**、16件の`GameComponentTick()`が記録された。記録時点の表示は**144 FPS / 360 TPS**。各行を計算すると、16メソッドの`average_ms_per_cycle`の合算は**0.00200805 ms/サイクル**。
- DPA CSVに`RimworldStatistics`名の`GameComponentTick()`エントリは**0件**。これはこのカテゴリの観測結果であり、MOD全体のゼロ負荷を意味しない。
- CSVの`root_average_ms`は空欄、`tps_target`は0。これらを使った全体CPU使用率・TPS目標達成率の判定はしない。
- 利用者による申告：30倍速にしてもFPS/TPS低下を体感・観測せず、`Update → Game Components`は空、`GameComponent_RimworldStatistics.ExposeData`も一覧に出なかった。**同じ条件のMOD無効時ベースラインは未採取。**
- ソース照合：`Source/RimworldStatistics/GameComponent_RimworldStatistics.cs`には`GameComponentTick`・`GameComponentUpdate`のoverrideがなく、`ExposeData`、`LoadedGame`、`StartedNewGame`、`RecordSave`などを持つ。
- 別の通常プレイ検証では、タイトル画面からの再スキャンで「新規読み込み0件・既読スキップ55件・失敗0件・識別不可0件」を観測し、概要と詳細統計が表示された（35コロニー、合計除外0）。**表示された数値の正確性まで独立照合したものではない。**

## 判定

| 項目 | 現状 |
| --- | --- |
| DPAの`Tick → Game Component Tick`内でのMOD固有メソッド | この範囲では未観測 |
| 毎tick処理の有無 | `GameComponent`の毎tick overrideなしをソースで確認。Harmony等の別経路の実行時間は対象外 |
| 30倍速時の実効性能 | 利用者の主観的な低下なし。実測スナップショットは360 TPS。30倍速目標達成率の証拠ではない |
| 本体のTPS悪化率 ≤2% | **未検証**（同一条件の有効・無効比較なし） |
| `ExposeData`追加時間 ≤50ms または ≤5% | **未測定**（対象メソッドをDPAで表示できず） |
| 保存前後の一貫性 | 未検証（同じ値の保存前後比較が必要） |
| 統計収集264項目の正確性 | **未検証** |

## 次に必要な測定

1. 実ゲームの同じセーブ・MOD構成・ゲーム速度・マップ負荷で、本体有効／無効を交互に切り替え、安定化したTPSを**各5区間以上**記録し中央値を比較する。DPA計測自体のオーバーヘッドに注意する。
2. 保存処理では、**保存成功10組以上**を同じ構成で比較する。所要時間は壁時計または信頼できるスコープ付き計測で測り、`ExposeData`がDPAに表示されない場合は**0 msではなく未測定**と記録する。
3. 部屋と汚染の最適化の正確性は、隔離したVerifyHarnessでread-onlyな`Check room/pollution map contracts`を実施し、`mismatches=0`かつ`errors=0`を確認する。**通常のプレイ環境で破壊的なguided full auditを実行しない。**
4. 比較可能な基準値が得られた場合にのみ、再現性のあるボトルネックに対するテストと最小変更を検討する。現時点では性能改善の本体コード変更を正当化できない。

追跡：[Issue #63](https://github.com/pengin0503/Rimworld-Statistics/issues/63)、統計検証：[Issue #62](https://github.com/pengin0503/Rimworld-Statistics/issues/62)。
