# Player Profile and Display Name

> **Status:** Approved V1 design specification.
> **Game language:** English.

## Display Name

On first launch, the player creates a local in-game display name. It is used in co-op HUD elements, transit voting, revive prompts, spectator UI, and party displays.

### Validation

- Length: **3–16 characters** after trimming/normalization.
- Allowed visible characters: `A-Z`, `a-z`, `0-9`, spaces, `_`, `-`.
- Leading/trailing spaces are removed.
- Consecutive spaces are collapsed to a single space.
- Control characters and Unity Rich Text / markup-like tags are rejected or sanitized so names cannot inject UI formatting.
- Names do **not** need to be globally unique.
- A small local **case-insensitive profanity blocklist** prevents confirmation of blocked names.
- The display name can later be changed from profile/settings UI using the same validation.

V1 does not require accounts, passwords, friend infrastructure, global username uniqueness, reports, or a live moderation backend. The local display name is transmitted to the host as session player information.

## Persistent Profile

The profile includes display name, XP, level, unspent skill points, six invested attributes, Banked Coins, Storage, Storage capacity/upgrades, base upgrades, and current safe loadout state.
