using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Edit presentation-only saved colony groups, not the statistics profile.</summary>
public sealed class Dialog_ColonyGroups : Window
{
    private readonly ViewPreferences preferences;
    private readonly IReadOnlyList<Colony> colonies;
    private readonly IReadOnlyDictionary<string, string> names;
    private readonly Action onChange;
    private Vector2 groupScroll;
    private Vector2 memberScroll;
    private string newGroupName = "";
    private string? selectedId;
    private bool nameDirty;

    public Dialog_ColonyGroups(ViewPreferences preferences, StatsView? view, Action onChange)
    {
        this.preferences = preferences;
        colonies = view?.Colonies ?? Array.Empty<Colony>();
        names = view?.Stats.Colonies.ToDictionary(s => s.Key, Dialog_RimworldStatistics.ColonyName)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.onChange = onChange;
        selectedId = preferences.Groups.FirstOrDefault()?.Id;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new(820f, 640f);

    public override void PostClose()
    {
        // A completed rename is persisted once, not after every TextField keystroke.
        if (nameDirty)
            onChange();
        base.PostClose();
    }

    private void SaveStructuralChange()
    {
        // A structural operation persists an earlier rename as part of the same write.
        nameDirty = false;
        onChange();
    }

    public override void DoWindowContents(Rect rect)
    {
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), "RWS_Groups_Title".Translate());
        var input = new Rect(rect.x, rect.y + 34f, Math.Max(50f, rect.width - 160f), 30f);
        newGroupName = Widgets.TextField(input, newGroupName);
        if (Widgets.ButtonText(new Rect(input.xMax + 8f, input.y, 140f, 30f), "RWS_Groups_Add".Translate())
            && !string.IsNullOrWhiteSpace(newGroupName) && preferences.Groups.Count < 64)
        {
            var group = new ColonyGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = newGroupName.Trim()[..Math.Min(64, newGroupName.Trim().Length)],
            };
            preferences.Groups.Add(group);
            selectedId = group.Id;
            newGroupName = "";
            SaveStructuralChange();
        }

        var top = rect.y + 72f;
        var leftWidth = Math.Min(270f, Math.Max(130f, rect.width * 0.36f));
        var left = new Rect(rect.x, top, leftWidth, rect.yMax - top);
        var right = new Rect(left.xMax + 12f, top, Math.Max(0f, rect.width - leftWidth - 12f), left.height);
        Widgets.DrawMenuSection(left);
        Widgets.DrawMenuSection(right);
        var groupsRect = left.ContractedBy(7f);
        var groupsView = new Rect(0f, 0f, groupsRect.width - 16f, preferences.Groups.Count * 34f + 8f);
        Widgets.BeginScrollView(groupsRect, ref groupScroll, groupsView);
        for (var i = 0; i < preferences.Groups.Count; i++)
        {
            var group = preferences.Groups[i];
            var y = i * 34f;
            var choose = new Rect(0, y, groupsView.width - 34f, 30f);
            if (Widgets.ButtonText(choose, group.Name.Truncate(choose.width)))
                selectedId = group.Id;
            if (Widgets.ButtonText(new Rect(choose.xMax + 2f, y, 30f, 30f), "×"))
            {
                preferences.Groups.RemoveAt(i);
                if (selectedId == group.Id)
                    selectedId = preferences.Groups.FirstOrDefault()?.Id;
                SaveStructuralChange();
                break;
            }
        }
        Widgets.EndScrollView();

        var selected = preferences.Groups.FirstOrDefault(g => g.Id == selectedId);
        if (selected is null)
        {
            Widgets.Label(right.ContractedBy(12f), "RWS_Groups_None".Translate());
            return;
        }
        var margin = 10f;
        var width = right.width - margin * 2;
        var nameRect = new Rect(right.x + margin, right.y + margin, width, 30f);
        var renamed = Widgets.TextField(nameRect, selected.Name);
        if (renamed != selected.Name)
        {
            selected.Name = renamed.Length > 64 ? renamed[..64] : renamed;
            nameDirty = true;
        }
        var filtered = new HashSet<string>(selected.ColonyKeys, StringComparer.Ordinal);
        var unavailable = selected.ColonyKeys.Count(k => colonies.All(c => c.Key != k && !c.AliasKeys.Contains(k)));
        var notice = new Rect(nameRect.x, nameRect.yMax + 4f, width, 32f);
        Widgets.Label(notice, "RWS_Groups_Members".Translate(selected.ColonyKeys.Count, unavailable));
        if (unavailable > 0 && Widgets.ButtonText(new Rect(nameRect.x, notice.yMax, width, 26f), "RWS_Groups_RemoveMissing".Translate()))
        {
            selected.ColonyKeys.RemoveAll(k => colonies.All(c => c.Key != k && !c.AliasKeys.Contains(k)));
            SaveStructuralChange();
        }

        var listRect = new Rect(nameRect.x, notice.yMax + (unavailable > 0 ? 32f : 3f),
            width, Math.Max(0f, right.yMax - notice.yMax - (unavailable > 0 ? 40f : 11f) - margin));
        var visible = colonies.OrderBy(c => names.GetValueOrDefault(c.Key) ?? c.Key, StringComparer.CurrentCulture).ToList();
        var listView = new Rect(0f, 0f, Math.Max(0f, listRect.width - 16f), visible.Count * 30f + 8f);
        Widgets.BeginScrollView(listRect, ref memberScroll, listView);
        for (var i = 0; i < visible.Count; i++)
        {
            var colony = visible[i];
            var was = filtered.Contains(colony.Key) || colony.AliasKeys.Any(filtered.Contains);
            var included = was;
            var label = (names.GetValueOrDefault(colony.Key) ?? colony.Key)
                + (colony.Excluded ? " (" + "RWS_Groups_Excluded".Translate() + ")" : "");
            Widgets.CheckboxLabeled(new Rect(0f, i * 30f, listView.width, 28f), label.Truncate(listView.width - 30f), ref included);
            if (included == was)
                continue;
            // After a manual merge, replace any stale aliases with the current canonical key.
            selected.ColonyKeys.RemoveAll(k => k == colony.Key || colony.AliasKeys.Contains(k));
            if (included)
                selected.ColonyKeys.Add(colony.Key);
            SaveStructuralChange();
        }
        Widgets.EndScrollView();
    }
}
