# RimWorld Statistics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RimWorld 1.7 用の MOD を作る。タイトル画面から、1.5〜1.7 の全セーブと MOD 導入後の記録をまとめた通算統計を表示する。

**Architecture:** ゲームに依存しない中核 DLL（`RimworldStatistics.Core`）と、ゲーム接続 DLL（`RimworldStatistics`）の 2 層に分ける。
- 中核 DLL: セーブの XML を XmlReader で先頭から順に読む処理、コロニーへのまとめ、記録ファイル（JSON）、統計の計算を受け持つ。ゲームを起動せずに xUnit で検証する。
- ゲーム接続 DLL: Harmony の Prefix／Postfix と GameComponent でゲームとつなぐ。検証は、ビルドと静的な参照照合と、手動確認で行う。

**Tech Stack:**
- C# / .NET 10（`net10.0`）、`System.Xml`、`System.Text.Json`、`System.IO.Compression`
- Harmony 3.0（`0Harmony.dll`、Workshop 2009463077 の `1.7/Assemblies`）
- xunit.v3 4.0.2（Microsoft.Testing.Platform 方式）

**Spec:** `docs/superpowers/specs/2026-11-06-rimworld-statistics-design.md`（以下「仕様」）。根拠は `docs/research/2026-11-06-rimworld-1.7-feasibility.md`（以下「調査」）。

## Global Constraints

- TargetFramework は `net10.0`。中核 DLL は BCL 以外の NuGet パッケージにもゲーム DLL にも依存しない。
- 配布物の前提 MOD は Harmony（`brrainz.harmony`）だけ。packageId は `pengin0503.rimworldstatistics`、supportedVersions は `1.7` だけ。
- ゲーム接続 DLL が参照するのは `Assembly-CSharp`、`UnityStubs`、`0Harmony`（`Private=false`）。参照先は MSBuild プロパティ `GameDir` と `HarmonyDll` で与える。
- セーブには一切書き込まない。開くときは `FileAccess.Read` かつ `FileShare.ReadWrite | FileShare.Delete`。
- 毎ティックの処理を作らない（`GameComponentTick` を上書きしない）。
- Harmony は Prefix と Postfix だけ使う。Transpiler は使わない。パッチの本体はすべて try/catch で囲む。
- 裏スレッドからゲーム本体の API（`Verse.*`、`RimWorld.*`、Unity）を呼ばない。
- 記録ファイルは `<GenFilePaths.SaveDataFolderPath>/RimworldStatistics/profile.json`。`schemaVersion` は 1。バックアップは `profile.json.bak1`〜`bak3`。
- 設定の既定値: `includeDevMode = false`、`autoImport = true`。
- 画面の文言はすべて Keyed 翻訳キー（接頭辞 `RWS_`）にし、日本語と英語の両方を用意する。
- リポジトリは公開。実セーブ、ローカルのパス、ユーザー名、セーブ名・コロニー名をコミットしない。
- JSON は camelCase。列挙値は camelCase の文字列（例 `"royalAscent"`、`"seededTrace"`、`"ok"`）。日時は UTC の ISO 8601。

## Review Focus

1. **日本語のファイル名・勢力名・ワールド名**（全角括弧や「」を含む）が、記録ファイルの往復で変わらないこと。→ Task 1 のテスト `Json_RoundTrip_PreservesJapaneseText` で確かめる。
2. **新しい形式の記録ファイル**（将来の版が書いた `schemaVersion` 2 以上）を読んでも上書きせず、読み取り専用で動くこと。→ Task 6 `Load_NewerSchema_IsReadOnlyAndSaveThrows`、Task 8 `Mutate_WhenReadOnly_DoesNotWriteAndRecordsError`。
3. **MOD などで `Wealth_Total` の記録やシードが欠けたセーブ**が、落ちずに「識別不可」と理由つきで扱われること。→ Task 2 `Read_MissingIdentity_IsUnidentifiableWithReasons`。
4. **取り込み中にゲームを始めて中断した場合**、読み終えた分は残り、次回は残りだけを読むこと。→ Task 9 `Run_Cancelled_KeepsDoneAndResumes`。
5. **書き込み途中や壊れたセーブ**が、エラーとして記録され、次回のスキャンで再び読まれること。→ Task 2 `Read_TruncatedFile_IsError`、Task 9 `Run_ErrorFile_IsRetriedNextRun`。

---

## File Structure

```
global.json                                   dotnet test を Microsoft.Testing.Platform 方式にする
Directory.Build.props                         共通設定。GameDir と HarmonyDll の既定値（.refs/ 以下）
RimworldStatistics.sln
.gitignore                                    bin/ obj/ .refs/ .devinputs/ 1.7/Assemblies/ artifacts/ を除外
About/About.xml
Languages/Japanese/Keyed/RimworldStatistics.xml
Languages/English/Keyed/RimworldStatistics.xml
tools/fetch_dev_inputs.py                     開発用。R2 から参照 DLL・実セーブ・監査ツールを取得
tools/package.sh                              配布用 zip を作る
docs/manual-test-checklist.md                 仕様 §11.2 の手順書
Source/RimworldStatistics.Core/
  RimworldStatistics.Core.csproj
  Model/Enums.cs                              EndingType, EndingSource, ObservationStatus
  Model/ColonyIdentity.cs
  Model/ObservedValues.cs
  Model/DisplayInfo.cs
  Model/SaveObservation.cs
  Model/LiveCampaign.cs
  Model/EndingEvent.cs
  Model/LineageEntry.cs                       セーブ内の lineageEndings の 1 要素（トークン形式）
  Model/Profile.cs                            Profile, UserDecisions, ColonyKeyEntry, ImportState
  Model/ProfileJson.cs
  Reading/SaveReader.cs
  Reading/RecordsCodec.cs
  Reading/EndingTraces.cs                     TraceInputs と Detect（セーブとゲーム中で共通）
  Matching/Colony.cs
  Matching/CampaignMatcher.cs
  Profiles/ProfileMutations.cs
  Profiles/ProfileStore.cs
  Profiles/ProfileSession.cs
  Stats/StatsCalculator.cs                    StatsOptions, ColonyStats, StatsResult
  Stats/StatFormat.cs
  Import/ImportEngine.cs
  Endings/EndingLedger.cs                     EndingSignal, EndingLedger
  Endings/CreditsContext.cs
Tests/RimworldStatistics.Core.Tests/
  RimworldStatistics.Core.Tests.csproj
  Fixtures/SaveXmlBuilder.cs                  合成セーブ XML の組み立て
  Fixtures/TempDir.cs
  ModelJsonTests.cs
  SaveReaderTests.cs
  SaveReaderHistoryTests.cs
  CampaignMatcherTests.cs
  ProfileMutationsTests.cs
  ProfileStoreTests.cs
  StatsCalculatorTests.cs
  ProfileSessionTests.cs
  ImportEngineTests.cs
  EndingLedgerTests.cs
  RealSavesRegressionTests.cs
Source/RimworldStatistics/
  RimworldStatistics.csproj                   出力先は ../../1.7/Assemblies/
  RimworldStatisticsMod.cs                    Mod、設定画面、Harmony の起動
  RimworldStatisticsSettings.cs
  PatchRunner.cs
  ProfileService.cs
  ImportRunner.cs
  LiveStateReader.cs
  GameComponent_RimworldStatistics.cs
  EndingRuntime.cs                            CreditsContext の置き場（Task 15 と 16 で共有）
  Patches/MainMenuButtonPatch.cs
  Patches/GoToMainMenuPatch.cs                タイトル画面へ戻るときに記録ファイルを書く
  Patches/EndingPatches.cs
  UI/Dialog_RimworldStatistics.cs
  UI/OverviewTab.cs
  UI/EndingsTab.cs
  UI/ColoniesTab.cs
  UI/ImportTab.cs
```

---

### Task 1: ソリューションの土台、データモデル、JSON

**Files:**
- Create: `global.json`, `Directory.Build.props`, `RimworldStatistics.sln`, `.gitignore`（既存に追記）
- Create: `Source/RimworldStatistics.Core/RimworldStatistics.Core.csproj`、`Model/*.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj`、`Fixtures/TempDir.cs`、`ModelJsonTests.cs`

**Interfaces:**
- Produces（名前空間はすべて `RimworldStatistics.Core`）:
  - `enum EndingType { Ship, RoyalAscent, AmbiguousLaunch, Archonexus, CerebrexScavenged, CerebrexDestroyed, VoidEmbraced, VoidDisrupted, Other }`
  - `enum EndingSource { Live, SeededTrace }`、`enum ObservationStatus { Ok, Unidentifiable, Error }`
  - `sealed class ColonyIdentity`
    - プロパティ: `string? SeedString`、`int PersistentRandomValue`、`string? StartingTile`（"tile,layer" に正規化済み）、`int GameStartAbsTick`、`uint? FirstWealthBits`、`List<string> StartingPawnIds`、`string? ModCampaignId`
    - `[JsonIgnore] bool HasFingerprint => SeedString != null && StartingTile != null && FirstWealthBits != null`
    - `string? FingerprintHash()`: 次の文字列を UTF-8 で SHA-256 にかけ、先頭 8 バイトを小文字 16 進にする。`HasFingerprint` が偽なら null。
      `$"{SeedString}\u001f{PersistentRandomValue}\u001f{StartingTile}\u001f{GameStartAbsTick}\u001f{FirstWealthBits:X8}"`
    - `static string NormalizeTile(string raw)`: `,` を含まなければ `",0"` を補う。
  - `sealed class ObservedValues`
    - プロパティ: `int TicksGame`、`double RealPlayTime`（秒）、`int NumRaidsEnemy`、`int NumThreatsQueued`、`int ColonistsKilled`、`int ColonistsLaunched`、`int GreatestPopulation = 3`、`double WealthMax`、`double WealthLast`、`int WealthPoints`
    - `ObservedValues Clone()`
  - `sealed class DisplayInfo`: `string? FactionName, WorldName, Scenario, Storyteller, Difficulty`、`bool Permadeath`
  - `sealed class SaveObservation`
    - プロパティ: `string FileName`、`long FileSize`、`DateTime LastWriteUtc`、`int ReaderVersion`、`bool Superseded`、`ObservationStatus Status`、`string? ErrorReason`、`string? GameVersion`、`int ModCount`、`ColonyIdentity Identity`、`ObservedValues Values`、`List<EndingType> EndingTraces`、`DisplayInfo Display`
    - `bool SameFileContent(long size, DateTime lastWriteUtc)`
  - `sealed class LiveCampaign`: `string CampaignId`、`ColonyIdentity Identity`、`ObservedValues Max`、`ObservedValues Latest`、`DisplayInfo Display`、`DateTime LastSavedUtc`
  - `sealed class EndingEvent`: `string CampaignId`、`EndingType Type`、`int Ordinal`、`EndingSource Source`、`int TicksGame`、`DateTime RealTimeUtc`、`int Departed`、`int LaunchedTotalAfter`、`bool DevMode`、`string? GameVersion`、`string? CreditsExcerpt`
  - `readonly record struct LineageEntry(EndingType Type, int Ordinal, int TicksGame)`
    - `string ToToken()`: 形は `"ship|0|123456"`（種類は camelCase）。
    - `static bool TryParse(string token, out LineageEntry entry)`
  - `sealed class UserDecisions`: `List<string> ExcludedCampaignKeys`、`List<List<string>> ManualMerges`、`List<List<string>> ManualSplits`
  - `sealed class ColonyKeyEntry`: `string Key`、`string FingerprintHash`、`List<string> StartingPawnIds`
  - `sealed class ImportState`: `DateTime? LastScanUtc`
  - `sealed class Profile`
    - `const int CurrentSchemaVersion = 1`
    - プロパティ: `int SchemaVersion = 1`、`List<SaveObservation> SaveObservations`、`List<LiveCampaign> LiveCampaigns`、`List<EndingEvent> EndingEvents`、`UserDecisions UserDecisions`、`List<ColonyKeyEntry> ColonyKeys`、`ImportState ImportState`
  - `static class ProfileJson`: `JsonSerializerOptions Options`、`string Serialize(Profile)`、`Profile Deserialize(string json)`。null のリストやオブジェクトは空のものに置き換える。
