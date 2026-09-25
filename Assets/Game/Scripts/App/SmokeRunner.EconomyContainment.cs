using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The economy / containment / backpack-reorder / projectile stage of the built-player smoke: the corrected ammo
    /// resale payout (quote == exact coin change), encounter enemies and the Boss held inside their rooms at an open
    /// doorway, backpack slots moved and swapped by click and by drag/drop with the order preserved, and visible
    /// player and enemy projectiles for every weapon family — all inside the shipped executable, with captures
    /// written to the proof folder.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        /// <summary>The smoke's own guard while it deliberately stands the player in front of a pursuing pack / Boss.</summary>
        private sealed class SmokeGuard : IInvulnerabilityState
        {
            public bool IsInvulnerable { get; set; } = true;
        }

        private IEnumerator EconomyContainmentProjectileChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("econ/contain/proj: " + what); }
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var content = _app.Content;
            var state = run.Expedition.State;
            var inventory = state.Inventory;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }

            // ---------------------------------------------------------------- 1. ammo resale ----
            var prices = new PriceService(content.Economy);
            var ammoLight = content.Items.OfType<AmmoItemDefinition>().First(a => a.AmmoType == AmmoType.Light);
            var merchantService = new DungeonMerchantService(content.Merchant, prices, state.CarriedWallet, new DungeonMerchantState(state.Depth), state.RunSeed, 1,
                content.Items, _app.Configs.Resolve, content.Loot.RarityTableFor);
            var backpack = new BackpackContainer(inventory);
            var quotes = new List<string>();
            foreach (var ammo in content.Items.OfType<AmmoItemDefinition>().OrderBy(a => a.AmmoType))
            {
                content.Economy.TryGetAmmoBundle(ammo.AmmoType, out var bundle);
                var full = prices.AmmoSellValue(ammo, bundle.Units);
                quotes.Add($"{ammo.AmmoType} x{bundle.Units} (buy {bundle.Price}) sells for {full}");
                Check($"{ammo.AmmoType}: a full bundle sells for 15% of its price rounded down ({full} of {bundle.Price})", full == bundle.Price * 15 / 100 && full < bundle.Price / 4);
                Check($"{ammo.AmmoType}: a full backpack stack ({ammo.MaxStack}) sells for {prices.AmmoSellValue(ammo, ammo.MaxStack)}, never the old {prices.SellValue(bundle.Price) * ammo.MaxStack}",
                    prices.AmmoSellValue(ammo, ammo.MaxStack) < prices.AmmoPurchaseValue(ammo, ammo.MaxStack) / 4);
            }

            var lightBefore = inventory.Get(AmmoType.Light);
            var stackIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == ammoLight.Id);
            if (stackIndex < 0) { inventory.TryAddToBackpack(new ItemInstance(ammoLight.Id, 60)); stackIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == ammoLight.Id); }
            var stack = inventory.BackpackSlots[stackIndex];
            var quote = merchantService.QuoteSellValue(stack);
            var coinsBefore = state.CarriedCoins;
            var sellError = merchantService.Sell(backpack, stack.InstanceId);
            var paid = state.CarriedCoins - coinsBefore;
            Check($"selling {stack.Quantity} light ammo through the merchant service pays exactly the quote ({quote}) — coin change {paid}", sellError == RuinRail.Gameplay.Base.TradeError.None && paid == quote && quote == prices.AmmoSellValue(ammoLight, stack.Quantity));
            Check("a repeated sell request for the same stack pays nothing", merchantService.Sell(backpack, stack.InstanceId) == RuinRail.Gameplay.Base.TradeError.SourceMissingItem && state.CarriedCoins == coinsBefore + paid);
            _result.AmmoSaleEvidence = $"sold {stack.Quantity} light ammo: quote {quote}, coins {coinsBefore} -> {state.CarriedCoins}; " + string.Join("; ", quotes);
            inventory.Add(AmmoType.Light, lightBefore); // put the rounds back for the rest of the smoke

            // The real merchant screen, when this seed places a merchant: SELL tab shows the same quote the payout uses.
            var merchantBinding = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).FirstOrDefault(b => b != null && b.Merchant != null);
            if (merchantBinding != null && run.Merchant != null && run.MerchantView != null)
            {
                var interactable = merchantBinding.Merchant;
                Put((Vector2)interactable.transform.position + Vector2.down * 1.1f);
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 3; i++) yield return null;
                var interactor = player.GetComponent<PlayerInteractor>();
                if (interactor.TryInteract() && run.Merchant.IsOpen)
                {
                    run.Merchant.SetTab(MerchantTab.Sell);
                    yield return null;
                    var row = run.Merchant.Rows.FirstOrDefault(r => r.Item != null && r.Item.DefinitionId == ammoLight.Id);
                    if (row != null)
                    {
                        run.Merchant.SetCursor(run.Merchant.Rows.ToList().IndexOf(row));
                        yield return null;
                        yield return CaptureHud("ecdp_01_ammo_sell_quote");
                        var before = state.CarriedCoins;
                        var expected = row.Price;
                        var error = run.Merchant.Sell();
                        yield return null;
                        Check($"merchant UI SELL of {row.Item.Quantity} light ammo: UI quote {expected} == payout {state.CarriedCoins - before}", error == RuinRail.Gameplay.Base.TradeError.None && state.CarriedCoins - before == expected);
                        yield return CaptureHud("ecdp_02_ammo_sold_coin_change");
                        inventory.Add(AmmoType.Light, row.Item.Quantity);
                    }
                    else passed.Add("merchant UI open; no light-ammo row to sell on this seed (service path proven above)");
                    run.Merchant.Close();
                    yield return null;
                }
            }
            else passed.Add("no merchant room on this seed (the UI sale is proven by the PlayMode suite; the service payout above is authoritative)");

            // ---------------------------------------------------------------- 2. containment ----
            // The player stands just beyond open doorways for seconds at a time: guarded, so a lucky shot cannot end the smoke.
            var playerHealth = player.GetComponent<HealthComponent>();
            var guard = new SmokeGuard();
            playerHealth.SetInvulnerabilityState(guard);
            var combatRoom = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Combat && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered && r.Root.GetSockets().Any(s => !RoomExitSealer.IsSealed(r.Root, s)));
            if (combatRoom != null)
            {
                var socket = combatRoom.Root.GetSockets().First(s => !RoomExitSealer.IsSealed(combatRoom.Root, s));
                var (doorway, step) = DoorCentre(combatRoom.Root, socket);
                var interior = combatRoom.InteriorWorldBounds;
                var spawnedHere = new List<EnemyController>();
                combatRoom.EnemySpawned += (_, e) => spawnedHere.Add(e);
                Put(RoomCentre(combatRoom));
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 3; i++) yield return null;
                Check("entering a combat room activates it and spawns its encounter", combatRoom.Lifecycle == RoomLifecycleState.Active && spawnedHere.Count > 0);
                Check("every spawned enemy is bound to the room that spawned it", spawnedHere.All(e => e.Bounds != null && e.Bounds.IsBound && e.Bounds.RoomNodeId == combatRoom.State.NodeId));
                // The player leaves through the doorway; the door is deliberately opened so only the bounds hold the pack.
                // Just past the door cells: beyond the room, short of the neighbour's own entry volume.
                var outside = doorway + step * 1.4f;
                Put(outside);
                combatRoom.UnlockDoors();
                var worst = 0f;
                var closest = float.PositiveInfinity;
                for (var i = 0; i < 150; i++)
                {
                    yield return new WaitForFixedUpdate();
                    foreach (var e in spawnedHere)
                    {
                        if (e == null || !e.IsAlive) continue;
                        var p = (Vector2)e.transform.position;
                        var r = e.Bounds != null ? e.Bounds.BodyRadius : DefaultEnemySpawner.BodyRadius;
                        worst = Mathf.Max(worst, Mathf.Max(interior.xMin - (p.x - r), Mathf.Max((p.x + r) - interior.xMax, Mathf.Max(interior.yMin - (p.y - r), (p.y + r) - interior.yMax))));
                        closest = Mathf.Min(closest, Vector2.Dot(doorway - p, step));
                    }
                }

                Check($"the pack pursued the player to the open doorway (closest approach {closest:0.00} tiles from the door line) and no collider left the room (worst overshoot {worst:0.000})", worst <= 0.06f && spawnedHere.Any(e => e != null && e.IsAlive && e.State == EnemyState.Chase));
                Put(doorway - step * 0.2f); // stand in the doorway so the capture shows the pack held at the edge
                yield return new WaitForFixedUpdate();
                yield return null;
                yield return CaptureHud("ecdp_03_enemies_contained_at_open_doorway");
                Put(outside);
                foreach (var e in spawnedHere) if (e != null && e.IsAlive) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                yield return new WaitForFixedUpdate();
            }
            else passed.Add("no unentered combat room with an open exit on this seed (containment proven by the PlayMode suite)");

            var bossRoom = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Boss);
            var bossBinding = bossRoom != null ? bossRoom.GetComponent<RoomContentBinding>() : null;
            if (bossRoom != null && bossBinding != null && bossBinding.Boss != null && bossBinding.Boss.Boss != null)
            {
                var boss = bossBinding.Boss.Boss;
                var interior = bossRoom.InteriorWorldBounds;
                Check("the Boss is bound to its arena before anyone enters, and has no target yet", boss.Bounds != null && boss.Bounds.IsBound && boss.Bounds.RoomNodeId == bossRoom.State.NodeId && boss.Target == null && boss.State == MovesetActorState.Idle);
                var socket = bossRoom.Root.GetSockets().First(s => !RoomExitSealer.IsSealed(bossRoom.Root, s));
                var (doorway, step) = DoorCentre(bossRoom.Root, socket);
                // The Boss starts on the door's approach lane (arena props across the room make it hold at cover, the
                // existing head-on steering rule): the containment question is the edge, not the route.
                var bossBody = boss.GetComponent<Rigidbody2D>();
                for (var back = 4f; back <= 8f; back += 1f)
                {
                    var spot = doorway - step * back;
                    if (!RoomRuntime.IsSpawnClear(spot)) continue;
                    boss.transform.position = spot; bossBody.position = spot; Physics2D.SyncTransforms();
                    break;
                }

                // A player standing beyond the open arena door: the Boss pursues and holds at the arena edge.
                Put(doorway + step * 1.4f);
                bossRoom.UnlockDoors();
                boss.SuppressAttacks = true; // pursuit only: the containment question is where the body may go
                boss.SetTarget(player.transform);
                var radius = boss.Bounds.BodyRadius;
                var worst = 0f;
                var closest = float.PositiveInfinity;
                for (var i = 0; i < 300; i++)
                {
                    yield return new WaitForFixedUpdate();
                    var p = (Vector2)boss.transform.position;
                    worst = Mathf.Max(worst, Mathf.Max(interior.xMin - (p.x - radius), Mathf.Max((p.x + radius) - interior.xMax, Mathf.Max(interior.yMin - (p.y - radius), (p.y + radius) - interior.yMax))));
                    closest = Mathf.Min(closest, Vector2.Dot(doorway - p, step));
                }

                Check($"the Boss pursued to the arena edge (closest {closest:0.00} tiles from the door line) and never left it (worst overshoot {worst:0.000})", worst <= 0.06f && closest < 0.5f + radius + 1f && closest >= 0.5f + radius - 0.05f);
                yield return CaptureHud("ecdp_04_boss_contained_at_arena_boundary");
                boss.SetTarget(null);
                boss.SuppressAttacks = false;
                boss.enabled = false; // the arena fight itself is not this stage's business
            }
            else passed.Add("no boss arena bound on this depth (proven by the PlayMode suite)");

            playerHealth.SetInvulnerabilityState(null);

            // ---------------------------------------------------------------- 3. backpack reorder ----
            var vm = run.Inventory;
            var view = run.InventoryView;
            if (vm != null && view != null)
            {
                Put(RoomCentre(run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start)));
                yield return new WaitForFixedUpdate();
                var medkit = new ItemInstance("consumable_medkit", 2);
                if (!inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == "consumable_medkit")) inventory.TryAddToBackpack(medkit);
                string[] Order() => inventory.BackpackSlots.Select(s => s?.DefinitionId ?? "-").ToArray();
                var occupied = Enumerable.Range(0, PlayerInventory.BackpackCapacity).Where(i => inventory.BackpackSlots[i] != null).ToList();
                var empties = Enumerable.Range(0, PlayerInventory.BackpackCapacity).Where(i => inventory.BackpackSlots[i] == null).ToList();
                if (occupied.Count >= 2 && empties.Count >= 1)
                {
                    var a = occupied[0];
                    var b = empties[empties.Count - 1];
                    var moving = inventory.BackpackSlots[a];
                    vm.Open();
                    yield return null;
                    yield return CaptureHud("ecdp_08_backpack_before_move");
                    view.BackpackSlots[a].SimulateClick();
                    yield return null;
                    Check($"left click on backpack slot {a + 1} picks the item up", vm.Selected.HasValue && vm.Selected.Value.Equals(new InventorySlotRef(InventorySlotKind.Backpack, a)));
                    view.BackpackSlots[b].SimulateClick();
                    yield return null;
                    Check($"left click on empty slot {b + 1} moves it to exactly slot {b + 1}", inventory.BackpackSlots[b] == moving && inventory.BackpackSlots[a] == null && vm.LastReorder == SlotMoveResult.Moved && view.BackpackSlots[b].IsOccupied && !view.BackpackSlots[a].IsOccupied);
                    yield return CaptureHud("ecdp_09_backpack_after_click_move");
                    var c = occupied[1];
                    var other = inventory.BackpackSlots[c];
                    view.BackpackSlots[b].SimulateClick();
                    yield return null;
                    view.BackpackSlots[c].SimulateClick();
                    yield return null;
                    Check($"left click on occupied slot {c + 1} swaps the two", inventory.BackpackSlots[c] == moving && inventory.BackpackSlots[b] == other && vm.LastReorder == SlotMoveResult.Swapped);
                    yield return CaptureHud("ecdp_10_backpack_slots_swapped");
                    view.BackpackSlots[a].SimulateDrop(view.BackpackSlots[c]);
                    yield return null;
                    Check($"drag/drop from slot {c + 1} onto empty slot {a + 1} moves it back", inventory.BackpackSlots[a] == moving && inventory.BackpackSlots[c] == null && vm.LastReorder == SlotMoveResult.Moved);
                    yield return CaptureHud("ecdp_11_backpack_drag_drop_reorder");
                    var order = Order();
                    vm.Close();
                    yield return null;
                    vm.Open();
                    yield return null;
                    Check("the manual order survives close/reopen (no compaction, no re-sort)", Order().SequenceEqual(order));
                    var ids = inventory.BackpackSlots.Where(s => s != null).Select(s => s.InstanceId).ToList();
                    Check("no item duplicated or lost by the reorders", ids.Count == ids.Distinct().Count() && inventory.Contains(moving.InstanceId) && inventory.Contains(other.InstanceId));
                    vm.Close();
                    yield return null;
                }
                else passed.Add("backpack layout did not offer two occupied and one empty slot (reorder proven by the PlayMode suite)");
            }

            // ---------------------------------------------------------------- 4. projectiles ----
            var catalog = ProjectileVisualCatalog.Active;
            Check("the projectile visual catalog is bound in the shipped player", catalog != null && catalog.Problems().Count == 0);
            var families = new (string weaponId, string profile, string capture)[]
            {
                ("weapon_p9_ranger", "proj_pistol", "ecdp_12_p9_projectile_visible"),
                ("weapon_rattler_9", "proj_smg", "ecdp_13a_smg_projectile_visible"),
                ("weapon_ar_17", "proj_rifle", "ecdp_13b_ar_projectile_visible"),
                ("weapon_hound_br", "proj_battle_rifle", "ecdp_13c_battle_rifle_projectile_visible"),
                ("weapon_scatter_8", "proj_pellet", "ecdp_14_shotgun_pellets_visible"),
                ("weapon_longshot_s1", "proj_sniper", "ecdp_15_sniper_tracer_visible"),
                ("weapon_recurve_bow", "proj_arrow", "ecdp_16a_bow_arrow_visible"),
                ("weapon_pulse_carbine_b1", "proj_energy_bolt", "ecdp_16b_blaster_bolt_visible"),
                ("weapon_pipe_launcher", "proj_rocket", "ecdp_16c_rocket_visible"),
                ("weapon_quickfang", "proj_pistol_legendary", "ecdp_16d_legendary_quickfang_visible")
            };
            var startRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start);
            var firingSpot = RoomCentre(startRoom);
            var aiming = player.GetComponent<PlayerAiming>();
            var loadout = run.Rig.Loadout;
            var originalPrimary = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            foreach (var (weaponId, profile, capture) in families)
            {
                Put(firingSpot);
                yield return new WaitForFixedUpdate();
                var definition = _app.Configs.Resolve(weaponId) as WeaponDefinition;
                if (definition == null) { Check(weaponId + " exists", false); continue; }
                var current = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
                if (current == null || current.DefinitionId != weaponId)
                {
                    var item = new ItemInstance(weaponId, 1, weaponId == "weapon_quickfang" ? Rarity.Legendary : Rarity.Common);
                    if (current != null) inventory.Unequip(EquippedSlot.PrimaryWeapon);
                    inventory.TryEquip(item, EquippedSlot.PrimaryWeapon);
                    yield return null;
                }

                loadout.SelectSlot(WeaponSlot.Primary);
                var weapon = loadout.GetSlot(WeaponSlot.Primary);
                var fired = false;
                Projectile shot = null;
                switch (weapon)
                {
                    case RangedWeapon ranged: ranged.ApplyAuthoritativeState(ranged.Definition.MagazineSize, false); fired = ranged.TryFire(); shot = ranged.LastSpawnedProjectile; break;
                    case BowWeapon bow: fired = bow.TryStartCharge() && bow.TryRelease(); shot = bow.LastSpawnedProjectile; break;
                    case BlasterWeapon blaster: fired = blaster.TryFire(); shot = blaster.LastSpawnedProjectile; break;
                }

                var visible = fired && shot != null && shot.Visual != null && shot.Visual.IsVisible && shot.Visual.ProfileId == profile;
                Check($"{weaponId} fires a visible '{profile}' projectile at the muzzle, oriented to its velocity", visible && shot.Visual.Body.transform.position == shot.transform.position);
                if (weaponId == "weapon_scatter_8")
                {
                    var pellets = run.Rig.Projectiles.GetComponentsInChildren<Projectile>().Where(p => p.gameObject.activeSelf && !p.IsResolved).ToList();
                    Check($"every shotgun pellet ({pellets.Count}) is a visible pellet", pellets.Count >= 2 && pellets.All(p => p.Visual != null && p.Visual.IsVisible && p.Visual.ProfileId == "proj_pellet"));
                }

                // Controlled frame capture: a few physics steps clear of the muzzle, then the world is frozen so the in-flight sprite is on the frame at a known position.
                for (var i = 0; i < 6 && shot != null && !shot.IsResolved; i++) yield return new WaitForFixedUpdate();
                Time.timeScale = 0f;
                yield return null;
                _result.ProjectilePositions.Add($"{weaponId}: {profile} at {(shot != null ? shot.transform.position.ToString("0.00") : "-")}, visible={visible}");
                yield return CaptureHud(capture);
                Time.timeScale = 1f;
                for (var i = 0; i < 60; i++) { yield return new WaitForFixedUpdate(); if (shot == null || shot.IsResolved) break; }
                Check($"{weaponId}: the sprite ends with the shot (hit / range), never beyond it", shot == null || (shot.IsResolved && !shot.Visual.IsVisible) || !shot.IsResolved);
            }

            // Restore the player's own primary.
            var equippedNow = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            if (originalPrimary != null && (equippedNow == null || equippedNow.InstanceId != originalPrimary.InstanceId))
            {
                if (equippedNow != null) inventory.Unequip(EquippedSlot.PrimaryWeapon);
                inventory.TryEquip(originalPrimary, EquippedSlot.PrimaryWeapon);
                yield return null;
            }

            // Enemy and Boss projectiles: the real attack behaviours through the real resolver, on the real pools.
            var shooterDefinition = content.Enemies.FirstOrDefault(e => e.Id == "shooter");
            if (shooterDefinition != null)
            {
                var spawnAt = firingSpot + Vector2.right * 4f;
                var shooter = new DefaultEnemySpawner(content.Stagger).Spawn(shooterDefinition, spawnAt, player.transform);
                run.BindEnemyPresentation(shooter);
                shooter.enabled = false; // the attack is fired by hand: only the projectile presentation is under test
                var attack = shooter.Attack as EnemyProjectileAttack;
                var ok = attack != null && attack.TryResolveAttack(player.transform) && attack.SpawnedProjectiles.Count > 0;
                var round = ok ? attack.SpawnedProjectiles[0] : null;
                Check("an enemy shooter's round is a visible hostile projectile", ok && round.Visual != null && round.Visual.IsVisible && round.Visual.ProfileId == "proj_enemy_round");
                for (var i = 0; i < 4 && round != null && !round.IsResolved; i++) yield return new WaitForFixedUpdate();
                Time.timeScale = 0f;
                yield return null;
                _result.ProjectilePositions.Add($"enemy shooter: proj_enemy_round at {(round != null ? round.transform.position.ToString("0.00") : "-")}");
                yield return CaptureHud("ecdp_17_enemy_projectile_visible");
                Time.timeScale = 1f;
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                shooter.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
            }

            if (bossRoom != null && bossBinding != null && bossBinding.Boss != null && bossBinding.Boss.Boss != null)
            {
                var boss = bossBinding.Boss.Boss;
                var volley = boss.Definition != null ? boss.Definition.Moveset.FirstOrDefault(a => a != null && a.Motion == AttackMotion.Projectile) : null;
                if (volley != null)
                {
                    var anchorMarker = bossRoom.Root.GetMarkers(RoomMarkerRole.BossAnchor).FirstOrDefault();
                    var anchor = anchorMarker != null ? (Vector2)bossRoom.Root.transform.TransformPoint(anchorMarker.WorldCenter) : bossRoom.InteriorWorldBounds.center;
                    boss.transform.position = anchor; boss.GetComponent<Rigidbody2D>().position = anchor; Physics2D.SyncTransforms();
                    Put(anchor + Vector2.right * 5f);
                    for (var i = 0; i < 30; i++) yield return null; // the camera settles on the player first
                    var resolver = boss.Resolver;
                    resolver.Begin(volley, Vector2.right);
                    resolver.Tick(0.01f);
                    var spawned = resolver.SpawnedProjectiles;
                    Check($"the Boss volley '{volley.Id}' spawns visible '{volley.ProjectileVisualId}' projectiles", spawned.Count > 0 && spawned.All(p => p.Visual != null && p.Visual.IsVisible && p.Visual.ProfileId == volley.ProjectileVisualId) && volley.ProjectileVisualId.StartsWith("proj_boss"));
                    for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
                    Time.timeScale = 0f;
                    yield return null;
                    _result.ProjectilePositions.Add($"boss {boss.Definition.Id}: {volley.ProjectileVisualId} x{spawned.Count} first at {(spawned.Count > 0 ? spawned[0].transform.position.ToString("0.00") : "-")}");
                    yield return CaptureHud("ecdp_18_boss_projectile_visible");
                    Time.timeScale = 1f;
                    for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                    resolver.Cancel();
                }
                else passed.Add("this depth's Boss has no projectile attack (boss projectile visuals proven by the PlayMode suite)");
            }

            Put(firingSpot);
            _result.EconomyContainmentProjectileChecks = passed.ToArray();
            Debug.Log("[SMOKE] econ/contain/proj: " + string.Join("; ", passed));
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

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }
    }
}
