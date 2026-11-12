using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Api;
using RimworldStatistics.Metrics;
using RimWorld;
using Verse;

namespace RimworldStatistics.VanillaTradingExpandedAddon;

/// <summary>
/// Optional VTE 1.6 bank bridge (loaded for RimWorld 1.7 by VTE loadFolders.xml).
/// The source uses actual balance mutations: UI intent, cancelled dialogs and
/// ordinary TradeDeal operations never enter these hooks.
/// </summary>
internal static class VteBankStatistics
{
    private const string Unit = "mod.vanillaexpanded.vanillatradingexpanded.bank";
    private const string Credits = Unit + ".credited_silver";
    private const string Debits = Unit + ".debited_silver";
    private const string LoanCount = Unit + ".loans_issued";
    private const string LoanPrincipal = Unit + ".loan_principal";
    private static PropertyInfo? balanceProperty;
    private static FieldInfo? factionField;
    private static FieldInfo? loansField;

    private sealed record BalanceBefore(string BankId, int Tick, int Balance);
    private sealed record LoanBefore(string BankId, int Tick, int Loans, int Principal);

    internal static void TryRegister()
    {
        if (!StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
            return;
        // Only the 1.6 source signature selected by the 1.7 loadFolders rule is supported.
        var bank = AccessTools.TypeByName("VanillaTradingExpanded.Bank");
        var loanOption = AccessTools.TypeByName("VanillaTradingExpanded.LoanOption");
        if (bank is null || loanOption is null)
            return;
        var deposit = AccessTools.DeclaredMethod(bank, "DepositSilver", [typeof(List<Thing>), typeof(int)]);
        var withdraw = AccessTools.DeclaredMethod(bank, "WithdrawSilver", [typeof(Pawn), typeof(int)]);
        var loan = AccessTools.DeclaredMethod(bank, "TakeLoan",
            [typeof(Pawn), typeof(int), typeof(int), typeof(int), loanOption]);
        var balance = AccessTools.Property(bank, "DepositAmount");
        var faction = AccessTools.Field(bank, "parentFaction");
        var loans = AccessTools.Field(bank, "loans");
        if (deposit is null || withdraw is null || loan is null ||
            deposit.ReturnType != typeof(void) || withdraw.ReturnType != typeof(void) ||
            loan.ReturnType != typeof(void) || balance?.PropertyType != typeof(int) ||
            faction?.FieldType != typeof(Faction) || loans is null ||
            !typeof(IList).IsAssignableFrom(loans.FieldType))
            return;

        balanceProperty = balance;
        factionField = faction;
        loansField = loans;
        if (host.Register(Def("credited_silver", ApiEventMode.Sum, "Silver",
                "RWS_VTE_BankCredits", "RWS_VTE_BankCreditsDesc")) != ApiRegistrationResult.Accepted ||
            host.Register(Def("debited_silver", ApiEventMode.Sum, "Silver",
                "RWS_VTE_BankDebits", "RWS_VTE_BankDebitsDesc")) != ApiRegistrationResult.Accepted ||
            host.Register(Def("loans_issued", ApiEventMode.Count, "Count",
                "RWS_VTE_LoansIssued", "RWS_VTE_LoansIssuedDesc")) != ApiRegistrationResult.Accepted ||
            host.Register(Def("loan_principal", ApiEventMode.Sum, "Silver",
                "RWS_VTE_LoanPrincipal", "RWS_VTE_LoanPrincipalDesc")) != ApiRegistrationResult.Accepted)
            return;

        ExternalSdkHost.Instance.IntegratedHooks.Defer(Unit, () =>
        {
            var harmony = new Harmony("pengin0503.rws.vte.bank");
            try
            {
                harmony.Patch(deposit,
                    prefix: new HarmonyMethod(typeof(VteBankStatistics), nameof(BeforeBalance)),
                    postfix: new HarmonyMethod(typeof(VteBankStatistics), nameof(AfterDeposit)));
                harmony.Patch(withdraw,
                    prefix: new HarmonyMethod(typeof(VteBankStatistics), nameof(BeforeBalance)),
                    postfix: new HarmonyMethod(typeof(VteBankStatistics), nameof(AfterWithdraw)));
                harmony.Patch(loan,
                    prefix: new HarmonyMethod(typeof(VteBankStatistics), nameof(BeforeLoan)),
                    postfix: new HarmonyMethod(typeof(VteBankStatistics), nameof(AfterLoan)));
                return true;
            }
            catch
            {
                harmony.Unpatch(deposit, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(withdraw, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(loan, HarmonyPatchType.All, harmony.Id);
                return false;
            }
        });
    }

    private static ApiMetricDefinition Def(string name, ApiEventMode mode, string unit, string label, string description) => new()
    {
        ProviderId = "rws.vte.bank",
        OwnerPackageId = "VanillaExpanded.VanillaTradingExpanded",
        FamilyId = "bank",
        MetricName = name,
        ProviderVersion = "1.0.0",
        Revision = 1,
        Kind = ApiMetricKind.Event,
        EventMode = mode,
        Category = "Economy",
        Unit = unit,
        Overall = "Sum",
        Selection = "Representative",
        LabelKey = label,
        DescriptionKey = description,
    };

    private static bool CanRecord() =>
        MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing &&
        Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static bool TryIdentity(object instance, out string bankId)
    {
        bankId = "";
        if (factionField?.GetValue(instance) is not Faction faction)
            return false;
        bankId = faction.GetUniqueLoadID();
        return !string.IsNullOrEmpty(bankId);
    }

    private static int Balance(object instance) => (int)(balanceProperty?.GetValue(instance) ?? 0);
    private static int LoanSize(object instance) => (loansField?.GetValue(instance) as IList)?.Count ?? -1;

    private static void BeforeBalance(object __instance, out BalanceBefore? __state)
    {
        __state = null;
        if (!CanRecord())
            return;
        try
        {
            if (TryIdentity(__instance, out var id))
                __state = new BalanceBefore(id, Find.TickManager.TicksGame, Balance(__instance));
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void AfterDeposit(object __instance, bool __runOriginal, BalanceBefore? __state)
        => Settle(__instance, __runOriginal, __state, true, Credits, "deposit");

    private static void AfterWithdraw(object __instance, bool __runOriginal, BalanceBefore? __state)
        => Settle(__instance, __runOriginal, __state, false, Debits, "withdraw");

    private static void Settle(object instance, bool ran, BalanceBefore? previous, bool credit,
        string metricId, string action)
    {
        if (!ran || previous is null || !CanRecord())
            return;
        try
        {
            var after = Balance(instance);
            var quantity = VteTradeMath.SettledAmount(previous.Balance, after, credit);
            if (quantity > 0 && StatisticsApi.TryGetHost(1, 1, out var host))
                _ = host?.RecordEvent(metricId,
                    VteTradeMath.EventId(previous.BankId, action, previous.Tick, previous.Balance, after), quantity);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void BeforeLoan(object __instance, int __1, out LoanBefore? __state)
    {
        __state = null;
        if (!CanRecord() || __1 <= 0)
            return;
        try
        {
            var count = LoanSize(__instance);
            if (count >= 0 && TryIdentity(__instance, out var id))
                __state = new LoanBefore(id, Find.TickManager.TicksGame, count, __1);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void AfterLoan(object __instance, bool __runOriginal, LoanBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanRecord())
            return;
        try
        {
            var count = LoanSize(__instance);
            if (count != __state.Loans + 1 || !StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            var eventId = VteTradeMath.EventId(__state.BankId, "loan", __state.Tick, __state.Loans, count);
            _ = host.RecordEvent(LoanCount, eventId);
            _ = host.RecordEvent(LoanPrincipal, eventId, __state.Principal);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }
}
