using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Binds each room's category behaviour from its RoomDefinition metadata (55) — never from prefab or scene names:
    /// Loot/Treasure → one-time chests at ChestSpawn markers; Merchant → the depth's single merchant stock at the
    /// MerchantAnchor; Event → exactly one authored event at the EventAnchor (kind pinned by an "event:&lt;kind&gt;" tag
    /// or picked by seed); Medical/Recovery → the Medical Station; Boss → boss engagement, Boss Cache and Transit Car.
    /// Resolved interactions are recorded in the room state, so a revisit restores them instead of regenerating.
    /// </summary>
    public static class RoomCategoryComposer
    {
        public const string EventTagPrefix = "event:";
        private const int EventKindSalt = 0x4B4E; // "KN"
        public const int ChestSourceStride = 16;

        public static readonly DungeonEventKind[] RandomEventKinds =
        {
            DungeonEventKind.CursedChest,
            DungeonEventKind.LockedVault,
            DungeonEventKind.BrokenMachine,
            DungeonEventKind.SupplySignal,
            DungeonEventKind.WeaponCache
        };

        public static RoomContentBinding Compose(RoomRuntime room, DungeonRuntimeContext context, DungeonRuntimeServices services)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            services ??= new DungeonRuntimeServices();
            var binding = RoomContentBinding.For(room);
            switch (room.State.RoomType)
            {
                case RoomType.Loot:
                    BindChests(room, binding, context, services, LootSourceKind.EquipmentChest);
                    break;
                case RoomType.Treasure:
                    BindChests(room, binding, context, services, LootSourceKind.TreasureChest);
                    break;
                case RoomType.Merchant:
                    BindMerchant(room, binding, context, services);
                    break;
                case RoomType.Event:
                    BindEvent(room, binding, context, services, ResolveEventKind(room, context));
                    break;
                case RoomType.MedicalRecovery:
                    BindEvent(room, binding, context, services, DungeonEventKind.MedicalStation);
                    break;
                case RoomType.Boss:
                    BindBoss(room, binding, context, services);
                    break;
                case RoomType.Combat:
                    if (context.HasSupplyChest(room.State.NodeId)) BindSupplyChest(room, binding, context, services);
                    if (room.State.IsElite) BindEliteReward(room, binding, context, services);
                    break;
            }

            return binding;
        }

        // ---- Loot / Treasure ----

        private static void BindChests(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services, LootSourceKind kind)
        {
            if (!services.HasLoot)
            {
                binding.Skipped.Add("chests:no_loot_catalog");
                return;
            }

            var markers = room.Root.GetMarkers(RoomMarkerRole.ChestSpawn);
            if (markers.Count == 0) binding.Skipped.Add("chests:no_chest_markers");
            for (var i = 0; i < markers.Count; i++)
            {
                var chest = CreateChest(room, services, markers[i], $"Chest_{i}", kind, context, room.State.NodeId * ChestSourceStride + i);
                var resolvedId = $"chest:{i}";
                if (room.State.IsResolved(resolvedId)) chest.RestoreOpened();
                chest.Opened += (_, _) => room.State.MarkResolved(resolvedId);
                binding.Chests.Add(chest);
            }
        }

        /// <summary>
        /// Ordinary-room Supply Chest (58 "common" container): rooms the SupplyChestPlanner selected get exactly one, on a
        /// seeded walkable cell clear of doors, spawn points and markers (SupplyChestPlacement). A room without a valid
        /// cell is reported and skipped rather than patched. Source index 15 of the room's stride keeps its loot stream
        /// apart from marker chests (index 0..14).
        /// </summary>
        public const int SupplyChestSourceSlot = ChestSourceStride - 1;
        public const string SupplyChestResolvedId = "chest:supply";
        public const string BossCacheResolvedId = "boss_cache";

        private static void BindSupplyChest(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services)
        {
            if (!services.HasLoot)
            {
                binding.Skipped.Add("supply_chest:no_loot_catalog");
                return;
            }

            var cell = SupplyChestPlacement.Choose(room.Root, context.RunSeed, context.Depth, room.State.NodeId);
            if (!cell.HasValue)
            {
                binding.Skipped.Add("supply_chest:no_valid_cell");
                return;
            }

            var chest = CreateChest(room, services, SupplyChestPlacement.WorldCenter(room.Root, cell.Value), "SupplyChest", LootSourceKind.SupplyChest, context, room.State.NodeId * ChestSourceStride + SupplyChestSourceSlot);
            if (room.State.IsResolved(SupplyChestResolvedId)) chest.RestoreOpened();
            chest.Opened += (_, _) => room.State.MarkResolved(SupplyChestResolvedId);
            binding.SupplyChestCell = cell;
            binding.Chests.Add(chest);
        }

        // ---- Elite (mini-boss) reward ----

        /// <summary>Loot-seed slot of an Elite room's reward chest (apart from the marker chests 0..13 and the supply slot).</summary>
        public const int EliteRewardSourceSlot = ChestSourceStride - 2;
        public const string EliteRewardResolvedId = "chest:elite_reward";

        /// <summary>45: defeating the room's Elite leaves exactly one normal chest (a Supply Chest) at the room's playable centre.</summary>
        private static void BindEliteReward(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services)
        {
            if (!services.HasLoot)
            {
                binding.Skipped.Add("elite_reward:no_loot_catalog");
                return;
            }

            EncounterRewardChest.Bind(room, binding, EliteRewardResolvedId,
                () => room.State.State == RoomLifecycleState.Cleared,
                position => CreateChest(room, services, position, "EliteRewardChest", LootSourceKind.SupplyChest, context, room.State.NodeId * ChestSourceStride + EliteRewardSourceSlot));
        }

        private static SupplyChest CreateChest(RoomRuntime room, DungeonRuntimeServices services, RoomMarker marker, string name, LootSourceKind kind, DungeonRuntimeContext context, int sourceIndex) =>
            CreateChest(room, services, room.Root.transform.TransformPoint(marker.WorldCenter), name, kind, context, sourceIndex);

        private static SupplyChest CreateChest(RoomRuntime room, DungeonRuntimeServices services, Vector2 worldPosition, string name, LootSourceKind kind, DungeonRuntimeContext context, int sourceIndex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room.transform, false);
            go.transform.position = worldPosition;
            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;
            var spawner = services.CreateLootSpawner(go);
            var chest = go.AddComponent<SupplyChest>();
            services.LootCatalog.Configure(chest, kind, context.RunSeed, context.Depth, sourceIndex, context.PartySize, services.UsefulAmmoTypes, spawner,
                services.Prices?.Config);
            chest.AttachVisual(); // final crate art; the sprite follows closed / opened / locked from here on
            return chest;
        }

        // ---- Merchant ----

        private static void BindMerchant(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services)
        {
            if (!services.HasMerchant)
            {
                binding.Skipped.Add("merchant:services_missing");
                return;
            }

            var anchor = room.Root.GetMarkers(RoomMarkerRole.MerchantAnchor).FirstOrDefault();
            if (anchor == null)
            {
                binding.Skipped.Add("merchant:no_anchor");
                return;
            }

            var service = new DungeonMerchantService(services.MerchantConfig, services.Prices, services.CarriedWallet, services.MerchantStateFor(context.Depth),
                context.RunSeed, context.PartySize, services.Items, services.ResolveDefinition ?? (_ => null), services.RarityTables);
            var go = CreateAnchorObject(room, anchor, "Merchant", WorldObjectArt.DungeonMerchant);
            var interactable = go.AddComponent<DungeonMerchantInteractable>();
            interactable.Bind(service);
            binding.Merchant = interactable;
        }

        // ---- Event / Medical ----

        public static DungeonEventKind ResolveEventKind(RoomRuntime room, DungeonRuntimeContext context) =>
            ResolveEventKind(room.Root.Definition != null ? room.Root.Definition.Tags : Array.Empty<string>(), context.RunSeed, context.Depth, room.State.NodeId);

        /// <summary>Pure form (planning / simulation): the authored "event:&lt;kind&gt;" tag wins, otherwise the seeded pick on the Loot stream.</summary>
        public static DungeonEventKind ResolveEventKind(IReadOnlyList<string> tags, int runSeed, int depth, int nodeId)
        {
            tags ??= Array.Empty<string>();
            foreach (var tag in tags)
            {
                if (tag == null || !tag.StartsWith(EventTagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var name = tag.Substring(EventTagPrefix.Length).Replace("_", string.Empty);
                foreach (DungeonEventKind kind in Enum.GetValues(typeof(DungeonEventKind)))
                {
                    if (string.Equals(kind.ToString(), name, StringComparison.OrdinalIgnoreCase)) return kind;
                }
            }

            var random = new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Loot, EventKindSalt, nodeId));
            return RandomEventKinds[random.NextInt(RandomEventKinds.Length)];
        }

        private static void BindEvent(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services, DungeonEventKind kind)
        {
            if (!services.HasEvents)
            {
                binding.Skipped.Add("event:services_missing");
                return;
            }

            var anchor = room.Root.GetMarkers(RoomMarkerRole.EventAnchor).FirstOrDefault();
            if (anchor == null)
            {
                binding.Skipped.Add("event:no_anchor");
                return;
            }

            var go = CreateAnchorObject(room, anchor, $"Event_{kind}", WorldObjectArt.EventKey(kind.ToString()));
            var spawner = services.CreateLootSpawner(go);
            var deliverer = new LootSpawnerDeliverer(spawner, () => go.transform.position, room.transform);
            var eventContext = new DungeonEventContext(context.RunSeed, context.Depth, room.State.NodeId, context.PartySize, room.Root.Definition != null ? room.Root.Definition.Biome : Biome.RuinedMetro, services.UsefulAmmoTypes);
            var rewards = new EventRewardRoller(services.LootCatalog, services.Prices?.Config);
            var tags = room.Root.Definition != null ? room.Root.Definition.Tags : null;
            IDungeonEvent instance = kind switch
            {
                DungeonEventKind.CursedChest => new CursedChestEvent(eventContext, services.EventConfig, rewards, deliverer, context.Archetypes, tags),
                DungeonEventKind.LockedVault => new LockedVaultEvent(eventContext, services.EventConfig, services.Prices, rewards, deliverer),
                DungeonEventKind.BrokenMachine => new BrokenMachineEvent(eventContext, services.EventConfig, services.Prices, rewards, deliverer),
                DungeonEventKind.SupplySignal => new SupplySignalEvent(eventContext, services.EventConfig, rewards, deliverer, context.Archetypes, tags),
                DungeonEventKind.MedicalStation => new MedicalStationEvent(eventContext, services.EventConfig, services.Prices, services.ReviveAuthority),
                _ => new WeaponCacheEvent(eventContext, services.EventConfig, services.Items, services.RarityTables)
            };

            var interactable = go.AddComponent<DungeonEventInteractable>();
            interactable.Bind(instance);
            binding.Event = interactable;
            binding.EventInstance = instance;

            var resolvedId = $"event:{kind}";
            var visual = go.GetComponent<WorldObjectVisual>();
            if (instance is DungeonEventBase restorable && room.State.IsResolved(resolvedId))
            {
                restorable.RestoreResolved(room.State.IsResolved(resolvedId + ":success"));
                visual?.SetTint(WorldObjectVisual.ResolvedTint);
            }

            instance.Completed += (_, result) =>
            {
                room.State.MarkResolved(resolvedId);
                if (result.Outcome == DungeonEventOutcome.Success) room.State.MarkResolved(resolvedId + ":success");
                visual?.SetTint(WorldObjectVisual.ResolvedTint); // a used event reads as used
            };

            HostEncounterEvents(room, interactable, instance, go);
        }

        /// <summary>Events that spawn enemies run them as extra encounters of the room; the Cursed Chest also locks doors.</summary>
        private static void HostEncounterEvents(RoomRuntime room, DungeonEventInteractable interactable, IDungeonEvent instance, GameObject host)
        {
            switch (instance)
            {
                case CursedChestEvent cursed:
                    cursed.EncounterStarted += (e, plan) =>
                    {
                        room.LockDoors();
                        var runtime = room.SpawnAdditionalEncounter(plan, interactable.LastActor?.GameObject);
                        if (runtime == null) { e.ReportEncounterFailed(); return; }
                        runtime.Completed += _ => e.ReportEncounterCleared();
                    };
                    cursed.Completed += (_, _) => { if (room.Lifecycle != RoomLifecycleState.Active) room.UnlockDoors(); };
                    break;
                case SupplySignalEvent signal:
                    signal.WaveStarted += (e, plan) => room.SpawnAdditionalEncounter(plan, interactable.LastActor?.GameObject);
                    host.AddComponent<SupplySignalTicker>().Bind(signal);
                    break;
            }
        }

        // ---- Boss ----

        private static void BindBoss(RoomRuntime room, RoomContentBinding binding, DungeonRuntimeContext context, DungeonRuntimeServices services)
        {
            var anchor = room.Root.GetMarkers(RoomMarkerRole.BossAnchor).FirstOrDefault();
            if (anchor == null)
            {
                binding.Skipped.Add("boss:no_anchor");
                return;
            }

            var alreadyBeaten = room.State.IsResolved("boss");
            if (!alreadyBeaten)
            {
                if (!services.HasBoss)
                {
                    binding.Skipped.Add("boss:no_spawner");
                }
                else
                {
                    var definition = room.Root.Definition;
                    var request = new BossSpawnRequest(definition != null ? definition.Biome : Biome.RuinedMetro, definition != null ? definition.Tags : null, context.RunSeed, context.Depth, room.State.NodeId, room.Root.transform.TransformPoint(anchor.WorldCenter), room.transform);
                    var encounter = services.BossSpawner.Spawn(request);
                    if (encounter != null)
                    {
                        binding.Boss = encounter;
                        if (encounter.Boss != null)
                        {
                            // The arena owns the boss: it never leaves the room interior, and the fight begins when the
                            // first player enters (BossEngagement.Begin hands over the target) — not when the actor
                            // happens to see a player elsewhere on the depth at spawn time.
                            room.BindEncounterBounds(encounter.Boss.gameObject);
                            // Attack choice draws from RunSeed + Depth + this room, so the same run replays the same
                            // sequence of boss attacks and a client rebuilding the encounter agrees with the host.
                            encounter.Boss.SetSelectionSeed(context.RunSeed, context.Depth, room.State.NodeId);
                            encounter.Boss.SetTarget(null);
                        }

                        if (encounter.Boss != null && encounter.Boss.Definition != null)
                        {
                            // 59/83: boss HP scales by depth then party (boss curve); damage bands by depth.
                            encounter.Boss.SetDamageRoller(new DepthScaledDamageRoller(new UnityRandomDamageRoller(), context.Depth, context.Scaling));
                            encounter.Boss.Health.SetMaxHealth(DepthScaling.ScaledHealth(encounter.Boss.Definition.BaseHealth, context.Depth, context.PartySize, true, context.Scaling));
                            // Boss summons are normal enemies: normal-HP party curve, never the boss multiplier; they stay in the arena too.
                            encounter.Boss.Summoned += (_, summons) =>
                            {
                                foreach (var summon in summons)
                                {
                                    EnemySpawnScaling.Apply(summon, context.Depth, context.PartySize, context.Scaling);
                                    room.BindEncounterBounds(summon.gameObject);
                                }
                            };
                        }

                        room.SetEngagement(new BossEngagement(encounter, BossEngagement.DefaultIntroHoldSeconds));
                        encounter.BossDefeated += (_, _) => room.State.MarkResolved("boss");
                    }
                    else
                    {
                        binding.Skipped.Add("boss:spawner_returned_null");
                    }
                }
            }

            // Boss Cache (58/46 "boss death spawns a high-quality Boss Cache"): the boss's one reward chest, created by the
            // shared encounter-reward path when the boss dies, at the arena's playable centre. Same loot kind and seed
            // slot as always, so its contents are unchanged; one-time like every source.
            if (services.HasLoot)
            {
                var boss = binding.Boss;
                var reward = EncounterRewardChest.Bind(room, binding, BossCacheResolvedId,
                    () => room.State.State == RoomLifecycleState.Cleared || room.State.IsResolved("boss") || (boss != null && boss.IsDefeated),
                    position => CreateChest(room, services, position, "BossCache", LootSourceKind.BossCache, context, room.State.NodeId * ChestSourceStride));
                // The defeat itself is a trigger too (after the "boss" marker above), so the cache never waits on the room.
                if (boss != null) boss.BossDefeated += (_, _) => reward.TrySpawn();
            }
            else
            {
                binding.Skipped.Add("boss_cache:no_loot_catalog");
            }

            // Transit Car: activates on boss defeat and records the defeat on the expedition (TransitCar owns that hook).
            var transitMarker = room.Root.GetMarkers(RoomMarkerRole.InteractableSpawn).FirstOrDefault();
            var transitAnchor = transitMarker != null ? transitMarker : anchor;
            var transitObject = CreateAnchorObject(room, transitAnchor, "TransitCar", WorldObjectArt.TransitCar);
            var transit = transitObject.AddComponent<TransitCar>();
            transit.Configure(services.Expedition, binding.Boss);
            if (alreadyBeaten) transit.Activate();
            binding.Transit = transit;
        }

        private static GameObject CreateAnchorObject(RoomRuntime room, RoomMarker marker, string name, string artKey)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room.transform, false);
            go.transform.position = room.Root.transform.TransformPoint(marker.WorldCenter);
            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;
            // Final world art for the interactable, y-sorted with the characters like every standing object.
            WorldObjectVisual.Attach(go, artKey, RuinRail.Core.Rendering.SortingRole.Character);
            return go;
        }
    }

    /// <summary>Advances a Supply Signal's survival timer while it runs.</summary>
    public sealed class SupplySignalTicker : MonoBehaviour
    {
        private SupplySignalEvent _signal;
        public void Bind(SupplySignalEvent signal) => _signal = signal;
        private void Update() => _signal?.Tick(Time.deltaTime);
    }
}
