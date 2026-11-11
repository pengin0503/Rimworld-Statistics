# 2026-11-11 — 19候補の存廃確定（PR #102）

本書はユーザーが明示的に指定した統計存廃の**正式なスコープ決定**である。提案ではなく、過去の一覧・旧設計書に矛盾する記載があれば本書を優先する。対象: `pengin0503/Rimworld-Statistics` のドラフト PR #102。正式版 v1.4.0 の `main` は変更しない。

## 残す（番号 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 14, 15）

| 番号 | 継続する統計群 | 既存の代表キー / 実装 |
|---:|---|---|
| 1 | 襲撃ごとの入植者死亡者数 | `HealthDeathFamily.DeathsByRaid` |
| 2 | 殺害手段別の撃破数 | `HealthDeathFamily.RangedKills`, `MeleeKills`, `ExplosiveKills`, `EnvironmentKills`, etc. |
| 3 | 襲撃の殲滅・撤退・無ダウン撃退 | `RaidFamily.Wiped`, `Retreated`, `RepelledNoDown` |
| 4 | 誘拐・救出・最長拘束日数 | `MembershipFamily.Kidnaps`, `KidnapRescues`, `CaptiveMax` |
| 6 | キャラバンの喪失・最長遠征 | `CaravanFamily.Lost_`, `LongestExpedition` |
| 7 | 手術の成功率・インプラント装着数 | `MedicineFamily.SurgerySuccessRate`, `Implants` |
| 8 | 死因別死亡数・病死率 | `HealthDeathFamily.DeathCauses`, `DiseaseDeathRate` |
| 9 | Ideologyの儀式結果・成功率 | `IdeologyFamily.RitualResults`, `RitualSuccessRate` |
| 10 | Biotechの子どもの成長段階 | `BiotechFamily.ChildStages` |
| 11 | 病気・感染症の継続日数 | `MedicineFamily.DiseaseDurationMean`, `InfectionDurationMax` |
| 12 | 結婚・離婚・恋人関係 | `SocialFamily.Marriages`, `Divorces`, `Lovers` |
| 14 | 所属動物・メカノイド数とコロニー別比較 | `RosterSnapshot.Animals`, `Mechs`, `cross.max.*`, `cross.min.*` |
| 15 | 敵対者・遠征先での救助 | `MedicineFamily.HostileRescues`, `AwayRescues` |

これらの既存のイベント統計について「過去の全セーブから完全復元できない」ことだけを理由に除去しない。記録開始後の継続イベント測定と、根拠のある過去セーブ復元とは別の取得方式・証拠水準である。現在の実機検証状況を勝手に `Verified` に昇格させない。

## 削除（番号 5, 13, 16, 17, 18, 19）

| 番号 | 削除対象の統計キー | 削除した実装・影響 |
|---:|---|---|
| 5 | `membership.unique`, `membership.rejoin` | 公開定義と出力、再加入の専用追跡を削除。初回加入・出産・囚人勧誘の重複を防ぐ内部 `Members` ID 集合は**別目的なので維持** |
| 13 | `membership.recruit_time.min`, `membership.recruit_time.max` | 囚人拘束開始～勧誘成功の専用追跡・イベントフック連携・公開出力を削除。勧誘成功回数は維持 |
| 16 | `facilities.rooms.peak` | 屋内Room数の公開出力と重い `map.regionGrid.AllRegions` 全走査を削除 |
| 17 | `food.no_shortage.longest` | ResourceCounterの食料有無の高頻度イベント監視、マップ別キャッシュ、継続区間追跡、出力を削除。食料の消費・腐敗と育成区画当たり収穫は維持 |
| 18 | `work.crops.cold_loss`, `work.crops.other_loss`, `work.crops.loss_rate` | `Plant.MakeLeafless` の作物死因フック、死亡・播種コホート追跡、死亡率出力を削除。播種・収穫・収穫失敗・枯病イベント（**別の災害統計**）は維持 |
| 19 | `facilities.medical_beds.peak` | `Building_Bed.Medical` 照合と出力を削除。その他の建物・砲台数は維持 |

**既に削除決定済み:** `facilities.running_generators.latest`（実際の稼働中発電機数と派生ランキング）、収納容器**内部**の在庫走査（`IThingHolder` 再帰）。前者は旧PR実装で登録定義から削除済み。後者は単独の公開キーが存在したわけではなく、内部の内容走査を廃止済みである。**別の統計**である定格発電容量・バッテリー容量、直接配置アイテム・Pawn装備／携行品は維持する。

## データと互換性

- 削除する10の公開キーは `LiveCatalog`（8）および `SnapshotCatalog`（2）から削除し、家族別出力・表示フィルタ・翻訳・旧データ出力・関連テスト・ソース由来調査用CSVも更新する。
- PR #102 のカタログは **414 → 404** 登録定義、うち **321 基本 + 83 派生**。存続する統計キー／定義リビジョンは変更しない。
- ソース起点の過去セーブ復元候補台帳は **274 → 264**（live 216、snapshot 39、derived 9）。台帳の新旧数値は、現行レジストリ／旧ドキュメントの基準差による。
- `SaveReader.Version = 15`（変更前 v14）。削除した施設の過去セーブ読込結果を再生成させる。**チェックポイント形式 v1 とプロフィール形式 2 は維持**し、存続メトリックの既存カウントを捨てる目的で Family.Revision を増やさない。
- 過去の保存済みcheckpoint/profileに旧キーが残っていても、**違う新規統計の意味へ再利用しない・既存ファイルを破壊的に編集しない**。現行 `MetricRegistry.Catalog` / `MetricRegistry.Compose` では旧キーは表示されない。旧バイナリ互換と無関係な形で履歴を推定しない。
- 以前のドキュメントに存在する削除項目の説明は**履歴資料**として扱う。未使用であることを確認せず原設計書全体を削除しない。

## 受入条件

1. 削除キー10件＋前回削除の発電機キー1件が、登録・公開・家族出力・残存テストからなくなり、対応するHarmony監視が削除される。
2. 存続13グループと隣接する加入／播種／収穫／食料消費／砲台・定格発電容量について、テストで存在と値の取得を確認する。
3. 日本語・英語の翻訳キー集合を一致させる。旧値を新値と混同しない。実セーブの原本は書き換えない。
4. 全Core CI・静的検査・翻訳・パッケージ化を確認し、結果と未検証の実機カウント比較を明示する。ユーザーが求めるまでPR #102のマージとリリースはしない。
