# 最適化レビュー 4/7：採用する改善案の確定設計

- 対象：`main@2cd7a2d`　作成日：2026-11-11
- 採否の理由は [3 優先順位](2026-11-11-optimization-3-prioritization.md)、作業手順は [5 実装計画](../plans/2026-11-11-optimization-5-implementation-plan.md)。
- ここに書く名前・シグネチャ・規則は確定事項である。実装者は変えない。

## 全体に共通する不変条件

どのタスクも次を守る。1つでも破る変更は、そのタスクの範囲外である。

1. 統計ID（`MetricDefinition.Key`／`StoredKey`）、改訂番号、`SaveReader.Version`（15）、`Profile.CurrentSchemaVersion`（2）、`CheckpointPayload.CurrentFormatVersion`（1）を変えない。
2. 既存テストの期待値を書き換えない。既存テストは変更なしで通ること（T6 の `SauAddonStartup` も含む）。
3. `Unknown` を数値に、欠測をゼロにしない。`Partial`／`Experimental` を強い側へ昇格させない。
4. `profile.json` に既にあるデータを削除しない（T11 は退避ファイルに書いてから軽くする）。
5. 公開SDK `RimworldStatistics.Api` の型・メンバーを変えない。
6. 翻訳キーを追加・削除しない（`tools/check_keys.sh` が通ること）。
7. `README.md` を変えない。

---

## D1（T1）指紋ハッシュの記憶

**場所**　`Source/RimworldStatistics.Core/Model/ColonyIdentity.cs`

**設計**　`FingerprintHash()` の結果を、計算に使った5つの値と組にして1件だけ覚える。呼ばれるたびに現在の5値と照合し、同じなら覚えた結果を返す。違えば計算し直して覚え直す。

```csharp
private sealed record CachedFingerprint(string? Seed, int Random, string? Tile, int AbsTick, uint? Wealth, string? Hash);
private CachedFingerprint? _fingerprint;

public string? FingerprintHash()
{
    var c = _fingerprint;
    if (c is not null && ReferenceEquals(c.Seed, SeedString) && c.Random == PersistentRandomValue
        && ReferenceEquals(c.Tile, StartingTile) && c.AbsTick == GameStartAbsTick && c.Wealth == FirstWealthBits)
        return c.Hash;
    var hash = FingerprintHash(CultureInfo.InvariantCulture.NumberFormat);
    _fingerprint = new CachedFingerprint(SeedString, PersistentRandomValue, StartingTile, GameStartAbsTick, FirstWealthBits, hash);
    return hash;
}
```

**決めたこと**

- 文字列は参照で比べる。内容が同じで別の文字列インスタンスが入った場合は計算し直すだけで、結果は正しい。古い値を返すことはない。
- 無効化の通知は作らない。プロパティのセッターは変えない。
- `_fingerprint` は private フィールドなので JSON に出ない。`Clone()`（`MemberwiseClone`）は参照を写すが、記録は不変で値と照合するので安全。
- 1つの参照への代入だけなので、ロックは要らない。
- `LegacyFingerprints()` と引数付きの private `FingerprintHash(NumberFormatInfo)` は変えない。

## D2（T2）証拠種別の結合

**場所**　`Source/RimworldStatistics.Core/Stats/MetricEvidence.cs`

```csharp
public static MetricEvidence Combine(IEnumerable<MetricEvidence> values)
{
    var any = false;
    var mixed = false;
    var first = MetricEvidence.UnknownLegacy;
    foreach (var v in values)
    {
        if (v == MetricEvidence.UnknownLegacy)
            return MetricEvidence.UnknownLegacy;
        if (!any) { first = v; any = true; }
        else if (v != first) mixed = true;
    }
    return !any ? MetricEvidence.UnknownLegacy : mixed ? MetricEvidence.Mixed : first;
}

public static MetricEvidence Combine(MetricEvidence a, MetricEvidence b)
    => a == MetricEvidence.UnknownLegacy || b == MetricEvidence.UnknownLegacy ? MetricEvidence.UnknownLegacy
        : a == b ? a : MetricEvidence.Mixed;
```

