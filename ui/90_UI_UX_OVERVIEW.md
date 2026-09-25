# UI / UX Overview

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
UI must remain compact, readable, controller-friendly, and consistent with the pixel-art game. Critical information must not rely on color alone.

## Core Principles
- One consistent Interact action.
- Always show rarity as text (`COMMON`, `RARE`, etc.) in addition to color.
- No Gear Score/Power Score abstraction; compare actual stats/effects.
- Important systems should be visually understandable without reading long tutorial text.
- All menus support mouse/keyboard and controller navigation.

## Death / Run Lost Screen (implementation note 2026-09-19)

A conclusive run failure (solo death; co-op wipe — nobody Alive) shows the **RUN LOST** screen over the closed expedition failure transaction (`ExpeditionService.Fail()`, exactly once; no second loss implementation). A co-op player who is merely Downed and revivable, or Dead while a teammate lives, sees no final screen — the existing spectate/wait behaviour stays. The screen lists tracked figures only: depth reached, biome, rooms cleared, enemies defeated, elites defeated, Boss defeated yes/no, carried coins lost, items lost, XP earned (kept) and a level change when there was one. Two controls: `RETURN TO SHELTER` (focused first) and `MAIN MENU`; no Retry. Mouse, keyboard and controller reach both through the run's one focus stack; gameplay input is held, the solo world stays paused, Esc does not open the pause menu over it and the inventory cannot open. The loss is saved before the screen appears. Style: charcoal panel, steel edge, restrained danger-red title rule, pixel font.

## Settings (implementation note 2026-09-20)
- Settings is category-based from the Main Menu and the Pause Menu: the root lists **VIDEO**, **AUDIO**, **CONTROLS** and **GAMEPLAY** (plus RESET TO DEFAULTS and BACK); choosing a category opens its page of adjustable controls; Back returns page → categories → the screen that opened Settings. Pause → Settings → Back → Back lands on the pause root, never in gameplay; gameplay input stays held while the panel is up.
- VIDEO: display mode (Fullscreen / Windowed), resolution (Native or the display's options), VSync, frame-rate limit (Unlimited / 30 / 60 / 120 / 144 / 240). VSync and the limit persist on Back; a display-mode / resolution change is applied provisionally through APPLY DISPLAY SETTINGS and reverts unless KEEP is chosen within 12 s (Back, REVERT or the timeout restore the previous values).
- AUDIO: Master, Music, SFX and Ambience sliders (0–100 % in 5 % steps; pointer drag, keyboard / D-pad left-right) and MUTE ALL; changes are heard at once and persist on Back; an explicit 0 is a valid mute; fresh profiles default to 100 %.
- CONTROLS: the binding scheme selector (Keyboard & Mouse / Controller), every discrete binding as a row (Enter / click listens). Fixed by design, per technical/116's rebinding contract: Pause; Aim — the mouse pointer position / the right-stick axis, positional/analog input rather than a key binding; and the gamepad Move stick through the one existing rebinder, RESET BINDINGS. The shipped app composes a real rebinder for this page.
- GAMEPLAY: screen shake (with intensity), damage numbers, hit flash, tutorial prompts, reset tutorials — only preferences that already exist; no balance toggles.
