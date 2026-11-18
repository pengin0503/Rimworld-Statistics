using System.Text;
using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ExportAndDiagnosticsEnhancementsTests
{
    [Fact]
    public void ColoniesCsv_AppendsTrailingFactionNameWithoutShiftingExistingColumnsAndEscapesFormulas()
    {
        using var root = new TempDir();
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = 1,
                Colonies =
                [
                    new ColonyStats
                    {
                        Key = "col-alpha",
                        FactionName = "=OriginalFaction",
                        WorldName = "Terra",
                        Scenario = "Crashlanded",
                        Storyteller = "Cassandra",
                        Difficulty = "StriveToSurvive",
                        Permadeath = true,
                        HasObservedPoint = true,
                        MaxTicks = 120_000,
                        SaveCount = 3,
                        EndingCounts = new Dictionary<EndingType, int>(),
                        GameVersions = ["1.7.5011"],
                    },
                ],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = NumericView.Empty,
        };

        var labels = new ExportText(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["col-alpha"] = "Custom Starbase" },
            new Dictionary<NumericRow, string>(),
            "ja",
            "All colonies",
            "1.5.0");

        var export = StatisticsExport.Write(
            view,
            labels,
            _ => UnitRecordingState.NotSelected,
            root.Path,
            TestContext.Current.CancellationToken);

        var lines = File.ReadAllLines(Path.Combine(export.Directory, "colonies.csv"));
        Assert.Equal(2, lines.Length);

        var headers = lines[0].Split(',');
        string[] expectedHeaders =
        [
            "colony_key", "name", "world", "scenario", "storyteller", "difficulty", "permadeath", "excluded", "in_scope",
            "days", "real_play_hours", "peak_wealth", "last_wealth", "raids", "threats", "killed", "departed",
            "max_population", "endings", "saves", "last_played_utc", "game_versions", "faction_name",
        ];
        Assert.Equal(expectedHeaders, headers);

        var cols = lines[1].Split(',');
        Assert.Equal(23, cols.Length);
        Assert.Equal("col-alpha", cols[0]);
        Assert.Equal("Custom Starbase", cols[1]);
        Assert.Equal("true", cols[8]); // in_scope stays at index 8
        Assert.Equal("1.7.5011", cols[21]); // game_versions stays at index 21
        Assert.Equal("'=OriginalFaction", cols[22]); // trailing faction_name escaped against formula injection
    }

    [Fact]
    public void ColonyChronicleExport_WritesMarkdownAndOfflineHtmlAtomicallyAndEscapesUntrustedStrings()
    {
        using var root = new TempDir();
        var utc = new DateTime(2026, 11, 18, 9, 30, 0, DateTimeKind.Utc);
        var text = new ColonyChronicleExportText
        {
            ColonyKey = "col-xss`key",
            ColonyName = "CON/<script>",
            OriginalFactionLine = "Original faction: <img src=x onerror=alert(1)>",
            ModVersion = "1.5.0",
            Language = "ja",
            PageTitle = "Chronicle: Custom Colony <script>bad()</script>",
            Subtitle = "Branch: Day 42.0 · Oldest first",
            SummaryLine = "Showing 2 of 2 entries across 3 nodes",
            NoticeLines = ["Other branch has 1 ending <tag>"],
            ColTime = "Day",
            ColKind = "Kind",
            ColEvent = "Event | Detail",
            ColEvidence = "Evidence",
            Entries =
            [
                new ChronicleExportEntryText(
                    "Day 1.5",
                    "Milestone",
                    ColonyChronicleExport.KindCssClass(ChronicleEntryKind.Milestone),
                    "First raid | +3 raiders\nline2",
                    "Live event",
                    IsPartial: false),
                new ChronicleExportEntryText(
                    "Day 10.0 - 15.0",
                    "Gap",
                    ColonyChronicleExport.KindCssClass(ChronicleEntryKind.RecordingGap),
                    "Missing parent <script>alert(2)</script>",
                    "Checkpoint · Partial",
                    IsPartial: true),
            ],
            EmptyMessage = "No entries",
            UndatedTitle = "Undated traces (1)",
            UndatedSubtitle = "Shown separately without fabricated dates.",
            UndatedLines = ["Ship launch — Save trace <b>raw</b>"],
            FooterNote = "Single-branch chronicle.",
        };

        // Simulated write failure leaves no .tmp or partial file behind.
        Assert.Throws<IOException>(() =>
            ColonyChronicleExport.WriteMarkdown(text, root.Path, utc, _ => throw new IOException("disk full")));
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));

        var mdRes = ColonyChronicleExport.WriteMarkdown(text, root.Path, utc);
        Assert.EndsWith(".md", mdRes.FilePath, StringComparison.Ordinal);
        Assert.StartsWith("colony-chronicle-CON_script-", Path.GetFileName(mdRes.FilePath), StringComparison.Ordinal);
        var md = File.ReadAllText(mdRes.FilePath, Encoding.UTF8);
        Assert.Contains("# Chronicle: Custom Colony <script>bad()</script>", md);
        Assert.Contains("- Original faction: <img src=x onerror=alert(1)>", md);
        Assert.Contains("> Other branch has 1 ending <tag>", md);
        Assert.Contains("| Event \\| Detail |", md);
        Assert.Contains("| Day 1.5 | Milestone | First raid \\| +3 raiders<br>line2 | Live event |", md);
        Assert.Contains("## Undated traces (1)", md);
        Assert.Contains("- Ship launch — Save trace <b>raw</b>", md);

        var htmlRes = ColonyChronicleExport.WriteHtml(text, root.Path, utc);
        Assert.EndsWith(".html", htmlRes.FilePath, StringComparison.Ordinal);
        var html = File.ReadAllText(htmlRes.FilePath, Encoding.UTF8);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>raw</b>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;bad()&lt;/script&gt;", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.Contains("class=\"chron-kind-milestone\"", html);
        Assert.Contains("class=\"chron-partial\"", html);
    }

    [Fact]
    public void ColonyReportHtmlExport_RendersOriginalFactionAndIntegratedChronicleSection()
    {
        var report = new ColonySummaryReport
        {
            Overview = new ColonyReportOverview
            {
                ColonyKey = "col-1",
                FactionName = "Original Arrivers <faction>",
                WorldName = "Rim",
                HasObservedPoint = true,
                SaveCount = 2,
                GameVersions = ["1.7"],
                EndingCounts = new Dictionary<EndingType, int>(),
                CountedEndings = [],
                UnexplainedTraces = [],
            },
            Coverage = new ColonyReportCoverageSummary(5, 3, 1, 1, 0, 0, 0),
            KeyRecords = [],
            TopRankedHighlights = [],
            ModHighlights = [],
        };

        var htmlText = new ColonyReportHtmlText
        {
            ColonyName = "Custom Name",
            ScopeName = "All",
            ModVersion = "1.5.0",
            Language = "ja",
            PageTitle = "Colony Summary: Custom Name",
            Subtitle = "Scope: All",
            OriginalFactionLine = "元の派閥名: Original Arrivers <faction>",
            SectionOverview = "Overview",
            SectionEndings = "Endings",
            SectionCoverage = "Coverage",
            SectionKeyRecords = "Key Records",
            SectionTopRanks = "Top Ranks",
            SectionModMetrics = "MOD Metrics",
            OverviewFields = [("元の派閥名", "Original Arrivers <faction>"), ("World", "Rim")],
            EndingLines = [],
            EndingsEmptyText = "None",
            CoverageSummaryLine = "5 metrics",
            ColMetric = "Metric",
            ColValue = "Value",
            ColKind = "Kind",
            ColRank = "Rank",
            ColState = "State",
            ColRange = "Range",
            KeyRecords = [],
            TopRanks = [],
            ModMetrics = [],
            ChronicleSection = new ColonyChronicleSectionHtmlText
            {
                SectionTitle = "コロニー年表（代表セーブ系統）",
                SummaryLine = "2件表示",
                NoticeLines = ["別分岐にエンディング 1件"],
                ColTime = "時期",
                ColKind = "種別",
                ColEvent = "出来事",
                ColEvidence = "根拠",
                Entries =
                [
                    new ChronicleExportEntryText("Day 12.0", "マイルストーン", "chron-kind-milestone", "初の研究完了 <tag>", "ライブ", false),
                ],
                EmptyMessage = "なし",
                UndatedTitle = "日付不明の痕跡 (1)",
                UndatedSubtitle = "セーブ痕跡",
                UndatedLines = ["宇宙船発進 — セーブ痕跡"],
            },
            FooterNote = "Read-only summary",
        };

        var html = ColonyReportHtmlExport.RenderHtml(report, htmlText, new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc));
        Assert.Contains("元の派閥名: Original Arrivers &lt;faction&gt;", html);
        Assert.Contains("<h2>コロニー年表（代表セーブ系統）</h2>", html);
        Assert.Contains("初の研究完了 &lt;tag&gt;", html);
        Assert.Contains("日付不明の痕跡 (1)", html);
        Assert.Contains("宇宙船発進 — セーブ痕跡", html);
    }

    [Fact]
    public void ColonyMetricHistoryCsv_ExportsTimeSeriesPreservingNullGapsEscapingFormulasAndBatchingArchiveReads()
    {
        using var root = new TempDir();
        var profileDir = Path.Combine(root.Path, "profile");
        var exportDir = Path.Combine(root.Path, "exports");

        var countingArchive = new CountingMemoryArchive();
        var craftsDef = MetricRegistry.ImplementedCatalog.First(d => d.Key == WorkFamily.CraftedUnits);
        var tradeDef = MetricRegistry.ImplementedCatalog.First(d => d.Key == TradeFamily.Deals);

        var archivedCp1 = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = "cp-1",
            PayloadHash = "hash-1",
            SaveTick = 60_000,
            Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
            Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
            {
                [craftsDef.StoredKey] = MetricEntry.Of(
                    craftsDef.Revision,
                    new MetricValue
                    {
                        Coverage = MetricCoverage.Complete,
                        IntegerValue = 10,
                        SinceTick = 0,
                    }),
                [tradeDef.StoredKey] = MetricEntry.Of(
                    tradeDef.Revision,
                    new MetricValue
                    {
                        Coverage = MetricCoverage.Complete,
                        IntegerValue = 4,
                        SinceTick = 0,
                    }),
            },
        };
        countingArchive.Append("camp-a", [JsonSerializer.Serialize(archivedCp1, ProfileJson.CompactOptions)]);

        var store = new ProfileStore(profileDir);
        var session = new ProfileSession(store, () => new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), countingArchive);
        session.Mutate(profile =>
        {
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = "a1.rws",
                Status = ObservationStatus.Ok,
                Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
                Display = new DisplayInfo { FactionName = "AlphaOriginal", WorldName = "WorldA" },
                Values = new ObservedValues { TicksGame = 60_000, WealthMax = 15_000, WealthLast = 14_000, WealthPoints = 5, GreatestPopulation = 4 },
            });
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = "a2.rws",
                Status = ObservationStatus.Ok,
                Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
                Display = new DisplayInfo { FactionName = "AlphaOriginal", WorldName = "WorldA" },
                Values = new ObservedValues { TicksGame = 180_000, WealthMax = 45_000, WealthLast = 42_000, WealthPoints = 12, GreatestPopulation = 7 },
            });

            // Checkpoint 1: compressed & archived (inline Metrics empty) so RestoreExportHistories must restore it.
            profile.MetricObservations.Add(new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = "cp-1",
                PayloadHash = "hash-1",
                SaveTick = 60_000,
                Compressed = true,
                ArchivedMetrics = true,
                Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
                Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal),
            });
            // Checkpoint 2: intermediate checkpoint where craftsDef was NOT recorded (explicit null gap), then cp-3 has craftsDef=25!
            profile.MetricObservations.Add(new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = "cp-2",
                ParentKey = "cp-1",
                PayloadHash = "hash-2",
                SaveTick = 120_000,
                Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
                Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
                {
                    [tradeDef.StoredKey] = MetricEntry.Of(
                        tradeDef.Revision,
                        new MetricValue
                        {
                            Coverage = MetricCoverage.Complete,
                            IntegerValue = 9,
                            SinceTick = 0,
                        }),
                },
            });
            profile.MetricObservations.Add(new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = "cp-3",
                ParentKey = "cp-2",
                PayloadHash = "hash-3",
                SaveTick = 180_000,
                Identity = new ColonyIdentity { ModCampaignId = "camp-a" },
                Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
                {
                    [craftsDef.StoredKey] = MetricEntry.Of(
                        craftsDef.Revision,
                        new MetricValue
                        {
                            Coverage = MetricCoverage.Partial,
                            IntegerValue = 25,
                            SinceTick = 0,
                        }),
                    [tradeDef.StoredKey] = MetricEntry.Of(
                        tradeDef.Revision,
                        new MetricValue
                        {
                            Coverage = MetricCoverage.Complete,
                            IntegerValue = 15,
                            SinceTick = 0,
                        }),
                },
            });

            // Excluded colony B must NOT appear in colony_metric_history.csv.
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = "b1.rws",
                Status = ObservationStatus.Ok,
                Identity = new ColonyIdentity { ModCampaignId = "camp-b" },
                Display = new DisplayInfo { FactionName = "ExcludedColony", WorldName = "WorldB" },
                Values = new ObservedValues { TicksGame = 120_000, WealthMax = 20_000, WealthLast = 20_000, WealthPoints = 5, GreatestPopulation = 3 },
            });
        }, SaveMode.None);

        var initialView = session.GetView(new StatsOptions(), AcquisitionUnits.All.Select(u => u.Id).ToArray());
        var colBKey = initialView.Stats.Colonies.Single(c => c.FactionName == "ExcludedColony").Key;
        session.Mutate(p => p.UserDecisions.ExcludedCampaignKeys.Add(colBKey), SaveMode.None);

        var view = session.GetView(new StatsOptions(), AcquisitionUnits.All.Select(u => u.Id).ToArray());
        var colAKey = view.Stats.Colonies.Single(c => c.FactionName == "AlphaOriginal").Key;

        countingArchive.ResetReadCounts();
        var histories = session.ReadExportHistories(view);
        // Single-pass archive batching: at most 1 ReadMany call for camp-a across both craftsDef and tradeDef!
        Assert.Equal(1, countingArchive.ReadManyCalls);
        Assert.Equal(0, countingArchive.SingleReadCalls);

        var export = StatisticsExport.Write(
            view,
            new ExportText(
                new Dictionary<string, string> { [craftsDef.Key] = "+Crafted" },
                new Dictionary<string, string> { [colAKey] = "=CustomAlpha" },
                new Dictionary<NumericRow, string>(),
                "en",
                "All colonies",
                "1.5.0"),
            _ => UnitRecordingState.Recording,
            exportDir,
            TestContext.Current.CancellationToken,
            historySeries: histories);

        var historyPath = Path.Combine(export.Directory, "colony_metric_history.csv");
        var rawBytes = File.ReadAllBytes(historyPath);
        // UTF-8 BOM (EF BB BF) + CRLF line endings
        Assert.True(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF);
        var rawText = Encoding.UTF8.GetString(rawBytes.AsSpan(3));
        Assert.Contains("\r\n", rawText);

        var lines = File.ReadAllLines(historyPath);
        Assert.Equal(
            "colony_key,colony_name,metric_key,label,unit,history_kind,point_index,day,tick,value,coverage,evidence,predecessor_index,unlinked",
            lines[0]);

        // Excluded colony B never appears.
        Assert.DoesNotContain(lines.Skip(1), l => l.StartsWith(colBKey + ",", StringComparison.Ordinal));

        var craftRows = lines.Skip(1)
            .Select(l => l.Split(','))
            .Where(cols => cols[0] == colAKey && cols[2] == craftsDef.Key)
            .ToArray();
        Assert.Equal(3, craftRows.Length);

        // Point 0 (tick 60000, restored from archive): value=10, predecessor="", formula-escaped colony_name & label!
        Assert.Equal("'=CustomAlpha", craftRows[0][1]);
        Assert.Equal("'+Crafted", craftRows[0][3]);
        Assert.Equal("CheckpointLineage", craftRows[0][5]);
        Assert.Equal("0", craftRows[0][6]);
        Assert.Equal("1", craftRows[0][7]);
        Assert.Equal("60000", craftRows[0][8]);
        Assert.Equal("10", craftRows[0][9]);
        Assert.Equal("Complete", craftRows[0][10]);
        Assert.Equal("", craftRows[0][12]); // root has no predecessor
        Assert.Equal("false", craftRows[0][13]);

        // Point 1 (tick 120000, explicit unmeasured gap): value MUST be "" (NEVER "0"), coverage="Unknown", predecessor="0"!
        Assert.Equal("1", craftRows[1][6]);
        Assert.Equal("2", craftRows[1][7]);
        Assert.Equal("120000", craftRows[1][8]);
        Assert.Equal("", craftRows[1][9]);
        Assert.Equal("Unknown", craftRows[1][10]);
        Assert.Equal("", craftRows[1][11]);
        Assert.Equal("0", craftRows[1][12]);

        // Point 2 (tick 180000): value=25, coverage="Partial", predecessor="1".
        Assert.Equal("2", craftRows[2][6]);
        Assert.Equal("25", craftRows[2][9]);
        Assert.Equal("Partial", craftRows[2][10]);
        Assert.Equal("1", craftRows[2][12]);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(export.Directory, "manifest.json")));
        Assert.Equal(lines.Length - 1, manifest.RootElement.GetProperty("rows").GetProperty("colony_metric_history.csv").GetInt32());
    }

    [Fact]
    public void DiagnosticSummaryBuilder_StrictlyExcludesEveryPiiCanaryPathNameIdHashAndSecret()
    {
        using var root = new TempDir();

        const string winPathCanary = @"C:\Users\AliceSecret\AppData\LocalLow\Ludeon Studios\RimWorld\Saves\SecretSave.rws";
        const string linuxPathCanary = "/home/bob_secret/.config/unity3d/Ludeon Studios/RimWorld/Saves/PrivateSave.rws";
        const string corruptFileCanary = @"C:\Users\AliceSecret\profile.corrupt.20261118.json";
        const string factionCanary = "SecretFactionAlpha";
        const string worldCanary = "SecretWorldBeta";
        const string scenarioCanary = "CustomScenarioGamma";
        const string storytellerCanary = "CustomStorytellerDelta";
        const string campaignIdCanary = "camp-secret-uuid-9999";
        const string colonyKeyCanary = "colony-secret-key-42";
        const string fingerprintCanary = "sha256-deadbeefcafe1234";
        const string payloadHashCanary = "payload-hash-8888";
        const string checkpointKeyCanary = "cp-secret-key-777";
        const string thirdPartyModCanary = "alice_private.secretmodpack";
        const string secretTokenCanary = "R2_SECRET_ACCESS_KEY=super_secret_token_98765";
        using var quarantinedDoc = JsonDocument.Parse("{\"corrupt\":true}");

        var profile = new Profile
        {
            SchemaVersion = 2,
            SaveObservations =
            [
                new SaveObservation
                {
                    FileName = winPathCanary,
                    ContentHash = fingerprintCanary,
                    Status = ObservationStatus.Ok,
                    Identity = new ColonyIdentity { ModCampaignId = campaignIdCanary },
                    Display = new DisplayInfo
                    {
                        FactionName = factionCanary,
                        WorldName = worldCanary,
                        Scenario = scenarioCanary,
                        Storyteller = storytellerCanary,
                    },
                    Values = new ObservedValues { TicksGame = 60_000 },
                },
                new SaveObservation
                {
                    FileName = linuxPathCanary,
                    Status = ObservationStatus.Error,
                    ErrorReason = $"IOException: {linuxPathCanary} changed during read",
                },
                new SaveObservation
                {
                    FileName = "CorruptSaveNameCanary.rws",
                    Status = ObservationStatus.Error,
                    ErrorReason = $"XmlException: Unexpected EOF in {winPathCanary}",
                },
                new SaveObservation
                {
                    FileName = "NoIdentityCanary.rws",
                    Status = ObservationStatus.Unidentifiable,
                },
            ],
            MetricObservations =
            [
                new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint,
                    Key = checkpointKeyCanary,
                    PayloadHash = payloadHashCanary,
                    ArchivedMetrics = true,
                    Conflicted = true,
                    Identity = new ColonyIdentity { ModCampaignId = campaignIdCanary },
                },
                new MetricObservation
                {
                    Source = MetricObservationSource.SaveSnapshot,
                    Key = "snap-secret-999",
                    Quarantined = quarantinedDoc.RootElement.Clone(),
                },
            ],
            ModMetricDescriptors =
            [
                new ModMetricDescriptor
                {
                    ProviderId = thirdPartyModCanary,
                    OwnerPackageId = thirdPartyModCanary,
                    FamilyId = "secret",
                    MetricName = "metric",
                    LabelKey = "SecretMetricLabelCanary",
                },
            ],
        };

        var loadResult = new ProfileLoadResult
        {
            Profile = profile,
            Source = ProfileLoadSource.Backup1,
            CorruptFileRenamedTo = corruptFileCanary,
            ReadOnly = false,
        };

        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = 1,
                Colonies =
                [
                    new ColonyStats
                    {
                        Key = colonyKeyCanary,
                        FactionName = factionCanary,
                        WorldName = worldCanary,
                        Scenario = scenarioCanary,
                        Storyteller = storytellerCanary,
                        HasObservedPoint = true,
                        EndingCounts = new Dictionary<EndingType, int> { [EndingType.Ship] = 2 },
                        GameVersions = ["1.7.5011"],
                    },
                ],
                EndingsByType = new Dictionary<EndingType, int> { [EndingType.Ship] = 2 },
            },
            Numeric = new NumericView
            {
                Colonies = [],
                Overall =
                [
                    new NumericRow
                    {
                        Definition = MetricRegistry.Find(MetricKeys.QuestSuccess)!,
                        Value = new NumericValue { Number = 5, Coverage = MetricCoverage.Complete },
                    },
                    new NumericRow
                    {
                        Definition = MetricRegistry.Find(MetricKeys.WealthTotalMean)!,
                        Value = new NumericValue { Number = 1000, Coverage = MetricCoverage.Partial },
                    },
                ],
            },
        };

        var saveException = new IOException($"Access to path '{winPathCanary}' denied; {secretTokenCanary}");
        var archiveException = new UnauthorizedAccessException($"Cannot open {linuxPathCanary}");

        var input = DiagnosticSummaryBuilder.Capture(
            profile,
            loadResult,
            view,
            new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc),
            gameVersion: "1.7.5011 rev24",
            modVersion: secretTokenCanary, // poisoned version string must be rejected to "unknown"!
            programState: DiagnosticProgramState.Playing,
            activePackageIds: ["ludeon.rimworld", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey", thirdPartyModCanary],
            adapters: new DiagnosticAdapterCounts
            {
                CoreUnitsTotal = 18,
                CoreUnitsRecording = 16,
                CoreUnitsFailed = 1,
                CoreUnitsDlcMissing = 1,
                OptionalUnitsTotal = 95,
                OptionalUnitsRecording = 4,
                OptionalUnitsTargetModMissing = 90,
                OptionalUnitsFailed = 1,
                ExternalSdkProvidersRegistered = 2,
                ActiveThirdPartyModsCount = 14,
            },
            lastSaveError: saveException,
            lastArchiveError: archiveException);

        var written = DiagnosticSummaryBuilder.WriteFile(input, root.Path);
        var summary = File.ReadAllText(written.FilePath, Encoding.UTF8);

        string[] forbiddenCanaries =
        [
            "AliceSecret", "bob_secret", "SecretSave", "PrivateSave", "CorruptSaveNameCanary", "NoIdentityCanary",
            "profile.corrupt", factionCanary, worldCanary, scenarioCanary, storytellerCanary,
            campaignIdCanary, colonyKeyCanary, fingerprintCanary, payloadHashCanary, checkpointKeyCanary,
            thirdPartyModCanary, "SecretMetricLabelCanary", "R2_SECRET_ACCESS_KEY", "super_secret_token",
            @"C:\", "/home/", ".rws", ".json",
        ];
        foreach (var canary in forbiddenCanaries)
            Assert.DoesNotContain(canary, summary, StringComparison.OrdinalIgnoreCase);

        // Verify expected non-PII diagnostic facts ARE present accurately.
        Assert.Contains("game_version: 1.7.5011 rev24", summary);
        Assert.Contains("mod_version: unknown", summary);
        Assert.Contains("program_state: Playing", summary);
        Assert.Contains("official_dlcs: Biotech, Anomaly, Odyssey", summary);
        Assert.Contains("active_third_party_mods: 14", summary);
        Assert.Contains("load_source: Backup1", summary);
        Assert.Contains("corrupt_primary_recovered: true", summary);
        Assert.Contains("last_save_error_type: IOException", summary);
        Assert.Contains("last_archive_error_type: UnauthorizedAccessException", summary);
        Assert.Contains("saves_ok: 1", summary);
        Assert.Contains("saves_failed: 2", summary);
        Assert.Contains("saves_unidentifiable: 1", summary);
        Assert.Contains("import_problem_ChangedDuringRead: 1", summary);
        Assert.Contains("import_problem_Damaged: 1", summary);
        Assert.Contains("import_problem_MissingIdentity: 1", summary);
        Assert.Contains("checkpoints_total: 1", summary);
        Assert.Contains("checkpoints_archived: 1", summary);
        Assert.Contains("checkpoints_conflicted: 1", summary);
        Assert.Contains("quarantined_observations: 1", summary);
        Assert.Contains("external_sdk_providers_registered: 2", summary);
        Assert.Contains("complete: 1", summary);
        Assert.Contains("partial: 1", summary);
    }

    private sealed class CountingMemoryArchive : ICheckpointArchive
    {
        private readonly Dictionary<string, List<string>> _linesByCampaign = new(StringComparer.Ordinal);
        public int SingleReadCalls { get; private set; }
        public int ReadManyCalls { get; private set; }

        public void ResetReadCounts()
        {
            SingleReadCalls = 0;
            ReadManyCalls = 0;
        }

        public void Append(string campaignId, IReadOnlyList<string> jsonLines)
        {
            if (!_linesByCampaign.TryGetValue(campaignId, out var list))
                _linesByCampaign[campaignId] = list = [];
            list.AddRange(jsonLines);
        }

        public IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey)
        {
            SingleReadCalls++;
            return Parse(campaignId).Where(o => o.Key == checkpointKey).ToArray();
        }

        public IReadOnlyList<MetricObservation> ReadMany(string campaignId, IReadOnlySet<string> checkpointKeys)
        {
            ReadManyCalls++;
            return Parse(campaignId).Where(o => checkpointKeys.Contains(o.Key)).ToArray();
        }

        private IEnumerable<MetricObservation> Parse(string campaignId)
        {
            if (!_linesByCampaign.TryGetValue(campaignId, out var lines))
                yield break;
            foreach (var line in lines)
            {
                if (JsonSerializer.Deserialize<MetricObservation>(line, ProfileJson.Options) is { } obs)
                    yield return obs;
            }
        }
    }
}
