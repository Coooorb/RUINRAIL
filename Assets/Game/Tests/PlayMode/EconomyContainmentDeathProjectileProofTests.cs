using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.RunEnd;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of this pass through the real boot flow (Main Menu → Shelter → generated dungeon): the corrected
    /// ammo resale (quote == coin change), a pack and the Boss held at open doorways, backpack click/swap/drag reorder,
    /// visible player / enemy / Boss projectiles with position evidence, the solo death → Run Lost screen → RETURN TO
    /// SHELTER, and the Starter Loadout fallback at Ready. Captures and evidence go to
    /// <c>TestResults/EconomyContainmentDeathProjectileProof</c>.
    /// </summary>
    public sealed class EconomyContainmentDeathProjectileProofTests
    {
        private const string Folder = "TestResults/EconomyContainmentDeathProjectileProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        private sealed class Guard : IInvulnerabilityState { public bool IsInvulnerable => true; }

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_ecdp_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder, "live_*")) File.Delete(stale);
            CursorService.SetApplier(_ => true);
            GameplayInputGate.Reset();
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "live_economy_containment_death_projectile_evidence.txt"), _evidence.ToString());
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
            ProjectileVisualCatalog.Active = null;
            CursorService.Reset();
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
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
            Debug.Log("[ECDP] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }

        private static (Vector2 centre, Vector2 step) DoorCentre(RoomRoot root, DoorSocket socket)
        {
            var step = (Vector2)DoorDirections.Step(socket.Direction);
            var cells = socket.Cells();
            var centre = Vector2.zero;
            foreach (var cell in cells) centre += (Vector2)root.transform.TransformPoint(RuinRail.Dungeon.Grid.GridCoordinates.CellToWorldCenter(cell));
            centre /= cells.Length;
            return (centre, step);
        }

        private static float Overshoot(Rect interior, Vector2 p, float r) =>
            Mathf.Max(interior.xMin - (p.x - r), Mathf.Max((p.x + r) - interior.xMax, Mathf.Max(interior.yMin - (p.y - r), (p.y + r) - interior.yMax)));

        [UnityTest]
        public IEnumerator LiveRun_AmmoSale_Containment_BackpackReorder_Projectiles_Death_AndStarterFallback()
        {
            const int seed = 11; // a first depth with a merchant room (used by the merchant pass before this one)
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("ECDP Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var state = run.Expedition.State;
            var inventory = state.Inventory;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var health = player.GetComponent<HealthComponent>();
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); for (var i = 0; i < 3; i++) yield return null; }
            Note($"seed {seed}: {run.Expedition.State.Biome}, {run.Rooms.Count} rooms on depth 1");

            // ---------------------------------------------------------------- 1. ammo sale ----
            var prices = new PriceService(content.Economy);
            var ammoLight = content.Items.OfType<AmmoItemDefinition>().First(a => a.AmmoType == AmmoType.Light);
            foreach (var ammo in content.Items.OfType<AmmoItemDefinition>().OrderBy(a => a.AmmoType))
            {
                content.Economy.TryGetAmmoBundle(ammo.AmmoType, out var bundle);
                Note($"ammo {ammo.AmmoType}: bundle x{bundle.Units} buys for {bundle.Price}; sells for {prices.AmmoSellValue(ammo, bundle.Units)} (full stack x{ammo.MaxStack}: {prices.AmmoSellValue(ammo, ammo.MaxStack)}; old formula paid {prices.SellValue(bundle.Price) * ammo.MaxStack})");
            }

            var merchantBinding = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).FirstOrDefault(b => b != null && b.Merchant != null);
            Assert.IsNotNull(merchantBinding, "seed 11 places a merchant on depth 1");
            var interactable = merchantBinding.Merchant;
            var interactor = player.GetComponent<PlayerInteractor>();
            Put((Vector2)interactable.transform.position + Vector2.down * 1.1f);
            yield return Settle();
            Assert.IsTrue(interactor.TryInteract() && run.Merchant.IsOpen, "the merchant opened");
            run.Merchant.SetTab(MerchantTab.Sell);
            yield return null;
            var stackIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == ammoLight.Id);
            Assert.GreaterOrEqual(stackIndex, 0, "the starter light ammo is in the backpack");
            var stack = inventory.BackpackSlots[stackIndex];
            var row = run.Merchant.Rows.First(r => r.Item != null && r.Item.InstanceId == stack.InstanceId);
            run.Merchant.SetCursor(run.Merchant.Rows.ToList().IndexOf(row));
            yield return null;
            Assert.AreEqual(prices.AmmoSellValue(ammoLight, stack.Quantity), row.Price, "the UI quote is the 15% rule");
            Assert.AreEqual(row.Price, merchantBinding.Merchant.Merchant.QuoteSellValue(stack), "UI quote == service quote");
            Note($"merchant SELL row: {stack.Quantity} light ammo quoted at {row.Price} coins (was {prices.SellValue(60) * stack.Quantity} before the fix)");
            LiveDungeonCapture.Capture(Folder, "live_01_ammo_sell_quote", camera, ppu, includeUi: true);
            var coinsBefore = state.CarriedCoins;
            Assert.AreEqual(TradeError.None, run.Merchant.Sell());
            yield return null;
            var paid = state.CarriedCoins - coinsBefore;
            Assert.AreEqual(row.Price, paid, "exact coin change equals the quote");
            Assert.AreEqual(0, inventory.Get(AmmoType.Light), "the whole stack left the backpack; nothing to oversell");
            Note($"sold: coins {coinsBefore} -> {state.CarriedCoins} (+{paid}); a second sell of the same stack: {merchantBinding.Merchant.Merchant.Sell(new BackpackContainer(inventory), stack.InstanceId)}");
            LiveDungeonCapture.Capture(Folder, "live_02_ammo_sold_exact_coin_change", camera, ppu, includeUi: true);
            run.Merchant.Close();
            yield return null;
            inventory.Add(AmmoType.Light, stack.Quantity);

            // ---------------------------------------------------------------- 2. containment ----
            health.SetInvulnerabilityState(new Guard());
            var combatRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered && r.Root.GetSockets().Any(s => !RoomExitSealer.IsSealed(r.Root, s)));
            var spawned = new List<EnemyController>();
            combatRoom.EnemySpawned += (_, e) => spawned.Add(e);
            Put(RoomCentre(combatRoom));
            yield return Settle();
            Assert.AreEqual(RoomLifecycleState.Active, combatRoom.Lifecycle);
            Assert.Greater(spawned.Count, 0);
            Assert.IsTrue(spawned.All(e => e.Bounds != null && e.Bounds.IsBound && e.Bounds.RoomNodeId == combatRoom.State.NodeId), "the room owns everything it spawned");
            var socket = combatRoom.Root.GetSockets().First(s => !RoomExitSealer.IsSealed(combatRoom.Root, s));
            var (doorway, step) = DoorCentre(combatRoom.Root, socket);
            var interior = combatRoom.InteriorWorldBounds;
            Put(doorway + step * 1.4f);
            combatRoom.UnlockDoors();
            var worst = 0f; var closest = float.PositiveInfinity;
            for (var i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                foreach (var e in spawned)
                {
                    if (e == null || !e.IsAlive) continue;
                    worst = Mathf.Max(worst, Overshoot(interior, e.transform.position, e.Bounds.BodyRadius));
                    closest = Mathf.Min(closest, Vector2.Dot(doorway - (Vector2)e.transform.position, step));
                }
            }

            Assert.LessOrEqual(worst, 0.06f, "no enemy collider crossed the room boundary through the open doorway (a contact push of under 2 px is pulled back the next step)");
            Assert.Less(closest, 2f, "the pack did come to the doorway");
            Note($"combat room {combatRoom.State.RoomId} ({socket.Direction} door open, player 1.4 tiles outside): {spawned.Count(e => e != null && e.IsAlive)} enemies alive, closest approach {closest:0.00} tiles from the door line, worst overshoot {worst:0.000}, corrections {spawned.Sum(e => e != null ? e.Bounds.Corrections : 0)}");
            Put(doorway - step * 0.2f);
            yield return Settle();
            LiveDungeonCapture.Capture(Folder, "live_03_enemies_contained_at_open_doorway", camera, ppu, includeUi: true);
            foreach (var e in spawned) if (e != null && e.IsAlive) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
            yield return new WaitForFixedUpdate();

            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            var bossBinding = bossRoom.GetComponent<RoomContentBinding>();
            var boss = bossBinding.Boss.Boss;
            Assert.IsTrue(boss.Bounds != null && boss.Bounds.IsBound && boss.Bounds.RoomNodeId == bossRoom.State.NodeId, "the arena owns the Boss");
            Assert.IsNull(boss.Target, "the Boss has no target before the arena is entered");
            Assert.AreEqual(MovesetActorState.Idle, boss.State);
            var bossSocket = bossRoom.Root.GetSockets().First(s => !RoomExitSealer.IsSealed(bossRoom.Root, s));
            var (bossDoor, bossStep) = DoorCentre(bossRoom.Root, bossSocket);
            var arena = bossRoom.InteriorWorldBounds;
            // The Boss starts on its anchor across the arena; arena props between it and the door make it hold at cover
            // (the existing head-on steering rule). The containment question is the edge, so it starts on the door's
            // approach lane, a few tiles inside, and pursues straight at the player beyond the open door.
            var bossBody = boss.GetComponent<Rigidbody2D>();
            for (var back = 4f; back <= 8f; back += 1f)
            {
                var spot = bossDoor - bossStep * back;
                if (!RoomRuntime.IsSpawnClear(spot)) continue;
                boss.transform.position = spot; bossBody.position = spot; Physics2D.SyncTransforms();
                break;
            }

            Note($"  arena {bossRoom.State.RoomId}: interior {arena}, {bossSocket.Direction} door at {bossDoor:0.00}, boss placed at {boss.transform.position:0.00} on the door lane");
            Put(bossDoor + bossStep * 1.4f);
            bossRoom.UnlockDoors();
            boss.SuppressAttacks = true; // pursuit only for the containment proof
            boss.SetTarget(player.transform);
            worst = 0f; closest = float.PositiveInfinity;
            for (var i = 0; i < 300; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Overshoot(arena, boss.transform.position, boss.Bounds.BodyRadius));
                closest = Mathf.Min(closest, Vector2.Dot(bossDoor - (Vector2)boss.transform.position, bossStep));
                if (i % 100 == 0) Note($"  boss step {i}: state {boss.State}, pos {boss.transform.position:0.00}, vel {bossBody.linearVelocity:0.00}, corrections {boss.Bounds.Corrections}");
            }

            Assert.LessOrEqual(worst, 0.06f, "the Boss never left the arena");
            Assert.GreaterOrEqual(closest, 0.5f + boss.Bounds.BodyRadius - 0.05f, "and never entered the door cells");
            Assert.Less(closest, 0.5f + boss.Bounds.BodyRadius + 1f, "but did pursue to the arena edge");
            Note($"boss {boss.Definition.Id} in {bossRoom.State.RoomId} ({bossSocket.Direction} door open, player 1.4 tiles outside): closest approach {closest:0.00} tiles from the door line, worst overshoot {worst:0.000}, corrections {boss.Bounds.Corrections}");
            LiveDungeonCapture.Capture(Folder, "live_04_boss_contained_at_arena_boundary", camera, ppu, includeUi: true);
            boss.SetTarget(null);
            boss.SuppressAttacks = false;
            boss.enabled = false;
            health.SetInvulnerabilityState(null);

            // ---------------------------------------------------------------- 3. backpack reorder ----
            var startRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start);
            Put(RoomCentre(startRoom));
            yield return Settle();
            inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 2));
            var vm = run.Inventory;
            var view = run.InventoryView;
            string[] Order() => inventory.BackpackSlots.Select(s => s?.DefinitionId ?? "-").ToArray();
            var occupied = Enumerable.Range(0, PlayerInventory.BackpackCapacity).Where(i => inventory.BackpackSlots[i] != null).ToList();
            var empties = Enumerable.Range(0, PlayerInventory.BackpackCapacity).Where(i => inventory.BackpackSlots[i] == null).ToList();
            Assert.GreaterOrEqual(occupied.Count, 2); Assert.GreaterOrEqual(empties.Count, 1);
            var a = occupied[0]; var b = empties[^1]; var c = occupied[1];
            var moving = inventory.BackpackSlots[a]; var other = inventory.BackpackSlots[c];
            vm.Open();
            yield return null;
            Note($"backpack before: [{string.Join(", ", Order())}]");
            LiveDungeonCapture.Capture(Folder, "live_08_backpack_before_move", camera, ppu, includeUi: true);
            view.BackpackSlots[a].SimulateClick();
            yield return null;
            Assert.IsTrue(vm.Selected.HasValue && vm.Selected.Value.Index == a);
            view.BackpackSlots[b].SimulateClick();
            yield return null;
            Assert.AreSame(moving, inventory.BackpackSlots[b]); Assert.IsNull(inventory.BackpackSlots[a]);
            Assert.AreEqual(SlotMoveResult.Moved, vm.LastReorder);
            Note($"click {a + 1} -> click empty {b + 1}: [{string.Join(", ", Order())}]");
            LiveDungeonCapture.Capture(Folder, "live_09_backpack_after_click_move", camera, ppu, includeUi: true);
            view.BackpackSlots[b].SimulateClick(); yield return null;
            view.BackpackSlots[c].SimulateClick(); yield return null;
            Assert.AreSame(moving, inventory.BackpackSlots[c]); Assert.AreSame(other, inventory.BackpackSlots[b]);
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Note($"click {b + 1} -> click occupied {c + 1} (swap): [{string.Join(", ", Order())}]");
            LiveDungeonCapture.Capture(Folder, "live_10_backpack_slots_swapped", camera, ppu, includeUi: true);
            view.BackpackSlots[a].SimulateDrop(view.BackpackSlots[c]);
            yield return null;
            Assert.AreSame(moving, inventory.BackpackSlots[a]); Assert.IsNull(inventory.BackpackSlots[c]);
            Note($"drag {c + 1} -> drop on empty {a + 1}: [{string.Join(", ", Order())}]");
            LiveDungeonCapture.Capture(Folder, "live_11_backpack_drag_drop_reorder", camera, ppu, includeUi: true);
            var order = Order();
            vm.Close(); yield return null; vm.Open(); yield return null;
            CollectionAssert.AreEqual(order, Order(), "the manual order survives close/reopen");
            var ids = inventory.BackpackSlots.Where(s => s != null).Select(s => s.InstanceId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "nothing duplicated");
            vm.Close(); yield return null;

            // ---------------------------------------------------------------- 4. projectiles ----
            Assert.IsNotNull(ProjectileVisualCatalog.Active, "the app bound the projectile catalog");
            var loadout = run.Rig.Loadout;
            var firingSpot = RoomCentre(startRoom);
            var families = new (string weaponId, string profile, string capture)[]
            {
                ("weapon_p9_ranger", "proj_pistol", "live_12_p9_projectile_visible"),
                ("weapon_rattler_9", "proj_smg", "live_13a_smg_projectile_visible"),
                ("weapon_ar_17", "proj_rifle", "live_13b_ar_projectile_visible"),
                ("weapon_hound_br", "proj_battle_rifle", "live_13c_battle_rifle_projectile_visible"),
                ("weapon_scatter_8", "proj_pellet", "live_14_shotgun_pellets_visible"),
                ("weapon_longshot_s1", "proj_sniper", "live_15_sniper_tracer_visible"),
                ("weapon_recurve_bow", "proj_arrow", "live_16a_bow_arrow_visible"),
                ("weapon_pulse_carbine_b1", "proj_energy_bolt", "live_16b_blaster_bolt_visible"),
                ("weapon_pipe_launcher", "proj_rocket", "live_16c_rocket_visible"),
                ("weapon_quickfang", "proj_pistol_legendary", "live_16d_legendary_quickfang_visible")
            };
            var originalPrimary = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            foreach (var (weaponId, profile, capture) in families)
            {
                Put(firingSpot);
                yield return new WaitForFixedUpdate();
                var current = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
                if (current == null || current.DefinitionId != weaponId)
                {
                    if (current != null) inventory.Unequip(EquippedSlot.PrimaryWeapon);
                    Assert.IsTrue(inventory.TryEquip(new ItemInstance(weaponId, 1, weaponId == "weapon_quickfang" ? Rarity.Legendary : Rarity.Common), EquippedSlot.PrimaryWeapon), weaponId);
                    yield return null;
                }

                loadout.SelectSlot(WeaponSlot.Primary);
                var weapon = loadout.GetSlot(WeaponSlot.Primary);
                Projectile shot = null;
                var fired = false;
                switch (weapon)
                {
                    case RangedWeapon ranged: ranged.ApplyAuthoritativeState(ranged.Definition.MagazineSize, false); fired = ranged.TryFire(); shot = ranged.LastSpawnedProjectile; break;
                    case BowWeapon bow: fired = bow.TryStartCharge() && bow.TryRelease(); shot = bow.LastSpawnedProjectile; break;
                    case BlasterWeapon blaster: fired = blaster.TryFire(); shot = blaster.LastSpawnedProjectile; break;
                }

                Assert.IsTrue(fired && shot != null, weaponId + " fired");
                Assert.AreEqual(profile, shot.Visual.ProfileId, weaponId);
                Assert.IsTrue(shot.Visual.IsVisible, weaponId + " visible in flight");
                Assert.AreEqual((Vector2)shot.transform.position, (Vector2)shot.Visual.Body.transform.position, weaponId + ": sprite on the physics body");
                var pellets = weaponId == "weapon_scatter_8" ? run.Rig.Projectiles.GetComponentsInChildren<Projectile>().Count(p => p.gameObject.activeSelf && p.Visual.IsVisible && p.Visual.ProfileId == "proj_pellet") : 1;
                if (weaponId == "weapon_scatter_8") Assert.GreaterOrEqual(pellets, 2, "several visible pellets");
                // Controlled frame capture: let the shot fly a few physics steps clear of the muzzle, then freeze it where it is.
                for (var i = 0; i < 6 && !shot.IsResolved; i++)
                {
                    yield return new WaitForFixedUpdate();
                    Assert.AreEqual((Vector2)shot.transform.position, (Vector2)shot.Visual.Body.transform.position, weaponId + ": the sprite follows the body in flight");
                }

                Time.timeScale = 0f;
                yield return null;
                var result = LiveDungeonCapture.Capture(Folder, capture, camera, ppu, includeUi: false);
                var px = result.WorldToPixel(shot.transform.position);
                var lit = result.CountLit(new RectInt(px.x - 12, px.y - 6, 24, 12));
                Note($"{weaponId}: '{profile}' at world {shot.transform.position:0.00} = capture pixel ({px.x},{px.y}), rotation {shot.transform.eulerAngles.z:0}°, {pellets} visible projectile(s), lit pixels around it {lit}");
                Time.timeScale = 1f;
                for (var i = 0; i < 90 && !shot.IsResolved; i++) yield return new WaitForFixedUpdate();
                if (shot.IsResolved) Assert.IsFalse(shot.Visual.IsVisible, weaponId + ": the sprite ended with the shot");
            }

            if (originalPrimary != null)
            {
                inventory.Unequip(EquippedSlot.PrimaryWeapon);
                inventory.TryEquip(originalPrimary, EquippedSlot.PrimaryWeapon);
                yield return null;
            }

            // Enemy round and Boss volley.
            var shooter = new DefaultEnemySpawner(content.Stagger).Spawn(content.Enemies.First(e => e.Id == "shooter"), firingSpot + Vector2.right * 4f, player.transform);
            run.BindEnemyPresentation(shooter);
            shooter.enabled = false;
            var attack = (EnemyProjectileAttack)shooter.Attack;
            Assert.IsTrue(attack.TryResolveAttack(player.transform));
            var round = attack.SpawnedProjectiles[0];
            Assert.AreEqual("proj_enemy_round", round.Visual.ProfileId);
            Assert.IsTrue(round.Visual.IsVisible);
            for (var i = 0; i < 4 && !round.IsResolved; i++) yield return new WaitForFixedUpdate();
            Time.timeScale = 0f; yield return null;
            var enemyCapture = LiveDungeonCapture.Capture(Folder, "live_17_enemy_projectile_visible", camera, ppu, includeUi: false);
            Note($"enemy shooter: 'proj_enemy_round' at world {round.transform.position:0.00} = pixel {enemyCapture.WorldToPixel(round.transform.position)}");
            Time.timeScale = 1f;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            shooter.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));

            var volley = boss.Definition.Moveset.FirstOrDefault(m => m != null && m.Motion == AttackMotion.Projectile);
            if (volley != null)
            {
                // Back on its anchor (the arena centre, inside the camera's dungeon bounds), the player five tiles to its right.
                var anchor = (Vector2)bossRoom.Root.transform.TransformPoint(bossRoom.Root.GetMarkers(RoomMarkerRole.BossAnchor).First().WorldCenter);
                boss.transform.position = anchor; boss.GetComponent<Rigidbody2D>().position = anchor; Physics2D.SyncTransforms();
                Put(anchor + Vector2.right * 5f);
                for (var i = 0; i < 30; i++) yield return null; // the camera rig settles on the player before the frame is taken
                var resolver = boss.Resolver;
                resolver.Begin(volley, Vector2.right);
                resolver.Tick(0.01f);
                Assert.Greater(resolver.SpawnedProjectiles.Count, 0);
                Assert.IsTrue(resolver.SpawnedProjectiles.All(p => p.Visual.IsVisible && p.Visual.ProfileId == volley.ProjectileVisualId));
                StringAssert.StartsWith("proj_boss", volley.ProjectileVisualId);
                for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                Time.timeScale = 0f; yield return null;
                // Framed on the arena explicitly: the gameplay camera is clamped to the depth bounds and still easing after the teleport.
                var bossCapture = LiveDungeonCapture.Capture(Folder, "live_18_boss_projectile_visible", camera, (Vector2)boss.transform.position + Vector2.right * 2.5f, LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false);
                Note($"boss {boss.Definition.Id}: '{volley.ProjectileVisualId}' x{resolver.SpawnedProjectiles.Count}, first at world {resolver.SpawnedProjectiles[0].transform.position:0.00} = pixel {bossCapture.WorldToPixel(resolver.SpawnedProjectiles[0].transform.position)}");
                Time.timeScale = 1f;
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                resolver.Cancel();
            }
            else Note($"boss {boss.Definition.Id} has no projectile attack on this seed; boss projectile profiles are proven by ProjectileVisualTests");

            // ---------------------------------------------------------------- 5. death -> Run Lost screen -> RETURN TO SHELTER ----
            Put(firingSpot);
            yield return Settle();
            run.Expedition.AddCarriedCoins(77);
            var bankedBefore = _app.Menu.Session.Profile.BankedCoins;
            var ended = 0;
            run.Expedition.ExpeditionEnded += _ => ended++;
            health.TryApplyDamage(new DamageRequest(999999));
            yield return null; yield return null;
            var life = player.GetComponent<PlayerLifeStateComponent>();
            Assert.IsTrue(life.IsDead, "solo: dead outright");
            Assert.IsFalse(run.Expedition.IsExpeditionActive);
            Assert.AreEqual(1, ended, "Fail() once");
            Assert.IsTrue(run.RunFailedScreen.IsShowing, "the Run Lost screen is up");
            Assert.AreEqual(1, run.RunFailed.Shows);
            Assert.IsTrue(GameplayInputGate.IsHeld);
            run.Pause.HandlePauseInput();
            yield return null;
            Assert.IsFalse(run.Pause.IsOpen, "Esc does not open the pause menu over it");
            Assert.AreEqual("runfailed.shelter", run.RunFailedScreen.List.Focused.Id);
            Note("run lost: " + string.Join(" | ", run.RunFailedScreen.RowTexts.Select(r => $"{r.label}={r.value}")));
            LiveDungeonCapture.Capture(Folder, "live_06_run_lost_screen", camera, ppu, includeUi: true);
            var control = run.RunFailedScreen.Controls.First(x => x.Id == "runfailed.shelter");
            control.SimulateHover(true);
            control.SimulateClick();
            yield return WaitComposed(SceneNames.Base);
            yield return null;
            Assert.AreEqual(RunFailedChoice.ReturnToShelter, run.RunFailed.Choice);
            var session = _app.Menu.Session;
            Assert.IsFalse(session.Expedition.IsExpeditionActive);
            Assert.AreEqual(bankedBefore, session.Profile.BankedCoins, "carried coins were lost, banked coins intact");
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "the at-risk loadout went with the run");
            var probe = _app.ProbeSave();
            Assert.IsTrue(probe.Success && !probe.ExpeditionMarkerOpen, "saved with the marker closed");
            Note($"RETURN TO SHELTER: base composed, banked {session.Profile.BankedCoins} (unchanged), marker closed, loadout empty");
            LiveDungeonCapture.Capture(Folder, "live_07_return_to_shelter_after_run_lost", null, ppu, includeUi: true);

            // ---------------------------------------------------------------- 6. starter fallback at Ready ----
            hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var member = session.Lobby.Get(BaseSession.LocalClientId);
            Assert.IsFalse(member.HasValidLoadout, "no loadout after the wipe");
            var storageBefore = session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList();
            hub.Hub.Open(BaseStation.Multiplayer); // the station READY lives in; its feedback line shows the notice
            yield return null;
            LiveDungeonCapture.Capture(Folder, "live_05a_no_loadout_before_ready", null, ppu, includeUi: true);
            var readyTask = hub.Terminal.ActivateAsync(RuinRail.UI.Multiplayer.TerminalAction.ToggleReady); // the real READY control
            while (!readyTask.IsCompleted) yield return null;
            yield return null;
            Assert.IsTrue(readyTask.Result.IsNone && member.IsReady, "READY went through");
            Assert.AreEqual(RuinRail.UI.Multiplayer.TerminalViewModel.StarterLoadoutEquippedNotice, hub.Terminal.Notice);
            Assert.IsTrue(hub.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text == RuinRail.UI.Multiplayer.TerminalViewModel.StarterLoadoutEquippedNotice), "the notice is drawn in the station");
            Assert.AreEqual(StarterKitService.PistolId, session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId);
            Assert.AreEqual(StarterKitService.KnifeId, session.Loadout.GetEquipped(EquippedSlot.SecondaryWeapon)?.DefinitionId);
            Assert.AreEqual(StarterKitService.VestId, session.Loadout.GetEquipped(EquippedSlot.Armor)?.DefinitionId);
            Assert.IsTrue(member.HasValidLoadout);
            Assert.AreEqual(1, session.StarterLoadoutFallbacks);
            CollectionAssert.AreEqual(storageBefore, session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList(), "storage untouched");
            Note($"starter fallback at READY: notice '{hub.Terminal.Notice}', status '{hub.Terminal.StatusText}'; loadout = {session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId} / {session.Loadout.GetEquipped(EquippedSlot.SecondaryWeapon).DefinitionId} / {session.Loadout.GetEquipped(EquippedSlot.Armor).DefinitionId}, light ammo {session.Loadout.Get(AmmoType.Light)}, fallbacks {session.StarterLoadoutFallbacks}");
            LiveDungeonCapture.Capture(Folder, "live_05b_starter_loadout_equipped", null, ppu, includeUi: true);
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(false));
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true), "the TRANSIT station's READY path shares the fallback");
            Assert.AreEqual(1, session.StarterLoadoutFallbacks, "a second Ready grants nothing more");
        }
    }
}