`Combine(new[] { x, y })` の形の呼び出し5箇所を2引数版に置き換える：`ProfileMutations.cs` の3箇所（`Captured`、`MergeMetrics` 内の2箇所）、`MetricCalculator.EvidenceFor`、`LiveMetricAccumulator.Resume`。

## D3（T3）規則の一本化と未使用コードの削除

挙動は変えない。式を移すだけである。

| 新しい置き場所 | 内容 | 置き換える箇所 |
|---|---|---|
| `Stats/MetricObservationOrder.cs`（新規）<br>`public static MetricObservation? Latest(IEnumerable<MetricObservation> observations)` | `OrderByDescending(o => o.SaveTick).ThenByDescending(o => o.ObservedUtc).ThenBy(o => o.Key, StringComparer.Ordinal).FirstOrDefault()` | `MetricCalculator.Representative`、`MetricCalculator.ColonyRow` の `historical`、`ProfileMutations.CompactSaveSnapshots` の `representative` と `lastCaptured`、`CheckpointSelector.Select` の `rep`（いずれも直前の `Where` は呼び出し側に残す） |
| `Stats/SnapshotSelectionPolicy.cs`（新規）<br>`public static bool UsesLastCapture(MetricDefinition d)` | `d.ProviderId is not null \|\| d.Acquisition == "factions"` | `MetricCalculator.ColonyRow`、`ProfileMutations.CompactSaveSnapshots` |
| `ProfileMutations`<br>`public static bool IsCurrentRead(SaveObservation o, int readerVersion)` | `!o.Superseded && o.Status != ObservationStatus.Error && o.ReaderVersion >= readerVersion` | `ProfileMutations.NeedsRead`（`o.FileName == fileName && o.SameFileContent(...)` と組み合わせる）、`ImportEngine.Run` の索引作成 |

削除するもの：

- `CheckpointSaveSession._foreign` と、それを増減する `Enter` の最後の `else` 節、`Exit` の `else if` 節（#127）。別スレッドの保存が何も確定しない挙動は変わらない。
- `MetricObservation.Compressed` の要約コメントを実装に合わせて直す（「値は保持する。代表には選ばれない」）。

`ModMetricAdapterRegistration.ValidateBinding` の削除は D6 で行う。

## D4（T4）取得可否の確認と記録の分離

**場所**　`Source/RimworldStatistics/Metrics/LiveMetrics.cs` ほか

```csharp
/// 現在のゲームでそのファミリーが記録中か。回数は数えない。
/// メインスレッド以外から呼ばれた場合は、従来どおり欠測の印を付けて false を返す。
public static bool IsRecordingNow(string name) => Resolve(name) != null;

/// 記録に使う状態を返す。受理1回として数える。
public static FamilyState? Family(string name)
{
    var family = Resolve(name);
    if (family != null)
        Hits[name] = Hits.GetValueOrDefault(name) + 1;
    return family;
}

private static FamilyState? Resolve(string name) { /* 現在の Family の本体から Hits の加算だけを除いたもの */ }
```

**置換の規則**　`LiveMetrics.Family(X)` の戻り値を **null と比べるだけ** の箇所（`is null`、`!= null`、`== null`、`is not null`。パターン変数 `{ } s` に束縛していない、引数に渡していない、変数に代入していない）を `IsRecordingNow(X)` に置き換える。該当は38箇所（`GameFacts.AnyRecording` の中の1箇所を含む）。

**置き換えない箇所（9箇所）**　状態を使って記録するもの：`ExternalSdkHost.cs:43`、`CaravanMetricPatches.cs:251`、`GameFacts.cs:72`、`ResearchMetricPatches.cs:59,125,145`、`MentalMetricPatches.cs:58,78`、`DeathMetricPatches.cs:39`。

