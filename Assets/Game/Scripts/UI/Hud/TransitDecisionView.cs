using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// The post-boss Transit decision (dungeon/60, ui/91) as a compact panel at the top of the run screen: the depth
    /// context, two real buttons — RETURN TO SHELTER and DESCEND DEEPER — each with its vote tally in co-op, and one
    /// status line (whose vote is missing, this player's vote, the Return warning, how to take the choice).
    ///
    /// It draws a <see cref="TransitVoteViewModel"/> and activates the entries of the decision's existing focus list
    /// (<see cref="ScreenNavigation.TransitVote"/>), so the vote, its authority and its outcome are exactly the
    /// existing ones. It never takes input by itself: the run decides when the panel owns keyboard/controller focus
    /// (<see cref="SetEngaged"/>) and reads <see cref="IsPointerOver"/> to keep a click on it from also firing.
    /// </summary>
    public sealed class TransitDecisionView : MonoBehaviour
    {
        public static readonly UiRect Panel = new(152, 4, 336, 84);
        public const int ButtonWidth = 158;
        public const int ButtonHeight = 22;
        private const int StatusTop = 57;

        /// <summary>Current panel height (reference px): buttons + as many status lines as there are + the hint line.</summary>
        public int PanelHeight { get; private set; } = Panel.Height;
        public const string ReturnId = "vote.return";
        public const string DescendId = "vote.descend";
        public const string ConfirmId = "vote.confirm";
        public const string CancelId = "vote.cancel";

        private TransitVoteViewModel _vote;
        private FocusList _list;
        private Func<(string depth, string detail)> _context;
        private RectTransform _panel;
        private readonly Dictionary<string, UiControl> _buttons = new();
        private readonly Dictionary<string, Vector2> _centres = new();
        private readonly Dictionary<string, System.Action<bool>> _rebind = new();
        private Text _title;
        private Text _depth;
        private Text _detail;
        private Text _status;
        private Text _hint;
        private bool _engaged;

        public FocusList FocusList => _list;
        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;
        public bool IsEngaged => _engaged;
        public IReadOnlyDictionary<string, UiControl> Buttons => _buttons;
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string DepthText => _depth != null ? _depth.text : string.Empty;
        public string DetailText => _detail != null ? _detail.text : string.Empty;
        public string StatusText => _status != null ? _status.text : string.Empty;
        public string HintText => _hint != null ? _hint.text : string.Empty;
        public string ButtonText(string id) => _buttons.TryGetValue(id, out var c) ? c.GetComponentInChildren<Text>()?.text ?? string.Empty : string.Empty;
        public bool IsButtonShown(string id) => _buttons.TryGetValue(id, out var c) && c.gameObject.activeSelf;

        /// <summary>True while the mouse is over the panel (the run holds gameplay input then, so a click never also fires).</summary>
        public bool IsPointerOver
        {
            get
            {
                if (!IsVisible) return false;
                if (_buttons.Values.Any(b => b != null && b.gameObject.activeSelf && b.IsHovered)) return true;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                return mouse != null && RectTransformUtility.RectangleContainsScreenPoint(_panel, mouse.position.ReadValue(), null);
            }
        }

        public static TransitDecisionView Create(Transform canvas, TransitVoteViewModel vote, FocusList list, Func<(string depth, string detail)> context)
        {
            var go = new GameObject("TransitDecision", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(UiTheme.ScreenWidth, UiTheme.ScreenHeight);
            var view = go.AddComponent<TransitDecisionView>();
            view._vote = vote;
            view._list = list;
            view._context = context;
            view.Build();
            list.Navigator = view.Navigate;
            view.Render();
            return view;
        }

        private void Build()
        {
            // A plain charcoal plate with the steel edge (the skin's 9-slice frame is sized for full windows and breaks
            // up at this height); the height follows the status line (see Render).
            var panel = UiBuild.Panel(transform, Panel, "Panel", UiTheme.NearBlack, UiTheme.PanelEdge);
            _panel = (RectTransform)panel.transform;
            // Amber rule across the top: this is a decision, not a notice.
            UiBuild.Plate(_panel, new UiRect(0, 0, Panel.Width, 2), UiTheme.Amber, "DecisionRule");
            _title = UiBuild.Label(_panel, "TRANSIT READY", new UiRect(8, 5, 150, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, "Title");
            _depth = UiBuild.Label(_panel, string.Empty, new UiRect(Panel.Width - 8 - 170, 5, 170, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Ink, false, "Depth");
            _depth.alignment = TextAnchor.UpperRight;
            _detail = UiBuild.Label(_panel, string.Empty, new UiRect(8, 17, Panel.Width - 16, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Detail");

            var y = 31;
            // This player's vote keeps its button marked (the control's "active" state), so the choice made stays visible.
            AddButton(ReturnId, new UiRect(8, y, ButtonWidth, ButtonHeight), () => _vote.LocalVote == TransitChoice.ReturnToShelter);
            AddButton(DescendId, new UiRect(Panel.Width - 8 - ButtonWidth, y, ButtonWidth, ButtonHeight), () => _vote.LocalVote == TransitChoice.DescendDeeper);
            AddButton(ConfirmId, new UiRect(8, y, ButtonWidth, ButtonHeight), null);
            AddButton(CancelId, new UiRect(Panel.Width - 8 - ButtonWidth, y, ButtonWidth, ButtonHeight), null);

            _status = UiBuild.Label(_panel, string.Empty, new UiRect(8, StatusTop, Panel.Width - 16, UiText.LineHeight * 2), 1, TextAnchor.UpperLeft, UiTheme.Ink, true, "Status");
            _hint = UiBuild.Label(_panel, string.Empty, new UiRect(8, StatusTop, Panel.Width - 16, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.InkFaint, false, "Hint");
            _hint.alignment = TextAnchor.UpperRight;
        }

        private void AddButton(string id, UiRect bounds, Func<bool> isActive)
        {
            var item = _list.Find(id);
            if (item == null) return;
            var rect = UiBuild.NewRect(_panel, "Control:" + id, bounds);
            var fill = rect.gameObject.AddComponent<Image>();
            fill.raycastTarget = true;
            var inner = new UiRect(0, 0, bounds.Width, bounds.Height);
            var edges = UiBuild.Border(rect, inner, UiTheme.PanelEdgeSoft);
            var marker = UiBuild.Plate(rect, new UiRect(0, 0, 2, bounds.Height), UiTheme.Amber, "SelectedMarker");
            marker.enabled = false;
            var brackets = UiBuild.Brackets(rect, inner, UiTheme.Amber);
            var label = UiBuild.Label(rect, item.Label, new UiRect(0, (bounds.Height - UiText.LineHeight) / 2, bounds.Width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Ink, false, "Label");
            label.alignment = TextAnchor.UpperCenter;
            var control = rect.gameObject.AddComponent<UiControl>();
            System.Action<bool> bind = engaged => control.Bind(engaged ? _list : null, item, ControlRole.Primary, fill, label, edges, brackets, marker, isActive,
                i => { _list.Focus(i.Id); _list.ActivateFocused(); Render(); }, bounds.Width, 1);
            bind(false); // no focus look until the player takes the panel (hover and "my vote" still show)
            _rebind[id] = bind;
            _buttons[id] = control;
            _centres[id] = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
        }

        /// <summary>Left/right between the two buttons (they sit side by side); up/down do nothing.</summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null || direction.x == 0 || !_centres.TryGetValue(focused.Id, out var from)) return null;
            return _list.Items
                .Where(i => i.Id != focused.Id && i.IsEnabled && _centres.ContainsKey(i.Id) && _buttons[i.Id].gameObject.activeSelf)
                .Where(i => Mathf.Sign(_centres[i.Id].x - from.x) == Mathf.Sign(direction.x) && Mathf.Abs(_centres[i.Id].x - from.x) > 1f)
                .OrderBy(i => Mathf.Abs(_centres[i.Id].x - from.x))
                .Select(i => i.Id)
                .FirstOrDefault();
        }

        /// <summary>The run hands keyboard/controller focus to the panel (or takes it back); the hint and focus follow.</summary>
        public void SetEngaged(bool engaged)
        {
            _engaged = engaged;
            foreach (var bind in _rebind.Values) bind(engaged);
            if (engaged)
            {
                // Start on this player's current vote, or on RETURN (the Return confirmation starts on CONFIRM).
                var start = _vote.AwaitingReturnConfirmation ? ConfirmId : _vote.LocalVote == TransitChoice.DescendDeeper ? DescendId : ReturnId;
                _list.Focus(start);
            }

            Render();
        }

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        private void Update() => Render();

        /// <summary>Re-reads the vote (tallies, pending voters, resolution) — cheap, and a remote vote needs no extra event.</summary>
        public void Render()
        {
            if (_vote == null || _panel == null || !_panel.gameObject.activeSelf) return;
            var (depth, detail) = _context != null ? _context() : (string.Empty, string.Empty);
            _depth.text = UiText.Fit(depth, 170);
            _detail.text = UiText.Fit(detail, Panel.Width - 16);

            var confirming = _vote.AwaitingReturnConfirmation;
            Show(ReturnId, !confirming);
            Show(DescendId, !confirming);
            Show(ConfirmId, confirming);
            Show(CancelId, confirming);
            var coop = _vote.VoterCount > 1;
            SetLabel(ReturnId, coop ? $"RETURN TO SHELTER  {_vote.VotesFor(TransitChoice.ReturnToShelter)}/{_vote.VoterCount}" : "RETURN TO SHELTER");
            SetLabel(DescendId, coop ? $"DESCEND DEEPER  {_vote.VotesFor(TransitChoice.DescendDeeper)}/{_vote.VoterCount}" : "DESCEND DEEPER");
            SetLabel(ConfirmId, "CONFIRM RETURN");
            SetLabel(CancelId, "CANCEL");
            foreach (var control in _buttons.Values) control.Refresh();

            // Status: the Return warning, a dead player's missing vote, this player's vote and who is still deciding.
            if (confirming)
            {
                _status.text = _vote.ReturnWarningText;
                _status.color = UiTheme.Danger;
            }
            else if (!string.IsNullOrEmpty(_vote.NoVoteText))
            {
                _status.text = _vote.NoVoteText.ToUpperInvariant();
                _status.color = UiTheme.InkMuted;
            }
            else if (_vote.LocalVote.HasValue || _vote.IsResolved)
            {
                var mine = _vote.LocalVote == TransitChoice.DescendDeeper ? "DESCEND" : "RETURN";
                _status.text = _vote.IsResolved ? _vote.StatusText.ToUpperInvariant() : $"YOUR VOTE: {mine}  ·  {_vote.StatusText.ToUpperInvariant()}";
                _status.color = UiTheme.Terminal;
            }
            else
            {
                _status.text = coop ? "THE PARTY DECIDES TOGETHER" : string.Empty;
                _status.color = UiTheme.InkMuted;
            }

            // Height follows the status: none (solo, undecided), one line, or the two-line Return warning.
            var statusLines = string.IsNullOrEmpty(_status.text) ? 0 : UiText.Width(_status.text) > Panel.Width - 16 ? 2 : 1;
            var hintTop = StatusTop + statusLines * UiText.LineHeight + (statusLines > 0 ? 1 : 0);
            var height = hintTop + UiText.LineHeight + 4;
            if (height != PanelHeight || _panel.sizeDelta.y != height) ResizePanel(height);
            ((RectTransform)_hint.transform).anchoredPosition = new Vector2(8, -hintTop);

            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            _hint.text = !_vote.HasVoteControls || _vote.IsResolved ? string.Empty
                : _engaged ? (gamepad ? "D-PAD: SELECT   A: CONFIRM   B: BACK" : "ARROWS: SELECT   ENTER: CONFIRM   ESC: BACK")
                : (gamepad ? "D-PAD UP: CHOOSE" : "F: CHOOSE   OR CLICK");
        }

        /// <summary>The plate, its side edges and its bottom edge follow the height (they are fixed-size children).</summary>
        private void ResizePanel(int height)
        {
            PanelHeight = height;
            _panel.sizeDelta = new Vector2(Panel.Width, height);
            foreach (RectTransform child in _panel)
            {
                switch (child.name)
                {
                    case "Fill":
                    case "EdgeLeft":
                    case "EdgeRight":
                        child.sizeDelta = new Vector2(child.sizeDelta.x, height);
                        break;
                    case "EdgeBottom":
                        child.anchoredPosition = new Vector2(child.anchoredPosition.x, -(height - 1));
                        break;
                }
            }
        }

        private void Show(string id, bool shown)
        {
            if (_buttons.TryGetValue(id, out var control) && control.gameObject.activeSelf != shown) control.gameObject.SetActive(shown);
        }

        private void SetLabel(string id, string text)
        {
            if (!_buttons.TryGetValue(id, out var control)) return;
            var label = control.GetComponentInChildren<Text>();
            if (label != null) label.text = UiText.Fit(text, ButtonWidth - 8);
        }
    }
}
