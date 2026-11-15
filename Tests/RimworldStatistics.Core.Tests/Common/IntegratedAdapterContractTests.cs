using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Opt-in: the common registration contract applied to every optional MOD statistic the built game DLL declares, so each
/// new integration is checked without a test of its own. It needs the real game assemblies, which CI does not have:
/// run <c>python3 tools/fetch_dev_inputs.py refs &lt;folder&gt;</c>, set RWS_GAME_MANAGED_DIR to &lt;folder&gt;/game and
/// RWS_HARMONY_DLL to &lt;folder&gt;/harmony/0Harmony.dll, and build the game DLL (RWS_STATISTICS_DLL overrides its path,
/// default <c>1.7/Assemblies/RimworldStatistics.dll</c>).
/// <para>
/// Only the declarations and their value builders run; no hook is installed. Only Assembly-CSharp, UnityStubs, Waylith,
/// Harmony and this MOD's own assemblies are loaded: the engine, Steam and logging assemblies are refused even when the
/// folder holds them, so game start-up code (settings, save folders) cannot run. A module whose declaration needs the
/// running game is listed as not checked, not passed.
/// </para>
/// </summary>
[Trait("Category", "Contract")]
[Trait("Category", "OptionalGameDll")]
public sealed class IntegratedAdapterContractTests
{
    private static readonly string[] Allowed =
        ["Assembly-CSharp", "UnityStubs", "Waylith", "0Harmony", "RimworldStatistics", "RimworldStatistics.Api", "RimworldStatistics.Core"];

    private sealed record Declared(string Module, ModMetricDescriptor Descriptor, LiveFamily? Family, Delegate? Install, Delegate? Snapshot);

    [Fact]
    public void EveryDeclaredOptionalStatisticFollowsTheRegistrationContract()
    {
        var (declared, notChecked, unregistered) = Declarations();
        var problems = new List<string>();
        void Check(bool ok, Declared x, string problem)
        {
            if (!ok)
                problems.Add($"{x.Descriptor.StableKey} ({x.Module}): {problem}");
        }

        problems.AddRange(unregistered.Select(m => m + ": declares statistics but is never registered"));
        var registry = new ModMetricProviderRegistry();
        var builtInFamilies = LiveFamilies.All.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var x in declared)
        {
            var d = x.Descriptor;
            Check(d.IsValid(), x, "invalid descriptor");
            var registered = registry.Register(d);
            Check(registered.Accepted, x, "rejected: " + registered.Failure);
            Check(!builtInFamilies.Contains(d.AcquisitionId), x, "its unit takes a built-in family's name");
            Check(ModProviderDiscovery.Resolve(d, []).State == ModProviderAvailability.MissingPackage, x, "available without its MOD");
            if (d.Source == MetricObservationSource.Checkpoint)
            {
                Check(x.Family is not null && x.Install is not null, x, "an event statistic without a family or a hook");
                Check(x.Family is null || x.Family.Name == d.AcquisitionId, x, "family " + x.Family?.Name + " is not its unit");
                Check(x.Family is null || x.Family.Revision == d.Revision, x, "family revision differs from the descriptor's");
                Check(x.Family is null || x.Family.RequiredDlc == d.RequiredDlc, x, "family DLC differs from the descriptor's");
            }
            else
            {
                Check(x.Snapshot is not null, x, "a snapshot statistic without a reader");
            }
        }

        // The game installs one family and hook per unit (the first declared) and builds every output of the unit from it.
        foreach (var unit in declared.Where(x => x.Family is not null).GroupBy(x => x.Descriptor.AcquisitionId))
        {
            var family = unit.First().Family!;
            var keys = unit.ToDictionary(x => x.Descriptor.StableKey, StringComparer.Ordinal);
            var built = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
            try
            {
                family.Build(new FamilyOutputContext(new FamilyState { Revision = family.Revision, SinceTick = 0, ThroughTick = 100 }, 100, built));
            }
            catch (Exception e)
            {
                problems.Add(unit.Key + ": building an empty state throws " + e.GetType().Name);
                continue;
            }
            problems.AddRange(built.Keys.Where(k => !keys.ContainsKey(k)).Select(k => unit.Key + ": writes " + k + ", which no descriptor shows"));
            foreach (var (key, x) in keys)
            {
                Check(built.ContainsKey(key), x, "never written by its unit's family");
                Check(!built.TryGetValue(key, out var entry) || entry.Revision == x.Descriptor.Revision, x,
                    "written with another revision, so its values are never shown");
            }
        }