`LiveMetrics.Accumulator?.Family(X)` の4箇所はもともと回数を数えないので触らない。

**意味の変化**　`HitCounts()` は「そのファミリーが記録用の状態を渡した回数」になり、コメントの記述（受理した回数）と一致する。統計値には影響しない。

## D5（T5）SDK定義一覧の保持

**場所**　`Source/RimworldStatistics/Metrics/ModProviderAdapters.cs`、`Source/RimworldStatistics/RimworldStatisticsMod.cs`

- `KnownUnits`、`KnownDescriptors` を、それぞれ `Lazy<IReadOnlyList<…>>` で1回だけ作る。中身の式は現在のまま。作る時点も現在と同じ（最初に読まれたとき。`Declarations` と同じ）。
- 追加：`public static ModMetricDescriptor? FirstDescriptorFor(string acquisitionId)`。`KnownDescriptors` を先頭から見て、取得単位IDごとに最初の1件を持つ辞書（序数比較）を `Lazy` で作る。
- `ActiveDescriptors` と `HasSnapshots` は、`Initialize` が `active` を代入するときに一緒に計算して静的フィールドに持つ。初期値は空配列と `false`。
- 設定画面：`selectable`（`AcquisitionUnits.All.Concat(KnownUnits).ToArray()`）を静的フィールドに1回だけ作る。行ごとの `KnownDescriptors.FirstOrDefault(d => d.AcquisitionId == unit.Id)` を `FirstDescriptorFor(unit.Id)` にする。

## D6（T6）同梱MODのフックを開いている単位にだけ付ける

**考え方**　「定義の登録」と「Harmonyパッチの適用」を分ける。登録は従来どおり MOD コンストラクタで行う（設定画面に単位が並び、定義がプロファイルに記録される）。パッチの適用は、起動時の `ModProviderAdapters.Initialize` が「選択されていて、DLC・依存・バージョンが適格」と判定した単位に対してだけ行う。そのための枠 `InstallEventHook` は既にあり、今は空処理が入っている。

**新規（Core、テスト可能）**　`Source/RimworldStatistics.Core/Stats/DeferredHookRegistry.cs`

```csharp
/// 取得単位ごとに「後で1回だけ実行する取り付け処理」を預かる。スレッドセーフ。
public sealed class DeferredHookRegistry
{
    /// 取り付け処理を預ける。同じ単位に複数預けてよい。Run 済みの単位に預けた場合は実行されない。
    public void Defer(string unitId, Func<bool> install);

    /// その単位の取り付け処理をすべて、預けた順に1回だけ実行する。
    /// 預かりがなければ true。1つでも false を返すか例外を投げたら false（残りは実行しない）。
    /// 2回目以降の呼び出しは、最初の結果を返すだけで何も実行しない。
    public bool Run(string unitId);

    /// Run が true を返した単位。
    public IReadOnlyCollection<string> Installed { get; }
}
```

**ゲーム側の変更**

1. `ExternalSdkHost` に `internal DeferredHookRegistry IntegratedHooks { get; } = new();` を持たせる。
2. `ExternalSdkHost.BuildAdapters` のイベント用登録を、空処理から次に変える。

   ```csharp
   InstallEventHook: _ =>
   {
       if (!IntegratedHooks.Run(d.AcquisitionId))
           throw new InvalidOperationException("Integrated event hook could not be installed for " + d.AcquisitionId);
   },
   ```

   例外は `ModProviderAdapters.Initialize` の既存の `Guard.TryRun(…, (unit, e) => MetricGates.Fail(unit, e))` が受け、その単位だけを閉じる。外部MODが SDK で登録した単位には預かりがないので、従来どおり何も起きない。