- 補足: すべてのリストは空で初期化する。プロパティは `{ get; set; }`。

- [ ] **Step 1: 開発環境を用意する**

`dotnet --list-sdks` に 10.0.x が無ければ、次で入れる（Ubuntu 24.04 の公式リポジトリ。root で実行。root でなければ先頭に `sudo` を付ける）。

```bash
apt-get update && apt-get install -y dotnet-sdk-10.0
```

`global.json`:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

`Directory.Build.props`:
- `TargetFramework=net10.0`、`Nullable=enable`、`ImplicitUsings=enable`、`TreatWarningsAsErrors=true`
- `GameDir` の既定値: `$(MSBuildThisFileDirectory).refs/game`
- `HarmonyDll` の既定値: `$(MSBuildThisFileDirectory).refs/harmony/0Harmony.dll`
- 環境変数で上書きできるように、`Condition="'$(GameDir)' == ''"` を付ける。

テスト csproj:
- `OutputType=Exe`、`IsPackable=false`
- `PackageReference xunit.v3 4.0.2` だけ
- 中核 csproj への ProjectReference

- [ ] **Step 2: 失敗するテストを書く**（`ModelJsonTests.cs`）

```csharp
[Fact] public void Json_RoundTrip_PreservesJapaneseText()
{
    var p = new Profile();
    p.SaveObservations.Add(new SaveObservation {
        FileName = "人々派契約国「バットストバ」 (パーマデス).rws.old", Status = ObservationStatus.Ok,
        Display = new DisplayInfo { FactionName = "rim共同体", WorldName = "JDK-49・メリディアナ" },
        EndingTraces = { EndingType.AmbiguousLaunch } });
    var back = ProfileJson.Deserialize(ProfileJson.Serialize(p));
    Assert.Equal("人々派契約国「バットストバ」 (パーマデス).rws.old", back.SaveObservations[0].FileName);
    Assert.Equal("JDK-49・メリディアナ", back.SaveObservations[0].Display.WorldName);
    Assert.Equal(EndingType.AmbiguousLaunch, back.SaveObservations[0].EndingTraces[0]);
}
[Fact] public void Json_UsesCamelCaseEnumStrings()
{
    var json = ProfileJson.Serialize(new Profile { EndingEvents = { new EndingEvent { Type = EndingType.RoyalAscent, Source = EndingSource.SeededTrace } } });
    Assert.Contains("\"type\": \"royalAscent\"", json);
    Assert.Contains("\"source\": \"seededTrace\"", json);
    Assert.Contains("\"schemaVersion\": 1", json);
}
[Fact] public void FingerprintHash_IsStableAndNullWhenIncomplete()
{
    var id = new ColonyIdentity { SeedString = "gracie", PersistentRandomValue = 1911680894, StartingTile = "204285,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 };
    Assert.Equal(16, id.FingerprintHash()!.Length);
    Assert.Equal(id.FingerprintHash(), new ColonyIdentity { SeedString = "gracie", PersistentRandomValue = 1911680894, StartingTile = "204285,0", GameStartAbsTick = 1570000, FirstWealthBits = 0x46AD9C00 }.FingerprintHash());
    Assert.Null(new ColonyIdentity { SeedString = "gracie", StartingTile = "1,0" }.FingerprintHash());
    Assert.Equal("221529,0", ColonyIdentity.NormalizeTile("221529"));
    Assert.Equal("204285,1", ColonyIdentity.NormalizeTile("204285,1"));
}
[Fact] public void LineageToken_RoundTrips_AndRejectsGarbage()
{
    var e = new LineageEntry(EndingType.RoyalAscent, 1, 2500000);
    Assert.Equal("royalAscent|1|2500000", e.ToToken());
    Assert.True(LineageEntry.TryParse(e.ToToken(), out var back)); Assert.Equal(e, back);
    Assert.False(LineageEntry.TryParse("nope|x", out _));
}
```

- [ ] **Step 3: 失敗することを確かめる**

Run: `dotnet test`
Expected: 型が無いことによるコンパイルエラー

- [ ] **Step 4: Model と `ProfileJson` を実装する**

`ProfileJson.Options` の設定:
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`
- `WriteIndented = true`
- `Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)`。日本語を `\uXXXX` にしないため。

- [ ] **Step 5: 成功することを確かめる**

Run: `dotnet test`
Expected: 4 件 PASS

- [ ] **Step 6: コミットする**

```bash
git add global.json Directory.Build.props RimworldStatistics.sln .gitignore Source/RimworldStatistics.Core Tests/RimworldStatistics.Core.Tests
git commit -m "feat(core): add solution scaffold, profile model and JSON codec"
```

---

### Task 2: SaveReader（基本項目・識別・既定値・エラー）

**Files:**
- Create: `Source/RimworldStatistics.Core/Reading/SaveReader.cs`、`Reading/RecordsCodec.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/Fixtures/SaveXmlBuilder.cs`、`SaveReaderTests.cs`

**Interfaces:**
- Consumes: Task 1 の Model
- Produces:
  - `static class RecordsCodec`: `float[] Decode(string base64, bool deflate)`。改行と空白は無視する。`deflate` が真なら、生の deflate として展開する（`System.IO.Compression.DeflateStream`）。
  - `sealed class SaveReader`
    - `const int Version = 1`
    - `const string ComponentClassName = "RimworldStatistics.GameComponent_RimworldStatistics"`
    - `SaveObservation Read(string path)`: `FileInfo` からサイズと更新日時（UTC）を取る。IO の例外は `Status=Error` にして返し、外へ投げない。
    - `SaveObservation Read(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc)`
  - `SaveXmlBuilder`（テスト用）: 流れるように書ける組み立て用クラス。例 `.Meta("1.6.4566 rev607", mods: 3)`、`.Info(realPlay: 600.5f, tile: "221529", pawns: "Thing_Human1,Thing_Human2")`、`.Tick(ticksGame: 120000, start: 15000)`、`.World(seed: "x", prv: 5)`、`.Stats(raids: 2)`、`.WealthTotal(params float[] values)`、`.WealthTotalDeflate(params float[] values)`、`.Minimal()`（識別できる最小のセーブ）、`.Maps(sizeBytes)`、`.Build() -> byte[]`

- [ ] **Step 1: 失敗するテストを書く**（`SaveReaderTests.cs`）

```csharp
[Fact] public void Read_BasicSections()
{
    var obs = Read(new SaveXmlBuilder().Meta("1.6.4566 rev607", mods: 3)
        .Info(realPlay: 600.5f, tile: "221529", pawns: "Thing_Human1,Thing_Human2", permadeath: true)
        .Tick(ticksGame: 120000, start: 15000).World(seed: "合弁会社", prv: 735390269, name: "エラクラブ-V")
        .Scenario("不時着").Storyteller("Cassandra", "Rough")
        .Stats(raids: 2, threats: 3, killed: 1, launched: 0, greatestPop: 5).WealthTotal(15746f, 20000f, 18000f));
    Assert.Equal(ObservationStatus.Ok, obs.Status);
    Assert.Equal("1.6.4566 rev607", obs.GameVersion); Assert.Equal(3, obs.ModCount);
    Assert.Equal(120000, obs.Values.TicksGame); Assert.Equal(600.5, obs.Values.RealPlayTime, 3);
    Assert.Equal("221529,0", obs.Identity.StartingTile); Assert.Equal(15000, obs.Identity.GameStartAbsTick);
    Assert.Equal(new[] { "Thing_Human1", "Thing_Human2" }, obs.Identity.StartingPawnIds);
    Assert.Equal("合弁会社", obs.Identity.SeedString); Assert.Equal(735390269, obs.Identity.PersistentRandomValue);
    Assert.Equal((2, 3, 1, 0, 5), (obs.Values.NumRaidsEnemy, obs.Values.NumThreatsQueued, obs.Values.ColonistsKilled, obs.Values.ColonistsLaunched, obs.Values.GreatestPopulation));
    Assert.Equal(BitConverter.SingleToUInt32Bits(15746f), obs.Identity.FirstWealthBits);
    Assert.Equal((20000.0, 18000.0, 3), (obs.Values.WealthMax, obs.Values.WealthLast, obs.Values.WealthPoints));
    Assert.Equal(("エラクラブ-V", "不時着", "Cassandra", "Rough", true), (obs.Display.WorldName, obs.Display.Scenario, obs.Display.Storyteller, obs.Display.Difficulty, obs.Display.Permadeath));
    Assert.Equal(SaveReader.Version, obs.ReaderVersion);
}
[Fact] public void Read_AbsentElements_UseGameDefaults()
// statsRecord・realPlayTimeInteracting・gameStartAbsTick・persistentRandomValue が無いセーブ
// → GreatestPopulation == 3、他の数値は 0、Permadeath == false
[Fact] public void Read_MissingIdentity_IsUnidentifiableWithReasons()
// seedString・startingTile・Wealth_Total が無い → Status == Unidentifiable、ErrorReason == "seedString,startingTile,Wealth_Total"
[Fact] public void Read_TruncatedFile_IsError()
// Build() の結果を半分で切ったもの → Status == Error、ErrorReason に "Xml" を含む
[Theory] [InlineData("records", "!!not-base64!!")] [InlineData("recordsDeflate", "AAAA")]   // 後者は deflate として壊れたデータ
public void Read_MalformedRecordsPayload_IsError(string label, string payload)
// XML は正しいが Wealth_Total の中身が壊れている → 例外を投げず Status == Error、ErrorReason が例外の型名で始まる
[Fact] public void Read_NotASavegame_IsError()
// <Defs/> → Status == Error、ErrorReason == "not a savegame"
[Fact] public void Read_SkipsMapsAndUnknownSections()
// world/maps と未知の要素（MOD のデータを想定した深い入れ子 5MB）があっても Ok。値は Read_BasicSections と同じ
[Fact] public void Read_PathOverload_SetsFileInfo()
// 一時ファイルに書いて Read(path) → FileName・FileSize・LastWriteUtc がファイルと一致
[Theory] [InlineData(false)] [InlineData(true)]
public void Records_RawAndDeflate_DecodeTheSameValues(bool deflate)
{
    float[] v = { 12560f, 13000.5f, 46709f, 39185f };
    var b = new SaveXmlBuilder().Minimal();
    var o = Read(deflate ? b.WealthTotalDeflate(v) : b.WealthTotal(v));
    Assert.Equal((4, 46709.0, 39185.0), (o.Values.WealthPoints, o.Values.WealthMax, o.Values.WealthLast));
    Assert.Equal(BitConverter.SingleToUInt32Bits(12560f), o.Identity.FirstWealthBits);
}
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.SaveReaderTests"`
Expected: コンパイルエラー

- [ ] **Step 3: `SaveReader` を実装する**

