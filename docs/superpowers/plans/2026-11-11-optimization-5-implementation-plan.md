# Rimworld Statistics 最適化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 統計の意味と保存データを変えずに、取込・ゲームスレッド・UI・保存データ量の無駄を減らし、主要ファイルを保守しやすい大きさに分ける。

**Architecture:** Core（ゲーム非依存）とゲーム側アセンブリの分離は維持する。変更は「結果を変えない記憶・一本化・遅延」と「先読み並列読取」「フック適用の遅延」に限り、最後にファイル分割とチェックポイント退避（着手条件付き）を行う。

**Tech Stack:** C# / .NET 10（`net10.0`、Nullable、警告はエラー）、xUnit v3（Microsoft.Testing.Platform）、Harmony 3、RimWorld 1.7.4985 参照DLL、System.Text.Json。

**Spec:** [4 確定設計](../specs/2026-11-11-optimization-4-adopted-design.md)（D1〜D11）。背景は [2 問題一覧](../specs/2026-11-11-optimization-2-findings.md) と [3 優先順位](../specs/2026-11-11-optimization-3-prioritization.md)。検証は [6 検証手順](2026-11-11-optimization-6-verification.md)。

## Global Constraints

- 統計ID、改訂番号、`SaveReader.Version = 15`、`Profile.CurrentSchemaVersion = 2`、`CheckpointPayload.CurrentFormatVersion = 1` を変えない。
- 既存テストのファイルを編集しない（追加のみ）。既存テストは変更なしで通ること。基準：合計797、成功788、スキップ9、失敗0。
- `Unknown` を数値に、欠測をゼロにしない。`Partial`／`Experimental` を昇格させない。
- `profile.json` の既存データを削除しない。
- `Source/RimworldStatistics.Api/` を変えない。翻訳キーを追加・削除しない。`README.md` を変えない。
- 1タスク＝1ブランチ＝1PR。`main` へ直接pushしない。マージとリリースは所有者が行う。
- 設計書にない最適化を足さない。迷ったら実装せず、PR本文に質問として書く。
- 各タスクの最後に [6 検証手順](2026-11-11-optimization-6-verification.md) §2 の共通検査をすべて通す。
- T11 は所有者が着手を指示した場合だけ行う。

## Review Focus

仕様が明示していないが、壊れやすい入力・状況。各行のテストは括弧内のタスクに入れてある。

1. 指紋の一部が後から埋まる（新規ゲームで最初の資産記録が入る `MergeIdentity`）ときに古いハッシュを返さないこと（T1 Step 1）。
2. 先読み後、反映までの間にセーブファイルが上書きされたとき、古い読取結果を採用しないこと（T7 Step 1 の2本目）。
3. 取込の中断時に、先読みが開いたファイルを残さず、読み終えた分だけが記録されること（T7：既存の `Run_Cancelled_KeepsDoneAndResumes` が変更なしで通る）。
4. 同じ取得単位に複数の指標があるとき、フックの取り付けが1回だけ行われること（T6 Step 1）。
5. 退避ファイルが消えた・壊れた状態で競合が起きたとき、値がゼロではなく不明になり、例外で保存や表示が止まらないこと（T11 Step 5）。

---

## 準備（全タスク共通）

```bash
python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install -q boto3
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py refs .refs
dotnet build -c Release --nologo
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo
```

R2 の環境変数がない場合は、`-p:UseNuGetRefs=true` を `dotnet build`／`dotnet test` に付ける（CIと同じ参照パッケージを使う）。

期待：ビルド成功、テスト `total: 797 / failed: 0 / succeeded: 788 / skipped: 9`。

---

### Task 0: 計測ハーネスを追加する

**Files:**
- Create: `tools/OptimizationBenchmark/OptimizationBenchmark.csproj`
- Create: `tools/OptimizationBenchmark/Program.cs`

**Interfaces:**
- Produces: コマンド `dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile -- <mode> [args]`。mode は `hash`／`scale`／`savepath <種類数> <保存回数>`／`store <profile.json>`／`real <セーブのフォルダ> [skip]`／`parallel <セーブのフォルダ>`／`profile-report <profile.json>`。以降のタスクが効果の確認に使う。

- [ ] **Step 1: csproj を作る**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>RimworldStatistics.OptimizationBenchmark</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../Source/RimworldStatistics.Core/RimworldStatistics.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `Program.cs` を作る**

[計測ハーネスのソース](2026-11-11-optimization-benchmark-harness.md) のコードブロックをそのまま写す。先頭に次のコメントを付ける。

```csharp
// Standalone, game-independent research benchmark. Measurements of this runner only; never an in-game TPS claim.
// No threshold is enforced. Private saves and profiles are read-only and only anonymous counts and timings are printed.
```

警告がエラーになるので、ビルドで出た未使用変数などは、出力が変わらない範囲で直す。

- [ ] **Step 3: 実行して基準値を記録する**

```bash
dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile -- hash
dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile -- scale
dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile -- savepath 150 1000
```

期待：例外なく終了し、`FingerprintHash ns/op=…`、`SCALE N=…` 5行、`save,prepare_ms,…` の表が出る。出力を PR 本文に貼る（これが以降の比較の基準になる）。

- [ ] **Step 4: Commit**

```bash
git add tools/OptimizationBenchmark
git commit -m "tools: add game-independent optimization benchmark harness"
```

CI のワークフローには追加しない（閾値のない計測を増やさない）。

---

### Task 1: 指紋ハッシュの記憶（D1）

**Files:**
- Modify: `Source/RimworldStatistics.Core/Model/ColonyIdentity.cs:34-35`
- Test: `Tests/RimworldStatistics.Core.Tests/ColonyIdentityFingerprintCacheTests.cs`（新規）

**Interfaces:**
- Produces: `ColonyIdentity.FingerprintHash()` のシグネチャと戻り値は不変。

- [ ] **Step 1: テストを書く**

```csharp
namespace RimworldStatistics.Core.Tests;

public sealed class ColonyIdentityFingerprintCacheTests
{
    private static ColonyIdentity Sample() => new()
    {
        SeedString = "seed", PersistentRandomValue = 7, StartingTile = "12,0", GameStartAbsTick = 3600000,
        FirstWealthBits = 0x41200000, StartingPawnIds = { "Thing_Human1" },
    };

    private static string? Fresh(ColonyIdentity source) => new ColonyIdentity
    {
        SeedString = source.SeedString, PersistentRandomValue = source.PersistentRandomValue, StartingTile = source.StartingTile,
        GameStartAbsTick = source.GameStartAbsTick, FirstWealthBits = source.FirstWealthBits,
    }.FingerprintHash();

    [Fact]
    public void RepeatedCalls_ReturnTheSameValue()
    {
        var id = Sample();
        var first = id.FingerprintHash();
        Assert.NotNull(first);
        Assert.Equal(first, id.FingerprintHash());
        Assert.Equal(first, Fresh(id));
    }

    [Fact]
    public void ChangingAnyFingerprintField_IsNeverServedFromAStaleValue()
    {
        var id = Sample();
        _ = id.FingerprintHash();
        id.SeedString = "other";
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.PersistentRandomValue = 8;
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.StartingTile = "13,0";
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.GameStartAbsTick = 1;
        Assert.Equal(Fresh(id), id.FingerprintHash());
        id.FirstWealthBits = 5;
        Assert.Equal(Fresh(id), id.FingerprintHash());
    }

    [Fact]
    public void IncompleteIdentity_BecomesHashedOnceItsMissingPartArrives()
    {
        var id = Sample();
        id.FirstWealthBits = null;
        Assert.Null(id.FingerprintHash());
        id.FirstWealthBits = 0x41200000;   // what MergeIdentity does for a new game's first wealth record
        Assert.Equal(Fresh(id), id.FingerprintHash());
    }

    [Fact]
    public void Clone_DoesNotShareAStaleValue()
    {
        var id = Sample();
        var before = id.FingerprintHash();
        var copy = id.Clone();
        copy.SeedString = "changed";
        Assert.Equal(before, id.FingerprintHash());
        Assert.Equal(Fresh(copy), copy.FingerprintHash());
        Assert.NotEqual(before, copy.FingerprintHash());
    }

    [Fact]
    public void SerializedProfile_HasNoCacheField()
    {
        var profile = new Profile();
        var id = Sample();
        _ = id.FingerprintHash();
        profile.SaveObservations.Add(new SaveObservation { FileName = "a.rws", Identity = id });
        var json = ProfileJson.Serialize(profile);
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id.FingerprintHash(), ProfileJson.Deserialize(json).SaveObservations[0].Identity.FingerprintHash());
    }
}
```

