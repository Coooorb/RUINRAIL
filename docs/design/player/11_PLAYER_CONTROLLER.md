# Player Controller

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Read With

- `player/14_DASH_AND_MOVEMENT.md`
- `technical/116_INPUT_SYSTEM.md`
## Control Model

The game uses independent movement and aim.

- Movement: WASD / left stick.
- Aim: mouse / right stick, full 360 degrees.
- Fire normal attack: LMB / controller trigger.
- Legendary weapon special: RMB / controller equivalent, only when the active weapon is legendary.
- Dash: dedicated input.
- Interact: dedicated input.
- Reload: dedicated input for weapons that reload.
- Weapon 1 / Weapon 2: direct selection.
- Weapon Swap: mouse wheel or controller button may toggle between both equipped weapons.
- Inventory: dedicated input.
- Active Consumable: dedicated input.

Movement direction is independent of aim direction. The character may run left while firing right.

## Player Component Responsibilities

Keep movement, aiming, dash, combat, inventory, interaction, life state, stats, and networking in separate responsibilities rather than one monolithic player script.