- `XmlReaderSettings`: `DtdProcessing = Prohibit`、`IgnoreWhitespace = true`、`IgnoreComments = true`、`CheckCharacters = false`。
- `savegame` 直下は `meta`（`XElement.Load(ReadSubtree())` で読む）と `game` だけを見る。
- `game` 直下の扱い:
  - 小さい要素は XElement で読む: `info`、`tickManager`、`scenario`（`name` だけ使う）、`storyteller`、`storyWatcher`
  - 中に入って読み進める: `world`（`info` は XElement で読み、`factionManager` は Task 3）
  - それ以外は Task 3 で扱う。今は `Skip()` する。
- 読む項目と既定値は、調査 §3.1 の表のとおり。`info/startingAndOptionalPawns/li` の中身が `null` の要素は除く。
- 識別不可（Unidentifiable）の理由は、欠けたものを `seedString`、`startingTile`、`Wealth_Total` の順で `,` でつなぐ。`Identity.ModCampaignId` が null でないセーブは、識別不可にしない（Task 3）。
- Error の扱い: 読み取り中の例外は、型を問わずすべて Error にして返す（`XmlException`、`IOException`、`EndOfStreamException`、`UnauthorizedAccessException`、Base64 の `FormatException`、deflate の `InvalidDataException` を含む）。理由は `$"{例外の型名}: {メッセージ}"` の先頭 200 文字。`Read` は外へ例外を投げない。
- `history/autoRecorderGroups/li/recorders/li` は、子要素の `def` が `Wealth_Total` のものだけ、`records` または `recordsDeflate` を `RecordsCodec` で読む。それ以外の recorder は Skip する。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.SaveReaderTests"`
Expected: 11 件 PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Reading Tests/RimworldStatistics.Core.Tests
git commit -m "feat(core): read save identity and counters with game defaults"
```

---

### Task 3: SaveReader（クエスト・勢力・コンポーネント・痕跡）

**Files:**
- Create: `Source/RimworldStatistics.Core/Reading/EndingTraces.cs`
- Modify: `Source/RimworldStatistics.Core/Reading/SaveReader.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/SaveReaderHistoryTests.cs`

**Interfaces:**
- Consumes: Task 2 の `SaveReader`、`SaveXmlBuilder`
- Produces:
  - `sealed class TraceInputs`: `int ColonistsLaunched`、`bool RoyalAscentSucceeded`、`bool MechhiveSucceeded`、`bool MechanoidsDeactivated`、`string? AnomalyLevelDef`、`IReadOnlyCollection<EndingType> LineageTypes`（セーブ内の `lineageEndings` の種類。MOD 導入前のセーブなら空）
  - `static class EndingTraces`: `List<EndingType> Detect(TraceInputs i)`。結果は列挙型の順に並べる。

```
ColonistsLaunched ≥ 1 かつ !RoyalAscentSucceeded → Ship
ColonistsLaunched ≥ 1 かつ  RoyalAscentSucceeded → AmbiguousLaunch
MechhiveSucceeded かつ  MechanoidsDeactivated  → CerebrexDestroyed
MechhiveSucceeded かつ !MechanoidsDeactivated  → CerebrexScavenged
AnomalyLevelDef == "Embraced"  → VoidEmbraced
AnomalyLevelDef == "Disrupted" → VoidDisrupted

一覧で説明できる痕跡は除く（仕様 §4.3）:
LineageTypes に Ship・RoyalAscent・AmbiguousLaunch のどれかがある → Ship と AmbiguousLaunch を除く
LineageTypes に CerebrexScavenged・CerebrexDestroyed のどれかがある → Cerebrex の 2 種を除く
LineageTypes に VoidEmbraced・VoidDisrupted のどれかがある → Void の 2 種を除く
```

- [ ] **Step 1: 失敗するテストを書く**（`SaveReaderHistoryTests.cs`。`SaveXmlBuilder` に `.Quest(root, ended, outcome)`、`.Faction(def, name, deactivated)`、`.Component(cls, child, value)` を足す）

```csharp
[Fact] public void Read_PlayerFactionName_And_MechanoidFlag()
// Faction("PlayerColony","rim同盟",false) と Faction("Mechanoid",null,true)
// → Display.FactionName == "rim同盟"、Detect への入力で MechanoidsDeactivated == true
[Fact] public void Read_ModCampaignId_FromComponent()
// Component(SaveReader.ComponentClassName, "campaignId", "6f1c…") → Identity.ModCampaignId == "6f1c…"
// seedString が無くても Status == Ok（ID で識別できる）
[Theory]
[InlineData(1, false, false, false, null, new[] { EndingType.Ship })]
[InlineData(2, true,  false, false, null, new[] { EndingType.AmbiguousLaunch })]
[InlineData(0, true,  false, false, null, new EndingType[0])]           // クエスト成功だけではエンディングにしない
[InlineData(0, false, true,  true,  null, new[] { EndingType.CerebrexDestroyed })]
[InlineData(0, false, true,  false, null, new[] { EndingType.CerebrexScavenged })]
[InlineData(0, false, false, false, "Embraced",  new[] { EndingType.VoidEmbraced })]
[InlineData(0, false, false, false, "Disrupted", new[] { EndingType.VoidDisrupted })]
[InlineData(0, false, false, false, "Waking",    new EndingType[0])]
public void Detect_FollowsSpecTable(int launched, bool royal, bool mechhive, bool deactivated, string? level, EndingType[] expected)
    => Assert.Equal(expected, EndingTraces.Detect(new TraceInputs { ColonistsLaunched = launched, RoyalAscentSucceeded = royal, MechhiveSucceeded = mechhive, MechanoidsDeactivated = deactivated, AnomalyLevelDef = level, LineageTypes = Array.Empty<EndingType>() }));
[Theory]
[InlineData(3, true,  false, false, null, new[] { EndingType.RoyalAscent })]       // プレイ中に王族の昇天を記録した後のセーブ
[InlineData(2, false, false, false, null, new[] { EndingType.Ship })]              // 開発モードの打ち上げを記録した後のセーブ
[InlineData(2, false, false, false, null, new[] { EndingType.AmbiguousLaunch })]   // 導入前の痕跡を引き継いだ後のセーブ
[InlineData(0, false, true,  true,  null, new[] { EndingType.CerebrexDestroyed })]
[InlineData(0, false, false, false, "Embraced", new[] { EndingType.VoidEmbraced })]
public void Detect_SuppressesTracesExplainedByLineage(int launched, bool royal, bool mechhive, bool deactivated, string? level, EndingType[] lineage)
    => Assert.Empty(EndingTraces.Detect(new TraceInputs { ColonistsLaunched = launched, RoyalAscentSucceeded = royal, MechhiveSucceeded = mechhive, MechanoidsDeactivated = deactivated, AnomalyLevelDef = level, LineageTypes = lineage }));
[Fact] public void Detect_UnrelatedLineage_KeepsTrace()
// LineageTypes == [CerebrexScavenged] で launched 1 → [Ship]（別系統のエンディングでは除かない）
[Fact] public void Read_QuestStates_FeedTraces()
// Quest("EndGame_RoyalAscent", true, "Success") と Stats(launched: 3) → EndingTraces == [AmbiguousLaunch]
// Quest("Gravcore_Mechhive", true, "Success") と Faction("Mechanoid", null, false) → EndingTraces に CerebrexScavenged
// Component("RimWorld.GameComponent_Anomaly", "levelDef", "Embraced") → EndingTraces に VoidEmbraced
[Fact] public void Read_OwnComponentLineage_SuppressesExplainedTraces()
// Component(SaveReader.ComponentClassName) に campaignId と lineageEndings（li に "royalAscent|0|2500000"）、
// Quest("EndGame_RoyalAscent", true, "Success")、Stats(launched: 3) → EndingTraces は空
// lineageEndings の li に解釈できない文字列が混ざっても例外にせず、その要素だけ無視する
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.SaveReaderHistoryTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

- `world/factionManager/allFactions/li`: 1 件ずつ `ReadSubtree` で読み、直下の `def`、`name`、`deactivated` だけ拾う。それより深い要素は Skip する。
  - `def` が `Player` で始まる最初の勢力の `name` を `FactionName` にする。
  - `def == "Mechanoid"` の `deactivated` を使う。
- `questManager/quests/li`: 直下の `root` と `endOutcome` だけ拾い、`parts` などは Skip する。
  - `root == "EndGame_RoyalAscent"` かつ `endOutcome == "Success"` → RoyalAscentSucceeded
  - `root == "Gravcore_Mechhive"` かつ `endOutcome == "Success"` → MechhiveSucceeded
- `components/li`:
  - `Class == SaveReader.ComponentClassName` なら `campaignId` と `lineageEndings/li`（`LineageEntry.TryParse` で読み、種類を `LineageTypes` に入れる）を読む。
  - `Class == "RimWorld.GameComponent_Anomaly"` なら `levelDef` を読む。
  - それ以外は Skip する。
- 最後に `EndingTraces.Detect` の結果を `EndingTraces` に入れる。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test`
Expected: すべて PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Reading Tests/RimworldStatistics.Core.Tests
git commit -m "feat(core): read quests, factions, components and ending traces"
```

---

### Task 4: CampaignMatcher

**Files:**
- Create: `Source/RimworldStatistics.Core/Matching/Colony.cs`、`Matching/CampaignMatcher.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/CampaignMatcherTests.cs`

**Interfaces:**
- Consumes: Task 1 の Model
- Produces:
  - `sealed class Colony`
    - `string Key`
    - `List<string> AliasKeys`（台帳の複数項目に当たったときの、使わなかったキー。仕様 §6.2）
    - `List<SaveObservation> Observations`、`List<LiveCampaign> LiveCampaigns`、`List<EndingEvent> Endings`
    - `HashSet<EndingType> Traces`（観測の `EndingTraces` の和集合）
    - `bool Excluded`
    - `string? FingerprintHash`
    - `HashSet<string> StartingPawnUnion`
  - `static class CampaignMatcher`
    - `bool SameColony(ColonyIdentity a, ColonyIdentity b)`: 両方 `HasFingerprint` で、`FingerprintHash` が等しく、`StartingPawnIds` の集合が包含関係にある。
    - `IReadOnlyList<Colony> BuildColonies(Profile profile)`: `profile.ColonyKeys` に追記することがある。
    - `string? FindCampaignIdFor(Profile profile, ColonyIdentity identity)`: `SameColony` が真になる `LiveCampaign` があれば、その `CampaignId` を返す。無ければ null。

- [ ] **Step 1: 失敗するテストを書く**（観測は、テスト内の補助関数 `Obs(file, seed, tile, start, wealth0, pawns, id?)` で作る。`Hash` は、同じ値で作った `ColonyIdentity` の `FingerprintHash()`）

