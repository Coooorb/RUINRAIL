# Base UI

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Approved station screens:
- Storage: item grid + filters/sort, shown as the **stash** — the survivor's worn slots and backpack beside the paged Storage grid; click / confirm / drag moves the chosen item across (Storage → backpack by default; a Storage item dropped on a worn slot, or EQUIP with F / pad X, is equipped into its valid slot — an item already worn there swaps into that Storage cell, so no free Storage or backpack slot is needed; a weapon goes to the free weapon slot, else swaps with Primary), a stack that does not fit whole moves the part that fits (the strip says `TAKE 10 OF 120`); a backpack ↔ Storage drag onto an occupied cell of another kind swaps the two items in place, so it needs no free slot on either side (same-kind stacks merge instead) — on keyboard / controller, SWAP (R / pad Y) picks the focused item, marks every valid target on the other side and moves the focus there, Enter / A on a marked cell performs the same swap, Back (Esc / B) or R / Y cancels without moving anything; a Storage item dragged onto an empty backpack cell lands in that cell; STORE WHOLE BACKPACK stores the bag in one action, and a refused move shows its rule (Storage full, backpack full) and names the swap route. After a successful return with loot, the STORAGE tab and the survivor card point at the stash until it is opened.
- Loadout: equipped slots + backpack + storage source.
- Item inspection (Storage + Loadout): resting on an item for ~0.45 s — pointer hover or keyboard / controller focus — opens a compact card beside the slot (never over it, kept on screen) with name, rarity, category and the item's own tooltip stat lines, plus the relevant worn item's card beside it (ui/93 inventory inspection); it closes when the pointer leaves, the focus moves or a drag starts, and never changes what a click or confirm does.
- Trader: Buy / Sell.
- Character: Level, XP, Skill Points, six attributes, Respec.
- Workshop: approved base upgrades.
- Multiplayer: Solo / Host / Join, join code, Ready states.
- Transit: Start Expedition; coins for the run — `-step` / amount / `+step`, NONE / ALL, with banked now, taking and what stays banked beside it (77).

> **Implementation note (2026-10-03, Shelter UI pass):** an open station gets one wide panel (the middle and right columns, `ScreenLayout.StationColumn`); the survivor card stays on the left and the expedition card steps aside until no station is open. Every station action is an 18 px button with a centred label; OPEN STASH and START EXPEDITION are the 22 px primaries; normal / hover / focused / pressed / disabled / selected come from the one `UiControl` state set. The panel header carries a 16×16 station pictogram drawn at native pixels. Storage shows what is stored as native-size icon tiles in their rarity frames ("+N" when more than fit); the Trader lists offers (or the survivor's items) on the left with the details and comparison in their own column beside them; a cost the bank cannot cover reads red; neutral status plates are information plates, not grey buttons. An action's footer message clears after a few seconds. UI sound: every Shelter interaction plays one cue on the shared UI sound bus — step / hover (navigate), confirm, tab change, back (cancel), a refused or disabled action (failure) and a coin transaction (purchase) — via `UiSoundCues`, which yields to a view model that already played its own cue in that frame.

## Main Menu
Initial menu:
- PLAY.
- SETTINGS.
- QUIT.

`PLAY` enters/loads the base; multiplayer organization happens inside the base rather than a sprawling main-menu flow.