- [ ] **Step 2: テストを実行する**

Run: `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo -- --filter-class "*ColonyIdentityFingerprintCacheTests"`
Expected: 5件とも成功（挙動は現行でも同じ。これは記憶を入れても挙動が変わらないことを固定するテストである）。

- [ ] **Step 3: `FingerprintHash()` を D1 のコードに置き換える**

`public string? FingerprintHash() => FingerprintHash(CultureInfo.InvariantCulture.NumberFormat);` の1行を、設計 D1 の `CachedFingerprint` レコード、`_fingerprint` フィールド、メソッド本体に置き換える。他は触らない。

- [ ] **Step 4: 全テストと計測**

Run: `dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release --nologo`
Expected: `total: 802 / failed: 0 / succeeded: 793 / skipped: 9`

Run: ハーネスの `hash` と `scale`。
Expected: `FingerprintHash ns/op` が T0 の値の10分の1以下、`bytes/op=0`。`SCALE N=2000 C=200 modVary=True` の `mutate_total_ms` が T0 の値の3分の1以下。各行の `saveObs`／`metricObs`／`json_MB` は T0 と同じ。数値を PR 本文に貼る。

- [ ] **Step 5: Commit**

```bash
git add Source/RimworldStatistics.Core/Model/ColonyIdentity.cs Tests/RimworldStatistics.Core.Tests/ColonyIdentityFingerprintCacheTests.cs
git commit -m "perf(core): memoize the colony fingerprint hash by its input values"
```

---

### Task 2: 証拠種別の結合を割当なしにする（D2、#122）

**Files:**
- Modify: `Source/RimworldStatistics.Core/Stats/MetricEvidence.cs:13-22`
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.cs`（`new[] { … }` を渡す3箇所）
- Modify: `Source/RimworldStatistics.Core/Stats/MetricCalculator.cs`（`EvidenceFor`）
- Modify: `Source/RimworldStatistics.Core/Stats/LiveMetricAccumulator.cs`（`Resume`）
- Test: `Tests/RimworldStatistics.Core.Tests/MetricEvidenceCombineTests.cs`（新規）

**Interfaces:**
- Produces: `MetricEvidencePolicy.Combine(IEnumerable<MetricEvidence>)`（結果不変）、`MetricEvidencePolicy.Combine(MetricEvidence a, MetricEvidence b)`（新規）。

- [ ] **Step 1: テストを書く**

```csharp
namespace RimworldStatistics.Core.Tests;

public sealed class MetricEvidenceCombineTests
{
    private static readonly MetricEvidence[] All = Enum.GetValues<MetricEvidence>();

    /// <summary>The implementation shipped through v1.5.0, kept here as the oracle.</summary>
    private static MetricEvidence Oracle(IEnumerable<MetricEvidence> values)
    {
        var seen = new HashSet<MetricEvidence>(values);
        if (seen.Count == 0 || seen.Contains(MetricEvidence.UnknownLegacy))
            return MetricEvidence.UnknownLegacy;
        return seen.Count == 1 ? seen.Single() : MetricEvidence.Mixed;
    }

    [Fact]
    public void Sequence_MatchesTheOriginalForEveryInputUpToLengthThree()
    {
        Assert.Equal(Oracle(Array.Empty<MetricEvidence>()), MetricEvidencePolicy.Combine(Array.Empty<MetricEvidence>()));
        foreach (var a in All)
        {
            Assert.Equal(Oracle(new[] { a }), MetricEvidencePolicy.Combine(new[] { a }));
            foreach (var b in All)
            {
                Assert.Equal(Oracle(new[] { a, b }), MetricEvidencePolicy.Combine(new[] { a, b }));
                foreach (var c in All)
                    Assert.Equal(Oracle(new[] { a, b, c }), MetricEvidencePolicy.Combine(new[] { a, b, c }));
            }
        }
    }

    [Fact]
    public void Pair_MatchesTheSequenceForm()
    {
        foreach (var a in All)
            foreach (var b in All)
                Assert.Equal(Oracle(new[] { a, b }), MetricEvidencePolicy.Combine(a, b));
    }

