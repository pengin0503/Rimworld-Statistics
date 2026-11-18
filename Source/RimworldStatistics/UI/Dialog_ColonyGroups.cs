using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Edit presentation-only saved colony groups, not the statistics profile.</summary>
public sealed class Dialog_ColonyGroups : Window
{
    private readonly ViewPreferences preferences;
    private readonly bool viewLoaded;
    private readonly IReadOnlyList<Colony> colonies;
    private readonly IReadOnlyDictionary<string, ColonyStats> statsByKey;
    private readonly Dictionary<string, string> savedNames = new(StringComparer.Ordinal);
    private readonly Action onChange;
    private Vector2 groupScroll;
    private Vector2 memberScroll;
    private string newGroupName = "";
    private string memberSearch = "";
    private string? selectedId;
    private bool nameDirty;

    public Dialog_ColonyGroups(ViewPreferences preferences, StatsView? view, Action onChange)
    {
        this.preferences = preferences;
        viewLoaded = view is not null;
        colonies = view?.Colonies ?? Array.Empty<Colony>();
        statsByKey = view?.Stats.Colonies.ToDictionary(s => s.Key, StringComparer.Ordinal)
            ?? new Dictionary<string, ColonyStats>(StringComparer.Ordinal);
        foreach (var group in preferences.Groups)
            if (!string.IsNullOrWhiteSpace(group.Name))
                savedNames[group.Id] = group.Name.Trim();
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
        {
            NormalizeGroupNames();
            onChange();
        }
        base.PostClose();
    }

    private void SaveStructuralChange()
    {
        // A structural operation persists an earlier rename as part of the same write.
        NormalizeGroupNames();
        nameDirty = false;
        onChange();
    }

    private void NormalizeGroupNames()
    {
        foreach (var group in preferences.Groups)
        {
            var trimmed = group.Name.Trim();
            if (trimmed.Length == 0)
            {
                group.Name = savedNames.GetValueOrDefault(group.Id) is { Length: > 0 } previous ? previous : group.Id;
            }
            else
            {
                group.Name = trimmed.Length > 64 ? trimmed[..64] : trimmed;
                savedNames[group.Id] = group.Name;
            }
        }
    }

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawWindowContents(rect);
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }

    private void DrawWindowContents(Rect rect)
    {
        Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), "RWS_Groups_Title".Translate());
        var input = new Rect(rect.x, rect.y + 34f, Math.Max(50f, rect.width - 160f), 30f);
        newGroupName = Widgets.TextField(input, newGroupName);
        if (Widgets.ButtonText(new Rect(input.xMax + 8f, input.y, 140f, 30f), "RWS_Groups_Add".Translate())
            && !string.IsNullOrWhiteSpace(newGroupName) && preferences.Groups.Count < 64)
        {
            var trimmed = newGroupName.Trim();
            var group = new ColonyGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = trimmed[..Math.Min(64, trimmed.Length)],
            };
            savedNames[group.Id] = group.Name;
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
        try
        {
        for (var i = 0; i < preferences.Groups.Count; i++)
        {
            var group = preferences.Groups[i];
            var y = i * 34f;
            var choose = new Rect(0, y, groupsView.width - 34f, 30f);
            var displayName = string.IsNullOrWhiteSpace(group.Name)
                ? savedNames.GetValueOrDefault(group.Id, group.Id) : group.Name;
            if (Widgets.ButtonText(choose, displayName.Truncate(choose.width)))
            {
                NormalizeGroupNames();
                selectedId = group.Id;
            }
            if (Widgets.ButtonText(new Rect(choose.xMax + 2f, y, 30f, 30f), "×"))
            {
                preferences.Groups.RemoveAt(i);
                savedNames.Remove(group.Id);
                if (selectedId == group.Id)
                    selectedId = preferences.Groups.FirstOrDefault()?.Id;
                SaveStructuralChange();
                break;
            }
        }
        }
        finally
        {
            Widgets.EndScrollView();
        }

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
        var unavailable = viewLoaded
            ? selected.ColonyKeys.Count(k => colonies.All(c => c.Key != k && !c.AliasKeys.Contains(k)))
            : 0;
        var notice = new Rect(nameRect.x, nameRect.yMax + 4f, width, 32f);
        Widgets.Label(notice, "RWS_Groups_Members".Translate(selected.ColonyKeys.Count, unavailable));
        var controlsY = notice.yMax;
        if (unavailable > 0)
        {
            if (Widgets.ButtonText(new Rect(nameRect.x, controlsY, width, 26f), "RWS_Groups_RemoveMissing".Translate()))
            {
                selected.ColonyKeys.RemoveAll(k => colonies.All(c => c.Key != k && !c.AliasKeys.Contains(k)));
                SaveStructuralChange();
            }
            controlsY += 30f;
        }
        else
        {
            controlsY += 2f;
        }

        var searchLabel = (string)"RWS_Colonies_Search".Translate();
        var searchLabelWidth = Math.Min(Text.CalcSize(searchLabel).x + 8f, Math.Max(0f, width * 0.42f));
        var searchLabelRect = new Rect(nameRect.x, controlsY, searchLabelWidth, 28f);
        var prevAnchor = Text.Anchor;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(searchLabelRect, searchLabel.Truncate(searchLabelRect.width));
        }
        finally
        {
            Text.Anchor = prevAnchor;
        }
        TooltipHandler.TipRegion(searchLabelRect, "RWS_Colonies_SearchTip".Translate());

        var searchFieldRect = new Rect(searchLabelRect.xMax + 4f, controlsY, Math.Max(0f, width - searchLabelWidth - 4f), 28f);
        var nextSearch = Widgets.TextField(searchFieldRect, memberSearch) ?? "";
        if (nextSearch != memberSearch)
        {
            memberSearch = nextSearch;
            memberScroll = Vector2.zero;
        }
        controlsY += 34f;

        var activePrefs = Dialog_RimworldStatistics.ActivePreferences;
        string DisplayNameFor(Colony c)
            => statsByKey.TryGetValue(c.Key, out var s)
                ? Dialog_RimworldStatistics.ColonyName(s, activePrefs)
                : ColonyPresentation.TryGetCustomName(activePrefs, c) ?? c.Key;

        var listRect = new Rect(nameRect.x, controlsY, width, Math.Max(0f, right.yMax - controlsY - margin));
        var visible = colonies
            .Where(c => ColonyPresentation.MatchesGroupMemberSearch(
                c,
                statsByKey.GetValueOrDefault(c.Key),
                activePrefs,
                Dialog_RimworldStatistics.UnnamedColonyFallback,
                memberSearch))
            .OrderBy(DisplayNameFor, StringComparer.CurrentCulture)
            .ToList();
        if (visible.Count == 0 && colonies.Count > 0)
        {
            Widgets.Label(listRect, "RWS_Details_NoMatch".Translate());
            return;
        }

        var listView = new Rect(0f, 0f, Math.Max(0f, listRect.width - 16f), visible.Count * 30f + 8f);
        Widgets.BeginScrollView(listRect, ref memberScroll, listView);
        try
        {
            for (var i = 0; i < visible.Count; i++)
            {
                var colony = visible[i];
                var was = filtered.Contains(colony.Key) || colony.AliasKeys.Any(filtered.Contains);
                var included = was;
                var label = DisplayNameFor(colony)
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
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }
}