```csharp
[Fact] public void ExampleA_PermadeathRename_Merges()
// 同じ指紋、pawns {891,951,1047} と {891}、ファイル名もパーマデス名も別 → 1 コロニー
[Fact] public void ExampleB_SameWorldDifferentStart_Separates()
// seed・prv・tile・start は同じで、wealth0 が 12560 と 13212 → 2 コロニー、キーは別
[Fact] public void ExampleC_EarlyAndLateSavesWithDifferentNames_Merge()
[Fact] public void ExampleD_DeadStartingPawns_SubsetMerges()
// pawns {359,794,406} と {406} → 1 コロニー
[Fact] public void CampaignId_TakesPriority_EvenWithoutFingerprint()
// LiveCampaign "g1"（指紋あり）と、ModCampaignId == "g1" の観測（seed 無し）→ 1 コロニー
[Fact] public void ErrorAndUnidentifiable_AreNotGrouped()
[Fact] public void SameFingerprint_NonSubsetPawns_GetDistinctPersistedKeys()
{
    var p = Profile(Obs("a.rws", pawns: "Thing_Human123,Thing_Human124"), Obs("b.rws", pawns: "Thing_Human123,Thing_Human125")); // 同じ指紋
    var keys = CampaignMatcher.BuildColonies(p).Select(c => c.Key).OrderBy(k => k).ToArray();
    Assert.Equal(2, keys.Distinct().Count());
    Assert.Equal($"fp:{Hash}", keys[0]); Assert.Equal($"fp:{Hash}#2", keys[1]);
    Assert.Equal(2, p.ColonyKeys.Count);
    p.SaveObservations.Add(Obs("old.rws", pawns: "Thing_Human123,Thing_Human124,Thing_Human126")); // 後から見つかった古いセーブ
    Assert.Equal(keys, CampaignMatcher.BuildColonies(p).Select(c => c.Key).OrderBy(k => k).ToArray()); // キーは変わらない
}
[Fact] public void AncestorFoundLater_MergesBranches_AndKeepsDecisionViaAlias()
{
    var p = Profile(Obs("b1.rws", pawns: "Thing_Human1,Thing_Human2"), Obs("b2.rws", pawns: "Thing_Human1,Thing_Human3")); // 同じ指紋の 2 分岐
    var first = CampaignMatcher.BuildColonies(p);
    Assert.Equal(2, first.Count);
    p.UserDecisions.ExcludedCampaignKeys.Add($"fp:{Hash}#2");
    p.SaveObservations.Add(Obs("ancestor.rws", pawns: "Thing_Human1,Thing_Human2,Thing_Human3"));
    var c = Assert.Single(CampaignMatcher.BuildColonies(p));
    Assert.Equal($"fp:{Hash}", c.Key); Assert.Contains($"fp:{Hash}#2", c.AliasKeys);
    Assert.True(c.Excluded);   // 別名に付いた除外も当てる
}
[Fact] public void ResultDoesNotDependOnObservationOrder()
// 同じ観測の集合を 5 通りの順に並べ替えた Profile → コロニー数・各キー・各コロニーの観測ファイル名の集合がすべて同じ
[Fact] public void ManualMerge_CombinesListedColonies()
[Fact] public void ExcludedKey_MarksColony()
[Fact] public void Endings_AttachToColonyOfTheirCampaign()
[Fact] public void FindCampaignIdFor_ReturnsMatchingLiveCampaign()
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.CampaignMatcherTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

アルゴリズムは次のとおり。順序を決めておかないとキーが安定しないため、ここで固定する。

1. **単位を作る。**
   - 各 `LiveCampaign` が 1 単位。
   - 各 `SaveObservation` のうち、`Status != Error` で、かつ `HasFingerprint` か `ModCampaignId != null` のものが 1 単位。
2. **ID でまとめる。** `ModCampaignId`（観測）と `CampaignId`（ライブ）が同じ単位を 1 グループにする。
3. **並べる。** グループを (pawn 和集合の要素数の降順, 最古の `LastWriteUtc`／`LastSavedUtc` の昇順, 最小ファイル名の序数比較) で並べる。
4. **指紋でまとめる。** 並べた順に、既存コロニーのうち `FingerprintHash` が等しく、pawn 和集合が包含関係にある最初のものへ合流させる。無ければ新しいコロニーにする。指紋の無いグループは単独のコロニーにする。
5. **手動統合を当てる。** `ManualMerges` の各リストに含まれるキーのコロニーを 1 つにまとめる。キーはリストの先頭。`ManualSplits` は v1 では読むだけで、当てない（仕様 §13 で操作画面が範囲外のため）。
6. **キーを付ける。**
   - 指紋の無いコロニー: `id:<CampaignId>`
   - 指紋があり、同じハッシュのコロニーが 1 つだけで、`ColonyKeys` にそのハッシュの項目も無い: `fp:<hash>`
   - それ以外（重なりがある）: 並べた順に、`ColonyKeys` の同じハッシュの項目のうち pawn が包含関係にあるものをすべて探す。
     - 1 つ以上見つかれば、番号の最も小さいもの（番号なしが最小）を `Key` にし、残りを `AliasKeys` に入れる。当たった項目すべての pawn 和集合を更新する。
     - 見つからなければ、まだ誰も使っていなければ `fp:<hash>`、使われていれば `fp:<hash>#n`（既存の最大 n + 1。最小は 2）を割り当て、`ColonyKeys` に追記する。
7. **エンディングを付ける。** `EndingEvents` は、`CampaignId` を持つコロニーに付ける。該当が無ければ `id:<CampaignId>` のコロニーを作る。
8. **除外を付ける。** `Key` か `AliasKeys` のどれかが `ExcludedCampaignKeys` にあれば `Excluded = true`。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.CampaignMatcherTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Matching Tests/RimworldStatistics.Core.Tests/CampaignMatcherTests.cs
git commit -m "feat(core): group observations into colonies with stable keys"
```

---

### Task 5: ProfileMutations

**Files:**
- Create: `Source/RimworldStatistics.Core/Profiles/ProfileMutations.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/ProfileMutationsTests.cs`

**Interfaces:**
- Consumes: Task 1 の Model、Task 4 の `CampaignMatcher.SameColony`
- Produces（`static class ProfileMutations`）:
  - `bool NeedsRead(Profile p, string fileName, long size, DateTime lastWriteUtc, int readerVersion)`
  - `void AddSaveObservation(Profile p, SaveObservation obs)`
  - `bool Dominates(SaveObservation newer, SaveObservation older)`
  - `void RegisterLiveCampaign(Profile p, string campaignId, ColonyIdentity identity, DisplayInfo display, DateTime utcNow)`
  - `void ApplyLiveObservation(Profile p, string campaignId, ColonyIdentity identity, ObservedValues values, DisplayInfo display, DateTime savedUtc)`
  - `bool UpsertEnding(Profile p, EndingEvent ev)`: 重複を除くキーは `(CampaignId, Type, Ordinal, DevMode)`。追加したら true。同じキーがあれば、`Departed` と `LaunchedTotalAfter` を大きい方に更新して false。
  - `void SetExcluded(Profile p, IReadOnlyCollection<string> colonyKeys, bool excluded)`: `colonyKeys` は `Colony.Key` と `AliasKeys`。除外するときは先頭のキーだけを足し、外すときは渡されたキーをすべて取り除く。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void NeedsRead_FalseOnlyForSameContentOkObservationWithCurrentReader()
// 同じ名前・サイズ・時刻で Ok かつ現行の版 → false。
// 時刻が違う、ReaderVersion が古い、Status == Error、Superseded の観測しか無い → いずれも true
[Fact] public void Overwrite_ByDifferentColony_KeepsOldAsSuperseded()
{
    var p = new Profile();
    ProfileMutations.AddSaveObservation(p, Obs("Autosave-1.rws", t0, colony: "A", ticks: 500000));
    ProfileMutations.AddSaveObservation(p, Obs("Autosave-1.rws", t1, colony: "B", ticks: 10000));
    Assert.Equal(2, p.SaveObservations.Count);
    Assert.True(p.SaveObservations.Single(o => o.Values.TicksGame == 500000).Superseded);
}
[Fact] public void Overwrite_BySameColonyDominating_DropsOld()
// 同じコロニーで全項目が以上（ticks・実プレイ時間・各カウンタ・wealthMax・痕跡の上位集合）→ 1 件だけ残る
[Fact] public void Overwrite_DominatingButFewerPawns_KeepsOldAsEvidence()
// 同じファイルの {A,B,C} を、数値はすべて上回る {A,B} で上書き → 2 件とも残る（古い方は Superseded）。
// 別ファイルの {A,C} と合わせて BuildColonies → 1 コロニーのまま
[Fact] public void Overwrite_BySameColonyNotDominating_KeepsBoth()
// 同じコロニーで ticks が減った（古いセーブに戻して上書き）→ 2 件、古い方は Superseded
[Fact] public void SameContent_NewerReaderVersion_Replaces()
[Fact] public void ErrorObservation_IsReplacedByAnyNewRead()
[Fact] public void ApplyLive_TracksMaxAndWealthLastAtMaxTicks()
// ticks 100（wealthLast 10）→ 300（wealthLast 30）→ 200（wealthLast 99）の順に適用
// → Max.TicksGame == 300、Max.WealthLast == 30、Latest.TicksGame == 200
[Fact] public void ApplyLive_FillsMissingFingerprintFields_UnionsPawns()
[Fact] public void UpsertEnding_SameKeyAndProvenance_KeepsMax()
{
    var p = new Profile();
    Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 2, total: 2)));
    Assert.False(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 3, total: 5)));
    var e = Assert.Single(p.EndingEvents);
    Assert.Equal(3, e.Departed); Assert.Equal(5, e.LaunchedTotalAfter);
}
[Fact] public void UpsertEnding_DevAndNormal_AreKeptSeparately()
{
    var p = new Profile();
    Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: true, departed: 100, total: 100)));
    Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 1, total: 1)));
    Assert.Equal(2, p.EndingEvents.Count);
    Assert.Equal(1, p.EndingEvents.Single(e => !e.DevMode).Departed);   // 開発モードの 100 が混ざらない
}
[Fact] public void SetExcluded_AddsPrimaryOnce_RemovesAllAliases()
// SetExcluded(p, ["fp:a", "fp:a#2"], true) を 2 回 → ExcludedCampaignKeys == ["fp:a"]。
// 先に "fp:a#2" が入っている状態で SetExcluded(p, ["fp:a", "fp:a#2"], false) → 空
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileMutationsTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

規則は仕様 §6.2 と §7.1 のとおり。
- **`AddSaveObservation`:** 同じ `FileName` で `Superseded == false` の観測を探す。
  - 中身が同じ（名前・サイズ・時刻）なら、新しい版のときだけ置き換える。
  - Error の観測は無条件に置き換える。
  - 中身が違えば、古い観測を `Superseded = true` にして新しい観測を足す。そのうえで `SameColony(古い, 新しい) && Dominates(新しい, 古い)` なら、古い観測を消す。
- **`Dominates`:** 次をすべて満たすときに真。
  - `older.Status == Ok`
  - `ticksGame`、`realPlayTime`、5 つのカウンタ、`wealthMax` がすべて以上
  - 痕跡が上位集合
  - `newer.Identity.StartingPawnIds` が `older.Identity.StartingPawnIds` をすべて含む（分岐をつなぐ証拠を消さないため。仕様 §7.1）

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileMutationsTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Profiles/ProfileMutations.cs Tests/RimworldStatistics.Core.Tests/ProfileMutationsTests.cs
git commit -m "feat(core): append-only observations, live maxima and ending upsert rules"
```

---

### Task 6: ProfileStore

**Files:**
- Create: `Source/RimworldStatistics.Core/Profiles/ProfileStore.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/ProfileStoreTests.cs`

**Interfaces:**
- Consumes: Task 1 の `ProfileJson`
- Produces:
  - `enum ProfileLoadSource { Primary, Backup1, Backup2, Backup3, Empty, NewerSchema }`
  - `sealed class ProfileLoadResult`: `Profile Profile`、`ProfileLoadSource Source`、`string? CorruptFileRenamedTo`、`bool ReadOnly`
  - `sealed class ProfileStore`
    - `ProfileStore(string directory)`
    - `string ProfilePath`
    - `ProfileLoadResult Load(DateTime utcNow)`
    - `void Save(Profile profile)`: 読み取り専用で読んだ後なら `InvalidOperationException` を投げる。
    - `void SaveSerialized(string json)`: `ProfileSession` が、鍵の外で書くために使う。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Save_ThenLoad_RoundTrips()
