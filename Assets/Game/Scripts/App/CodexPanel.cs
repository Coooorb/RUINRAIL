using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Codex;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The Help / Codex panel over whatever screen opened it — one builder, like <see cref="SettingsPanel"/>, so the
    /// Main Menu and the pause menu show the identical page.
    ///
    /// The layout is the merchant's and the settings page's, not a new one: a section list on the left that focus
    /// walks, the section's lines on the right, and BACK to leave. Long sections page rather than scroll with a
    /// scrollbar, so keyboard, controller and pointer all reach every line. It renders text and nothing else — no
    /// gameplay state is readable from here and none is writable.
    /// </summary>
    public static class CodexPanel
    {
        public static readonly UiRect Bounds = new(40, 36, 560, 288);
        public const int SectionWidth = 156;
        public const int SectionHeight = 18;
        public const int SectionGap = 2;
        public const string BackFocusId = "codex.back";

        public sealed class Instance
        {
            public GameObject Panel;
            public CodexPanelDriver Driver;
            public readonly List<UiControl> Controls = new();
            public FocusList List => Driver != null ? Driver.List : null;
        }

        public static Instance Build(Transform root, CodexViewModel codex, MenuInput input)
        {
            var instance = new Instance();
            instance.Panel = UiKit.Panel(root, Bounds, "CodexPanel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.96f), UiTheme.PanelEdge);
            instance.Driver = instance.Panel.AddComponent<CodexPanelDriver>();
            instance.Driver.Bind(instance, codex, input);
            return instance;
        }

        public static void Close(Instance instance)
        {
            if (instance == null) return;
            instance.Driver?.Detach();
            if (instance.Panel != null) Object.Destroy(instance.Panel);
            instance.Panel = null;
        }
    }

    /// <summary>Builds and drives the Codex panel's controls and body text from its view model.</summary>
    public sealed class CodexPanelDriver : MonoBehaviour
    {
        private CodexPanel.Instance _instance;
        private CodexViewModel _codex;
        private MenuInput _input;
        private Text _title;
        private Text _position;
        private Text _more;
        private readonly List<Text> _body = new();
        private readonly List<UiControl> _sectionControls = new();

        public FocusList List { get; private set; }
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string PositionText => _position != null ? _position.text : string.Empty;
        public IReadOnlyList<string> BodyTexts => _body.Where(b => b.gameObject.activeSelf).Select(b => b.text).ToList();
        public IReadOnlyList<UiControl> SectionControls => _sectionControls;
        public int Renders { get; private set; }

        public void Bind(CodexPanel.Instance instance, CodexViewModel codex, MenuInput input)
        {
            _instance = instance;
            _codex = codex;
            _input = input;

            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, CodexPanel.Bounds.Width - UiTheme.Pad * 2, CodexPanel.Bounds.Height - UiTheme.Pad * 2);
            UiKit.Label(transform, "HELP", new UiRect(inner.X, inner.Y, 120, UiText.Height(1, 2)), 2, TextAnchor.UpperLeft, UiTheme.Amber);
            _position = UiKit.Label(transform, string.Empty, new UiRect(inner.Right - 60, inner.Y + 4, 60, UiText.Height()), 1, TextAnchor.UpperRight, UiTheme.InkMuted);
            var bodyY = inner.Y + UiText.Height(1, 2) + 6;
            UiKit.Plate(transform, new UiRect(inner.X, bodyY - 4, inner.Width, 1), UiTheme.PanelEdge, "HeaderRule");

            List = new FocusList("Codex");
            var sections = _codex.Sections;
            for (var i = 0; i < sections.Count; i++)
            {
                var index = i;
                List.Add(sections[i].Id, sections[i].Title, () => _codex.SelectSection(index));
            }

            List.Add(CodexPanel.BackFocusId, "BACK", () => _codex.Close());

            var bodyX = inner.X + CodexPanel.SectionWidth + UiTheme.Pad;
            var bodyWidth = inner.Right - bodyX;
            UiKit.Plate(transform, new UiRect(bodyX - UiTheme.Pad / 2, bodyY, 1, inner.Bottom - bodyY), UiTheme.PanelEdgeSoft, "ColumnRule");

            for (var i = 0; i < List.Items.Count; i++)
            {
                var y = bodyY + i * (CodexPanel.SectionHeight + CodexPanel.SectionGap);
                if (y + CodexPanel.SectionHeight > inner.Bottom) break;
                var item = List.Items[i];
                var role = item.Id == CodexPanel.BackFocusId ? ControlRole.Exit : ControlRole.Button;
                var control = UiKit.Control(transform, List, item, new UiRect(inner.X, y, CodexPanel.SectionWidth, CodexPanel.SectionHeight),
                    role, f => { List.Focus(f.Id); List.ActivateFocused(); }, () => _codex.Section.Id == item.Id);
                _sectionControls.Add(control);
                _instance.Controls.Add(control);
            }

            _title = UiKit.Label(transform, string.Empty, new UiRect(bodyX, bodyY, bodyWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            for (var i = 0; i < CodexViewModel.VisibleLines; i++)
            {
                var y = bodyY + UiText.LineHeight + 4 + i * UiText.LineHeight;
                if (y + UiText.Height() > inner.Bottom - UiText.LineHeight) break;
                _body.Add(UiKit.Label(transform, string.Empty, new UiRect(bodyX, y, bodyWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted));
            }

            _more = UiKit.Label(transform, string.Empty, new UiRect(bodyX, inner.Bottom - UiText.Height(), bodyWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);

            _codex.Changed += Render;
            _input?.Stack.Push(List);
            List.Focus(_codex.Section.Id);
            Render();
        }

        public void Detach()
        {
            if (_codex != null) _codex.Changed -= Render;
            if (_input != null && List != null) _input.Stack.Remove(List);
        }

        private void OnDestroy() => Detach();

        /// <summary>Repaints the open section. Pure presentation: the view model owns which section and which lines.</summary>
        public void Render()
        {
            Renders++;
            if (_codex == null || _title == null) return;
            _title.text = _codex.Section.Title;
            _position.text = _codex.PositionText;
            var lines = _codex.VisibleBody();
            for (var i = 0; i < _body.Count; i++)
            {
                var visible = i < lines.Count;
                _body[i].gameObject.SetActive(visible);
                if (visible) _body[i].text = UiText.Fit(lines[i], Mathf.RoundToInt(((RectTransform)_body[i].transform).sizeDelta.x));
            }

            _more.text = _codex.CanScrollDown ? $"MORE — {DetailPagingHint()}" : _codex.CanScrollUp ? "END" : string.Empty;
            foreach (var control in _sectionControls) control.Refresh();
        }

        private static string DetailPagingHint() => RuinRail.UI.Inventory.DetailPagingInput.Hint();

        private void Update()
        {
            if (_codex == null) return;
            var step = RuinRail.UI.Inventory.DetailPagingInput.Poll();
            if (step > 0) _codex.ScrollDown();
            else if (step < 0) _codex.ScrollUp();
        }
    }
}
