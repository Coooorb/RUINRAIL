using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>Anything that rolls loot: a table plus a source quality. Chest types differ only in data (58).</summary>
    public interface ILootSource
    {
        LootTableDefinition Table { get; }
        LootQuality Quality { get; }
    }

    /// <summary>
    /// Supply Chest: opens exactly once. Loot is rolled from the injected deterministic context the first time
    /// TryOpen succeeds and spawned as shared world pickups; every later interaction is a no-op that returns false.
    /// Equipment/Treasure/Boss Cache reuse this component with other tables/qualities.
    ///
    /// Presentation: the chest draws the final world art through <see cref="WorldObjectVisual"/> — closed crate,
    /// opened crate after the one reward transaction, the cache gate while locked — so a container is readable as an
    /// interactable before any prompt, and its opened state is obvious for the rest of the depth.
    /// </summary>
    public sealed class SupplyChest : MonoBehaviour, ILootSource, IInteractable, IInteractionPrompt
    {
        [SerializeField] private LootTableDefinition _table;
        [SerializeField] private LootQuality _quality = LootQuality.Standard;
        [SerializeField] private LootSpawner _spawner;

        private LootContext _context;
        private LootRoller _roller;
        private bool _isOpened;
        private bool _isLocked;
        private WorldObjectVisual _visual;

        public LootTableDefinition Table => _table;
        public LootQuality Quality => _quality;
        public bool IsOpened => _isOpened;

        /// <summary>Which of the four approved sources this chest is (data only; behaviour is identical).</summary>
        public LootSourceKind Kind { get; private set; } = LootSourceKind.SupplyChest;

        /// <summary>A locked source (Boss Cache before the boss falls) cannot be opened; the roll is deferred, never lost.</summary>
        public bool IsLocked => _isLocked;
        public LootResult LastResult { get; private set; }
        public IReadOnlyList<GameObject> SpawnedPickups { get; private set; } = Array.Empty<GameObject>();

        /// <summary>The chest's world art (null until <see cref="AttachVisual"/> ran).</summary>
        public WorldObjectVisual Visual => _visual;

        public event Action<SupplyChest, LootResult> Opened;
        public event Action<SupplyChest, bool> LockChanged;

        public void Configure(LootTableDefinition table, LootQuality quality, LootContext context, LootRoller roller, LootSpawner spawner = null)
        {
            _table = table;
            _quality = quality;
            _context = context;
            _roller = roller;
            _spawner = spawner != null ? spawner : _spawner;
        }

        public void SetKind(LootSourceKind kind) => Kind = kind;

        public void SetLocked(bool locked)
        {
            if (_isLocked == locked) return;
            _isLocked = locked;
            RefreshVisual();
            LockChanged?.Invoke(this, locked);
        }

        /// <summary>Restores a persisted opened state (room revisit / sync): the chest never rolls again.</summary>
        public void RestoreOpened()
        {
            _isOpened = true;
            RefreshVisual();
        }

        /// <summary>Draws the chest with the final world art; the sprite follows the closed / opened / locked state from here on.</summary>
        public WorldObjectVisual AttachVisual()
        {
            _visual = WorldObjectVisual.Attach(gameObject, ArtKey, SortingRole.Character);
            return _visual;
        }

        /// <summary>The art key for the current state: the cache gate while locked, the opened crate once looted, the closed crate otherwise.</summary>
        public string ArtKey => _isLocked ? WorldObjectArt.BossCacheGate : _isOpened ? WorldObjectArt.SupplyChestOpen : WorldObjectArt.SupplyChest;

        private void RefreshVisual()
        {
            if (_visual != null) _visual.Show(ArtKey);
        }

        public bool CanInteract(GameObject interactor) => !_isOpened && !_isLocked && _table != null && _context != null && _roller != null;

        public string PromptFor(GameObject interactor)
        {
            if (_isOpened) return string.Empty;
            if (_isLocked) return "LOCKED";
            return CanInteract(interactor) ? (Kind == LootSourceKind.BossCache ? "OPEN BOSS CACHE" : "OPEN CHEST") : string.Empty;
        }

        public bool Interact(GameObject interactor) => TryOpen(out _);

        /// <summary>Opens the chest once. Returns false (and rolls nothing) when already opened or not configured.</summary>
        public bool TryOpen(out LootResult result)
        {
            result = null;
            if (_isOpened || _isLocked || _table == null || _context == null || _roller == null)
            {
                return false;
            }

            // State flips before anything is spawned so a re-entrant callback can never roll twice.
            _isOpened = true;
            RefreshVisual();
            result = _roller.Roll(_table, _context);
            LastResult = result;

            if (_spawner != null)
            {
                SpawnedPickups = _spawner.Spawn(result, transform.position, transform.parent);
            }

            Opened?.Invoke(this, result);
            return true;
        }
    }
}