[Fact] public void Save_RotatesThreeBackups()
// 4 回保存すると、bak1 は 3 回目、bak2 は 2 回目、bak3 は 1 回目の内容。bak4 は存在しない
[Fact] public void Load_CorruptPrimary_FallsBackToBackup1()
[Fact] public void Load_AllCorrupt_StartsEmptyAndRenamesPrimary()
// Source == Empty、CorruptFileRenamedTo == "profile.json.corrupt-20261106T080000Z"（utcNow に固定時刻を渡す）
[Fact] public void Load_NoFile_IsEmptyPrimaryless()   // Source == Empty、CorruptFileRenamedTo == null
[Fact] public void Load_NewerSchema_IsReadOnlyAndSaveThrows()
// schemaVersion 2 の JSON → ReadOnly == true、Source == NewerSchema、Save で例外、ファイルの中身は変わらない
[Fact] public void Save_LeavesNoTmpFile()
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileStoreTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

**保存の手順:**
1. `profile.json.tmp` に書き、`FileStream.Flush(true)` する。
2. `profile.json` があれば、`bak3` を消して `bak2→bak3`、`bak1→bak2` と移す。
3. `File.Replace(tmp, profile.json, bak1)` で置き換える。`profile.json` が無ければ `File.Move(tmp, profile.json)`。

**読み込み:** `Primary → bak1 → bak2 → bak3` の順に試す。JSON として解析できないものは飛ばす。
**破損時の名前:** `profile.json.corrupt-yyyyMMdd'T'HHmmss'Z'`。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileStoreTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Profiles/ProfileStore.cs Tests/RimworldStatistics.Core.Tests/ProfileStoreTests.cs
git commit -m "feat(core): atomic profile store with backups and corruption recovery"
```

---

### Task 7: StatsCalculator と StatFormat

**Files:**
- Create: `Source/RimworldStatistics.Core/Stats/StatsCalculator.cs`、`Stats/StatFormat.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/StatsCalculatorTests.cs`

**Interfaces:**
- Consumes: Task 4 の `Colony`
- Produces:
  - `sealed class StatsOptions`: `bool IncludeDevMode`
  - `sealed class ColonyStats`
    - 識別と表示: `string Key`、`string? FactionName`、`string? WorldName`、`string? Scenario`、`string? Storyteller`
    - 数値: `int MaxTicks`、`double MaxRealPlay`、`int Raids`、`int Threats`、`int Killed`、`int Departed`、`int MaxPopulation`、`double WealthMax`、`double WealthLast`
    - エンディング: `IReadOnlyDictionary<EndingType,int> EndingCounts`、`bool HasDevOnlyEndings`
    - その他: `IReadOnlyList<string> GameVersions`、`int SaveCount`、`DateTime? LastPlayedUtc`、`bool Excluded`
  - `sealed class StatsResult`
    - `int ColonyCount`、`int ExcludedColonyCount`、`bool HasData => ColonyCount > 0`
    - `long TotalTicks`、`double TotalRealPlay`
    - `int TotalEndings`、`IReadOnlyDictionary<EndingType,int> EndingsByType`、`int TotalDeparted`
    - `double PeakWealthSum`、`double LastWealthSum`、`double SingleMaxWealth`
    - `int Raids`、`int Threats`、`int Killed`、`long LongestTicks`、`int MaxPopulation`
    - `IReadOnlyList<ColonyStats> Colonies`（除外分も含む）
  - `static class StatsCalculator`: `StatsResult Calculate(IReadOnlyList<Colony> colonies, StatsOptions options)`、`ColonyStats CalculateColony(Colony colony, StatsOptions options)`
  - `static class StatFormat`
    - `(long TotalDays, long Years, long Days) SplitDays(long ticks)`: 1 日 = 60000 ティック、1 年 = 60 日、端数は切り捨て。
    - `string Group(double value)`: 四捨五入して整数にし、`InvariantCulture` の `N0` で書く。
    - `double Hours(double seconds)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Colony_TakesMaximaAcrossSavesAndLive()
[Fact] public void Colony_WealthLast_FromHighestTicks_TieBreaksByNewestTime()
[Fact] public void Endings_CountDistinctOrdinals_AndTraceCountsOnce()
// Ship の ordinal 0（ID g1）・ordinal 0（ID g2、同じコロニー）・ordinal 1 と、痕跡 Ship → Ship == 2
// 痕跡 AmbiguousLaunch だけ → AmbiguousLaunch == 1
[Fact] public void DevOnlyEndings_HiddenByDefault_ShownWhenEnabled()
// devMode == true だけの組 → IncludeDevMode == false なら 0、true なら 1。
// 同じ (type, ordinal) に devMode == false の記録が 1 件でもあれば、どちらの設定でも 1
[Fact] public void Departed_UsesLaunchedTotalAfter()
// 観測の colonistsLaunched 5、Archonexus の記録 departed 3・launchedTotalAfter 8 → Departed == 8
[Fact] public void Departed_DevOff_SubtractsDevDepartures()
// 観測 7、dev の記録 departed 2、通常の記録 departed 1（別の組）→ IncludeDevMode == false なら max(7−2, 1) == 5、true なら max(7, 3) == 7
[Fact] public void Departed_DevThenNormalRedo_UsesNormalValue()
// 同じ (Ship, 0) に dev の記録（departed 100、launchedTotalAfter 100）と通常の記録（departed 1、launchedTotalAfter 1）
// → IncludeDevMode == false なら EndingCounts[Ship] == 1、Departed == 1。true なら Departed == 100
[Fact] public void Totals_SkipExcludedColonies()
[Fact] public void NoColonies_HasDataFalse()
[Fact] public void StatFormat_SplitDaysAndGroup()
{
    Assert.Equal((2262L, 37L, 42L), StatFormat.SplitDays(135723149));
    Assert.Equal("4,364,583", StatFormat.Group(4364583.0615));
    Assert.Equal(263.92, StatFormat.Hours(950114.07), 2);
}
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.StatsCalculatorTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

規則は仕様 §4.1〜§4.4 のとおり。
- 「最大 `colonistsLaunched`」は、次の 3 つの最大を取る。
  - 観測の `ColonistsLaunched`
  - `LiveCampaign.Max.ColonistsLaunched`
  - 全記録の `LaunchedTotalAfter`
- 記録の組（`(Type, Ordinal)` ごと）の `departed` は、設定がオフなら組の中の通常の記録の最大値、オンなら組の中の全記録の最大値を使う。
- 仕様 §4.4 の D は、組ごとに開発モードの記録の `departed` の最大値を取り、それを足したもの。通常の記録もある組も含む。
- `Superseded` の観測も計算に含める。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.StatsCalculatorTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Stats Tests/RimworldStatistics.Core.Tests/StatsCalculatorTests.cs
git commit -m "feat(core): compute cross-colony statistics per spec section 4"
```

---

### Task 8: ProfileSession

**Files:**
- Create: `Source/RimworldStatistics.Core/Profiles/ProfileSession.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/ProfileSessionTests.cs`

**Interfaces:**
- Consumes: Task 4 `CampaignMatcher`、Task 6 `ProfileStore`、Task 7 `StatsCalculator`
- Produces:
  - `enum SaveMode { None, Background, Immediate }`
  - `sealed class StatsView`: `IReadOnlyList<Colony> Colonies`、`StatsResult Stats`
  - `sealed class ProfileSession`
    - `ProfileSession(ProfileStore store, Func<DateTime> utcNow)`: コンストラクタの中で `Load` する。
    - `ProfileLoadResult LoadResult`
    - `void Mutate(Action<Profile> change, SaveMode mode)`: 鍵の中で変更し、ビューの写しを無効にする。
    - `T Read<T>(Func<Profile, T> read)`: 鍵の中で読む。
    - `StatsView GetView(StatsOptions options)`: 写しがあればそれを返す。`BuildColonies` が `ColonyKeys` を増やしたら、`Background` で保存する。
    - `void Flush()`
    - `Exception? LastSaveError`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Mutate_Immediate_WritesBeforeReturn()
[Fact] public void Mutate_Background_CoalescesAndFlushWrites()
// 8 スレッドで 100 回ずつ AddSaveObservation（Background）→ Flush 後に読み直すと 800 件
[Fact] public void GetView_IsCachedUntilMutation()
// 同じ options で 2 回 → 同じインスタンス。Mutate の後 → 別のインスタンス。options が違えば別
[Fact] public void Mutate_WhenReadOnly_DoesNotWriteAndRecordsError()
// schemaVersion 2 のファイル → Mutate(Immediate) してもファイルの中身は同じ。LastSaveError は null でない
[Fact] public void GetView_NewCollisionKeys_ArePersisted()
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileSessionTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

- 1 つの `lock` で守る。
- `Background` は、`Task.Run` で動く書き込みを一度に 1 つまでにする。書き込み中にまた要求が来たら、終わった後にもう一度書く。
- JSON への変換は鍵の中で行い、ファイルへの書き込みは鍵の外で `SaveSerialized` を使う。
- 例外は `LastSaveError` に入れ、外へ投げない。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ProfileSessionTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Profiles/ProfileSession.cs Tests/RimworldStatistics.Core.Tests/ProfileSessionTests.cs
git commit -m "feat(core): thread-safe profile session with cached statistics view"
```

---

### Task 9: ImportEngine

**Files:**
- Create: `Source/RimworldStatistics.Core/Import/ImportEngine.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/ImportEngineTests.cs`

**Interfaces:**
- Consumes: Task 2/3 `SaveReader`、Task 5 `ProfileMutations`、Task 8 `ProfileSession`
- Produces:
  - `sealed record ImportProgress(int Total, int Done, int Failed, string? CurrentFile)`
  - `sealed class ImportResult`: `int Read`、`int Skipped`、`int Failed`、`bool Cancelled`、`IReadOnlyList<string> PresentFiles`
  - `sealed class ImportEngine`
    - `ImportEngine(SaveReader reader)`
    - `static bool IsSaveFile(string fileName)`: 大文字小文字を区別せず、`.rws` または `.rws.old` で終わるか。
    - `ImportResult Run(string savesDir, ProfileSession session, IProgress<ImportProgress>? progress, CancellationToken ct)`

- [ ] **Step 1: 失敗するテストを書く**（`TempDir` に `SaveXmlBuilder` で作ったファイルを置く）

