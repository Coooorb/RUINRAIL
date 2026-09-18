using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Turns a LootResult into shared world pickups around a position: one WorldItemPickup per item instance and one
    /// CoinPickup for the coins. Pickups are plain objects (optionally from prefabs) — no per-player instancing.
    /// Every spawned pickup is tracked by the ground registry (when bound) so it lives exactly as long as the depth,
    /// and every bare pickup draws the final ground-item / coin art through <see cref="WorldObjectVisual"/>.
    /// </summary>
    public sealed class LootSpawner : MonoBehaviour, IWorldPickupFactory
    {
        [SerializeField] private WorldItemPickup _itemPickupPrefab;
        [SerializeField] private CoinPickup _coinPickupPrefab;
        [SerializeField, Min(0f)] private float _scatterRadius = 0.8f;

        private GroundLootRegistry _registry;
        private Func<string, ItemDefinition> _resolveDefinition;

        public GroundLootRegistry Registry => _registry;

        public void SetRegistry(GroundLootRegistry registry) => _registry = registry;

        /// <summary>Lets spawned pickups know their item category (attraction eligibility) and display name; optional.</summary>
        public void SetDefinitionResolver(Func<string, ItemDefinition> resolveDefinition) => _resolveDefinition = resolveDefinition;

        public List<GameObject> Spawn(LootResult result, Vector2 origin, Transform parent = null)
        {
            var spawned = new List<GameObject>();
            if (result == null)
            {
                return spawned;
            }

            var index = 0;
            foreach (var item in result.Items)
            {
                // Filled before it is tracked, so observers of the registry see a complete pickup (item, rarity, name).
                var pickup = _itemPickupPrefab != null
                    ? Instantiate(_itemPickupPrefab, ScatterPosition(origin, index), Quaternion.identity, parent)
                    : CreateBarePickup<WorldItemPickup>("ItemPickup", ScatterPosition(origin, index), parent, WorldObjectArt.ItemPickup);
                pickup.name = $"Pickup_{item.DefinitionId}";
                pickup.Hold(item, CategoryOf(item));
                pickup.SetDisplayName(DisplayNameOf(item));
                _registry?.Track(pickup.gameObject);
                spawned.Add(pickup.gameObject);
                index++;
            }

            if (result.Coins > 0)
            {
                var coins = _coinPickupPrefab != null
                    ? Instantiate(_coinPickupPrefab, ScatterPosition(origin, index), Quaternion.identity, parent)
                    : CreateBarePickup<CoinPickup>("CoinPickup", ScatterPosition(origin, index), parent, WorldObjectArt.CoinPickup);
                coins.SetAmount(result.Coins);
                _registry?.Track(coins.gameObject);
                spawned.Add(coins.gameObject);
            }

            return spawned;
        }

        /// <summary>An empty, tracked item pickup at a position (used by loot spawning and player drops alike).</summary>
        public WorldItemPickup CreateItemPickup(Vector2 position, Transform parent = null)
        {
            var pickup = _itemPickupPrefab != null
                ? Instantiate(_itemPickupPrefab, position, Quaternion.identity, parent)
                : CreateBarePickup<WorldItemPickup>("ItemPickup", position, parent, WorldObjectArt.ItemPickup);
            _registry?.Track(pickup.gameObject);
            return pickup;
        }

        public CoinPickup CreateCoinPickup(Vector2 position, Transform parent = null)
        {
            var coins = _coinPickupPrefab != null
                ? Instantiate(_coinPickupPrefab, position, Quaternion.identity, parent)
                : CreateBarePickup<CoinPickup>("CoinPickup", position, parent, WorldObjectArt.CoinPickup);
            _registry?.Track(coins.gameObject);
            return coins;
        }

        public ItemCategory? CategoryOf(ItemInstance item)
        {
            if (item == null || _resolveDefinition == null) return null;
            var definition = _resolveDefinition(item.DefinitionId);
            return definition != null ? definition.Category : null;
        }

        public string DisplayNameOf(ItemInstance item)
        {
            if (item == null || _resolveDefinition == null) return string.Empty;
            var definition = _resolveDefinition(item.DefinitionId);
            return definition != null ? definition.DisplayName : string.Empty;
        }

        private Vector2 ScatterPosition(Vector2 origin, int index)
        {
            // Deterministic ring so identical loot lands at identical positions (no random scatter). Every pickup lands
            // beside the container, never on its own origin where the opened crate would cover it.
            var angle = (index + 1) * 137.5f * Mathf.Deg2Rad;
            return origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _scatterRadius;
        }

        private static T CreateBarePickup<T>(string name, Vector2 position, Transform parent, string artKey) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;
            var component = go.AddComponent<T>();
            // Ground pickups sit on the Loot layer (art/102): small, always readable over the floor, never under a body.
            WorldObjectVisual.Attach(go, artKey, SortingRole.Loot);
            return component;
        }
    }

    /// <summary>Creates empty ground pickups; the drop service fills them through the transfer service.</summary>
    public interface IWorldPickupFactory
    {
        WorldItemPickup CreateItemPickup(Vector2 position, Transform parent = null);
        ItemCategory? CategoryOf(ItemInstance item);
    }
}