3. `IntegratedMods/` 配下で `new Harmony(…).Patch(` を呼ぶ4箇所（`SauOrderHook.TryInstall`、`HospitalityArrival.TryInstall`、`HospitalityRecruitHook` の取り付け、`RecruitmentSource.InstallEventHook`）を、起動時に直接呼ぶのをやめ、`ExternalSdkHost.Instance.IntegratedHooks.Defer(取得単位ID, 取り付け処理)` に預ける。取得単位IDは、そのフックが記録する指標定義の `ApiMetricDefinition.FamilyAcquisitionId`（`"mod." + 所有者ID小文字 + "." + ファミリーID小文字`）と同じ文字列にする。
4. 登録は「パッチが付いた」ことを待たない。対象の型・メンバーの解決（`TryBind`／`TryResolve`）に成功したら登録する。
   - Smart Apparel Upgrade：`SauAddonStartup.Activate` のシグネチャと中身は変えない。`installPatch` に「預けて `true` を返す」処理を渡す。
   - Recruitment：`RecruitmentSdk.Register(allowEvents: true)`（`TryBind` 成功時）。
   - Hospitality：イベント指標の登録条件から「パッチ適用の成否」を外す。
5. `ModMetricAdapterRegistration` から `ValidateBinding` を削除し、`Initialize` の該当ブロックも削除する（#125）。

**変わること**　対象MODがあり取得OFFのとき、フックは0件になる。取り付けに失敗した場合、以前は「イベント指標を登録しない」だったが、今後は「登録はするが、その単位を有効にしても取得されず、ログに1回出る」になる。保存済みの統計ID・改訂番号は変わらない。旧アドオン併用時の判定（`IntegratedModStartup.Safe`）は変えない。

**変えないもの**　スナップショット型の読取（Hospitality の滞在数、DBH、Recruitment の進行中数）はフックを持たず、既に保存時に `IsRecording` を確かめているので触らない。

## D7（T7）過去セーブの先読み並列読取

**場所**　`Source/RimworldStatistics.Core/Import/ImportEngine.cs`

**考え方**　反映（スキップ判定、`Mutate`、進捗通知、定期保存）は今と同じ順序・同じスレッドで行う。並列にするのは「これから必要になりそうなファイルを先に読んでおく」部分だけである。先に読んだ結果は、反映の時点でファイルのサイズと更新時刻が読取時と同じだった場合にだけ使う。そうでなければ今と同じように、その場で読み直す。したがって結果は順次実行と一致する。

**コンストラクタ**

```csharp
public sealed class ImportEngine(SaveReader reader, int readAhead = -1)
```

- `readAhead` は同時に走らせる先読みの数。`-1` は自動で `Math.Clamp(Environment.ProcessorCount / 2, 1, 3)`。`1` 以下は先読みなし（現在と同じ動き）。
- 既存の呼び出し `new ImportEngine(new SaveReader())` はそのまま動く。

**規則**

1. 先読みの対象は、反映位置より後ろのファイルを名前順に見て、「存在し、現時点のスキップ判定で読取が必要」なもの。同時に `readAhead` 件まで。
2. 先読みのタスクは `Task.Run`。本体は `ct.IsCancellationRequested ? null : reader.ReadDetailed(path)` を try で囲み、例外は null にする。
3. 反映時、スキップと判定されたら先読み結果は捨てる。読取が必要と判定されたら、先読みがあり、その結果の `Observation.FileName`／`FileSize`／`LastWriteUtc` が反映時点の `name`／`size`／`time` と一致する場合にだけ使う。一致しない、または null の場合は `reader.ReadDetailed(path)` をその場で呼ぶ。
4. `Run` を抜ける前に、残っている先読みタスクの終了を待つ（開いたファイルを残さない）。中断時も同じ。待ちは最大で読取1件分。
5. 進捗通知、`read`／`skipped`／`failed` の数え方、5件ごとの保存、最後の保存、戻り値は変えない。
6. `SaveReader` はインスタンス状態を持たないので、同じインスタンスを複数スレッドから呼んでよい（49件×7回の並列実行で確認済み）。

