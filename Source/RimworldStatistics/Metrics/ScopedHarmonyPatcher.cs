using System.Reflection;
using HarmonyLib;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Applies Harmony patches for one optional acquisition unit and rolls back only the specific patch methods it added if
/// any target fails, avoiding <see cref="HarmonyPatchType.All"/> unpatching on shared game/MOD methods.
/// </summary>
internal sealed class ScopedHarmonyPatcher
{
    private readonly Harmony _harmony;
    private readonly List<(MethodBase Target, MethodInfo Patch)> _applied = new();

    public ScopedHarmonyPatcher(Harmony harmony) => _harmony = harmony;

    public void Patch(
        MethodBase target,
        Type observer,
        string? prefix = null,
        string? postfix = null,
        string? transpiler = null,
        string? finalizer = null)
    {
        try
        {
            Patch(
                target,
                prefix: Resolve(observer, prefix),
                postfix: Resolve(observer, postfix),
                transpiler: Resolve(observer, transpiler),
                finalizer: Resolve(observer, finalizer));
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public void Patch(
        MethodBase target,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? transpiler = null,
        HarmonyMethod? finalizer = null)
    {
        try
        {
            _harmony.Patch(target, prefix: prefix, postfix: postfix, transpiler: transpiler, finalizer: finalizer);
            Track(target, prefix);
            Track(target, postfix);
            Track(target, transpiler);
            Track(target, finalizer);
        }
        catch
        {
            UntrackAttempted(target, prefix);
            UntrackAttempted(target, postfix);
            UntrackAttempted(target, transpiler);
            UntrackAttempted(target, finalizer);
            Rollback();
            throw;
        }
    }

    private static HarmonyMethod? Resolve(Type observer, string? methodName)
    {
        if (methodName is null)
            return null;
        var method = AccessTools.DeclaredMethod(observer, methodName)
            ?? throw new InvalidOperationException($"Missing patch method {observer.Name}.{methodName}");
        return new HarmonyMethod(method);
    }

    public void Track(MethodBase target, MethodInfo? patch)
    {
        if (patch is not null)
            _applied.Add((target, patch));
    }

    public void Rollback()
    {
        for (var i = _applied.Count - 1; i >= 0; i--)
        {
            var (target, patch) = _applied[i];
            try { _harmony.Unpatch(target, patch); }
            catch (Exception) { /* best-effort rollback */ }
        }
        _applied.Clear();
    }

    private void Track(MethodBase target, HarmonyMethod? method)
    {
        if (method?.method is { } patch)
            _applied.Add((target, patch));
    }

    private void UntrackAttempted(MethodBase target, HarmonyMethod? method)
    {
        if (method?.method is not { } patch)
            return;
        try { _harmony.Unpatch(target, patch); }
        catch (Exception) { /* best-effort cleanup */ }
    }
}
