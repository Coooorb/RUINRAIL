using System;
using UnityEngine;

namespace RuinRail.UI.Theme
{
    public enum CursorKind
    {
        /// <summary>The OS cursor: nothing custom applied (no skin, or explicitly released).</summary>
        System,
        Pointer,
        Hover,
        Aim
    }

    /// <summary>
    /// Owns the hardware cursor. Which cursor shows is a pure function of three inputs, so ownership never gets
    /// confused between screens:
    ///
    ///  - the <b>base</b> mode of the composed scene — Pointer for the menus and the Shelter, Aim for the dungeon;
    ///  - an <b>overlay count</b> — every open menu layer over gameplay (pause, inventory) takes the Pointer while it is
    ///    up and hands the Aim back when the last one closes;
    ///  - <b>hover</b> — a control under the pointer shows the hover/select variant, only while the pointer is the
    ///    effective cursor (never over gameplay).
    ///
    /// The result is applied through <see cref="Cursor.SetCursor"/>; regaining application focus re-applies the
    /// current result, because the OS cursor comes back on focus loss. Gamepad navigation never needs the mouse: the
    /// cursor simply stays where it is. The resolve rule is static and testable; the application is the one seam.
    /// </summary>
    public static class CursorService
    {
        private static CursorKind _base = CursorKind.System;
        private static int _overlays;
        private static bool _hover;
        private static Func<CursorKind, bool> _apply = ApplyHardware;

        public static CursorKind Base => _base;
        public static int Overlays => _overlays;
        public static bool Hovering => _hover;
        /// <summary>The cursor that is currently meant to be shown.</summary>
        public static CursorKind Current { get; private set; } = CursorKind.System;
        public static int Applications { get; private set; }

        /// <summary>Pure rule: overlays force the pointer; hover only decorates the pointer; otherwise the base.</summary>
        public static CursorKind Resolve(CursorKind baseKind, int overlays, bool hover)
        {
            if (baseKind == CursorKind.System) return CursorKind.System;
            var effective = overlays > 0 ? CursorKind.Pointer : baseKind;
            if (effective == CursorKind.Pointer && hover) return CursorKind.Hover;
            return effective;
        }

        /// <summary>The composed scene declares its base cursor (Pointer for menus/Shelter, Aim for the dungeon).</summary>
        public static void SetBase(CursorKind baseKind)
        {
            _base = baseKind;
            _overlays = 0;
            _hover = false;
            Reapply();
        }

        /// <summary>A menu layer opened over gameplay (pause, inventory): the pointer takes over until it closes.</summary>
        public static void PushOverlay()
        {
            _overlays++;
            Reapply();
        }

        public static void PopOverlay()
        {
            _overlays = Mathf.Max(0, _overlays - 1);
            if (_overlays == 0) _hover = false;
            Reapply();
        }

        /// <summary>A control under the pointer (UiControl enter/exit).</summary>
        public static void SetHover(bool hovering)
        {
            if (_hover == hovering) return;
            _hover = hovering;
            Reapply();
        }

        /// <summary>Re-applies the current result (application focus regained, skin reloaded).</summary>
        public static void Reapply()
        {
            Current = Resolve(_base, _overlays, _hover);
            if (_apply(Current)) Applications++;
        }

        /// <summary>Tests: swap the hardware application for a recorder. Null restores the real one.</summary>
        public static void SetApplier(Func<CursorKind, bool> apply)
        {
            _apply = apply ?? ApplyHardware;
        }

        /// <summary>Tests/teardown: back to the OS cursor and no state.</summary>
        public static void Reset()
        {
            _base = CursorKind.System;
            _overlays = 0;
            _hover = false;
            Current = CursorKind.System;
            Applications = 0;
            _apply = ApplyHardware;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
        }

        /// <summary>The texture + hotspot the skin binds for a kind, or null for the OS cursor.</summary>
        public static (Texture2D texture, Vector2 hotspot) TextureFor(CursorKind kind, UiSkin skin)
        {
            if (skin == null) return (null, Vector2.zero);
            return kind switch
            {
                CursorKind.Pointer => (skin.CursorPointer, skin.CursorPointerHotspot),
                CursorKind.Hover => (skin.CursorHover != null ? skin.CursorHover : skin.CursorPointer, skin.CursorHover != null ? skin.CursorHoverHotspot : skin.CursorPointerHotspot),
                CursorKind.Aim => (skin.CursorAim, skin.CursorAimHotspot),
                _ => (null, Vector2.zero)
            };
        }

        private static bool ApplyHardware(CursorKind kind)
        {
            var (texture, hotspot) = TextureFor(kind, UiSkin.Load());
            // A missing cursor texture falls back to the OS pointer rather than an invisible cursor.
            Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            return true;
        }
    }
}