## D8（T8）コロニー一覧

**場所**　`Source/RimworldStatistics/UI/ColoniesTab.cs`

- 並び順と各セルの文字列を、鍵 `(StatsView の参照, sortColumn, ascending)` が変わったときだけ作り直す。
- セルの文字列と文字幅（`Text.CalcSize(text).x`、フォントは Small）は、その行が初めて可視になったときに作って保持する。
- 描くのは可視範囲の行だけ。範囲の求め方は `DetailsTab.Draw` と同じ（`first = max(0, (int)(scroll.y / RowHeight))`、`last = min(count - 1, (int)((scroll.y + outRect.height) / RowHeight) + 1)`）。
- 並べ替えのキー（`Column.Sort`）と比較の仕方は変えない。除外チェック、行の選択、ヘッダのクリック、下段の詳細は現在のまま。

## D9（T9）詳細統計のツールチップ

**場所**　`Source/RimworldStatistics/UI/DetailsTab.cs`

- `Line` の `string Tip` を `Func<string> Tip` と `int TipId` に変える。
- `MakeLine` のツールチップ組み立て部分を `BuildTip(...)` に切り出し、`Line` には「初回に `BuildTip` を呼び、結果を覚えて返す」関数を入れる。値・状態・件数の文字列は今までどおり先に作る。
- `DrawLine` は `TooltipHandler.TipRegion(rect, line.Tip, line.TipId)` を呼ぶ（1.7.4985 に存在することを確認済み）。
- `TipId` は `unchecked(0x52570000 + buildVersion * 4099 + 行番号)`。`buildVersion` は `Build` を呼ぶたびに1増やす静的な整数。行を作り直したら番号が変わるので、古いツールチップは残らない。

## D10（T10）ファイルの分割

`partial class` に分けるだけで、メソッド本体・シグネチャ・可視性・順序以外は1文字も変えない。

| 元 | 分割後 | 入れるもの |
|---|---|---|
| `Reading/SaveReader.cs` | `SaveReader.cs` | 型宣言、定数、`Read`／`ReadDetailed`、`ReadState`、`NewObservation`、`Failed`、`ReadGame`、`ReadMeta`、`ReadInfo`、`ReadTickManager`、`ReadStoryteller`、`ReadStats`、`ReadScenario`、`Finish`、`PlayerFactionName`、`ForEachChild`、`Load`、`Text`／`Int`／`Double`／`Bool` |
| | `SaveReader.World.cs` | `ReadWorld`、`ReadWorldMapObjects`、`ReadFaction`、`ReadFactionRelation` |
| | `SaveReader.Maps.cs` | `ReadMaps`、`ReadSavedMapThings`、`ReadSavedPawn`、`ReadHistoricalAtom` |
| | `SaveReader.History.cs` | `ReadSavedResearch`、`ReadHistory`、`ReadRecorder`、`ReadCappedText`、`CountNonWhitespace`、`ReadQuests`、`ReadComponents`、`ReadOwnComponent` |
| | `SaveReader.Metrics.cs` | `TryBuildMetrics`、`TryBuildCheckpoint`、`BuildMetrics` |
| | `SaveReader.HashingStream.cs` | 入れ子クラス `HashingStream` |
| `Profiles/ProfileMutations.cs` | `ProfileMutations.cs` | 型宣言、`Ord`、`RememberModMetrics`、`RegisterLiveCampaign`、`ApplyLiveObservation`、`MergeIdentity`、`UpsertEnding`、`SetExcluded` |
| | `ProfileMutations.SaveObservations.cs` | `NeedsRead`、`IsCurrentRead`、`AddSaveObservation`、`Dominates`、`CanPrune` |
| | `ProfileMutations.MetricObservations.cs` | `UpsertMetricObservation`、`MergeMetrics`、`Supersedes`、`ModCount`、`Quality` |
| | `ProfileMutations.SnapshotCompaction.cs` | `CompactSaveSnapshots` |
| | `ProfileMutations.Checkpoints.cs` | `UpsertCheckpoint`、`UncompressOrphanedAncestors`、`CompressAncestors` |

