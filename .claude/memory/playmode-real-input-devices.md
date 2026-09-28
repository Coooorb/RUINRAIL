---
name: playmode-real-input-devices
description: Real keyboard/gamepad input in batch PlayMode tests needs IgnoreFocus + AllDeviceInputAlwaysGoesToGameView + EnableDevice/MakeCurrent, else presses are silently dropped
metadata:
  type: project
---

To drive the shipping input path (Keyboard.current / Gamepad.current, MenuInput.Poll, view Update polls) from a
PlayMode test: `InputSystem.AddDevice<Keyboard/Gamepad>()`, `EnableDevice`, `MakeCurrent`, then
`QueueStateEvent(device, new KeyboardState(Key.X))`, `yield return null`, queue the empty state, `yield return null`.
Batch PlayMode is unfocused, so two settings must be switched for the test and restored in `finally`:
`InputSystem.settings.backgroundBehavior = IgnoreFocus` (default disables devices: enabled=False) and
`editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView` (default routes keyboards to the editor: isPressed=False).
Example: ShelterUiInteractionTests.Stash_KeyboardAndController_Swap… (2026-09-27).

**Why:** without both, queued presses vanish with no error and look like a product bug.
**How to apply:** copy that setup; remove the added devices in `finally`. See [[batch-playmode-frame-loops]].
