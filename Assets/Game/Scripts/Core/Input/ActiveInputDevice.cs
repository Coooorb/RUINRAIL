using System;
using UnityEngine.InputSystem;

namespace RuinRail.Core.Input
{
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// The device the local player used last (116: prompts/glyphs adapt to it). Set from input callbacks by the reader
    /// and from menus; purely presentational — it never changes bindings or gameplay.
    /// </summary>
    public static class ActiveInputDevice
    {
        public static InputDeviceKind Current { get; private set; } = InputDeviceKind.KeyboardMouse;
        public static int Switches { get; private set; }

        public static event Action<InputDeviceKind> Changed;

        public static void Set(InputDeviceKind kind)
        {
            if (kind == Current) return;
            Current = kind;
            Switches++;
            Changed?.Invoke(kind);
        }

        /// <summary>Classifies the control's device; anything that is not a gamepad counts as keyboard/mouse.</summary>
        public static void NoteControl(InputControl control)
        {
            if (control == null) return;
            Set(control.device is Gamepad ? InputDeviceKind.Gamepad : InputDeviceKind.KeyboardMouse);
        }
    }
}