`SaveReader` は `sealed class` を `sealed partial class` に、`ProfileMutations` は `static class` を `static partial class` にする。

## D11（T11）チェックポイントの退避 — 着手条件付き

着手条件は [3 優先順位](2026-11-11-optimization-3-prioritization.md) §2。満たさない限り実装しない。

### 目的と考え方

圧縮済みの祖先チェックポイントが持つ数値のうち、**通常の集計が決して読まないもの**を `profile.json` から外し、別ファイルに追記して残す。競合が起きて祖先を復元する必要が出たときだけ、別ファイルから読み戻す。情報は失わない。

### 圧縮済み祖先の数値を読む経路（すべて確認済み）

| 経路 | 場所 | 退避後の扱い |
|---|---|---|
| A. 子が競合扱いになり、祖先を代表候補に戻す | `UncompressOrphanedAncestors` | 退避ファイルから読み戻す |
| B. 代表にその指標がないとき、最後に取得できた観測へ戻る | `MetricCalculator.ColonyRow`（`SnapshotSelectionPolicy.UsesLastCapture`） | 子が同じキー・同じ改訂の値を持つなら、tickの大きい子が必ず先に選ばれる。祖先の値は選ばれないので外してよい |
| C. 観測横断の最大・最小 | `MetricCalculator.AcrossObservations` | 選択が `Representative` でない定義のキーは外さない |
| D. 同じチェックポイントの再取込で内訳を足す | `MergeMetrics` | 退避済みの祖先には足さず、届いた観測を退避ファイルにも追記する。読み戻すときに順に畳み込む |

`HistoricalCounterIntervals`、`CheckpointSelector`、検証ハーネスは祖先の `Metrics` を読まない（キー、親、tick、内容ハッシュ、別名だけを使う）。

### データ

`MetricObservation` に任意のフィールドを1つ追加する。

```csharp
/// 数値の一部を退避ファイルへ移した圧縮済み祖先。残っている Metrics は外せなかったキーだけ。
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public bool ArchivedMetrics { get; set; }
```

`Profile` に保存しない一時リストを1つ追加する。

```csharp
/// 退避済みの祖先に対して再取込された観測。次の書込みで退避ファイルへ追記する。
[JsonIgnore]
public List<MetricObservation> PendingCheckpointArchive { get; } = new();
```

退避ファイル：`<プロファイルのフォルダ>/checkpoint-archive/<名前>.jsonl`。`<名前>` は `campaignId` の UTF-8 を SHA-256 にかけた先頭16バイトの小文字16進（32文字）。1行が1観測の JSON（インデントなし、`ProfileJson` と同じ変換規則）。追記のみ。

### 外してよいキーの規則

祖先 A のキー k を外してよいのは、次をすべて満たすときだけ。

1. A は隔離されておらず、`Compressed` かつ `Conflicted` でなく、`ArchivedMetrics` でなく、`Identity.ModCampaignId` が空でない。
2. 確認済みの子 C がある。C は同じキャンペーンのチェックポイントで、隔離も競合もなく、`ParentKey == A.Key`、`SaveTick > A.SaveTick`。複数あれば `MetricObservationOrder.Latest` で選ぶ。
3. A の k の値は隔離されておらず、その値と内訳のどれにも `ExtensionData` がない。
4. C に k があり、隔離されておらず、改訂番号が A の k と同じ。
5. `MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)` の中に `StoredKey == k` の定義が1つ以上あり、そのすべてが `Source == Checkpoint` かつ `Selection == Representative`。

k が1つも外せない祖先は退避しない（`ArchivedMetrics` を付けない）。

### 型