        TestContext.Current.SendDiagnosticMessage($"{declared.Count} optional statistics in "
            + $"{declared.Select(x => x.Descriptor.AcquisitionId).Distinct().Count()} units checked; "
            + (notChecked.Count == 0 ? "every module was checked" : "not checked (needs the running game): " + string.Join(", ", notChecked)));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static (IReadOnlyList<Declared> Declared, IReadOnlyList<string> NotChecked, IReadOnlyList<string> Unregistered) Declarations()
    {
        var game = Environment.GetEnvironmentVariable("RWS_GAME_MANAGED_DIR");
        var harmony = Environment.GetEnvironmentVariable("RWS_HARMONY_DLL");
        if (string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(harmony))
            Assert.Skip("Set RWS_GAME_MANAGED_DIR and RWS_HARMONY_DLL locally; game assemblies are never stored in CI.");
        var statistics = Environment.GetEnvironmentVariable("RWS_STATISTICS_DLL") is { Length: > 0 } path ? path
            : Path.Combine(RepositoryRoot(), "1.7", "Assemblies", "RimworldStatistics.dll");
        Assert.True(File.Exists(statistics), "Build the game DLL first: " + statistics);

        var folders = new[] { game!, Path.GetDirectoryName(Path.GetFullPath(harmony!))!, Path.GetDirectoryName(Path.GetFullPath(statistics))! };
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            if (!Allowed.Contains(name.Name, StringComparer.Ordinal))
                return null;   // refused: game start-up code cannot run without the engine
            return folders.Select(f => Path.Combine(f, name.Name + ".dll")).Where(File.Exists)
                .Select(context.LoadFromAssemblyPath).FirstOrDefault();
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            // The default context, so the game DLL shares this process's Core types (descriptors, families).
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(statistics));
            var registration = assembly.GetType("RimworldStatistics.Metrics.ModMetricAdapterRegistration", throwOnError: true)!;
            bool Declares(MethodInfo m) => m.IsStatic && m.GetParameters().Length == 0
                && (m.ReturnType == registration || typeof(IEnumerable<>).MakeGenericType(registration).IsAssignableFrom(m.ReturnType));

            var root = assembly.GetType("RimworldStatistics.Metrics.IntegratedTransitionAdapters", throwOnError: true)!
                .GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var modules = Calls(root).OfType<MethodInfo>().Where(Declares)
                .Append(assembly.GetType("RimworldStatistics.VehicleFramework.VehicleOwnershipSnapshot", throwOnError: true)!
                    .GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!)
                .Distinct().ToArray();

            var declared = new List<Declared>();
            var notChecked = new List<string>();
            foreach (var module in modules)
            {
                var name = module.DeclaringType!.Name + "." + module.Name;
                try
                {
                    var result = module.Invoke(null, null);
                    var items = result is System.Collections.IEnumerable many ? many.Cast<object>() : [result!];
                    foreach (var item in items)
                        declared.Add(Read(name, item));
                }
                catch (Exception e)
                {
                    notChecked.Add($"{name} ({(e is TargetInvocationException { InnerException: { } inner } ? inner : e).GetType().Name}"
                        + $", after {declared.Count(x => x.Module == name)} statistics)");
                }
            }

            // A module whose statistics nobody registers never records anything: its Build is called from nowhere.
            var types = assembly.GetTypes();
            var called = types.SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(t.GetConstructors(BindingFlags.Static
                    | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)))
                .SelectMany(Calls).ToHashSet();
            var unregistered = types.Where(t => t.Namespace?.StartsWith("RimworldStatistics.", StringComparison.Ordinal) == true)
                .Select(t => t.GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes))
                .OfType<MethodInfo>().Where(m => Declares(m) && m != root && !called.Contains(m))
                .Select(m => m.DeclaringType!.FullName!).Order(StringComparer.Ordinal).ToArray();
            return (declared, notChecked, unregistered);
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
        }
    }

    private static Declared Read(string module, object registration)
    {
        var type = registration.GetType();
        T? Get<T>(string name) where T : class => (T?)type.GetProperty(name)!.GetValue(registration);
        return new Declared(module, Get<ModMetricDescriptor>("Descriptor")!, Get<LiveFamily>("EventFamily"),
            Get<Delegate>("InstallEventHook"), Get<Delegate>("CaptureSnapshot"));
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>The methods a method body calls or takes the address of (a delegate), read from its IL.</summary>
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception) { yield break; }
        if (il is null)
            yield break;
        for (var i = 0; i < il.Length;)
        {
            var value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            if (!OpCodesByValue.TryGetValue(value, out var code))
                yield break;   // not IL we can follow: stop rather than misread
            if (code.OperandType == OperandType.InlineMethod)
            {
                MethodBase? target = null;
                try
                {
                    target = method.Module.ResolveMethod(BitConverter.ToInt32(il, i),
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (Exception) { /* a member of an assembly that is not loaded */ }
                if (target is not null)
                    yield return target;
            }
            i += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir.FullName, "Source")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
