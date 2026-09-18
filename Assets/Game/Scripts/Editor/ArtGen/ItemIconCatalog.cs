using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Maps the project's real armor, accessory, consumable and ammo definitions onto icon designs.
    ///
    /// Motifs are assigned from each definition's own id so the same item always gets the same object metaphor, and
    /// so two accessories never collide on one visual. FINAL_ART_PRODUCTION_SPEC 12.3 forbids inventing a visual that
    /// contradicts an accessory's name, so the mapping keys off recognisable words in the id first and only falls
    /// back to a stable hash when nothing matches.
    /// </summary>
    public static class ItemIconCatalog
    {
        public static IReadOnlyList<IconFactory.IconDesign> NonWeaponIcons()
        {
            var designs = new List<IconFactory.IconDesign>();

            foreach (var armor in Load<ArmorDefinition>())
                designs.Add(new IconFactory.IconDesign
                {
                    Id = armor.Id,
                    Kind = IconFactory.ItemKind.Armor,
                    Motif = MotifFor(armor.Id, new[] { "plate", "pad", "reinforc", "power" }, 4),
                    Primary = ArmorColor(armor.Id),
                    Secondary = RuinPalette.DarkSteel,
                    Accent = RuinPalette.AmberActive,
                    Seed = StableHash(armor.Id)
                });

            foreach (var accessory in Load<AccessoryDefinition>())
                designs.Add(new IconFactory.IconDesign
                {
                    Id = accessory.Id,
                    Kind = IconFactory.ItemKind.Accessory,
                    Motif = AccessoryMotif(accessory.Id),
                    Primary = RuinPalette.Hex("#5A6264"),
                    Secondary = RuinPalette.DarkSteel,
                    Accent = AccessoryAccent(accessory.Id),
                    Seed = StableHash(accessory.Id)
                });

            foreach (var consumable in Load<ConsumableDefinition>())
                designs.Add(new IconFactory.IconDesign
                {
                    Id = consumable.Id,
                    Kind = IconFactory.ItemKind.Consumable,
                    Motif = ConsumableMotif(consumable.Id),
                    Primary = RuinPalette.Hex("#6E7472"),
                    Secondary = RuinPalette.DarkSteel,
                    Accent = ConsumableAccent(consumable.Id),
                    Seed = StableHash(consumable.Id)
                });

            foreach (var ammo in Load<AmmoItemDefinition>())
                designs.Add(new IconFactory.IconDesign
                {
                    Id = ammo.Id,
                    Kind = IconFactory.ItemKind.Ammo,
                    Motif = (int)ammo.AmmoType,
                    Seed = StableHash(ammo.Id)
                });

            return designs;
        }

        private static List<T> Load<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null)
                .OrderBy(a => AssetDatabase.GetAssetPath(a), System.StringComparer.Ordinal)
                .ToList();

        /// <summary>Deterministic, framework-independent hash so icons never shuffle between runs.</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                var h = 17;
                foreach (var c in s ?? string.Empty) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        private static int MotifFor(string id, string[] keywords, int fallbackCount)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            for (var i = 0; i < keywords.Length; i++)
                if (lower.Contains(keywords[i])) return i;
            return StableHash(id) % fallbackCount;
        }

        /// <summary>Armour palette follows its material class: cloth, canvas, steel or powered.</summary>
        private static Color32 ArmorColor(string id)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("cloth") || lower.Contains("scav")) return RuinPalette.Hex("#6B6355");
            if (lower.Contains("canvas") || lower.Contains("field")) return RuinPalette.Hex("#5C6551");
            if (lower.Contains("power") || lower.Contains("proto")) return RuinPalette.Hex("#6E7678");
            return RuinPalette.Hex("#585F60");
        }

        /// <summary>Eight accessory metaphors, chosen from the accessory's own name where it says something.</summary>
        private static int AccessoryMotif(string id)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("module") || lower.Contains("chip") || lower.Contains("core")) return 0;
            if (lower.Contains("lens") || lower.Contains("optic") || lower.Contains("scope") || lower.Contains("sight")) return 1;
            if (lower.Contains("cell") || lower.Contains("batt") || lower.Contains("power")) return 2;
            if (lower.Contains("inject") || lower.Contains("stim") || lower.Contains("serum") || lower.Contains("vial")) return 3;
            if (lower.Contains("charm") || lower.Contains("token") || lower.Contains("tag") || lower.Contains("medal")) return 4;
            if (lower.Contains("sensor") || lower.Contains("scan") || lower.Contains("radar")) return 5;
            if (lower.Contains("plate") || lower.Contains("brace") || lower.Contains("frame") || lower.Contains("rig")) return 6;
            if (lower.Contains("coil") || lower.Contains("emit") || lower.Contains("field")) return 7;
            return StableHash(id) % 8;
        }

        private static Color32 AccessoryAccent(string id)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("heal") || lower.Contains("med") || lower.Contains("regen")) return RuinPalette.TerminalGreen;
            if (lower.Contains("heat") || lower.Contains("fire") || lower.Contains("burn")) return RuinPalette.OxideOrange;
            if (lower.Contains("shield") || lower.Contains("guard") || lower.Contains("armor")) return RuinPalette.ColdBlue;
            return RuinPalette.AmberActive;
        }

        private static int ConsumableMotif(string id)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("kit") || lower.Contains("bandage") || lower.Contains("medkit")) return 0;
            if (lower.Contains("stim") || lower.Contains("inject") || lower.Contains("syringe") || lower.Contains("adren")) return 1;
            if (lower.Contains("canister") || lower.Contains("tank") || lower.Contains("gas") || lower.Contains("flask")) return 2;
            if (lower.Contains("grenade") || lower.Contains("bomb") || lower.Contains("charge") || lower.Contains("frag")) return 3;
            return 4;
        }

        private static Color32 ConsumableAccent(string id)
        {
            var lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("grenade") || lower.Contains("bomb") || lower.Contains("frag")) return RuinPalette.EmergencyRed;
            if (lower.Contains("stim") || lower.Contains("adren") || lower.Contains("speed")) return RuinPalette.AmberActive;
            if (lower.Contains("shield") || lower.Contains("guard")) return RuinPalette.ColdBlue;
            return RuinPalette.TerminalGreen;
        }
    }
}
