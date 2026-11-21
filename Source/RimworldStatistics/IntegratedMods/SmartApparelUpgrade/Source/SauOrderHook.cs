using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.SmartApparelAddon;

/// <summary>
/// Version/source-audited adapter for Smart Apparel Upgrade 1.0.1:
/// ApparelScanner.OrderIdleUpgrades adds SwapStamp ONLY after the vanilla
/// Wear job actually becomes the pawn's current job. This records that job
/// issuance, never claims that the apparel was ultimately worn.
/// </summary>
internal static class SauOrderHook
{
    private const string Unit = "mod.pengin0503.smartapparelupgrade.orders";

    internal sealed record Binding(
        MethodInfo Target,
        Func<object, IList?> GetStamps,
        Func<object, int> GetIssuedTick,
        Func<object, string?> GetPawnId,
        Func<object, string?> GetApparelId);

    private static volatile Binding? active;

    public static bool TryResolve(out Binding binding)
    {
        binding = null!;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "SmartApparelUpgrade", StringComparison.Ordinal));
            var scanner = assembly?.GetType("Pengin.SmartApparelUpgrade.ApparelScanner", throwOnError: false);
            var stamp = assembly?.GetType("Pengin.SmartApparelUpgrade.SwapStamp", throwOnError: false);
            if (scanner is null || stamp is null || !typeof(MapComponent).IsAssignableFrom(scanner))
                return false;
            var target = scanner.GetMethod("OrderIdleUpgrades", flags,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            var stamps = scanner.GetField("stamps", flags);
            var issued = stamp.GetField("LastIssued", flags);
            var pawn = stamp.GetField("PawnId", flags);
            var apparel = stamp.GetField("TargetId", flags);
            if (target is null || target.ReturnType != typeof(void) || target.IsStatic ||
                stamps is null || !stamps.FieldType.IsGenericType ||
                stamps.FieldType.GetGenericTypeDefinition() != typeof(List<>) ||
                stamps.FieldType.GenericTypeArguments[0] != stamp ||
                issued?.FieldType != typeof(int) || pawn?.FieldType != typeof(string) ||
                apparel?.FieldType != typeof(string))
                return false;
            binding = new Binding(
                target,
                OptionalTransitionCallSite.CompileFieldGetter<IList?>(stamps),
                OptionalTransitionCallSite.CompileFieldGetter<int>(issued),
                OptionalTransitionCallSite.CompileFieldGetter<string?>(pawn),
                OptionalTransitionCallSite.CompileFieldGetter<string?>(apparel));
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Smart Apparel Upgrade binding failed ({e.GetType().Name}): {e.Message}",
                "RWS.sau.orders.bind".GetHashCode());
            return false;
        }
    }

    public static bool TryInstall(Binding binding)
    {
        try
        {
            var patcher = new ScopedHarmonyPatcher(new Harmony("pengin0503.rws.smartappareladdon"));
            patcher.Patch(binding.Target, typeof(SauOrderHook), postfix: nameof(AfterOrder));
            active = binding;
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Smart Apparel Upgrade patch failed ({e.GetType().Name}): {e.Message}",
                "RWS.sau.orders.install".GetHashCode());
            return false;
        }
    }

    private static void AfterOrder(object __instance)
    {
        var bound = active;
        if (bound is null || !SauHostClient.IsConnected || Find.TickManager is null)
            return;
        try
        {
            var now = Find.TickManager.TicksGame;
            if (bound.GetStamps(__instance) is not { } stamps)
                return;
            for (var i = 0; i < stamps.Count; i++)
            {
                var item = stamps[i];
                if (item is null || bound.GetIssuedTick(item) != now)
                    continue;
                var id = SauOrderEventId.Create(
                    bound.GetPawnId(item),
                    bound.GetApparelId(item), now);
                if (id is not null)
                    SauHostClient.OnIssuedOrder(id);
            }
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