```csharp
[Fact] public void Run_ReadsRwsAndOld_IgnoresOthers()
// a.rws, b.rws.old, c.rsc, d.rws.bak → Read == 2、PresentFiles == ["a.rws", "b.rws.old"]
[Fact] public void Run_SecondRun_SkipsUnchanged()
[Fact] public void Run_ChangedFile_AppendsObservation()
[Fact] public void Run_ErrorFile_IsRetriedNextRun()
// 壊れたファイル → 1 回目は Failed == 1。直して時刻を変えずに 2 回目 → Read == 1、Status == Ok
[Fact] public void Run_Cancelled_KeepsDoneAndResumes()
// 10 ファイル。3 件読んだところで progress のコールバックから Cancel → Cancelled == true、LastScanUtc は更新しない。
// 次の Run → Read == 7、Skipped == 3
[Fact] public void Run_MalformedPayload_FailsThatFileOnly()
// 正常なファイル 2 件の間に、Wealth_Total の Base64 が壊れたファイル 1 件 → Read == 3、Failed == 1、正常な 2 件は Ok
[Fact] public void Run_MissingDirectory_ReturnsEmpty()
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ImportEngineTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

- 対象は、`Directory.EnumerateFiles(savesDir)` のうち `IsSaveFile` が真のもの。ファイル名の序数順に並べる。
- 各ファイル:
  1. `NeedsRead` を `session.Read` で確かめる。
  2. 読む必要があれば `reader.Read(path)` を呼ぶ。
  3. 結果を `session.Mutate(..., SaveMode.None)` で記録に足す。
- 保存: 5 件ごとと最後に `Mutate(_ => { }, SaveMode.Background)` を呼ぶ。
- 中止の確認は、ファイルとファイルの間で行う。
- 念のため、ファイル 1 件ごとの処理を try/catch で囲む。`SaveReader` は例外を投げない約束だが、万一投げても、そのファイルを Error の観測として記録し、次のファイルへ進む。
- 中止されずに終わったときだけ `ImportState.LastScanUtc` を更新する。

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.ImportEngineTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Import Tests/RimworldStatistics.Core.Tests/ImportEngineTests.cs
git commit -m "feat(core): incremental, cancellable save import"
```

---

### Task 10: EndingLedger と CreditsContext

**Files:**
- Create: `Source/RimworldStatistics.Core/Endings/EndingLedger.cs`、`Endings/CreditsContext.cs`
- Create: `Tests/RimworldStatistics.Core.Tests/EndingLedgerTests.cs`

**Interfaces:**
- Consumes: Task 1 の Model（`LineageEntry` を含む）
- Produces:
  - `sealed record EndingSignal(EndingType Type, int Departed, string? CreditsExcerpt)`
  - `static class EndingLedger`
    - `EndingEvent Record(string campaignId, List<LineageEntry> lineage, EndingSignal signal, EndingSource source, int ticksGame, int launchedTotalAfter, bool devMode, string? gameVersion, DateTime realTimeUtc)`: ordinal は lineage 内の同じ種類の数。lineage に追記する。
    - `List<EndingEvent> SeedTraces(string campaignId, List<LineageEntry> lineage, IEnumerable<EndingType> traces, int ticksGame, int launchedTotal, string? gameVersion, DateTime realTimeUtc)`: lineage に無い種類だけを、ordinal 0、`SeededTrace`、departed 0 で記録する。
    - `string? ExcerptOf(string? creditsText)`: 最初の空でない行を trim して、最大 200 文字。
  - `sealed class CreditsContext`
    - `void Begin(EndingType type, int launchedBefore)`
    - `void NoteQuestEndingRecorded()`、`bool ConsumeQuestEndingRecorded()`
    - `void MarkAlreadyRecorded()`
    - `EndingSignal? Resolve(int launchedNow, string? creditsText)`: 文脈を消費する。記録済みなら null、文脈が無ければ `Other`。
    - `void Reset()`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Record_AssignsOrdinalPerTypeFromLineage()
// lineage が空 → Ship は ordinal 0。同じ lineage でもう一度 → ordinal 1。Archonexus → ordinal 0
[Fact] public void Record_FromReloadedLineage_ReusesOrdinal()
// lineage の写しを取り、元に Ship を記録する。写しに対してもう一度 Ship を記録すると ordinal 0（UpsertEnding で重複になる）
[Fact] public void SeedTraces_SkipsTypesAlreadyInLineage()
[Fact] public void Context_ShipDepartedIsDelta()
// Begin(Ship, 5) → Resolve(8, "…") == (Ship, 3, null)
[Fact] public void Context_NoContext_IsOtherWithExcerpt()
// Resolve(0, "\n  終わりの物語\n二行目") == (Other, 0, "終わりの物語")
[Fact] public void Context_AlreadyRecorded_ReturnsNullOnce()
[Fact] public void Context_CerebrexAndVoid_DepartedZero()
[Fact] public void Context_QuestEndingFlag_ConsumedOnce()
```

- [ ] **Step 2: 失敗することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.EndingLedgerTests"`
Expected: FAIL

- [ ] **Step 3: 実装する**

`Resolve` での旅立ち人数:
- `Ship` と `Archonexus`: `max(0, launchedNow − launchedBefore)`
- それ以外: 0

- [ ] **Step 4: 成功することを確かめる**

Run: `dotnet test --filter-class "RimworldStatistics.Core.Tests.EndingLedgerTests"`
Expected: PASS

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics.Core/Endings Tests/RimworldStatistics.Core.Tests/EndingLedgerTests.cs
git commit -m "feat(core): ending ledger with lineage ordinals and credits context"
```

---

### Task 11: 実セーブでの照合テスト

**Files:**
- Create: `tools/fetch_dev_inputs.py`
- Create: `Tests/RimworldStatistics.Core.Tests/RealSavesRegressionTests.cs`

**Interfaces:**
- Consumes: Task 8 `ProfileSession`、Task 9 `ImportEngine`、Task 4 `CampaignMatcher`、Task 7 `StatsCalculator`
- Produces:
  - `tools/fetch_dev_inputs.py {refs|saves|audit} <dest>`
    - 環境変数 `R2_ENDPOINT`、`R2_BUCKET`、`AWS_ACCESS_KEY_ID`、`AWS_SECRET_ACCESS_KEY` を使う（boto3 が必要）。
    - `refs`: `Game-Linux/{Assembly-CSharp,UnityStubs,Waylith}.dll` を `<dest>/game/` に、`Workshop/2009463077/1.7/Assemblies/0Harmony.dll` を `<dest>/harmony/` に置く。
    - `saves`: `Saves/` 以下を `<dest>/` に置く。
    - `audit`: `research/vanilla-1.7.4985-audited/` の `tools/`、`assembly/Assembly-CSharp/`、`assembly/UnityStubs/`、`runtimeconfig.json`、`audit-summary.json`、`index.json` を `<dest>/` に置く。
    - 秘密の値を出力しない。バケット名もパスもコードに書かない。

- [ ] **Step 1: 実セーブを取得する**

```bash
python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install -q boto3 && .devinputs/venv/bin/python -I tools/fetch_dev_inputs.py saves .devinputs/saves
```

Expected: 49 ファイル

- [ ] **Step 2: テストを書く**

```csharp
[Fact] public void RealSaves_MatchResearchTotals()
{
    var dir = Environment.GetEnvironmentVariable("RWSTATS_REAL_SAVES_DIR");
    if (dir is null) Assert.Skip("RWSTATS_REAL_SAVES_DIR is not set");
    using var tmp = new TempDir();
    var session = new ProfileSession(new ProfileStore(tmp.Path), () => DateTime.UtcNow);
    var hashesBefore = Directory.EnumerateFiles(dir).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    var sw = Stopwatch.StartNew();
    var r = new ImportEngine(new SaveReader()).Run(dir, session, null, CancellationToken.None);
    sw.Stop();
    TestContext.Current.SendDiagnosticMessage($"import {r.Read} files in {sw.Elapsed.TotalSeconds:F1}s");
    Assert.Equal(49, r.Read); Assert.Equal(0, r.Failed);
    var s = session.GetView(new StatsOptions()).Stats;
    Assert.Equal(33, s.ColonyCount);
    Assert.Equal(135723149L, s.TotalTicks);
    Assert.Equal(950114.07, s.TotalRealPlay, 0);
    Assert.Equal((190, 311, 29, 0), (s.Raids, s.Threats, s.Killed, s.TotalDeparted));
    Assert.Equal(4364583.06, s.PeakWealthSum, 0); Assert.Equal(4294287.26, s.LastWealthSum, 0);
    Assert.Equal(20835213L, s.LongestTicks); Assert.Equal(925789.69, s.SingleMaxWealth, 0);
    Assert.Equal(20, s.MaxPopulation); Assert.Equal(0, s.TotalEndings);
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60));
    var reread = new ImportEngine(new SaveReader()).Run(dir, session, null, CancellationToken.None);
    Assert.Equal((0, 49), (reread.Read, reread.Skipped));
    foreach (var (f, h) in hashesBefore)   // 受け入れ条件 9: セーブは読み取りだけで、変わらない
        Assert.Equal(h, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
}
```

- [ ] **Step 3: 走らせて確かめる**

Run: `RWSTATS_REAL_SAVES_DIR=$PWD/.devinputs/saves dotnet test --filter-method "RimworldStatistics.Core.Tests.RealSavesRegressionTests.RealSaves_MatchResearchTotals"`
Expected: PASS。出力された時間を、Task 17 の記録に書き写す。

値が合わない場合は、まずコロニー数の差を見る。調査 §6 の例 A〜D のどれが崩れたかをコロニー単位で比べて、原因を直す。期待値は書き換えない。

- [ ] **Step 4: 環境変数が無いときはスキップになることを確かめる**

Run: `dotnet test`
Expected: すべて PASS、skipped 1

- [ ] **Step 5: コミットする**

```bash
git add tools/fetch_dev_inputs.py Tests/RimworldStatistics.Core.Tests/RealSavesRegressionTests.cs
git commit -m "test(core): regression test against real saves (opt-in)"
```

---

### Task 12: ゲーム側の土台（About、csproj、Mod、設定、Harmony の起動、参照照合）

**Files:**
- Create: `About/About.xml`
- Create: `Source/RimworldStatistics/RimworldStatistics.csproj`、`RimworldStatisticsMod.cs`、`RimworldStatisticsSettings.cs`、`PatchRunner.cs`
- Create: `Languages/Japanese/Keyed/RimworldStatistics.xml`、`Languages/English/Keyed/RimworldStatistics.xml`
- Modify: `RimworldStatistics.sln`

**Interfaces:**
- Consumes: 中核 DLL（ProjectReference）
- Produces:
  - `sealed class RimworldStatisticsSettings : ModSettings`: `bool includeDevMode = false`、`bool autoImport = true`。`ExposeData` で `Scribe_Values` を使い、ラベルはフィールド名と同じにする。
  - `sealed class RimworldStatisticsMod : Mod`
    - `static RimworldStatisticsSettings Settings`
    - コンストラクタで `PatchRunner.ApplyAll(new Harmony("pengin0503.rimworldstatistics"))` を呼ぶ。
    - `SettingsCategory()` は `"RWS_SettingsCategory".Translate()` を返す。
    - `DoSettingsWindowContents(Rect)`: `Listing_Standard.CheckboxLabeled` を 2 つ置く。
  - `static class PatchRunner`
    - `void ApplyAll(Harmony harmony)`: アセンブリ内の `[HarmonyPatch]` 付きクラスを 1 つずつ `harmony.CreateClassProcessor(type).Patch()` する。それぞれ try/catch で囲む。
    - `IReadOnlyList<string> Failed`
    - `bool IsDisabled(Type patchClass)`、`void Disable(Type patchClass, Exception e)`: `Log.ErrorOnce` で 1 回だけ出す。

- [ ] **Step 1: 参照する DLL を取得する**

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py refs .refs
```

Expected: `.refs/game/Assembly-CSharp.dll`、`.refs/game/UnityStubs.dll`、`.refs/harmony/0Harmony.dll` がそろう。

- [ ] **Step 2: 実装する**

