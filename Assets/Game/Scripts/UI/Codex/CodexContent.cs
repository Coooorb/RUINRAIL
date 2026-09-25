using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Onboarding;

namespace RuinRail.UI.Codex
{
    /// <summary>One page of the Codex: a heading and the lines under it.</summary>
    public sealed class CodexSection
    {
        public CodexSection(string id, string title, params string[] lines)
        {
            Id = id;
            Title = title;
            Lines = lines ?? System.Array.Empty<string>();
        }

        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<string> Lines { get; }
    }

    /// <summary>
    /// The Help / Codex text.
    ///
    /// It is a manual, not an encyclopedia: eight short pages covering the rules a player has to know and nothing
    /// else. Every statement here describes what this build actually does, and the numbers are read from the code that
    /// enforces them (<see cref="PlayerInventory.BackpackCapacity"/>, <see cref="AmmoBalanceConfig"/>,
    /// <see cref="LevelCurve.MaxLevel"/>) rather than retyped, so the page cannot drift from the game behind it. Where
    /// something is designed but not connected in this build, it says so instead of implying it works.
    ///
    /// No Unity types: the EditMode tests assert the real content rather than a copy of it.
    /// </summary>
    public static class CodexContent
    {
        public const string CoreRunRulesId = "codex.core";
        public const string LoadoutId = "codex.loadout";
        public const string AmmoId = "codex.ammo";
        public const string RarityId = "codex.rarity";
        public const string CombatId = "codex.combat";
        public const string CoopId = "codex.coop";
        public const string ControlsId = "codex.controls";
        public const string SettingsId = "codex.settings";

        /// <summary>The sections in page order. <paramref name="glyphs"/> resolves control names for the current device.</summary>
        public static IReadOnlyList<CodexSection> Sections(IInputGlyphs glyphs = null, AmmoBalanceConfig ammo = null)
        {
            string G(string action) => glyphs != null ? glyphs.For(action) : action;
            string Cap(AmmoType type) => ammo != null ? ammo.GetStackLimit(type).ToString() : "—";

            return new List<CodexSection>
            {
                new(CoreRunRulesId, "CORE RUN RULES",
                    "A run goes Shelter, Expedition, Boss, Transit.",
                    "The Shelter is safe. Everything you carry into an expedition is at risk.",
                    "Each depth ends at a boss. Beating it opens the Transit.",
                    "At the Transit you RETURN or DESCEND.",
                    "RETURN secures everything in your loadout and backpack into the Shelter.",
                    "DESCEND goes one depth deeper: harder enemies, better rewards, nothing secured yet.",
                    "Dying or wiping loses the loadout and backpack you were carrying.",
                    "XP, level, skill points and Banked Coins survive a lost run.",
                    "Storage and the Shelter's stations are never at risk."),

                new(LoadoutId, "LOADOUT",
                    "Five equipment slots: Primary Weapon, Secondary Weapon, Armor, Accessory, Active Consumable.",
                    $"{PlayerInventory.BackpackCapacity} backpack slots for everything else you pick up.",
                    "Both weapon slots are live: you swap between them, you do not holster one.",
                    "Armor and the Accessory change your stats the moment they are equipped.",
                    "The Active Consumable is the one you use with a key; the rest sit in the backpack.",
                    "A full backpack refuses new items rather than dropping what you have."),

                new(AmmoId, "AMMO",
                    "Four ammo types: Light, Medium, Heavy, Shells. A weapon uses exactly one of them.",
                    $"Stack caps: Light {Cap(AmmoType.Light)}, Medium {Cap(AmmoType.Medium)}, Heavy {Cap(AmmoType.Heavy)}, Shells {Cap(AmmoType.Shells)}.",
                    "Ammo comes from supply chests and events, not from kills.",
                    "Two weapons on different ammo types spread the drain: that is the point of the second slot.",
                    "Blasters use no ammo. They build heat, and overheating locks the weapon out until it vents.",
                    "Melee weapons use no ammo and never run dry."),

                new(RarityId, "RARITY AND AFFIXES",
                    "Rarity ladder: Common, Uncommon, Rare, Epic, Legendary.",
                    "Rarity is how many affixes an item rolled, not a separate power tier.",
                    "An affix is a real stat change and applies as soon as the item is equipped.",
                    "Compare the affix lines, not the colour: a Rare with the right affixes can beat an Epic.",
                    "Legendary items carry a named mechanic on top of their affixes.",
                    "Deeper depths and better sources roll higher rarities more often."),

                new(CombatId, "COMBAT",
                    $"{G("Dash")}: Dash. It has a cooldown and grants brief invulnerability through an attack.",
                    $"{G("Reload")}: reload. An empty magazine reloads from your reserve, not from nothing.",
                    $"{G("WeaponSwap")}: swap weapons. {G("Weapon1")} and {G("Weapon2")} select one directly.",
                    $"{G("QuickGrenade")}: throw the first grenade you are carrying, without opening the inventory.",
                    "Aim assist softly bends a shot toward a target near your aim. It is ON by default and can be turned off in Settings.",
                    "Entering a combat room locks its doors until the room is cleared.",
                    "Elites are stronger versions of ordinary enemies and appear more often as you descend.",
                    "Bosses have two phases: the second starts at half health and adds a mechanic.",
                    "Enemy attacks telegraph before they land. The telegraph is the tell, not the damage."),

                new(CoopId, "CO-OP AND DOWNED",
                    "Co-op is built for up to 3 players and driven from the Shelter's Multiplayer Terminal.",
                    "The terminal offers SOLO, HOST CO-OP and JOIN BY CODE. Sessions are private; there is no matchmaking.",
                    "Online sessions need Unity Services configured for the build. When they are not, the terminal reports the error rather than pretending to connect.",
                    "Losing all health in co-op puts you Downed rather than dead.",
                    "A Downed player bleeds out on a timer and can be revived by a teammate holding Interact.",
                    "A Downed player cannot move, fire, dash or use items.",
                    "If everyone is Downed, the run is lost and everything carried is lost with it."),

                new(ControlsId, "CONTROLS",
                    $"Move {G("Move")}   Aim {G("Aim")}   Fire {G("Fire")}   Special {G("Special")}",
                    $"Dash {G("Dash")}   Reload {G("Reload")}   Interact {G("Interact")}",
                    $"Weapon 1 {G("Weapon1")}   Weapon 2 {G("Weapon2")}   Swap {G("WeaponSwap")}",
                    $"Consumable {G("Consumable")}   Quick Grenade {G("QuickGrenade")}   Inventory {G("Inventory")}   Pause {G("Pause")}",
                    "Keyboard, mouse and controller are all supported, and you can switch between them mid-run.",
                    "Every control above can be rebound in Settings, CONTROLS."),

                new(SettingsId, "SETTINGS",
                    "VIDEO: display mode, resolution, VSync, frame-rate limit.",
                    "AUDIO: master, music, SFX and ambience levels, plus mute.",
                    "CONTROLS: keyboard/mouse and controller rebinding.",
                    "GAMEPLAY: aim assist, screen shake and its intensity, damage numbers, hit flash, tutorial prompts, and resetting tutorials.",
                    "Settings are stored separately from your save: resetting one never touches the other.")
            };
        }

        /// <summary>The maximum level a survivor can reach, stated where the progression page needs it.</summary>
        public static int MaxLevel => LevelCurve.MaxLevel;
    }
}
