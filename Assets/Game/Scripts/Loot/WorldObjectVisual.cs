using System;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// The world-object art seam: the composition root registers how an art key (the Art/World file stem) resolves
    /// to a sprite; chests, pickups, coins, the merchant, event objects and the transit car draw through it. Gameplay
    /// never loads by path and never depends on the presentation assembly; without a resolver nothing is drawn.
    /// </summary>
    public static class WorldObjectArt
    {
        public const string SupplyChest = "world_supply_chest";
        public const string SupplyChestOpen = "world_supply_chest_open";
        public const string BossCacheGate = "world_boss_cache_gate";
        public const string ItemPickup = "world_item_pickup";
        public const string CoinPickup = "world_coin_pickup";
        public const string DungeonMerchant = "world_dungeon_merchant";
        public const string TransitCar = "world_transit_car";
        public const string EventPrefix = "event_";

        /// <summary>Every key the dungeon runtime draws; the content catalog must bind all of them.</summary>
        public static readonly string[] RequiredKeys =
        {
            SupplyChest, SupplyChestOpen, BossCacheGate, ItemPickup, CoinPickup, DungeonMerchant, TransitCar,
            EventPrefix + "CursedChest", EventPrefix + "LockedVault", EventPrefix + "BrokenMachine",
            EventPrefix + "SupplySignal", EventPrefix + "MedicalStation", EventPrefix + "WeaponCache"
        };

        public static Func<string, Sprite> Resolver { get; set; }

        public static Sprite Resolve(string key) => string.IsNullOrEmpty(key) ? null : Resolver?.Invoke(key);

        public static string EventKey(string kind) => EventPrefix + kind;
    }

    /// <summary>
    /// One SpriteRenderer on a world object, sorted by the art/102 convention (standing objects y-sort with the
    /// characters, ground pickups on the Loot layer). Re-showing a key swaps the sprite (closed → opened chest);
    /// a tint marks a consumed/resolved object.
    /// </summary>
    public sealed class WorldObjectVisual : MonoBehaviour
    {
        /// <summary>Resolved/consumed objects (an activated event) dim to this tint so their state reads at a glance.</summary>
        public static readonly Color ResolvedTint = new(0.55f, 0.55f, 0.55f, 1f);

        private SpriteRenderer _renderer;
        private SortingRole _role = SortingRole.Character;
        private float _feetOffset;

        public SpriteRenderer Renderer => _renderer;
        public string Key { get; private set; } = string.Empty;
        public SortingRole Role => _role;
        public bool IsVisible => _renderer != null && _renderer.enabled && _renderer.sprite != null;

        public static WorldObjectVisual Attach(GameObject host, string key, SortingRole role, float feetOffset = 0f)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var visual = host.GetComponent<WorldObjectVisual>();
            if (visual == null) visual = host.AddComponent<WorldObjectVisual>();
            visual.Build(role, feetOffset);
            visual.Show(key);
            return visual;
        }

        private void Build(SortingRole role, float feetOffset)
        {
            _role = role;
            _feetOffset = feetOffset;
            if (_renderer != null) return;
            var child = new GameObject("Visual");
            child.transform.SetParent(transform, false);
            _renderer = child.AddComponent<SpriteRenderer>();
            _renderer.sortingLayerName = SortingConvention.LayerOf(role);
            Refresh();
        }

        /// <summary>Draws the art for a key; an unresolvable key hides the renderer rather than drawing a placeholder.</summary>
        public void Show(string key)
        {
            Key = key ?? string.Empty;
            var sprite = WorldObjectArt.Resolve(Key);
            if (_renderer == null) return;
            _renderer.sprite = sprite;
            _renderer.enabled = sprite != null;
            Refresh();
        }

        public void SetTint(Color tint)
        {
            if (_renderer != null) _renderer.color = tint;
        }

        private void Refresh()
        {
            if (_renderer == null) return;
            _renderer.sortingOrder = SortingConvention.OrderOf(_role, transform.position.y + _feetOffset);
        }

        private void LateUpdate()
        {
            if (SortingConvention.IsYSorted(_role)) Refresh();
        }
    }
}