```csharp
// Source/RimworldStatistics.Core/Profiles/CheckpointArchive.cs
public interface ICheckpointArchive
{
    /// 行を追記し、ディスクへ同期してから戻る。失敗は例外。
    void Append(string campaignId, IReadOnlyList<string> jsonLines);

    /// そのキーの退避済み観測を、ファイルに書かれた順にすべて返す。なければ空。読めない行は飛ばす。例外を投げない。
    IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey);
}

public sealed class FileCheckpointArchive(string directory) : ICheckpointArchive { … }

// Source/RimworldStatistics.Core/Profiles/CheckpointArchivePlanner.cs
public sealed record ArchiveDrop(string Key, MetricEntry Entry, int ValueCount);
public sealed record ArchiveStep(MetricObservation Observation, IReadOnlyList<ArchiveDrop> Drops);

public static class CheckpointArchivePlanner
{
    /// 規則1〜5を満たす祖先と、外してよいキーを返す。プロファイルは変更しない。
    public static IReadOnlyList<ArchiveStep> Plan(Profile profile);

    /// 計画時と同じ値（参照が同じで、内訳の件数も同じ）のキーだけを外し、1つでも外せたら ArchivedMetrics を true にする。
    /// 祖先が計画後に競合・非圧縮・退避済みになっていたら何もしない。
    public static void Apply(ArchiveStep step);
}
```

`ProfileJson` に、インデントなしの変換設定 `public static JsonSerializerOptions CompactOptions { get; }` を追加し、退避ファイルの読み書きに使う。

### 書込みの順序（`ProfileSession.WriteLatest`）

1. ロック内：`Plan` を呼ぶ。各ステップの観測全体と、`PendingCheckpointArchive` の各観測を1行の JSON にする。
2. ロック外（`_ioLock` は保持）：キャンペーンごとに `Append`。例外が出たら、この回は退避をあきらめる（手順3を飛ばす）。エラーは `LastArchiveError` に残す。
3. ロック内：各ステップに `Apply`。追記できた `PendingCheckpointArchive` の要素を取り除く。
4. 以降は現在と同じ（直列化して `SaveSerialized`）。

手順2と4の間で落ちた場合、退避ファイルに行があり本体はまだ軽くなっていない状態になる。害はない（次回また追記され、重複行は読み戻し時に同じ結果へ畳み込まれる）。

`ProfileSession` のコンストラクタに `ICheckpointArchive? archive = null` を追加する。null のとき、または `LoadResult.ReadOnly` のときは退避を一切行わない。

### 読み戻し

`ProfileSession.Mutate` の変更処理の直後（同じロック内）と、コンストラクタの読込直後に、次を行う。

- `ArchivedMetrics && !Compressed` の観測それぞれについて `archive.Read(campaignId, key)` を呼ぶ。
- 1件以上返れば、順に `ProfileMutations.RestoreArchivedMetrics(observation, copy)`（中身は既存の `MergeMetrics`）を適用し、`ArchivedMetrics = false` にする。
- 0件なら何もしない。その観測は残っているキーだけを持ち、欠けた指標は不明として表示される。次回の読込時にまた試みる。

### 再取込

`UpsertCheckpoint` で、既存の観測が `ArchivedMetrics` で、内容ハッシュ・親・tick が一致する場合：別名の追加は従来どおり行い、`MergeMetrics` は呼ばず、届いた観測を `PendingCheckpointArchive` に加える。

### 互換性

- スキーマは2のまま。`archivedMetrics` は任意のフィールド。
- 旧版がこのプロファイルを読むと、`archivedMetrics` は未知のフィールドとして保持され、書き戻される。軽くなった祖先は「値の少ない圧縮済み祖先」として扱われ、通常の表示は変わらない。
- 退避ファイルが消えた場合、競合後の復元ができず、該当する値は不明になる。ゼロにはならない。
- `profile.json` のバックアップから古い状態に戻した場合、祖先は退避前の完全な形で戻る。退避ファイルに同じ行が残っていても害はない。
