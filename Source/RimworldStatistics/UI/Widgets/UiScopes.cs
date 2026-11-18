using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Allocation-free RAII scopes that guarantee IMGUI stacks (<c>BeginScrollView</c>, <c>BeginGroup</c>)
/// and global <see cref="Text"/>/<see cref="GUI"/> state are restored even when inner drawing throws.
/// </summary>
internal static class UiScopes
{
    public static ScrollScope ScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true)
    {
        Verse.Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, showScrollbars);
        return new ScrollScope(active: true);
    }

    public static GroupScope Group(Rect position)
    {
        GUI.BeginGroup(position);
        return new GroupScope(active: true);
    }

    public static GuiStateScope GuiState()
        => new(Text.Font, Text.Anchor, Text.WordWrap, GUI.color);

    public static void OpenOrReplaceWindow<T>(T window) where T : Window
    {
        if (Find.WindowStack is not { } stack)
            return;
        if (stack.WindowOfType<T>() is { } existing)
            existing.Close(doCloseSound: false);
        stack.Add(window);
    }

    public readonly struct ScrollScope(bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active)
                Verse.Widgets.EndScrollView();
        }
    }

    public readonly struct GroupScope(bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active)
                GUI.EndGroup();
        }
    }

    public readonly struct GuiStateScope(GameFont font, TextAnchor anchor, bool wordWrap, Color color) : IDisposable
    {
        public void Dispose()
        {
            Text.Font = font;
            Text.Anchor = anchor;
            Text.WordWrap = wordWrap;
            GUI.color = color;
        }
    }
}
