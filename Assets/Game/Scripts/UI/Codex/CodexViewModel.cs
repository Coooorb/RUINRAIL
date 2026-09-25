using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Onboarding;
using UnityEngine;

namespace RuinRail.UI.Codex
{
    /// <summary>
    /// The Help / Codex page's state: which section is open and how far down it is scrolled.
    ///
    /// It owns no content of its own — the sections come from <see cref="CodexContent"/> and are rebuilt whenever the
    /// input device changes, so the control names in the text are the ones currently bound. Opening it holds gameplay
    /// input exactly like the inventory does, and nothing it does can touch gameplay state.
    /// </summary>
    public sealed class CodexViewModel
    {
        private readonly IInputGlyphs _glyphs;
        private readonly AmmoBalanceConfig _ammo;
        private List<CodexSection> _sections;
        private int _section;
        private int _line;

        public CodexViewModel(IInputGlyphs glyphs = null, AmmoBalanceConfig ammo = null)
        {
            _glyphs = glyphs;
            _ammo = ammo;
            Rebuild();
        }

        /// <summary>Body lines visible at once; the page scrolls by this many.</summary>
        public const int VisibleLines = 14;

        public IReadOnlyList<CodexSection> Sections => _sections;
        public bool IsOpen { get; private set; }
        public int Opens { get; private set; }
        public int SectionIndex => _section;
        public CodexSection Section => _sections[Mathf.Clamp(_section, 0, _sections.Count - 1)];
        public int LineOffset => _line;
        public bool CanScrollDown => Section.Lines.Count - _line > VisibleLines;
        public bool CanScrollUp => _line > 0;

        public event Action Changed;
        /// <summary>The owner (Main Menu / Pause) closes the page when BACK is used.</summary>
        public Action CloseRequested { get; set; }

        /// <summary>Re-reads the content, so a rebind or a device change is reflected in the control names.</summary>
        public void Rebuild()
        {
            _sections = new List<CodexSection>(CodexContent.Sections(_glyphs, _ammo));
            _section = Mathf.Clamp(_section, 0, _sections.Count - 1);
            _line = 0;
            Raise();
        }

        public void Open()
        {
            IsOpen = true;
            Opens++;
            _section = 0;
            _line = 0;
            Rebuild();
        }

        public void Close()
        {
            IsOpen = false;
            Raise();
            CloseRequested?.Invoke();
        }

        /// <summary>Selects a section by index, wrapping, and returns to the top of it.</summary>
        public void SelectSection(int index)
        {
            if (_sections.Count == 0) return;
            _section = ((index % _sections.Count) + _sections.Count) % _sections.Count;
            _line = 0;
            Raise();
        }

        public void SelectSection(string id)
        {
            var at = _sections.FindIndex(s => s.Id == id);
            if (at >= 0) SelectSection(at);
        }

        public void StepSection(int steps) => SelectSection(_section + steps);

        /// <summary>Scrolls the body a page at a time; returns false when there is nothing further that way.</summary>
        public bool ScrollDown()
        {
            if (!CanScrollDown) return false;
            _line = Mathf.Min(_line + VisibleLines, Section.Lines.Count - VisibleLines);
            Raise();
            return true;
        }

        public bool ScrollUp()
        {
            if (!CanScrollUp) return false;
            _line = Mathf.Max(0, _line - VisibleLines);
            Raise();
            return true;
        }

        /// <summary>The body lines to draw right now.</summary>
        public IReadOnlyList<string> VisibleBody()
        {
            var lines = new List<string>();
            var source = Section.Lines;
            for (var i = _line; i < source.Count && lines.Count < VisibleLines; i++) lines.Add(source[i]);
            return lines;
        }

        /// <summary>"1 / 8" style position, so a player knows how much manual is left.</summary>
        public string PositionText => $"{_section + 1} / {_sections.Count}";

        private void Raise() => Changed?.Invoke();
    }
}
