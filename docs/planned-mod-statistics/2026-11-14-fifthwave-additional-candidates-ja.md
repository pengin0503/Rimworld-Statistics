# 2026-11-14：追加MOD候補の調査台帳（第5波・日本語）

この文書は、既存[人気27候補](2026-11-14-popular-mod-discovery-register.md)、[種族6候補](2026-11-14-popular-race-mod-audit.md)、および[公開対応MOD一覧](../supported-mods.md)と**重複しない、今回新しく調査した5候補**を記録する。**実装予定の確約や対応済みという意味ではない**。

詳細な実コード・元セーブ分析、比較設計、根拠URLは[追加調査日本語版](../research/2026-11-14-additional-investigation-ja.md)を参照。

## 追加候補5件

| ID | MOD | 規模の参考値（2026年11月クロール、概数） | 独立した結果として計測できるもの | 現時点の証拠 | 判断 |
|---|---|---:|---|---|---|
| N01 | [Vanilla Storytellers Expanded - Winston Waves](https://steamcommunity.com/sharedfiles/filedetails/?id=3215569151) | 約19万購読 | **撃退完了した**ウェーブ、ボスウェーブ、確定した報酬選択 | Workshop/更新履歴とmodLangの1.7表記は確認。選択DLL・成功イベント未確認。波の開始や報酬UI表示は不可 | **P0：詳細監査** |
| N02 | [Empire Refactored](https://steamcommunity.com/sharedfiles/filedetails/?id=3701480464) | 約5万購読 | 従属入植地の創設成功、段階上昇、施設完成、独自軍事勝利 | 上流 `LoadFolders.xml` とAboutに1.7専用経路。公開 `LifecycleRegistry.InvokeOn...` と `ISettlementListener` の存在を確認。DLL/実呼出し未検証 | **P0：ソース監査→P1実装** |
| N03 | [Caravan Adventures](https://steamcommunity.com/sharedfiles/filedetails/?id=2558957509) | 約7.6万購読 | 実際に決済された賞金クレジット、独自のストーリー達成 | Workshop説明と更新履歴。1.7ロード対象DLL/成功箇所の根拠不足 | **P1条件付き** |
| N04 | [World Domination 2.0](https://steamcommunity.com/sharedfiles/filedetails/?id=3680501610) | 約1.5万購読 | 自勢力アウトポスト新設、確定した世界制覇達成、実領土獲得 | [ソース](https://github.com/Likewise87/World-Domination-2.0)のAboutに1.7、独自outpost/questクラス。実成功境界は未監査 | **P2条件付き** |
| N05 | [Rim War (Continued)](https://steamcommunity.com/workshop/filedetails/?id=3652981330) | 最新購読数未確認 | 自勢力のワールドマップ上の戦争勝利、固有の勢力制覇 | [維持版ソース](https://github.com/myszsoda/RimWar---Threaded)のAboutに1.7。実成功境界と人気・重複は未監査 | **保留** |

## N02 Empire Refactored の明確な区分

- 元版EmpireやRoyaltyの帝国派閥と統計を混同しない。`packageId=Matathias.Empire` を識別する。
- [`ISettlementListener`](https://github.com/matathias/Empire-1_6-Continued/blob/master/Source/Core/FactionColonies/Interfaces/Lifecycle/ISettlementListener.cs)には創設、撤去、アップグレード、建設後の通知が定義されている。
- [`LifecycleRegistry`](https://github.com/matathias/Empire-1_6-Continued/blob/master/Source/Core/FactionColonies/Util/Registries/LifecycleRegistry.cs)の `InvokeOnSettlementCreated` 等が公開されているが、`Register` は `internal`。外部アダプタから登録可能とはまだ言えない。呼出しを確認した後に最小Harmonyフックを検討する。
- [`TaxLedger.AddTax`](https://github.com/matathias/Empire-1_6-Continued/blob/master/Source/Core/FactionColonies/Settlements/Tax/TaxLedger.cs)は請求書生成であり、プレイヤーの受領銀貨ではない。商品受領や着金が証明されるまで税収額は実装しない。
- 上流READMEの「1.6 only」は、現在の `About.xml` の1.7と `LoadFolders.xml` のv1.7選択先に比べて古い記述。**調査ではソース1.7対応表記まで確認**したが、公式Workshopでユーザーが使う実配布アセンブリの稼働検証は別工程。

## 明示的に今回見送るもの

| 候補 | 見送る理由 |
|---|---|
| RimCities（元版） | 人気は高いが正式版の1.7対応は未確認。非公式1.7移植は実験版で、成功時フックの裏付けが不足 |
| More Faction Interaction（旧版） | システムは興味深いが、1.7で有効にロードされるDLLの確証不足 |
| Colonial Statistics | 現時点では統計MODの**競合比較対象**。本MOD用の外部アダプターではなく、人口1人当たり富などの算出方法を参考にする |

## 実装判断の共通条件

1. 公開済み対応MOD64件、既存の27＋6候補、既存のMOD取得アダプター・汎用統計との重複確認を先に行う。
2. 対象MODの1.7で選択される実配布DLL、成功後の状態変化と自勢力帰属を検証し、初めてキーを登録する。
3. 着手/試行/規定出力/所持スナップショットの累積を成功実績と偽装しない。中断、払い戻し、ロード、枝分かれ、再利用、他勢力を負のテストに含める。
4. 過去のMODが無くなっても保存済み実績を保持。欠測はUnknown/Partial、明示的OFFを維持。毎tickの広域反射/ポーリングを行わない。
5. 専用アダプターが実装されるまで[対応MOD一覧](../supported-mods.md)には追加しない。実ゲーム確認は可能な範囲で `RimWorld-for-mod-test` を使用する。

**本資料の位置付け**：5つとも**調査候補**。N01、N02の監査を優先し、その他は確定できる固有成果が得られるときに実装を検討する。