    [Fact]
    public void Pair_DoesNotAllocate()
    {
        _ = MetricEvidencePolicy.Combine(MetricEvidence.Verified, MetricEvidence.Experimental);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _ = MetricEvidencePolicy.Combine(MetricEvidence.Verified, MetricEvidence.Experimental);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
```

- [ ] **Step 2: 失敗を確認する**

Run: `dotnet test … -- --filter-class "*MetricEvidenceCombineTests"`
Expected: コンパイルエラー（2引数の `Combine` がない）。

- [ ] **Step 3: `MetricEvidencePolicy` を D2 のコードにし、5箇所の `Combine(new[] { x, y })` を `Combine(x, y)` に置き換える**

対象の確認：`grep -rn "Combine(new\[\]" Source/` が0件になること。`ProfileMutations.Captured` の `Combine(new[] { direct, prior })`、`MergeMetrics` の2箇所、`MetricCalculator.EvidenceFor`、`LiveMetricAccumulator.Resume`（要素2つを改行して渡している箇所）。

- [ ] **Step 4: 全テスト**

Expected: T1 の後なら `total: 805 / failed: 0 / skipped: 9`。ハーネスの `hash` で `EvidenceCombine` の `bytes/op` が T0 より小さいこと。

- [ ] **Step 5: Commit**

```bash
git add -A Source/RimworldStatistics.Core Tests/RimworldStatistics.Core.Tests/MetricEvidenceCombineTests.cs
git commit -m "perf(core): combine capture evidence without allocating a set"
```

---

### Task 3: 規則の一本化と未使用コードの削除（D3、#127、#120 の一部）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/MetricObservationOrder.cs`
- Create: `Source/RimworldStatistics.Core/Stats/SnapshotSelectionPolicy.cs`
- Modify: `Source/RimworldStatistics.Core/Stats/MetricCalculator.cs:261-262,283-288`
- Modify: `Source/RimworldStatistics.Core/Stats/CheckpointSelector.cs:35-36`
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.cs:38-44,219-220,253,260-262`
- Modify: `Source/RimworldStatistics.Core/Import/ImportEngine.cs:71-75`
- Modify: `Source/RimworldStatistics.Core/Stats/CheckpointSaveSession.cs:14,43-46,88-94`
- Modify: `Source/RimworldStatistics.Core/Model/MetricObservation.cs:99-102`（コメントのみ）
- Test: `Tests/RimworldStatistics.Core.Tests/SharedSelectionRulesTests.cs`（新規）

**Interfaces:**
- Produces:
  - `public static class MetricObservationOrder { public static MetricObservation? Latest(IEnumerable<MetricObservation> observations); }`
  - `public static class SnapshotSelectionPolicy { public static bool UsesLastCapture(MetricDefinition d); }`
  - `ProfileMutations.IsCurrentRead(SaveObservation o, int readerVersion)`（public static bool）
- T7 が `IsCurrentRead` を、T11 が `MetricObservationOrder.Latest` と `SnapshotSelectionPolicy.UsesLastCapture` を使う。

- [ ] **Step 1: テストを書く**

```csharp
namespace RimworldStatistics.Core.Tests;

public sealed class SharedSelectionRulesTests
{
    private static MetricObservation Obs(string key, int tick, int minute) => new()
    {
        Key = key, SaveTick = tick, ObservedUtc = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Latest_PrefersTickThenTimeThenSmallestKey()
    {
        Assert.Null(MetricObservationOrder.Latest(Array.Empty<MetricObservation>()));
        Assert.Equal("b", MetricObservationOrder.Latest(new[] { Obs("a", 10, 9), Obs("b", 20, 0) })!.Key);
        Assert.Equal("b", MetricObservationOrder.Latest(new[] { Obs("a", 20, 1), Obs("b", 20, 2) })!.Key);
        Assert.Equal("a", MetricObservationOrder.Latest(new[] { Obs("b", 20, 2), Obs("a", 20, 2) })!.Key);
    }

    [Fact]
    public void UsesLastCapture_IsExactlyProvidersAndFactions()
    {
        foreach (var d in MetricRegistry.Catalog)
            Assert.Equal(d.ProviderId is not null || d.Acquisition == "factions", SnapshotSelectionPolicy.UsesLastCapture(d));
        Assert.Contains(MetricRegistry.Catalog, d => SnapshotSelectionPolicy.UsesLastCapture(d));
    }

    [Theory]
    [InlineData(false, ObservationStatus.Ok, 15, true)]
    [InlineData(true, ObservationStatus.Ok, 15, false)]
    [InlineData(false, ObservationStatus.Error, 15, false)]
    [InlineData(false, ObservationStatus.Unidentifiable, 15, true)]
    [InlineData(false, ObservationStatus.Ok, 14, false)]
    [InlineData(false, ObservationStatus.Ok, 16, true)]
    public void IsCurrentRead_MatchesTheScanRule(bool superseded, ObservationStatus status, int readerVersion, bool expected)
    {
        var o = new SaveObservation { FileName = "a.rws", Superseded = superseded, Status = status, ReaderVersion = readerVersion };
        Assert.Equal(expected, ProfileMutations.IsCurrentRead(o, 15));
    }
}
```

- [ ] **Step 2: 失敗を確認する**（コンパイルエラー）

- [ ] **Step 3: D3 の表のとおりに実装する**

式は一字一句そのまま移す。呼び出し側の `Where(...)` は残す。例：

```csharp
// MetricCalculator.Representative
private static MetricObservation? Representative(IEnumerable<MetricObservation> observations)
    => MetricObservationOrder.Latest(observations);

// MetricCalculator.ColonyRow
if (SnapshotSelectionPolicy.UsesLastCapture(d) && (rep is null || Entry(rep, d) is null))
{
    var historical = MetricObservationOrder.Latest(observations.Where(o => Entry(o, d) is not null));

// ProfileMutations.NeedsRead
=> !p.SaveObservations.Any(o => o.FileName == fileName && o.SameFileContent(size, lastWriteUtc) && IsCurrentRead(o, readerVersion));

// ImportEngine の索引
skippable = p.SaveObservations.Where(o => ProfileMutations.IsCurrentRead(o, SaveReader.Version))
    .Select(o => (o.FileName, o.FileSize, o.LastWriteUtc)).ToHashSet();
```

`CheckpointSaveSession`：`_foreign` フィールド、`Enter` の最後の `else { _foreign[…] = … }`、`Exit` の `else if (_foreign.TryGetValue(…)) { … }` を削除する。

`MetricObservation.Compressed` のコメントを次に置き換える。

```csharp
/// <summary>
/// An ancestor checkpoint with a confirmed later child: it is never the representative again. Its values are kept, so it can
/// be restored when that child is later set aside as conflicted.
/// </summary>
```

- [ ] **Step 4: 全テスト**

Expected: 既存テストは変更なしで全件成功（特に `NumericCheckpointTests`、`NumericSnapshotRetentionTests`、`ImportEngineTests`、`ProfileMutationsTests`）。ハーネス `scale` の `saveObs`／`metricObs`／`json_MB` が T0 と同じ。

- [ ] **Step 5: Commit**

```bash
git add -A Source/RimworldStatistics.Core Tests/RimworldStatistics.Core.Tests/SharedSelectionRulesTests.cs
git commit -m "refactor(core): single definitions for latest-observation order, last-capture policy and current-read rule; drop unused foreign-thread map"
```

---

### Task 4: 取得可否の確認と記録を分ける（D4、#132）

**Files:**
- Modify: `Source/RimworldStatistics/Metrics/LiveMetrics.cs:58-79,114-115`
- Modify: `Source/RimworldStatistics/Patches/Metrics/GameFacts.cs:84-90`
- Modify: `Source/RimworldStatistics/Patches/Metrics/*.cs`、`Source/RimworldStatistics/Metrics/*.cs` のうち該当38箇所

**Interfaces:**
- Produces: `LiveMetrics.IsRecordingNow(string name)`（public static bool）。`LiveMetrics.Family(string)` と `HitCounts()` のシグネチャは不変。

ゲーム側アセンブリには単体テストがないので、検索とビルドで確認する。

- [ ] **Step 1: 置換前の件数を確認する**

Run: `grep -rn "LiveMetrics.Family(" Source/RimworldStatistics --include=*.cs | grep -cE "\) (is null|!= null|== null|is not null)"`
Expected: `38`

Run: `grep -rn "LiveMetrics.Family(" Source/RimworldStatistics --include=*.cs | grep -vcE "\) (is null|!= null|== null|is not null)"`
Expected: `9`

- [ ] **Step 2: `LiveMetrics` を D4 のとおりにする**

現在の `Family` の本体を `private static FamilyState? Resolve(string name)` に移し、`Hits[name] = …` の2行だけを除く。`Family` と `IsRecordingNow` は D4 のコード。`HitCounts` のコメントを「How often each family handed out its state for recording this game」に直す。

- [ ] **Step 3: 38箇所を置き換える**

| 置換前 | 置換後 |
|---|---|
| `LiveMetrics.Family(X) is null` | `!LiveMetrics.IsRecordingNow(X)` |
| `LiveMetrics.Family(X) == null` | `!LiveMetrics.IsRecordingNow(X)` |
| `LiveMetrics.Family(X) != null` | `LiveMetrics.IsRecordingNow(X)` |
| `LiveMetrics.Family(X) is not null` | `LiveMetrics.IsRecordingNow(X)` |

`GameFacts.AnyRecording` の `if (LiveMetrics.Family(f) != null)` はこの38箇所に含まれる。D4 に挙げた9箇所と `LiveMetrics.Accumulator?.Family(` の4箇所は触らない。

- [ ] **Step 4: 確認する**

Run: `grep -rn "LiveMetrics.Family(" Source/RimworldStatistics --include=*.cs | grep -cE "\) (is null|!= null|== null|is not null)"`
Expected: `0`

Run: `grep -rn "LiveMetrics.Family(" Source/RimworldStatistics --include=*.cs | wc -l`
Expected: `9`

Run: `grep -rn "IsRecordingNow(" Source/RimworldStatistics --include=*.cs | wc -l`
Expected: `39`（定義1＋置換38）

Run: `dotnet build -c Release --nologo` と [6 検証手順](2026-11-11-optimization-6-verification.md) §2（パッチ対象の静的検査を含む）。
Expected: すべて成功。

- [ ] **Step 5: Commit**

```bash
git commit -am "fix(metrics): availability probes no longer count as accepted hook hits"
```

PR 本文に「検証ハーネスの `HitCounts` は今後、記録用の状態を渡した回数だけを数える。統計値は変わらない」と書く。

---

### Task 5: SDK定義一覧を1回だけ作る（D5、#124）

**Files:**
- Modify: `Source/RimworldStatistics/Metrics/ModProviderAdapters.cs:40-53,116-117`
- Modify: `Source/RimworldStatistics/RimworldStatisticsMod.cs:48,80`

**Interfaces:**
- Produces: `ModProviderAdapters.FirstDescriptorFor(string acquisitionId)`（public static `ModMetricDescriptor?`）。`KnownUnits`／`KnownDescriptors`／`ActiveDescriptors`／`HasSnapshots` の型と内容は不変。

- [ ] **Step 1: `ModProviderAdapters` を変える**

```csharp
private static readonly Lazy<IReadOnlyList<AcquisitionUnit>> KnownUnitList = new(() => /* 現在の KnownUnits の式 */);
private static readonly Lazy<IReadOnlyList<ModMetricDescriptor>> KnownDescriptorList = new(() => /* 現在の KnownDescriptors の式 */);
private static readonly Lazy<IReadOnlyDictionary<string, ModMetricDescriptor>> FirstByAcquisition = new(() =>
{
    var map = new Dictionary<string, ModMetricDescriptor>(StringComparer.Ordinal);
    foreach (var d in KnownDescriptorList.Value)
        map.TryAdd(d.AcquisitionId, d);
    return map;
});
private static IReadOnlyList<ModMetricDescriptor> activeDescriptors = Array.Empty<ModMetricDescriptor>();
private static bool hasSnapshots;

public static IReadOnlyList<AcquisitionUnit> KnownUnits => KnownUnitList.Value;
public static IReadOnlyList<ModMetricDescriptor> KnownDescriptors => KnownDescriptorList.Value;
public static ModMetricDescriptor? FirstDescriptorFor(string acquisitionId) => FirstByAcquisition.Value.GetValueOrDefault(acquisitionId);
public static IReadOnlyList<ModMetricDescriptor> ActiveDescriptors => activeDescriptors;
public static bool HasSnapshots => hasSnapshots;
```

`Initialize` の末尾、`active = enabled.ToArray();` の直後に次を足す。

```csharp
activeDescriptors = active.Select(x => x.Registration.Descriptor).ToArray();
hasSnapshots = active.Any(x => x.Registration.Descriptor.Source == MetricObservationSource.SaveSnapshot);
```

- [ ] **Step 2: 設定画面を変える**

`RimworldStatisticsMod` に `private static AcquisitionUnit[]? selectableUnits;` を足し、`var selectable = selectableUnits ??= AcquisitionUnits.All.Concat(ModProviderAdapters.KnownUnits).ToArray();` にする。`var provider = ModProviderAdapters.KnownDescriptors.FirstOrDefault(d => d.AcquisitionId == unit.Id);` を `var provider = ModProviderAdapters.FirstDescriptorFor(unit.Id);` にする。

- [ ] **Step 3: 確認する**

Run: `dotnet build -c Release --nologo` と §2 の共通検査。
Expected: 成功。`grep -n "KnownDescriptors.FirstOrDefault" Source -r` が0件。

- [ ] **Step 4: Commit**

```bash
git commit -am "perf(ui): build provider unit and descriptor lists once after registration is sealed"
```

---

### Task 6: 同梱MODのフックを開いている単位にだけ付ける（D6、#117、#125）

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/DeferredHookRegistry.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/DeferredHookRegistryTests.cs`（新規）
- Modify: `Source/RimworldStatistics/Metrics/ExternalSdkHost.cs`（`IntegratedHooks` の追加、`BuildAdapters` の `InstallEventHook`）
- Modify: `Source/RimworldStatistics/Metrics/ModProviderAdapters.cs:11-17,84-90`（`ValidateBinding` の削除）
- Modify: `Source/RimworldStatistics/Metrics/IntegratedModStartup.cs`
- Modify: `Source/RimworldStatistics/IntegratedMods/Hospitality/Source/HospitalitySdk.cs`、`HospitalityArrival.cs`、`HospitalityRecruitHook.cs`
- Modify: `Source/RimworldStatistics/IntegratedMods/Recruitment/Source/RecruitmentSdk.cs`、`RecruitmentSource.cs`
- Modify: `Source/RimworldStatistics/IntegratedMods/SmartApparelUpgrade/Source/SauOrderHook.cs`（呼び出し元のみ）
- Modify: `docs/manual-test-checklist.md`（確認項目の追加）

**Interfaces:**
- Produces: D6 の `DeferredHookRegistry`（`Defer`／`Run`／`Installed`）、`ExternalSdkHost.IntegratedHooks`（internal）。

- [ ] **Step 1: `DeferredHookRegistry` のテストを書く**

```csharp
namespace RimworldStatistics.Core.Tests;

public sealed class DeferredHookRegistryTests
{
    [Fact]
    public void NothingRunsUntilTheUnitIsRun()
    {
        var registry = new DeferredHookRegistry();
        var calls = 0;
        registry.Defer("mod.a.x", () => { calls++; return true; });
        Assert.Equal(0, calls);
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public void RunExecutesEveryInstallerOfThatUnitOnceInOrder()
    {
        var registry = new DeferredHookRegistry();
        var order = new List<string>();
        registry.Defer("mod.a.x", () => { order.Add("first"); return true; });
        registry.Defer("mod.a.x", () => { order.Add("second"); return true; });
        registry.Defer("mod.a.y", () => { order.Add("other"); return true; });
        Assert.True(registry.Run("mod.a.x"));
        Assert.True(registry.Run("mod.a.x"));   // a second metric of the same unit asks again
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Equal(new[] { "mod.a.x" }, registry.Installed);
    }

    [Fact]
    public void UnitWithoutInstallersSucceeds()
        => Assert.True(new DeferredHookRegistry().Run("mod.external.sdk"));

    [Fact]
    public void FailureOrExceptionIsRememberedAndNeverRetried()
    {
        var registry = new DeferredHookRegistry();
        var calls = 0;
        registry.Defer("mod.a.fail", () => { calls++; return false; });
        registry.Defer("mod.a.fail", () => { calls++; return true; });
        registry.Defer("mod.a.throw", () => { calls++; throw new InvalidOperationException("target changed"); });
        Assert.False(registry.Run("mod.a.fail"));
        Assert.False(registry.Run("mod.a.fail"));
        Assert.False(registry.Run("mod.a.throw"));
        Assert.Equal(2, calls);   // the installer after a failure never runs
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public void InstallerDeferredAfterRunIsIgnored()
    {
        var registry = new DeferredHookRegistry();
        Assert.True(registry.Run("mod.a.x"));
        var calls = 0;
        registry.Defer("mod.a.x", () => { calls++; return true; });
        Assert.True(registry.Run("mod.a.x"));
        Assert.Equal(0, calls);
    }
}
```

- [ ] **Step 2: 失敗を確認する**（コンパイルエラー）

- [ ] **Step 3: `DeferredHookRegistry` を実装する**

1つの `lock` で守る `Dictionary<string, List<Func<bool>>>`（序数比較）と `Dictionary<string, bool>`（実行結果）。`Run` はロック内で結果が既にあれば返し、なければ預かりを順に実行する。例外は捕まえて false にする。

- [ ] **Step 4: テストが通ることを確認する**

- [ ] **Step 5: ゲーム側を D6 の「ゲーム側の変更」1〜5のとおりに変える**

進め方：

1. `grep -rn "\.Patch(" Source/RimworldStatistics/IntegratedMods` で4箇所を確認する。
2. それぞれについて、そのフックが呼ぶ記録処理（`SauHostClient.OnIssuedOrder`、`HospitalitySdk.RecordArrival`、`HospitalitySdk.RecordRecruitment`、`RecruitmentSdk.RecordEvent`）が使う指標定義の `OwnerPackageId` と `FamilyId` を読み、取得単位IDを `"mod." + OwnerPackageId.ToLowerInvariant() + "." + FamilyId.ToLowerInvariant()` で求める。1つのフックが複数ファミリーの指標を記録する場合は、それぞれのIDに同じ取り付け処理を預ける（`DeferredHookRegistry` は単位ごとに1回しか実行しないが、取り付け処理の側でも二重適用を防ぐ静的フラグを持たせる）。
3. 起動時の直接呼び出しを `ExternalSdkHost.Instance.IntegratedHooks.Defer(id, 取り付け処理)` に置き換える。
4. 登録の条件から「パッチが付いたか」を外す（D6 の4）。`SauAddonStartup.cs` は変えない。
5. `ExternalSdkHost.BuildAdapters` の `InstallEventHook: _ => { }` を D6 のコードにする。
6. `ModMetricAdapterRegistration` の `Action? ValidateBinding = null` と、`Initialize` の `if (registration.ValidateBinding is not null && …) continue;` を削除する。

- [ ] **Step 6: 確認する**

Run: `grep -rn "\.Patch(" Source/RimworldStatistics/IntegratedMods | wc -l`
Expected: `4`（取り付け処理の中身は残る）

Run: `grep -rn "ValidateBinding" Source | wc -l`
Expected: `0`

Run: `python3 tools/IntegratedModAudit/check_embedded_integrations.py`
Expected: 成功。失敗した場合、このスクリプトが「起動時に直接パッチする」書き方を前提に文字列を検査しているなら、同じ意図（1つのMODに同梱されること、旧アドオン併用時に本体側が付けないこと、指標IDが変わらないこと）を保ったまま検査対象の文字列を新しい書き方に合わせる。意図を弱める変更はしない。

Run: 全テストと §2 の共通検査。
Expected: 成功。`SauAddonStartupTests`、`SauAddonContractTests`、`HospitalityRecruitmentTests`、`RecruitmentAccountingTests` は変更なしで通る。

- [ ] **Step 7: 手動確認項目を追加する**

`docs/manual-test-checklist.md` の末尾に節「同梱MODのフック適用（最適化 T6）」を足し、[6 検証手順](2026-11-11-optimization-6-verification.md) §4.1 の4項目を写す。

- [ ] **Step 8: Commit**

```bash
git add -A Source Tests docs/manual-test-checklist.md tools/IntegratedModAudit
git commit -m "perf(integrations): install bundled MOD hooks only for open, eligible acquisition units"
```

PR 本文に、D6 の「変わること」と、実ゲームでの確認が未実施であることを書く。

---

### Task 7: 過去セーブの先読み並列読取（D7）

**Files:**
- Modify: `Source/RimworldStatistics.Core/Import/ImportEngine.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/ImportEngineReadAheadTests.cs`（新規）

**Interfaces:**
- Consumes: `ProfileMutations.IsCurrentRead`（T3）。
- Produces: `ImportEngine(SaveReader reader, int readAhead = -1)`。`Run` のシグネチャと戻り値は不変。

- [ ] **Step 1: テストを書く**

```csharp
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ImportEngineReadAheadTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private sealed class SyncProgress(Action<ImportProgress> onReport) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => onReport(value);
    }

    private static byte[] Save(string seed, int ticks)
        => new SaveXmlBuilder().Minimal().World(seed: seed, prv: 1).Tick(ticksGame: ticks).Maps(64 * 1024).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime? time = null)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time ?? Stamp);
    }

    private static string Imported(TempDir saves, int readAhead)
    {
        using var store = new TempDir();
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var result = new ImportEngine(new SaveReader(), readAhead).Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.False(result.Cancelled);
        session.Mutate(p => p.ImportState.LastScanUtc = null, SaveMode.None);   // the only wall-clock value
        session.Flush();
        return session.Read(ProfileJson.Serialize);
    }

    [Fact]
    public void ReadAhead_ProducesExactlyTheSequentialProfile()
    {
        using var saves = new TempDir();
        for (var i = 0; i < 24; i++)   // 8 colonies, 3 saves each: grouping, superseding and pruning all take part
            Write(saves, $"c{i % 8}-s{i / 8}.rws", Save("seed" + (i % 8), 60000 * (i / 8 + 1)), Stamp.AddMinutes(i));
        File.WriteAllText(saves.File("broken.rws"), "<savegame><meta>");
        var sequential = Imported(saves, readAhead: 1);
        Assert.Equal(sequential, Imported(saves, readAhead: 3));
        Assert.Equal(sequential, Imported(saves, readAhead: 8));
    }

    [Fact]
    public void FileReplacedAfterItWasReadAhead_IsReadAgainAtItsTurn()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 6; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000));
        var replacement = Save("replaced", 120000);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFile == "s1.rws")   // s3 is within the read-ahead window by now, or will be read at its turn
                Write(saves, "s3.rws", replacement, Stamp.AddHours(1));
        });
        new ImportEngine(new SaveReader(), readAhead: 3).Run(saves.Path, session, progress, TestContext.Current.CancellationToken);
        var s3 = session.Read(p => p.SaveObservations.Single(o => o.FileName == "s3.rws" && !o.Superseded));
        Assert.Equal((long)replacement.Length, s3.FileSize);
        Assert.Equal(Stamp.AddHours(1), s3.LastWriteUtc);
        Assert.Equal(120000, s3.Values.TicksGame);
    }

    [Fact]
    public void UnchangedFilesAreNotReadAhead_OnAWarmScan()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 6; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000));
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var engine = new ImportEngine(new SaveReader(), readAhead: 3);
        engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        var warm = engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.Equal((0, 6, 0), (warm.Read, warm.Skipped, warm.Failed));
    }
}
```

- [ ] **Step 2: 失敗を確認する**（コンパイルエラー：コンストラクタに第2引数がない）

- [ ] **Step 3: 実装する**

`Run` の構造は変えない。足すのは次の4点だけである。

```csharp
public sealed class ImportEngine(SaveReader reader, int readAhead = -1)
{
    private readonly int _readAhead = readAhead < 0 ? Math.Clamp(Environment.ProcessorCount / 2, 1, 3) : readAhead;
```

(a) ループの前：

```csharp
var pending = new Dictionary<int, Task<SaveReadResult?>>();
var nextToSchedule = 0;
bool AlreadyRead(string name, long size, DateTime time) => session.ReadWithRevision((p, revision) => { /* 現在のラムダの本体 */ });

void ScheduleReadAhead(int current)
{
    if (_readAhead <= 1)
        return;
    nextToSchedule = Math.Max(nextToSchedule, current + 1);
    while (pending.Count < _readAhead && nextToSchedule < files.Count && !ct.IsCancellationRequested)
    {
        var index = nextToSchedule++;
        var (aheadPath, aheadName) = files[index];
        try
        {
            var aheadInfo = new FileInfo(aheadPath);
            if (!aheadInfo.Exists || AlreadyRead(aheadName, aheadInfo.Length, aheadInfo.LastWriteTimeUtc))
                continue;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            continue;   // decided again, and reported, at its turn
        }
        pending[index] = Task.Run(() =>
        {
            try { return ct.IsCancellationRequested ? null : reader.ReadDetailed(aheadPath); }
            catch (Exception) { return null; }
        });
    }
}
```

(b) `foreach (var (path, name) in files)` を添字付きの `for (var index = 0; index < files.Count; index++)` に変え、中断判定の直後（`done++` の前）で `ScheduleReadAhead(index);` を呼ぶ。

(c) スキップ判定は `AlreadyRead(name, size, time)` を呼ぶ形にし、スキップ時は `pending.Remove(index);` してから `continue`。読取は次に置き換える。

```csharp
SaveReadResult result;
if (pending.Remove(index, out var ahead) && ahead.GetAwaiter().GetResult() is { } prefetched
    && prefetched.Observation.FileName == name && prefetched.Observation.FileSize == size
    && prefetched.Observation.LastWriteUtc == time)
    result = prefetched;
else
    result = reader.ReadDetailed(path);
```

(d) ループ全体を `try { … } finally { … }` で囲み、`finally` で残りの先読みを待つ。

```csharp
finally
{
    foreach (var task in pending.Values)
    {
        try { task.Wait(); }
        catch (AggregateException) { /* the task body never throws; nothing to report */ }
    }
}
```

`info.Exists` が false で `continue` する箇所でも `pending.Remove(index);` を呼ぶ。それ以外（`read`／`failed` の数え方、`Mutate` の内容、`SaveEvery`、最後の保存、戻り値）は1行も変えない。

- [ ] **Step 4: テスト**

Run: 全テスト。
Expected: 新規3件と、既存の `ImportEngineTests` 13件・`NumericImportTests`・`RealSavesRegressionTests` が変更なしで成功。

Run（R2 の実セーブがある場合のみ）: `RWSTATS_REAL_SAVES_DIR=$PWD/.devinputs/saves dotnet test …` とハーネス `real .devinputs/saves skip`。
Expected: テスト成功。`IMPORT round=1` の `profile_bytes`／`saveObs`／`metricObs`／`colonies` が T0 と同じで、`cold_ms` が T0 より小さい。数値を PR 本文に貼る（コア数も書く）。

- [ ] **Step 5: Commit**

```bash
git add Source/RimworldStatistics.Core/Import/ImportEngine.cs Tests/RimworldStatistics.Core.Tests/ImportEngineReadAheadTests.cs
git commit -m "perf(import): read upcoming saves ahead in parallel, commit strictly in file order"
```

---

### Task 8: コロニー一覧の保持と可視行だけの描画（D8、#123）

**Files:**
- Modify: `Source/RimworldStatistics/UI/ColoniesTab.cs:49-77,105-140`
- Modify: `docs/manual-test-checklist.md`

**Interfaces:**
- Produces: `ColoniesTab.Draw(Rect, StatsView, SaveFileIndex?)` のシグネチャは不変。

- [ ] **Step 1: 保持用の型とフィールドを足す**

```csharp
private sealed class TableCache(StatsView view, int sortColumn, bool ascending, int[] order)
{
    public StatsView View { get; } = view;
    public int SortColumn { get; } = sortColumn;
    public bool Ascending { get; } = ascending;
    public int[] Order { get; } = order;
    public string[]?[] Cells { get; } = new string[order.Length][];
    public float[]?[] Widths { get; } = new float[order.Length][];
}

private static TableCache? cache;
```

- [ ] **Step 2: `Draw` を変える**

- 先頭の並べ替えを「`cache` が null、または `View` の参照・`SortColumn`・`Ascending` のどれかが違うときだけ」行い、結果の `int[]` で `TableCache` を作る。並べ替えの式（`OrderBy`／`OrderByDescending` と `Columns[sortColumn].Sort`）は現在のまま。
- `selected` は `Array.FindIndex(cache.Order, i => view.Colonies[i].Key == selectedKey)` で求め、見つかったら `cache.Order[その位置]` を使う。
- 行の描画ループを可視範囲だけにする（D8 の式。`outRect.height` を使う）。
- `DrawColony` に行番号 `row` と `cache` を渡し、`cache.Cells[row]` が null なら `Columns.Select(c => c.Text(s)).ToArray()` と、各文字列の `Text.CalcSize(text).x` を作って入れる。以降は保持した文字列と幅を使い、`Text.CalcSize` は呼ばない（`if (cache.Widths[row]![c] > cell.width) TooltipHandler.TipRegion(cell, text);`）。
- `Text.Anchor`、色、除外チェック、行クリックの処理は変えない。

- [ ] **Step 3: 確認する**

Run: `dotnet build -c Release --nologo`、§2 の共通検査。
Expected: 成功。

- [ ] **Step 4: 手動確認項目を追加する**

`docs/manual-test-checklist.md` に節「コロニー一覧（最適化 T8）」を足し、[6 検証手順](2026-11-11-optimization-6-verification.md) §4.2 の項目を写す。

- [ ] **Step 5: Commit**

```bash
git commit -am "perf(ui): keep the colony table order and cell text per view; draw visible rows only"
```

---

### Task 9: 詳細統計のツールチップを表示時に作る（D9）

**Files:**
- Modify: `Source/RimworldStatistics/UI/DetailsTab.cs:21,192-220,224-391`
- Modify: `docs/manual-test-checklist.md`

**Interfaces:**
- Produces: `DetailsTab.Draw` と `DetailsTab.Format` のシグネチャは不変。

- [ ] **Step 1: `Line` を変える**

```csharp
private sealed record Line(string Label, string Value, string State, string Count, Func<string> Tip, int TipId, int Depth, string? ExpandKey, bool Dim);

private sealed class LazyTip(Func<string> build)
{
    private string? _text;
    public string Get() => _text ??= build();
}

private static int buildVersion;
```

- [ ] **Step 2: `MakeLine` を分ける**

`var tip = new StringBuilder();` から `tip.Append((string)"RWS_Details_Tip_Ledger".Translate(d.LedgerIds, d.Key));` までを、同じ引数を受け取る `private static string BuildTip(NumericRow row, string label, int depth, Inputs input, NumericColony? colony, string count)` に、中身を変えずに移す（`count` は使っていなければ引数から外す）。`MakeLine` は引数に `int tipId` を足し、最後を次にする。

```csharp
var lazy = new LazyTip(() => BuildTip(row, label, depth, input, colony));
return new Line(label, value, state, count, lazy.Get, tipId, depth, expandKey,
    v.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown);
```

`Build` の先頭で `var version = unchecked(++buildVersion);` を取り、`MakeLine` を呼ぶ3箇所すべてで `tipId: unchecked(0x52570000 + version * 4099 + result.Count)` を渡す（`result.Count` はその行が入る位置）。

- [ ] **Step 3: `DrawLine` を変える**

`TooltipHandler.TipRegion(rect, line.Tip);` を `TooltipHandler.TipRegion(rect, line.Tip, line.TipId);` にする。

- [ ] **Step 4: 確認する**

Run: `dotnet build -c Release --nologo`、§2 の共通検査（`tools/check_keys.sh` を含む。翻訳キーは `BuildTip` の中にそのまま残っているので結果は変わらない）。
Expected: 成功。

- [ ] **Step 5: 手動確認項目を追加する**（[6 検証手順](2026-11-11-optimization-6-verification.md) §4.3）

- [ ] **Step 6: Commit**

```bash
git commit -am "perf(ui): build Details tooltips on hover instead of for every row"
```

---

### Task 10: `SaveReader` と `ProfileMutations` を部分クラスに分ける（D10）

**Files:**
- Modify: `Source/RimworldStatistics.Core/Reading/SaveReader.cs`
- Create: `Source/RimworldStatistics.Core/Reading/SaveReader.World.cs`、`SaveReader.Maps.cs`、`SaveReader.History.cs`、`SaveReader.Metrics.cs`、`SaveReader.HashingStream.cs`
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.cs`
- Create: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.SaveObservations.cs`、`ProfileMutations.MetricObservations.cs`、`ProfileMutations.SnapshotCompaction.cs`、`ProfileMutations.Checkpoints.cs`

**Interfaces:**
- Produces: 公開・内部のシグネチャはすべて不変。

2コミットに分ける（`SaveReader`、`ProfileMutations`）。

- [ ] **Step 1: 分割前の基準を取る**

Run: `dotnet test …`（件数を控える）。R2 の実セーブがある場合は、ハーネス `real .devinputs/saves skip` の `IMPORT round=1` 行を控える。

- [ ] **Step 2: `SaveReader` を D10 の表のとおりに分ける**

各ファイルの先頭は、元のファイルから必要な `using` と `namespace RimworldStatistics.Core;`、`public sealed partial class SaveReader { … }`。メソッドは切り取って貼るだけで、本体・コメント・空行を変えない。`using` は、そのファイルで使うものだけにする（警告がエラーになるため）。

- [ ] **Step 3: 確認して Commit**

Run: `dotnet build -c Release --nologo && dotnet test …`
Expected: 件数が Step 1 と同じ。

Run: `git show --stat HEAD` ではなく、移動だけであることの確認として `cat Source/RimworldStatistics.Core/Reading/SaveReader*.cs | grep -v "^using \|^namespace \|partial class SaveReader\|^{$\|^}$" | sort | md5sum` と、分割前の `git show HEAD:Source/RimworldStatistics.Core/Reading/SaveReader.cs | grep -v "^using \|^namespace \|class SaveReader\|^{$\|^}$" | sort | md5sum` を比べる。
Expected: 同じ値（行の集合が同じ）。

```bash
git add Source/RimworldStatistics.Core/Reading
git commit -m "refactor(core): split SaveReader into partial files by save section (move only)"
```

- [ ] **Step 4: `ProfileMutations` を同じ手順で分け、同じ確認をして Commit**

```bash
git add Source/RimworldStatistics.Core/Profiles
git commit -m "refactor(core): split ProfileMutations into partial files by record kind (move only)"
```

- [ ] **Step 5: 実セーブでの一致確認（R2 の実セーブがある場合）**

Run: ハーネス `real .devinputs/saves skip`
Expected: `IMPORT round=1` の `profile_bytes`／`saveObs`／`metricObs`／`colonies` が Step 1 と同じ。

---

### Task 11: チェックポイントの退避（D11）— 所有者の着手指示がある場合のみ

着手条件：[3 優先順位](../specs/2026-11-11-optimization-3-prioritization.md) §2。所有者が実プロファイルで次を実行し、条件を満たすと判断して指示した場合だけ進める。

```bash
dotnet run -c Release --project tools/OptimizationBenchmark/OptimizationBenchmark.csproj --no-launch-profile -- profile-report "<RimWorldの設定フォルダ>/RimworldStatistics/profile.json"
```

**Files:**
- Modify: `Source/RimworldStatistics.Core/Model/MetricObservation.cs`（`ArchivedMetrics`）
- Modify: `Source/RimworldStatistics.Core/Model/Profile.cs`（`PendingCheckpointArchive`）
- Modify: `Source/RimworldStatistics.Core/Model/ProfileJson.cs`（`CompactOptions`）
- Create: `Source/RimworldStatistics.Core/Profiles/CheckpointArchive.cs`
- Create: `Source/RimworldStatistics.Core/Profiles/CheckpointArchivePlanner.cs`
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.Checkpoints.cs`（`UpsertCheckpoint`、`RestoreArchivedMetrics`）
- Modify: `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`
- Modify: `Source/RimworldStatistics/ProfileService.cs`
- Test: `Tests/RimworldStatistics.Core.Tests/CheckpointArchiveTests.cs`（新規）

**Interfaces:**
- Consumes: `MetricObservationOrder.Latest`、`SnapshotSelectionPolicy.UsesLastCapture`（T3）。
- Produces: D11 の `ICheckpointArchive`、`FileCheckpointArchive`、`CheckpointArchivePlanner`、`ArchiveStep`、`ArchiveDrop`、`ProfileJson.CompactOptions`、`ProfileSession(ProfileStore, Func<DateTime>, ICheckpointArchive? archive = null)`、`ProfileSession.LastArchiveError`、`ProfileMutations.RestoreArchivedMetrics(MetricObservation target, MetricObservation archived)`。

テストの補助：`NumericCheckpointTests` の `Branches`、`ProfileWithColony`、`CloneWithAlias`、`Count`、`Overall` と同じ形の補助メソッドを、新しいテストファイルの中に写して使う（既存ファイルは編集しない）。メモリ上の `ICheckpointArchive` 実装（`Dictionary<string, List<string>>` に行を持ち、`Append` を失敗させるフラグと、`Read` を空にするフラグを持つ）をテスト内に作る。

- [ ] **Step 1: 退避ファイルのテストと実装（`FileCheckpointArchive`）**

テスト名と確認内容：

| テスト | 確認すること |
|---|---|
| `Append_ThenRead_ReturnsEveryCopyOfThatKeyInOrder` | 同じキーを2回追記すると `Read` が2件を書いた順に返す。別のキーは混ざらない。 |
| `Read_SkipsATornLastLine` | ファイル末尾に壊れた行（途中で切れた JSON）を足しても、それ以前の行は返り、例外が出ない。 |
| `Read_MissingFileOrDirectory_ReturnsEmpty` | フォルダがなくても空を返す。 |
| `FileName_IsAHashOfTheCampaignId` | `campaignId` に `../x` や `a/b` を渡しても、作られるファイルは `checkpoint-archive` 直下の `[0-9a-f]{32}\.jsonl` だけ。 |
| `Append_Failure_Throws` | フォルダの位置に同名のファイルがあるとき `IOException`（または `UnauthorizedAccessException`）を投げる。 |

実装：`Append` は `Directory.CreateDirectory`、`FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)`、各行＋`\n` を UTF-8（BOMなし）で書き、`Flush(flushToDisk: true)`。`Read` は全行を読み、`JsonSerializer.Deserialize<MetricObservation>(line, ProfileJson.CompactOptions)` を try で囲み、`Source == Checkpoint && Key == checkpointKey` のものを返す。全体を try で囲み、I/O 例外は空のリストにする。

- [ ] **Step 2: 計画のテストと実装（`CheckpointArchivePlanner`）**

| テスト | 確認すること |
|---|---|
| `Plan_DropsOnlyKeysTheConfirmingChildAlsoHolds` | 親と子が同じキー・同じ改訂を持つ組み込みチェックポイント指標は `Drops` に入る。子にないキーは入らない。 |
| `Plan_KeepsKeysOfAnotherRevisionInTheChild` | 子の同じキーが別の改訂なら入らない。 |
| `Plan_KeepsUnknownKeysQuarantinedEntriesAndExtensionData` | カタログにないキー、隔離された値、`ExtensionData` を持つ値は入らない。 |
| `Plan_KeepsKeysOfNonRepresentativeDefinitions` | `ModMetricDescriptor` で `Source = Checkpoint`、`Selection = ObservationMax` の指標を登録したとき、そのキーは入らない。 |
| `Plan_IgnoresUncompressedConflictedArchivedAndCampaignlessAncestors` | 該当する祖先は計画に出ない。 |
| `Plan_DoesNotChangeTheProfile` | 呼び出し前後で `ProfileJson.Serialize(profile)` が同じ。 |
| `Apply_SkipsEntriesThatChangedAfterPlanning` | 計画後に祖先のその値を差し替える（参照が変わる）、または内訳を1つ足すと、そのキーは外されない。 |
| `Apply_DoesNothingWhenTheAncestorWasUncompressedMeanwhile` | 計画後に `Compressed = false` にすると何も外さず、`ArchivedMetrics` も付かない。 |

- [ ] **Step 3: 表示が変わらないことのテスト（最重要）**

`Archiving_NeverChangesAnyShownValue`：次の筋書きそれぞれについて、(1) 退避なしのセッション、(2) メモリ上の退避先を持つセッションを作り、同じ順に観測を入れ、`Flush()` を呼んだ後、`GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits)` の `Numeric`（全コロニーの行と各コロニーの行について、定義キー、内訳キー、`Number`、`Coverage`、`Evidence`、`Numerator`、`Denominator`、`SampleCount`、`ThroughTick`、`Targets`、`Valid` を文字列に並べたもの）が一致することを確かめる。(2) では少なくとも1つの祖先が `ArchivedMetrics` になっていることも確かめる。

筋書き：
1. 直線 P → C1 → C2（すべて同じ指標を持つ）。
2. 途中でSDK提供元が外れる：P と C1 は提供元の指標を持ち、C2 は持たない（`ModMetricPersistenceTests.RemovedEventProviderRetainsCompressedAncestorCountsWithoutDoubleAdding` と同じ形を3世代に延ばす）。
3. 分岐：P の子が A と B。
4. 競合：1 の後、C2 と同じキーで内容ハッシュの違う観測を入れる（C1 が代表に戻る）。
5. 二段の競合：4 の後、C1 と同じキーで内容ハッシュの違う観測も入れる（P が代表に戻る）。
6. 再取込：1 の後、P と同じ内容で別名の違う観測をもう一度入れ、その後 4 を行う。

- [ ] **Step 4: 再取込と読み戻しの実装**

`UpsertCheckpoint`：D11「再取込」のとおり。`RestoreArchivedMetrics(target, archived)` は `MergeMetrics(target, archived)` を呼ぶだけの public メソッド。

- [ ] **Step 5: セッションへの組み込みと、失敗時のテスト**

`ProfileSession` を D11「書込みの順序」「読み戻し」のとおりに変える。`_views` は消さない（結果が同じなので）。`_changeSeq` も退避のためには増やさない。

| テスト | 確認すること |
|---|---|
| `Write_AppendsBeforeTheProfileIsSlimmed` | `Flush()` 後、退避先に祖先の完全な観測（外す前のキーをすべて含む）が1行あり、`profile.json` を読み直すとその祖先は `archivedMetrics: true` で、外したキーを持たない。 |
| `Write_WhenAppendFails_KeepsFullMetricsAndStillSavesTheProfile` | `Append` が例外を投げる退避先では、祖先は完全なまま保存され、`LastArchiveError` が設定され、`LastSaveError` は null。 |
| `Conflict_WithUnreadableArchive_ShowsUnknownNeverZero` | 退避後に `Read` が空を返すようにしてから競合を起こす。例外は出ず、祖先は `ArchivedMetrics && !Compressed` のまま残り、外されていた指標の全コロニー行は `Number == null` かつ `Coverage == Unknown`。 |
| `Reload_RetriesRehydration` | 上の状態のプロファイルを、今度は読める退避先でセッションに読み込むと、祖先の値が戻り `ArchivedMetrics == false` になる。 |
| `ReadOnlyOrNoArchive_NeverArchives` | 退避先なし、または新しいスキーマで読み取り専用のとき、`ArchivedMetrics` の祖先は1つもできない。 |
| `OlderBinary_RoundTripsTheFlag` | `archivedMetrics` を持つ JSON を、そのプロパティを知らない想定で確かめる代わりに、`ProfileJson.Deserialize` → `Serialize` で値が保たれること、`"archivedMetrics": false` は書かれないことを確かめる。 |

- [ ] **Step 6: ゲーム側の接続**

`ProfileService.StartLoading` の `new ProfileSession(new ProfileStore(path), () => DateTime.UtcNow)` に、第3引数 `new FileCheckpointArchive(Path.Combine(path, "checkpoint-archive"))` を足す。

- [ ] **Step 7: 効果の確認**

Run: ハーネス `savepath 150 1000`（ハーネスは `ProfileMutations` を直接呼ぶので退避は働かない。比較用に、`ProfileSession` と `FileCheckpointArchive` を使って同じ保存を繰り返すモード `savepath-session <種類数> <保存回数>` をハーネスに足し、10回ごとに `Mutate(…, SaveMode.Immediate)` する）。
Expected: `savepath-session 150 1000` の最終 `profile_MB` が、T0 の `savepath 150 1000` の値（70MB前後）の10分の1以下。数値を PR 本文に貼る。

- [ ] **Step 8: Commit**

```bash
git add -A Source Tests tools/OptimizationBenchmark
git commit -m "feat(profile): tier compressed checkpoint ancestors into an append-only archive without changing any shown value"
```

PR 本文に、D11「互換性」の4項目と、実ゲームでの確認（[6 検証手順](2026-11-11-optimization-6-verification.md) §4.4）が未実施であることを書く。
