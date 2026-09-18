using System.Collections.Generic;
using RuinRail.UI.Navigation;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The settings page as a panel over whatever screen opened it (Main Menu or the in-run pause menu) — one builder,
    /// so both places show the identical page over the identical view model.
    ///
    /// It scrolls by focus rather than by a scrollbar: the list is long, so the rows that fit are drawn and the
    /// window follows the focused entry. That keeps every control reachable by keyboard, controller and pointer
    /// without a scroll widget none of the three would share.
    /// </summary>
    public static class SettingsPanel
    {
        public static readonly UiRect Bounds = new(180, 46, 300, 268);

        public sealed class Instance
        {
            public GameObject Panel;
            public FocusList List;
            public readonly List<UiControl> Controls = new();
        }

        public static Instance Build(Transform root, SettingsViewModel settings)
        {
            var instance = new Instance();
            instance.Panel = UiKit.Panel(root, Bounds, "SettingsPanel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.96f), UiTheme.PanelEdge);
            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, Bounds.Width - UiTheme.Pad * 2, Bounds.Height - UiTheme.Pad * 2);

            UiKit.Label(instance.Panel.transform, "SETTINGS", new UiRect(inner.X, inner.Y, inner.Width, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(instance.Panel.transform, new UiRect(inner.X, inner.Y + UiText.Height() + 3, inner.Width, 1), UiTheme.PanelEdge, "Rule");

            instance.List = ScreenNavigation.Settings(settings);
            var list = new UiRect(inner.X, inner.Y + UiText.Height() + 9, inner.Width, inner.Height - UiText.Height() - 9);
            const int rowHeight = 13;
            const int gap = 1;
            var capacity = ScreenLayout.RowCapacity(list, rowHeight, gap);
            var rows = ScreenLayout.Rows(list, rowHeight, gap, capacity);

            var window = new FocusWindow(instance.List, capacity);
            var focusList = instance.List;
            for (var slot = 0; slot < rows.Count && slot < focusList.Items.Count; slot++)
            {
                var control = UiKit.Control(instance.Panel.transform, focusList, focusList.Items[slot], rows[slot],
                    ControlRole.Row, item => { focusList.Focus(item.Id); focusList.ActivateFocused(); });
                window.Register(slot, control);
                instance.Controls.Add(control);
            }

            instance.Panel.AddComponent<FocusWindowDriver>().Bind(window);
            return instance;
        }
    }
}
