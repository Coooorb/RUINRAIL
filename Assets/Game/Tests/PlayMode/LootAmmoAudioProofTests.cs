using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof (real boot flow → Shelter → generated dungeon, real rooms, real chests, real player) for the
    /// loot / ammo / starter-loadout / audio runtime pass. Captures and the deterministic evidence go to
    /// <c>TestResults/LootAmmoAudioProof</c> (prefixed <c>live_</c>; the shipped-player smoke writes the numbered set).
    /// </summary>
    public sealed class LootAmmoAudioProofTests
    {
        private const string Folder = "TestResults/LootAmmoAudioProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_lootproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder, "live_*")) File.Delete(stale);
            CursorService.SetApplier(_ => true);
            AudioLevels.Reset();
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "live_loot_ammo_evidence.txt"), _evidence.ToString());
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            WorldObjectArt.Resolver = null;
            CursorService.Reset();
            AudioLevels.Reset();
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private void Note(string line)
        {
            _evidence.AppendLine(line);
            Debug.Log("[PROOF] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        /// <summary>A seed whose first biome is the requested one (BiomeSelector is deterministic per seed).</summary>
        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        private IEnumerator EnterDungeon(int seed)
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Loot Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position, int settleFrames = 10)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < settleFrames; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_ChestsVisible_OpenOnce_AmmoPickupIncrementsReserve_StarterKnifeWorksAtZeroReserve_LootRoomsNeverEmpty()
        {
            yield return EnterDungeon(SeedFor(Biome.RuinedMetro));
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var ortho = LiveDungeonCapture.Height / (2f * ppu);
            var camera = run.Camera.Camera;
            var player = run.Rig.Player;
            var inventory = run.Expedition.State.Inventory;
            Note($"biome {run.Expedition.State.Biome} seed {run.Expedition.State.RunSeed} depth {run.Expedition.State.Depth} rooms {run.Rooms.Count}");

            // 7. Starter inventory: Primary P9 Ranger + ammo-free Secondary Field Knife, both mounted on the rig.
            Assert.AreEqual(StarterKitService.PistolId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId);
            Assert.AreEqual(StarterKitService.KnifeId, inventory.GetEquipped(EquippedSlot.SecondaryWeapon)?.DefinitionId);
            Assert.IsInstanceOf<RangedWeapon>(run.Rig.Loadout.GetSlot(WeaponSlot.Primary));
            Assert.IsInstanceOf<MeleeWeapon>(run.Rig.Loadout.GetSlot(WeaponSlot.Secondary));
            Assert.AreEqual(WeaponSlot.Primary, run.Rig.Loadout.ActiveSlot);
            Assert.AreEqual(60, inventory.Get(AmmoType.Light));
            CollectionAssert.AreEquivalent(new[] { AmmoType.Light }, run.UsefulAmmoTypes, "the carried firearm's ammo type is the useful type for loot rolls");
            LiveDungeonCapture.Capture(Folder, "live_07_starter_inventory_p9_and_field_knife", camera, ppu, includeUi: true);
            Note("starter: primary weapon_p9_ranger (RangedWeapon), secondary weapon_field_knife (MeleeWeapon), Light 60, useful ammo = Light");

            // Every room's content: loot/treasure rooms carry visible chests; the boss room a locked cache; ≥ 2 supply chests in ordinary rooms.
            var bindings = run.Rooms.Values.Select(r => (room: r, binding: r.GetComponent<RoomContentBinding>())).ToList();
            var lootRooms = bindings.Where(b => b.room.State.RoomType == RoomType.Loot || b.room.State.RoomType == RoomType.Treasure).ToList();
            foreach (var (room, binding) in lootRooms)
            {
                Assert.Greater(binding.Chests.Count, 0, $"{room.State.RoomId}: a {room.State.RoomType} room is never empty");
                Assert.IsTrue(binding.Chests.All(c => c.Visual != null && c.Visual.IsVisible && !c.IsOpened), room.State.RoomId);
                Note($"{room.State.RoomType} room {room.State.RoomId}: {binding.Chests.Count} chest(s) [{string.Join(",", binding.Chests.Select(c => c.Kind))}] drawn, closed");
            }

            var bossBinding = bindings.First(b => b.room.State.RoomType == RoomType.Boss).binding;
            Assert.IsNull(bossBinding.BossCache, "the Boss Cache appears only when the boss dies (46), never before");
            Assert.IsNotNull(bossBinding.Boss, "the boss is composed; its death spawns the cache");
            var supply = bindings.Where(b => b.binding != null && b.binding.SupplyChest != null).ToList();
            Assert.GreaterOrEqual(supply.Count, SupplyChestPlanner.MinimumPerDepth, "ordinary rooms carry the guaranteed floor of supply chests");
            Assert.IsTrue(supply.All(b => b.room.State.RoomType == RoomType.Combat));
            Note($"supply chests in ordinary rooms: {supply.Count} ({string.Join(", ", supply.Select(s => s.room.State.RoomId + "@" + s.binding.SupplyChestCell))}); chests total {bindings.Sum(b => b.binding != null ? b.binding.Chests.Count : 0)} + boss cache");
            foreach (var (room, binding) in supply)
            {
                var chest = binding.SupplyChest;
                var cell = binding.SupplyChestCell.Value;
                var grid = RoomLogicGrid.FromRoom(room.Root);
                Assert.IsTrue(grid.IsWalkable(cell), $"{room.State.RoomId}: chest on walkable floor");
                Assert.IsTrue(RoomRuntime.IsSpawnClear(chest.transform.position), $"{room.State.RoomId}: chest not inside solid geometry");
                Assert.AreEqual(SortingLayers.Characters, chest.Visual.Renderer.sortingLayerName, "standing objects y-sort with the characters, never under the floor");
                Assert.IsTrue(chest.GetComponent<Collider2D>().isTrigger, "a chest never blocks movement");
            }

            // 1–3. A closed chest in a generated room, opened exactly once, loot presented.
            var (chestRoom, chestBinding) = supply[0];
            var target = chestBinding.SupplyChest;
            var chestPos = (Vector2)target.transform.position;
            yield return Teleport(run, chestPos + Vector2.down * 0.9f);
            var interactor = player.GetComponent<PlayerInteractor>();
            Assert.AreSame(target, interactor.FindNearestInteractable(), "the chest is the nearest usable interactable");
            Assert.IsTrue(run.CurrentInteractionPrompt.Contains("OPEN CHEST"), run.CurrentInteractionPrompt);
            Assert.AreEqual(WorldObjectArt.SupplyChest, target.Visual.Key);
            LiveDungeonCapture.Capture(Folder, "live_01_closed_supply_chest", camera, chestPos, ortho, ppu, includeUi: true);
            Note($"chest {chestRoom.State.RoomId} cell {chestBinding.SupplyChestCell} world {chestPos}: closed, prompt '{run.CurrentInteractionPrompt}'");

            var audio = _app.Audio;
            var chestOpens = audio.PlayedCount(AudioEventIds.ChestOpen);
            Assert.IsTrue(interactor.TryInteract(), "the Interact press opens the chest");
            yield return null;
            Assert.IsTrue(target.IsOpened);
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, target.Visual.Key, "opened state is drawn");
            Assert.IsFalse(target.LastResult.IsEmpty);
            Assert.Greater(target.SpawnedPickups.Count, 0);
            // A pickup the survivor's baseline attraction reach has already drawn in is gone from the scene, which is
            // the QoL behaviour rather than a missing visual; every pickup still on the ground must be drawn.
            Assert.IsTrue(target.SpawnedPickups.Where(p => p != null).All(p => p.GetComponent<WorldObjectVisual>() != null && p.GetComponent<WorldObjectVisual>().IsVisible && p.GetComponent<WorldObjectVisual>().Renderer.sortingLayerName == SortingLayers.Loot));
            Assert.IsFalse(target.TryOpen(out _), "a second open is refused");
            Assert.AreEqual(chestOpens + 1, audio.PlayedCount(AudioEventIds.ChestOpen), "chest open cue played once");
            Assert.IsTrue(chestRoom.State.IsResolved(RoomCategoryComposer.SupplyChestResolvedId), "opened state recorded in the room state");
            LiveDungeonCapture.Capture(Folder, "live_02_supply_chest_opened", camera, chestPos, ortho, ppu, includeUi: false);
            LiveDungeonCapture.Capture(Folder, "live_03_loot_spawned", camera, chestPos, ortho, ppu, includeUi: true);
            Note($"opened once: items [{string.Join(", ", target.LastResult.Items.Select(i => i.DefinitionId + " x" + i.Quantity))}] coins {target.LastResult.Coins}; pickups {target.SpawnedPickups.Count}");

            // 4–5. Ammo pickup: reserve before/after, consumed once, cap respected.
            var ammoPickup = target.SpawnedPickups.Where(p => p != null).Select(p => p.GetComponent<WorldItemPickup>()).First(p => p != null && !p.IsConsumed && p.Category == ItemCategory.Ammo);
            var ammoType = content.Items.OfType<AmmoItemDefinition>().First(a => a.Id == ammoPickup.Item.DefinitionId).AmmoType;
            var before = inventory.Get(ammoType);
            var quantity = ammoPickup.Item.Quantity;
            var collectedId = ammoPickup.Item.InstanceId;
            Assert.Greater(quantity, 0);
            LiveDungeonCapture.Capture(Folder, "live_04_ammo_pickup_before_collection", camera, chestPos, ortho, ppu, includeUi: true);
            yield return Teleport(run, ammoPickup.transform.position, 3);
            Assert.IsTrue(ammoPickup == null || ammoPickup.IsConsumed || ammoPickup.Interact(player), "collected by the attractor or the interaction");
            yield return null;
            var after = inventory.Get(ammoType);
            var cap = content.AmmoBalance.GetStackLimit(ammoType);
            Assert.AreEqual(Mathf.Min(cap, before + quantity), after, "reserve incremented by exactly the stack, capped");
            Assert.IsTrue(ammoPickup == null || ammoPickup.IsConsumed, "the pickup is gone");
            Assert.AreEqual(0, Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Count(p => !p.IsConsumed && p.Item != null && p.Item.InstanceId == collectedId), "no duplicate of the collected stack on the ground");
            LiveDungeonCapture.Capture(Folder, "live_05_reserve_increased_after_pickup", camera, chestPos, ortho, ppu, includeUi: true);
            Note($"ammo pickup {ammoType} x{quantity}: reserve {before} → {after} (cap {cap}); pickup sound plays {audio.PlayedCount(AudioEventIds.PickupItem)}");
            Assert.Greater(audio.PlayedCount(AudioEventIds.PickupItem), 0);

            // 6. A Treasure/Loot room's guaranteed reward source where it stands (if this depth has one).
            if (lootRooms.Count > 0)
            {
                var reward = lootRooms[0].binding.Chests[0];
                yield return Teleport(run, (Vector2)reward.transform.position + Vector2.down * 1.5f);
                LiveDungeonCapture.Capture(Folder, "live_06_loot_room_guaranteed_reward_source", camera, reward.transform.position, ortho, ppu, includeUi: true);
                Note($"{lootRooms[0].room.State.RoomType} room reward source {reward.Kind} at {reward.transform.position} closed & drawn");
            }

            // 8–9. Firearm reserve to 0 → knife active → an enemy is damaged, nothing consumed, dry click authored.
            var pistol = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            inventory.Consume(AmmoType.Light, inventory.Get(AmmoType.Light));
            pistol.ApplyAuthoritativeState(0, false);
            var dryBefore = audio.PlayedCount(AudioEventIds.DryFire);
            Assert.IsFalse(pistol.TryFire());
            Assert.AreEqual(1, pistol.DryFires);
            Assert.AreEqual(dryBefore + 1, audio.PlayedCount(AudioEventIds.DryFire), "the dry click is audible");
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            var knife = run.Rig.Loadout.ActiveWeapon as MeleeWeapon;
            Assert.IsNotNull(knife);
            Assert.AreEqual(StarterKitService.KnifeId, knife.Definition.Id);
            Assert.AreEqual(WeaponSlot.Secondary, run.Rig.Loadout.ActiveSlot);
            var held = player.GetComponent<RuinRail.Presentation.Animation.HeldWeaponVisual>();
            Assert.AreEqual(StarterKitService.KnifeId, held.ShownWeaponId, "the knife is the drawn held weapon");
            LiveDungeonCapture.Capture(Folder, "live_08_knife_active_firearm_reserve_zero", camera, ppu, includeUi: true);

            var aiming = player.GetComponent<PlayerAiming>();
            var spot = (Vector2)player.transform.position + aiming.AimDirection.normalized * Mathf.Min(0.8f, knife.Definition.AttackRange * 0.7f);
            var victim = new DefaultEnemySpawner(content.Stagger).Spawn(content.Enemies.First(e => e.Id == "grunt"), spot, player.transform);
            run.BindEnemyPresentation(victim);
            victim.enabled = false;
            var vb = victim.GetComponent<Rigidbody2D>();
            vb.linearVelocity = Vector2.zero;
            vb.bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            var health = victim.GetComponent<HealthComponent>();
            var hp0 = health.CurrentHealth;
            var hitsBefore = audio.PlayedCount(AudioEventIds.EnemyHit);
            var reservesBefore = new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }.Select(inventory.Get).ToArray();
            Assert.IsTrue(knife.TryAttack());
            var deadline = Time.time + 2f;
            while (Time.time < deadline && health.CurrentHealth == hp0) yield return null;
            Assert.Less(health.CurrentHealth, hp0, "the knife damaged the enemy");
            CollectionAssert.AreEqual(reservesBefore, new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }.Select(inventory.Get).ToArray(), "no ammo of any type consumed by the swing");
            Assert.AreEqual(0, inventory.Get(AmmoType.Light), "the firearm reserve is still 0");
            Assert.AreEqual(hitsBefore + 1, audio.PlayedCount(AudioEventIds.EnemyHit));
            LiveDungeonCapture.Capture(Folder, "live_09_knife_damages_enemy_at_zero_reserve", camera, ppu, includeUi: true);
            Note($"knife swing at reserve 0: grunt HP {hp0} → {health.CurrentHealth}; all reserves 0; enemy hit cue played");

            // 10. Merchant (when this depth has one): present, drawn, with an ammo offer.
            var merchant = bindings.FirstOrDefault(b => b.binding != null && b.binding.Merchant != null);
            if (merchant.binding != null)
            {
                var offers = merchant.binding.Merchant.Merchant.Offers;
                Assert.IsTrue(offers.Any(o => o.Definition is AmmoItemDefinition && o.Item.Quantity > 0), "the merchant's ammo slot is stocked");
                Assert.IsTrue(merchant.binding.Merchant.GetComponent<WorldObjectVisual>().IsVisible);
                yield return Teleport(run, (Vector2)merchant.binding.Merchant.transform.position + Vector2.down * 1.2f);
                Assert.IsTrue(run.CurrentInteractionPrompt.Contains("TRADE"));
                LiveDungeonCapture.Capture(Folder, "live_10_dungeon_merchant", camera, merchant.binding.Merchant.transform.position, ortho, ppu, includeUi: true);
                Note($"merchant: {offers.Count} offers, ammo: {string.Join(", ", offers.Where(o => o.Definition is AmmoItemDefinition).Select(o => o.Definition.Id + " x" + o.Item.Quantity + " @" + o.Price))}");
            }

            // Leaving and re-entering the depth's room never re-rolls: the state stays resolved (the room runtime is the source of truth).
            Assert.IsTrue(chestRoom.State.IsResolved(RoomCategoryComposer.SupplyChestResolvedId));
            Assert.IsTrue(target.IsOpened);
        }

        [UnityTest]
        public IEnumerator LiveRun_AudioGraphIsAlive_MenuShelterDungeon_Listener_Beds_Sfx_PauseResume_ReturnToMenu()
        {
            // Boot → Main Menu.
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(Biome.Rustworks));
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            for (var i = 0; i < 3; i++) yield return null;
            var director = _app.Music;
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "exactly one listener in the Main Menu (a scene without a camera object)");
            Assert.IsNotNull(AudioListenerRig.Current);
            Assert.IsFalse(AudioListener.pause);
            Assert.AreEqual(MusicRole.MainMenu, director.ActiveRole);
            AssertBed(director, "main menu music", MusicRole.MainMenu, expectAmbience: null);
            Note("main menu: music bed playing, one listener, no ambience");
            var confirmBefore = _app.Audio.PlayedCount(AudioEventIds.UiConfirm);
            UiSoundBus.Raise(UiSound.Confirm);
            Assert.AreEqual(confirmBefore + 1, _app.Audio.PlayedCount(AudioEventIds.UiConfirm), "UI click routed to the audio service");
            var last = _app.Audio.LastStartedSource;
            Assert.IsTrue(last != null && last.clip != null && last.isPlaying && last.volume > 0f && last.spatialBlend == 0f);

            // Shelter.
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            for (var i = 0; i < 3; i++) yield return null;
            AssertBed(director, "shelter music", MusicRole.Shelter, expectAmbience: null);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Note("shelter: music bed playing, one listener");

            // Dungeon.
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Audio Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 5; i++) yield return null;
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var biome = run.Expedition.State.Biome;
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, listeners.Length, "the dungeon camera adds no second listener");
            Assert.Less(Vector3.Distance(AudioListenerRig.Position, run.Camera.transform.position), 0.01f, "the listener follows the expedition camera");
            AssertBed(director, "dungeon exploration music", MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration), expectAmbience: biome);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioService>(FindObjectsSortMode.None).Length, "one audio service survives the scene transitions");
            Assert.AreEqual(1, Object.FindObjectsByType<MusicDirector>(FindObjectsSortMode.None).Length);
            Note($"dungeon {biome}: {director.ActiveRole} playing, {biome} ambience playing");

            // Combat state: the combat bed replaces exploration, one track outside the crossfade.
            _app.MusicBinder.ObserveCombatStarted();
            Assert.AreEqual(MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Combat), director.ActiveRole);
            for (var f = 0f; f < MusicDirector.CrossfadeSeconds + 0.2f; f += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(1, director.PlayingTrackSources);
            _app.MusicBinder.ObserveCombatEnded();

            // Representative SFX through the real binders: weapon fire (visual driver counter), enemy hit, dash.
            var audio = _app.Audio;
            var player = run.Rig.Player;
            var fireBefore = audio.PlayedCount(AudioEventIds.FirePistol);
            var pistol = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            Assert.IsTrue(pistol.TryFire());
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(fireBefore + 1, audio.PlayedCount(AudioEventIds.FirePistol), "weapon fire reaches playback");
            var reloadBefore = audio.PlayedCount(AudioEventIds.Reload);
            Assert.IsTrue(pistol.TryStartReload());
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(reloadBefore + 1, audio.PlayedCount(AudioEventIds.Reload), "reload reaches playback");
            var hitBefore = audio.PlayedCount(AudioEventIds.PlayerHit);
            player.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(hitBefore + 1, audio.PlayedCount(AudioEventIds.PlayerHit), "player hit reaches playback");
            var doorBefore = audio.PlayedCount(AudioEventIds.DoorOpen);
            var combat = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat);
            combat.LockDoors();
            combat.UnlockDoors();
            Assert.GreaterOrEqual(audio.PlayedCount(AudioEventIds.DoorOpen), doorBefore + 1, "combat door lock/unlock reaches playback");
            Note($"sfx: fire {audio.PlayedCount(AudioEventIds.FirePistol)}, reload {audio.PlayedCount(AudioEventIds.Reload)}, player hit {audio.PlayedCount(AudioEventIds.PlayerHit)}, door {audio.PlayedCount(AudioEventIds.DoorOpen)}; silent events {audio.SilentEvents.Count}, unknown {audio.UnknownEvents.Count}");
            Assert.AreEqual(0, audio.SilentEvents.Count, string.Join(",", audio.SilentEvents));
            Assert.AreEqual(0, audio.UnknownEvents.Count, string.Join(",", audio.UnknownEvents));

            // Pause / resume: nothing mutes permanently.
            run.Pause.Open();
            for (var i = 0; i < 10; i++) yield return null;
            AssertBed(director, "music while paused", MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration), expectAmbience: biome);
            run.Pause.Close();
            for (var i = 0; i < 5; i++) yield return null;
            AssertBed(director, "music after resume", MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration), expectAmbience: biome);
            Assert.AreEqual(1f, Time.timeScale, 1e-4f);

            // Return to the Main Menu through the pause menu: the run fails once, the menu bed returns, ambience stops.
            run.Pause.Open();
            run.Pause.Activate(RuinRail.UI.Pause.PauseMenuItem.ReturnToMainMenu);
            run.Pause.Confirm();
            yield return WaitComposed(SceneNames.MainMenu);
            for (var i = 0; i < 3; i++) yield return null;
            AssertBed(director, "main menu music after return", MusicRole.MainMenu, expectAmbience: null);
            Assert.AreEqual(StingerRole.ExpeditionFailed, director.LastStinger);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<GameApp>(FindObjectsSortMode.None).Length, "one persistent app/audio root");
            Note("return to menu: menu bed restored, ambience off, expedition-failed stinger, one listener, one app root");
        }

        [UnityTest]
        public IEnumerator AllThreeBiomes_StartTheirAmbienceAndExplorationBed_WithoutDuplicates()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            yield return null;
            var director = _app.Music;
            var inventory = PlayerInventory.FromRegistry(_app.Registry, _app.Content.AmmoBalance);
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                _app.MusicBinder.EnterExpedition(new ExpeditionState(7, biome, inventory));
                yield return null;
                AssertBed(director, biome + " exploration", MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration), expectAmbience: biome);
                Note($"{biome}: track {director.ActiveRole} playing, ambience {director.ActiveAmbience} playing");
            }

            _app.MusicBinder.EnterShelter();
            yield return null;
            AssertBed(director, "shelter", MusicRole.Shelter, expectAmbience: null);
            Assert.IsFalse(GameApp.Ensure() != _app, "Ensure never creates a second app");
        }

        private static void AssertBed(MusicDirector director, string what, MusicRole role, Biome? expectAmbience)
        {
            Assert.AreEqual(role, director.ActiveRole, what);
            Assert.IsFalse(director.IsSilent, what + ": the role has a clip");
            var sources = director.GetComponentsInChildren<AudioSource>(true);
            // During a crossfade the outgoing bed is still audible on the other source; the incoming one carries the role's clip.
            var expectedClip = director.Catalog.TrackFor(role);
            var track = sources.FirstOrDefault(s => (s.name == "MusicA" || s.name == "MusicB") && s.isPlaying && s.clip == expectedClip);
            Assert.IsNotNull(track, what + ": a music source is playing the role's clip");
            Assert.Greater(track.volume, 0f, what);
            Assert.AreEqual(0f, track.spatialBlend, what);
            Assert.IsTrue(director.IsCrossfading || director.PlayingTrackSources == 1, what + ": one bed at a time");
            var ambience = sources.First(s => s.name == "Ambience");
            if (expectAmbience.HasValue)
            {
                Assert.AreEqual(expectAmbience, director.ActiveAmbience, what);
                Assert.IsTrue(ambience.isPlaying && ambience.loop && ambience.clip == director.Catalog.AmbienceFor(expectAmbience.Value) && ambience.volume > 0f, what + ": ambience loop playing");
                Assert.LessOrEqual(ambience.volume, MusicDirector.AmbienceCeiling * AudioService.GainFor(AudioBus.Ambience) + 1e-4f, what + ": ambience under the SFX/music ceiling");
            }
            else
            {
                Assert.IsNull(director.ActiveAmbience, what);
                Assert.IsFalse(ambience.isPlaying, what + ": no ambience outside the expedition");
            }
        }
    }
}
