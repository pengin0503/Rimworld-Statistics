using UnityEngine;
using Verse;

namespace StatisticsSdkSample;

/// <summary>Independent development MOD; only the optional SDK bridge references the host API.</summary>
public sealed class SampleMod : Mod
{
    public SampleMod(ModContentPack content) : base(content)
    {
        SampleSdkClient.TryAttach();
    }

    public override string SettingsCategory() => "Statistics SDK Sample (Development)";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var game = Current.Game;
        var component = game?.GetComponent<SampleGameComponent>();
        if (component is null)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "Start or load a colony to exercise the SDK; no event is emitted at the title screen.");
            return;
        }
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
            "Confirmed sample actions in this colony: " + component.CompletedActions);
        if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 40f, Math.Min(inRect.width, 350f), 32f),
                "Simulate one confirmed sample action"))
        {
            var savedToHost = component.SimulateConfirmedAction();
            Log.Message("[StatisticsSdkSample] Action completed; SDK event " +
                (savedToHost ? "recorded." : "not recorded (absent/disabled/duplicate)."));
        }
        if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 80f, Math.Min(inRect.width, 350f), 32f),
                "Arm snapshot exception for the next save"))
        {
            SampleSdkClient.ArmSnapshotExceptionOnce();
            Log.Message("[StatisticsSdkSample] The next SDK snapshot will deliberately throw. Other families must continue.");
        }
        Widgets.Label(new Rect(inRect.x, inRect.y + 130f, inRect.width, 70f),
            "Enable mod.pengin0503.rws.sdksample.events and .state in the host's experimental settings, " +
            "then restart the game. Save and reload to verify stable counters and partial coverage.");
    }
}
