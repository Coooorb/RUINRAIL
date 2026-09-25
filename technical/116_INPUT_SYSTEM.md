# Input System and Default Bindings

> **Status:** Approved V1 design specification.
> **Game language:** English.

Use Unity's Input System and action maps. Do not scatter `Input.GetKeyDown` logic through gameplay scripts.

## Rebinding Contract

- Every **discrete** gameplay binding is rebindable in Settings → CONTROLS, per scheme: every button action (Fire, Special, Dash, Reload, Interact, Weapon1, Weapon2, WeaponSwap, Consumable, QuickGrenade, Inventory) and each keyboard direction of Move (W/A/S/D as four separate bindings).
- **Aim is positional/analog input, not a key binding, and is intentionally fixed:** on keyboard/mouse it is the mouse pointer position; on a gamepad it is the right-stick axis. It therefore does not appear as a rebindable row. This is by design, not missing functionality — there is no key to remap it to.
- The gamepad **Move** stick is likewise an analog axis (left stick) and is fixed; keyboard Move directions stay rebindable.
- **Pause** is fixed on both schemes so the pause menu (and the Settings page itself) can always be reached.
- Everything else in the Player action map stays rebindable; adding an action means giving it a rebind label and keyboard + gamepad prompt glyphs (enforced by `FinalReleaseCandidateValidator`).

## Required Player Actions

- Move.
- Aim.
- Fire.
- Special.
- Dash.
- Reload.
- Interact.
- Weapon1.
- Weapon2.
- WeaponSwap.
- Consumable.
- Inventory.
- Pause.

## Keyboard / Mouse Defaults

| Action | Default |
|---|---|
| Move | WASD |
| Aim | Mouse position |
| Primary Attack | Left Mouse Button |
| Legendary Special | Right Mouse Button |
| Dash | Space |
| Interact | E |
| Reload | R |
| Select Primary Weapon | 1 |
| Select Secondary Weapon | 2 |
| Swap Weapon | Mouse Wheel |
| Use Active Consumable | G |
| Inventory | Tab |
| Pause | Escape |

## Controller Defaults

| Action | Xbox | PlayStation-style label |
|---|---|---|
| Move | Left Stick | Left Stick |
| Aim | Right Stick | Right Stick |
| Primary Attack | RT | R2 |
| Legendary Special | LT | L2 |
| Interact | A | Cross |
| Dash | B | Circle |
| Reload | X | Square |
| Swap Weapon | Y | Triangle |
| Use Active Consumable | RB | R1 |
| Select Primary Weapon | D-Pad Left | D-Pad Left |
| Select Secondary Weapon | D-Pad Right | D-Pad Right |
| Inventory | View | Touchpad/Create-equivalent |
| Pause | Menu | Options |

Input layer expresses player intent; gameplay controllers consume that intent.
