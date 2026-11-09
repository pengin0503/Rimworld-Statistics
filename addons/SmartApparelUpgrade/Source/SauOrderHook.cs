using System.Collections;
using System.Reflection;
using HarmonyLib;
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
    internal sealed record Binding(MethodInfo Target, FieldInfo Stamps, FieldInfo IssuedTick,
        FieldInfo PawnId, FieldInfo ApparelId);

    private static Binding? active;

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
            binding = new Binding(target, stamps, issued!, pawn!, apparel!);
            return true;
        }
        catch (Exception)
        {
            // A broken, disabled or changed optional MOD must never prevent
            // other MODs from starting, nor publish an unsupported collector.
            return false;
        }
    }

    public static bool TryInstall(Binding binding)
    {
        try
        {
            var method = typeof(SauOrderHook).GetMethod(nameof(AfterOrder), BindingFlags.Static | BindingFlags.NonPublic);
            if (method is null)
                return false;
            new Harmony("pengin0503.rws.smartappareladdon").Patch(binding.Target,
                postfix: new HarmonyMethod(method));
            active = binding;
            return true;
        }
        catch (Exception)
        {
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
            if (bound.Stamps.GetValue(__instance) is not IEnumerable stamps)
                return;
            foreach (var item in stamps)
            {
                if (item is null || (int?)bound.IssuedTick.GetValue(item) != now)
                    continue;
                var id = SauOrderEventId.Create(
                    bound.PawnId.GetValue(item) as string,
                    bound.ApparelId.GetValue(item) as string, now);
                if (id is not null)
                    SauHostClient.OnIssuedOrder(id);
            }
        }
        catch (Exception)
        {
            // Another MOD may have changed scanner state at runtime. Do not
            // interrupt the original auto-wear job or interfere with saving.
        }
    }
}