- **csproj:**
  - `OutputPath=$(MSBuildThisFileDirectory)../../1.7/Assemblies/`、`AppendTargetFrameworkToOutputPath=false`
  - `CopyLocalLockFileAssemblies=false`、`GenerateDependencyFile=false`
  - 参照: ゲーム DLL 3 つは `Private=false`、中核 DLL は ProjectReference（コピーする）
  - `CheckGameReferences` ターゲット: KeepAimNShoot と同じく、DLL が無ければ `Error` を出す。
- **About.xml:**
  - `name` は `Rimworld Statistics`、`author` は `pengin0503`、packageId と supportedVersions は Global Constraints のとおり。
  - `modDependencies` に Harmony（`steamWorkshopUrl` は `steam://url/CommunityFilePage/2009463077`）。
  - `loadAfter` に `brrainz.harmony`。
  - 説明文は日本語と英語。
- **翻訳キー:** この段階では `RWS_SettingsCategory`、`RWS_Setting_IncludeDevMode`、`RWS_Setting_AutoImport` だけ。

- [ ] **Step 3: ビルドする**

Run: `dotnet build -c Release`
Expected: 0 warnings、0 errors。`1.7/Assemblies/` に `RimworldStatistics.dll` と `RimworldStatistics.Core.dll` だけがある（ゲーム DLL と Harmony は入らない）。

- [ ] **Step 4: 静的な参照照合をする**

```bash
.devinputs/venv/bin/python -I tools/fetch_dev_inputs.py audit .devinputs/audit
dotnet build .devinputs/audit/tools/MetadataAudit/MetadataAudit.csproj -c Release
dotnet .devinputs/audit/tools/MetadataAudit/bin/Release/net10.0/MetadataAudit.dll index 1.7/Assemblies/RimworldStatistics.dll .devinputs/mod-index.json
python3 -I .devinputs/audit/tools/refcheck.py .devinputs/mod-index.json .devinputs/audit/assembly/Assembly-CSharp .devinputs/audit/assembly/UnityStubs --runtime-config .devinputs/audit/runtimeconfig.json --out .devinputs/refcheck.json
python3 -c "import json;c=json.load(open('.devinputs/refcheck.json'))['counts'];print(c);assert c.get('type-not-found',0)==0 and c.get('signature-not-found',0)==0"
```

Expected: 最後の行が assert を通る（`type-not-found` と `signature-not-found` が 0）。

- [ ] **Step 5: コミットする**

```bash
git add About Languages Source/RimworldStatistics RimworldStatistics.sln
git commit -m "feat(mod): mod entry, settings, guarded Harmony bootstrap"
```

---

### Task 13: ProfileService と ImportRunner

**Files:**
- Create: `Source/RimworldStatistics/ProfileService.cs`、`ImportRunner.cs`、`Patches/GoToMainMenuPatch.cs`

**Interfaces:**
- Consumes: Task 8 `ProfileSession`、Task 9 `ImportEngine`、Task 12 `RimworldStatisticsMod.Settings`
- Produces:
  - `static class ProfileService`
    - `ProfileSession Session`: メインスレッドで初めて使ったときに作る。ディレクトリは `Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics")`。
    - `StatsOptions Options`: 設定から作る。
    - `StatsView View`: `Session.GetView(Options)` を返す。
    - `void ReportLoadProblemsOnce()`: `LoadResult` が `Backup*`、`Empty` で破損あり、`NewerSchema` のときに 1 回だけ `Log.Warning` を出す。
  - `static class ImportRunner`
    - `void StartIfIdle()`: メインスレッドで `SavedGamesFolderPath` を取ってから `Task.Run` する。
    - `void Cancel()`
    - `bool Running`、`ImportProgress? Progress`、`ImportResult? LastResult`、`Exception? LastError`
    - `void AutoStartOnce()`: `autoImport` が真で、このセッションでまだ始めていなければ `StartIfIdle()` する。
  - `[HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))] static class GoToMainMenuPatch`: Prefix で `ProfileService.Session.Flush()` を呼ぶ。仕様 §6.3 の「タイトル画面へ戻ったとき」の書き込み。

- [ ] **Step 1: 実装する**

- `Progress` と `LastResult` は `volatile` の参照で、裏スレッドから差し替える。
- 裏スレッドの中では `Verse.*` を呼ばない。ログを出す必要があれば、メインスレッドで `LastError` を見て出す。

- [ ] **Step 2: ビルドと参照照合をする**

Run: `dotnet build -c Release`、続けて Task 12 の Step 4 のコマンド
Expected: 0 errors。`type-not-found` と `signature-not-found` が 0

- [ ] **Step 3: コミットする**

```bash
git add Source/RimworldStatistics/ProfileService.cs Source/RimworldStatistics/ImportRunner.cs Source/RimworldStatistics/Patches/GoToMainMenuPatch.cs
git commit -m "feat(mod): profile service, background import runner, flush on menu return"
```

---

### Task 14: 画面（メニューボタン、統計ウィンドウ、翻訳）

**Files:**
- Create: `Source/RimworldStatistics/Patches/MainMenuButtonPatch.cs`
- Create: `Source/RimworldStatistics/UI/Dialog_RimworldStatistics.cs`、`UI/OverviewTab.cs`、`UI/EndingsTab.cs`、`UI/ColoniesTab.cs`、`UI/ImportTab.cs`
- Modify: `Languages/Japanese/Keyed/RimworldStatistics.xml`、`Languages/English/Keyed/RimworldStatistics.xml`

**Interfaces:**
- Consumes: Task 13 `ProfileService`、`ImportRunner`、Task 7 `StatFormat`、Task 5 `ProfileMutations.SetExcluded`
- Produces:
  - **`[HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))] static class MainMenuButtonPatch`**
    - `static void Prefix(List<ListableOption> optList)`: 次の条件をすべて満たすときだけ、オプションの項目の直前に足す。
      - `Current.ProgramState == ProgramState.Entry`
      - `optList` に `label == "Options".Translate()` の項目がある
      - 自分の項目（`label == "RWS_MenuButton".Translate()`）がまだ無い
    - 足す項目は `new ListableOption("RWS_MenuButton".Translate(), () => Find.WindowStack.Add(new Dialog_RimworldStatistics()))`。
    - 足す前に `ImportRunner.AutoStartOnce()` を呼ぶ。
  - **`sealed class Dialog_RimworldStatistics : Window`**
    - `InitialSize = new Vector2(1000f, 720f)`、`doCloseX = true`、`absorbInputAroundWindow = true`、`closeOnClickedOutside = false`
    - タブは 4 つ（`RWS_Tab_Overview`、`RWS_Tab_Endings`、`RWS_Tab_Colonies`、`RWS_Tab_Import`）で、`TabDrawer.DrawTabs` で描く。
  - 各タブ: `static void Draw(Rect rect, StatsView view)`。`ImportTab` は `Draw(Rect rect)`。

**各タブの中身（仕様 §9.2）**
- **Overview:**
  - 上段: 日数（`RWS_DaysValue` = `{0}日（{1}年{2}日）`）、エンディング（種類ごとの内訳をツールチップに）、旅立った人数、最高資産の合計、コロニー数、実プレイ時間（`RWS_HoursValue` = `{0}時間`）
  - 下段: 襲撃、大きな脅威、死亡、最長存続、単一最高、最大人口、最終資産の合計
  - 各項目のツールチップには、`RWS_TooltipBasis`（`{0} コロニー分から計算（合計から外したもの {1}）`）を出す。
  - `HasData` が偽なら全項目を「—」にし、ツールチップは `RWS_NoData`。エンディングと旅立った人数のツールチップには、`RWS_EndingsTooltipPreInstall`（MOD 導入前はセーブの痕跡だけで、アーコネクサスは数えられない、という説明）を必ず含める。
- **Endings:** 全コロニーの `Endings` と `Traces` を 1 行ずつ並べる。
  - 列: 日時、コロニー、種類（`RWS_Ending_<EndingType>`）、何回目か（1 から）、人数、出所（`RWS_Source_Live`、`RWS_Source_SeededTrace`、`RWS_Source_SaveTrace`）
  - `includeDevMode` が偽なら、開発モードだけの組は出さない。
- **Colonies:** `ColonyStats` を 1 行ずつ並べる。
  - 列見出しをクリックすると、昇順と降順が入れ替わる。
  - 「合計から外す」チェックで `ProfileService.Session.Mutate(p => ProfileMutations.SetExcluded(p, [colony.Key, .. colony.AliasKeys], v), SaveMode.Background)` を呼ぶ。
  - 行を選ぶと、元になったセーブ（ファイル名、版、日数、`Superseded`、元ファイルの有無）を下に出す。元ファイルの有無は、ウィンドウを開いたときと取り込みが終わったときに、`ImportRunner.LastResult.PresentFiles` から作る。
  - 名前が無ければ `RWS_UnnamedColony` = `（未命名）{0}`（ワールド名）。
- **Import:**
  - 最後に読んだ日時、件数、失敗と識別不可の理由の一覧
  - `RWS_Rescan` ボタン、進捗バー（`Widgets.FillableBar`）、`RWS_Cancel` ボタン
  - 記録ファイルのパス、`LoadResult.Source`

- [ ] **Step 1: 翻訳キーを足す**

使うキー（日本語と英語の両方に同じキーを置く）:
- 入口とタブ: `RWS_MenuButton`、`RWS_WindowTitle`、`RWS_Tab_*`
- 概要の各項目名: `RWS_Stat_*`
- 単位と表示: `RWS_DaysValue`、`RWS_HoursValue`、`RWS_NoData`、`RWS_TooltipBasis`、`RWS_EndingsTooltipPreInstall`、`RWS_UnnamedColony`
- エンディングと出所: `RWS_Ending_*`（9 種）、`RWS_Source_*`
- コロニータブの列と操作: `RWS_Col_*`、`RWS_Exclude`
- 取り込みタブ: `RWS_Rescan`、`RWS_Cancel`、`RWS_Import_*`
- 設定: `RWS_Setting_*`

- [ ] **Step 2: 実装する**

- [ ] **Step 3: ビルドと参照照合をする**

Run: `dotnet build -c Release`、続けて Task 12 の Step 4 のコマンド
Expected: 0 errors。`type-not-found` と `signature-not-found` が 0

- [ ] **Step 4: 翻訳キーの抜けを確かめる**

Run: `grep -ohE '"RWS_[A-Za-z_]+"' -r Source/RimworldStatistics | sort -u | tr -d '"' > /tmp/used.txt; for f in Languages/*/Keyed/RimworldStatistics.xml; do grep -oE '<RWS_[A-Za-z_]+>' $f | tr -d '<>' | sort -u | comm -23 /tmp/used.txt - | sed "s|^|$f missing: |"; done`
Expected: 何も出ない

- [ ] **Step 5: コミットする**

```bash
git add Source/RimworldStatistics/Patches/MainMenuButtonPatch.cs Source/RimworldStatistics/UI Languages
git commit -m "feat(mod): main menu entry and statistics window"
```

---

### Task 15: プレイ中の記録（GameComponent と LiveStateReader）

**Files:**
- Create: `Source/RimworldStatistics/LiveStateReader.cs`、`GameComponent_RimworldStatistics.cs`、`EndingRuntime.cs`

