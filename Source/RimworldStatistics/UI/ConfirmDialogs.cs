using Verse;

namespace RimworldStatistics.UI;

/// <summary>Shared confirmation dialog with a consistent keyed title and destructive styling.</summary>
internal static class ConfirmDialogs
{
    public static void Confirm(string titleKey, string body, Action onConfirm, bool danger = false)
    {
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(body, onConfirm, danger, (string)titleKey.Translate()));
    }
}
