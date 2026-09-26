# Loot Tooltips and Comparison

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Ground equipment tooltip shows:
- Name.
- Rarity text.
- Relevant base stats.
- Affixes and integer values.
- Legendary special/passive description when applicable.
- Pickup prompt.

When looking at a compatible item, show side-by-side comparison with current equipment and clear up/down changes. Do not generate a single automatic “Power Score.”

Legendary drops may have stronger world glow/audio, but rarity still appears as explicit text for accessibility.

## Item Descriptions (implementation note 2026-09-20)
- Every player-facing item (weapons, armor, accessories, consumables, ammo) carries a description derived from its own data: consumables state amount / duration / use time / restriction (`Restore 25 HP when the 1.5 s use completes`, `Gain +20% Movement Speed for 8 s (0.5 s use)`, grenade radius, damage and zones); weapons state class, ammo type, magazine, reload, range and any pellet / spread / explosion / heat / draw mechanic; armor and accessories state their exact modifiers; a Legendary family states its special (RMB / LT, shots, damage, cooldown) or passive with its authored numbers. There is no hand-written effect text that can drift from a definition.
- The description leads the details panel of the Inventory, the Merchant and the Weapon Cache, followed by the Legendary line, the stat rows, the affixes and the comparison. A panel whose rows overflow pages them (mouse wheel / PageDown / right stick) with a MORE marker instead of cutting the tail.
- Text uses only the characters of the pixel face; a validator (`ItemDescriptionValidator`, part of the final production validation) fails on empty text, placeholders, internal ids, an unresolvable consumable effect or an undescribed Legendary mechanic.