**Interfaces:**
- Consumes: Task 5 の `RegisterLiveCampaign`・`ApplyLiveObservation`・`UpsertEnding`、Task 4 の `FindCampaignIdFor`、Task 10 の `EndingLedger`・`LineageEntry`、Task 3 の `EndingTraces`、Task 13 の `ProfileService`・`ImportRunner`
- Produces:
  - `static class LiveStateReader`（メインスレッド専用）
    - `ColonyIdentity ReadIdentity(string? campaignId)`
    - `ObservedValues ReadValues()`
    - `DisplayInfo ReadDisplay()`
    - `TraceInputs ReadTraceInputs(IReadOnlyCollection<EndingType> lineageTypes)`: `lineageTypes` には GameComponent の `Lineage` の種類を渡す。MOD 導入前のセーブを初めてロードしたときは空になる。
    - `string GameVersion => VersionControl.CurrentVersionStringWithRev`
  - `static class EndingRuntime`: `static readonly CreditsContext Context = new()`。Task 16 のパッチもこれを使う。
  - `sealed class GameComponent_RimworldStatistics : GameComponent`（完全名は `SaveReader.ComponentClassName` と同じ）
    - コンストラクタ: `(Game game)`
    - フィールド: `string? campaignId`、`List<string> lineageTokens`、`int dataVersion = 1`
    - `List<LineageEntry> Lineage`（`lineageTokens` との相互変換）
    - `static GameComponent_RimworldStatistics? CurrentOrNull => Current.Game?.GetComponent<GameComponent_RimworldStatistics>()`
    - 上書きするメソッド: `ExposeData`、`StartedNewGame`、`LoadedGame`、`FinalizeInit`

**ゲーム中の値の出どころ（調査 §5）**

| 項目 | 出どころ |
|---|---|
| シード、乱数値 | `Find.World.info.seedString`、`Find.World.info.persistentRandomValue` |
| 開始タイル | `Find.GameInfo.startingTile.ToString()` |
| 開始時刻 | `Find.TickManager.gameStartAbsTick` |
| 資産の先頭値 | `Find.History.Groups()` から `def.defName == "Wealth_Total"` の recorder を探し、`records[0]` を `BitConverter.SingleToUInt32Bits` で変換 |
| 初期入植者 | `Find.GameInfo.startingAndOptionalPawns` の `GetUniqueLoadID()` |
| 各数値 | `Find.TickManager.TicksGame`、`Find.GameInfo.RealPlayTimeInteracting`、`Find.StoryWatcher.statsRecord.*` |
| 資産の最大値・最新値 | 上の `records` の最大値と最後の値 |
| 表示用 | `Faction.OfPlayer.Name`、`Find.World.info.name`、`Find.Scenario.name`、`Find.Storyteller.def.defName`、`Find.Storyteller.difficultyDef.defName`、`Find.GameInfo.permadeathMode` |
| 痕跡用 | `Find.QuestManager.QuestsListForReading` のうち、`root.defName` が `EndGame_RoyalAscent`／`Gravcore_Mechhive` で、`State == QuestState.EndedSuccess` のもの。`Faction.OfMechanoids?.deactivated`、`Find.Anomaly?.LevelDef?.defName` |

- [ ] **Step 1: 実装する**

記録の変更はすべて `ProfileService.Session.Mutate(p => ProfileMutations.…(p, …), SaveMode.…)` の形で行う。

- **`StartedNewGame`:** `campaignId = Guid.NewGuid().ToString("N")` を付け、`RegisterLiveCampaign` を `Immediate` で記録する。`EndingRuntime.Context.Reset()` を呼ぶ。
- **`LoadedGame`:**
  - `campaignId` が null なら、`FindCampaignIdFor(identity)` の結果を使う。null なら新しい GUID を付ける。`RegisterLiveCampaign` する。
  - そのうえで `EndingLedger.SeedTraces` の結果を `UpsertEnding` し、まとめて `Immediate` で記録する。
  - `EndingRuntime.Context.Reset()` を呼ぶ。
- **`FinalizeInit`:** `ImportRunner.Cancel()` を呼ぶ。
- **`ExposeData`:**
  - 保存するもの: `Scribe_Values.Look(ref campaignId, "campaignId")`、`Scribe_Collections.Look(ref lineageTokens, "lineageEndings", LookMode.Value)`、`Scribe_Values.Look(ref dataVersion, "dataVersion", 1)`
  - `Scribe.mode == LoadSaveMode.Saving` のときは、try/catch の中で `ApplyLiveObservation` を `Background` で記録する。
  - `PostLoadInit` で `lineageTokens` が null なら空にする。
- クエストの成功は、`Quest.State == QuestState.EndedSuccess` で判断する。1.7.4985 の `RimWorld.QuestState` は `NotYetAccepted, Ongoing, EndedUnknownOutcome, EndedOfferExpired, EndedSuccess, EndedFailed, EndedInvalid` で、`ended && endOutcome == Success` のときに `EndedSuccess` になる。

- [ ] **Step 2: ビルドと参照照合をする**

Run: `dotnet build -c Release`、続けて Task 12 の Step 4 のコマンド
Expected: 0 errors。`type-not-found` と `signature-not-found` が 0

- [ ] **Step 3: コミットする**

```bash
git add Source/RimworldStatistics/LiveStateReader.cs Source/RimworldStatistics/GameComponent_RimworldStatistics.cs Source/RimworldStatistics/EndingRuntime.cs
git commit -m "feat(mod): stamp campaign id into saves and record live observations"
```

---

### Task 16: エンディングのパッチ

**Files:**
- Create: `Source/RimworldStatistics/Patches/EndingPatches.cs`

**Interfaces:**
- Consumes: Task 10 の `CreditsContext`・`EndingLedger`、Task 15 の `EndingRuntime.Context`・`GameComponent_RimworldStatistics`・`LiveStateReader`、Task 5 の `UpsertEnding`・`ApplyLiveObservation`、Task 12 の `PatchRunner`
- Produces:
  - `static class EndingPatches`
    - `static void RecordNow(EndingSignal signal)`: GameComponent が無いとき（`CurrentOrNull == null`）は記録しない。
    - 下の表の `Context` は、すべて `EndingRuntime.Context` を指す。
  - パッチ用クラス（各 1 つ）

| クラス | 対象 | 処理 |
|---|---|---|
| `ShowCreditsPatch` | `GameVictoryUtility.ShowCredits` の Postfix | `Context.Resolve(launchedNow, victoryText)` が null でなければ `RecordNow` |
| `CountdownEndedPatch` | `ShipCountdown.CountdownEnded` の Prefix | 下の分岐 |
| `QuestPartEndGamePatch` | `QuestPart_EndGame.Notify_QuestSignalReceived` の Prefix と Postfix（`__state` を使う） | 下の説明 |
| `ArchonexusEndGamePatch` | `ArchonexusCountdown.EndGame` の Prefix | `Begin(Archonexus, launched)` |
| `CerebrexDeactivatePatch` | `CompCerebrexCore.DeactivateCore` の Prefix（`bool scavenging`） | `Begin(scavenging ? CerebrexScavenged : CerebrexDestroyed, launched)` |
| `EmbraceTheVoidPatch` / `DisruptTheLinkPatch` | `VoidAwakeningUtility.EmbraceTheVoid` / `DisruptTheLink` の Prefix | `Begin(VoidEmbraced / VoidDisrupted, launched)` |

- **`CountdownEndedPatch` の分岐:** 静的フィールドの読み取りは `AccessTools.Field(typeof(ShipCountdown), "shipRoot")` と `"customLaunchString"` の `GetValue(null)` を使う。
  - `shipRoot` がある → `Begin(Ship, launched)`
  - `customLaunchString` が空でない
    - `ConsumeQuestEndingRecorded()` が真 → `MarkAlreadyRecorded()`
    - 偽 → 何もしない（`ShowCredits` 側で `Other` になる）
  - どちらも無い → `Begin(Ship, launched)`（開発モードの `ForceShipCountdown`）
- **`QuestPartEndGamePatch` の流れ:**
  1. Prefix で `(wasCountingDown, launchedBefore)` を `__state` に入れる。
  2. Postfix で `!wasCountingDown && ShipCountdown.CountingDown` なら記録する。種類は、`__instance.quest?.root?.defName == "EndGame_RoyalAscent"` なら `RoyalAscent`、それ以外なら `Other`（excerpt はクエストの defName）。人数は `colonistsLaunched` の前後の差。
  3. `RecordNow` の後に `NoteQuestEndingRecorded()` を呼ぶ。
- **パッチ共通:** 本体はすべて try/catch で囲み、例外が起きたら `PatchRunner.Disable(typeof(自分), e)` を呼ぶ。`IsDisabled` なら何もしない。
- **`RecordNow` の手順:**
  1. `EndingLedger.Record` で記録を作る（GameComponent の `Lineage`、`Prefs.DevMode`、`LiveStateReader.GameVersion`、`DateTime.UtcNow`、その時点の `colonistsLaunched` を `launchedTotalAfter` として渡す）。GameComponent の `lineageTokens` を更新する。
  2. 1 回の `ProfileService.Session.Mutate(..., SaveMode.Immediate)` の中で、`UpsertEnding` と `ApplyLiveObservation`（そのときの値）を両方行う。

- [ ] **Step 1: 実装する**

- [ ] **Step 2: ビルドと参照照合をする**

Run: `dotnet build -c Release`、続けて Task 12 の Step 4 のコマンド
Expected: 0 errors。`type-not-found` と `signature-not-found` が 0。パッチ対象の 7 メソッドが `matched` に含まれる。

- [ ] **Step 3: コミットする**

```bash
git add Source/RimworldStatistics/Patches/EndingPatches.cs
git commit -m "feat(mod): detect and record all ending types"
```

---

### Task 17: 仕上げ（手動確認の手順書、配布物、README）

**Files:**
- Create: `docs/manual-test-checklist.md`、`tools/package.sh`
- Modify: `README.md`

**Interfaces:**
- Consumes: すべて
- Produces:
  - `tools/package.sh <out.zip>`: Release ビルドの後、`About/`、`1.7/Assemblies/`、`Languages/` を `RimworldStatistics/` フォルダとして zip にする。
  - `docs/manual-test-checklist.md`: 仕様 §11.2 の 7 項目を、チェックボックスと期待値の付いた手順にする。
    - 期待値には Task 11 の合計値（33 コロニー、135,723,149 ティック、2,262 日など）と、測った取り込み時間を書く。
    - Force start ship の準備手順（開発モード、Ship_ComputerCore の設置、冷凍睡眠棺に入植者を入れる）を書く。

- [ ] **Step 1: 全体を確かめる**

Run: `dotnet test`、`RWSTATS_REAL_SAVES_DIR=$PWD/.devinputs/saves dotnet test`、`dotnet build -c Release`、Task 12 の Step 4 のコマンド
Expected: すべて PASS（環境変数なしのときは skipped 1）。0 warnings。`type-not-found` と `signature-not-found` が 0

- [ ] **Step 2: 手順書、配布用スクリプト、README を書く**

README の設計状況は「実装済み・手動確認待ち」にし、手順書へのリンクを足す。

- [ ] **Step 3: 配布物を作る**

Run: `bash tools/package.sh artifacts/RimworldStatistics.zip && unzip -l artifacts/RimworldStatistics.zip`
Expected: 次の 4 つ以外のファイルが入っていない。
- `RimworldStatistics/About/About.xml`
- `RimworldStatistics/1.7/Assemblies/RimworldStatistics.dll`
- `RimworldStatistics/1.7/Assemblies/RimworldStatistics.Core.dll`
- `RimworldStatistics/Languages/{Japanese,English}/Keyed/RimworldStatistics.xml`

- [ ] **Step 4: コミットする**

```bash
git add docs/manual-test-checklist.md tools/package.sh README.md
git commit -m "docs: manual test checklist and packaging script"
```
