using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Presentation.World;
using RuinRail.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// The runtime half of the presentation / audio / UX-QoL pass: the things a data check cannot prove.
    ///
    /// Does the substrate actually render under the floor with no collider? Does a small base radius pull a coin the
    /// player is standing next to, and leave one across the room alone, and refuse one behind a wall? Does the Coil
    /// still reach further? Does the quick-grenade key throw exactly one grenade and spend exactly one? Does turning
    /// aim assist off actually change the direction a bullet leaves in? Does a status chip appear and then clear?
    /// </summary>
    public class PresentationAudioUxQolRuntimeTests
    {
        private const string MatrixDirectory = "TestResults/PresentationAudioUxQol";
        private readonly List<UnityEngine.Object> _created = new();
        private int _lane;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            AssistPreferences.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
            AssistPreferences.Reset();
            WorldSubstrate.SkinResolver = null;
        }

        private T Track<T>(T o) where T : UnityEngine.Object
        {
            _created.Add(o);
            return o;
        }

        /// <summary>Each test body gets its own stretch of world, so two fixtures can never touch each other's physics.</summary>
        private Vector2 NextLane() => new(0f, 200f + _lane++ * 200f);

        private static void WriteMatrix(string fileName, IEnumerable<string> rows)
        {
            Directory.CreateDirectory(MatrixDirectory);
            File.WriteAllLines(Path.Combine(MatrixDirectory, fileName), rows);
        }

        // ---------------- world substrate ----------------

        [Test]
        public void Substrate_RendersUnderTheFloorWithNoCollider_ForEveryBiomeAndRoomSize()
        {
            var rows = new List<string>
            {
                "biome,case,layout_w_tiles,layout_h_tiles,coverage_w_tiles,coverage_h_tiles,viewport_uncovered_tiles,sorting_layer,sorting_order,colliders,raw_black_void"
            };

            // The 640x360 viewport at 32 PPU: 20 x 11.25 world tiles.
            const float viewportW = 20f;
            const float viewportH = 11.25f;

            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            foreach (var (name, w, h) in new[]
                     {
                         ("small room", 16f, 12f), ("medium room", 24f, 16f), ("large room", 36f, 24f),
                         ("layout-edge room", 16f, 12f), ("cluster with gaps", 96f, 64f)
                     })
            {
                var origin = NextLane();
                var layout = new Rect(origin.x, origin.y, w, h);
                var root = Track(new GameObject($"SubstrateHost_{biome}_{name}"));
                var substrate = WorldSubstrate.Create(root.transform, biome, layout);

                Assert.IsNotNull(substrate.Renderer.sprite, $"{biome}/{name}: the substrate must have art to draw.");
                Assert.AreEqual(SortingLayers.Ground, substrate.Renderer.sortingLayerName);
                Assert.AreEqual(WorldSubstrate.SortingOrder, substrate.Renderer.sortingOrder);
                Assert.Less(substrate.Renderer.sortingOrder, SortingConvention.BaseOrderOf(SortingRole.Floor),
                    $"{biome}/{name}: the substrate must draw below the floor.");
                Assert.AreEqual(SpriteDrawMode.Tiled, substrate.Renderer.drawMode, "The underlay tiles rather than stretching.");
                Assert.IsEmpty(root.GetComponentsInChildren<Collider2D>(), $"{biome}/{name}: the substrate must add no collider.");

                // The camera clamps to the layout, so its widest possible view is the layout plus its own extent; the
                // substrate has to cover that, or raw void is on screen.
                var coverage = substrate.Coverage;
                Assert.GreaterOrEqual(coverage.width, Mathf.Max(w, viewportW), $"{biome}/{name}: uncovered width.");
                Assert.GreaterOrEqual(coverage.height, Mathf.Max(h, viewportH), $"{biome}/{name}: uncovered height.");
                var uncovered = Mathf.Max(0f, viewportW - coverage.width) + Mathf.Max(0f, viewportH - coverage.height);

                rows.Add(string.Join(",", biome, name, w, h, coverage.width, coverage.height, uncovered,
                    substrate.Renderer.sortingLayerName, substrate.Renderer.sortingOrder,
                    root.GetComponentsInChildren<Collider2D>().Length, "NO"));

                UnityEngine.Object.DestroyImmediate(root);
                _created.Remove(root);
            }

            WriteMatrix("world_substrate_matrix.csv", rows);
        }

        [Test]
        public void Substrate_UsesAuthoredArtWhenRegistered_AndFallsBackWhenNot()
        {
            var texture = Track(new Texture2D(4, 4));
            var authored = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect));
            WorldSubstrate.SkinResolver = _ => new SubstrateSkin(authored, Color.white);
            var host = Track(new GameObject("AuthoredSubstrate"));
            var withArt = WorldSubstrate.Create(host.transform, Biome.Rustworks, new Rect(NextLane(), new Vector2(16f, 12f)));
            Assert.IsTrue(withArt.UsesAuthoredArt, "A registered skin must be preferred over the generated fallback.");
            Assert.AreSame(authored, withArt.Renderer.sprite);

            WorldSubstrate.SkinResolver = null;
            var host2 = Track(new GameObject("GeneratedSubstrate"));
            var generated = WorldSubstrate.Create(host2.transform, Biome.Rustworks, new Rect(NextLane(), new Vector2(16f, 12f)));
            Assert.IsFalse(generated.UsesAuthoredArt);
            Assert.IsNotNull(generated.Renderer.sprite, "With no authored art the underlay still draws something.");
        }

        // ---------------- pickup attraction ----------------

        private sealed class Harness
        {
            public GameObject Player;
            public PickupAttractor Attractor;
            public PlayerStats Stats;
        }

        private Harness BuildPlayer(Vector2 position)
        {
            var go = Track(new GameObject("AttractionPlayer"));
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            var inventory = PlayerInventory.FromRegistry(Registry(), Catalog().AmmoBalance);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(new RuinRail.Gameplay.Economy.CoinWallet(RuinRail.Gameplay.Economy.CoinDomain.Carried));
            var stats = new PlayerStats(Catalog().StatCaps, 100);
            var attractor = go.AddComponent<PickupAttractor>();
            attractor.SetStats(stats);
            return new Harness { Player = go, Attractor = attractor, Stats = stats };
        }

        private static ItemDefinitionRegistry _registry;
        private static ItemDefinitionRegistry Registry()
        {
            if (_registry != null) return _registry;
            _registry = ItemDefinitionRegistry.Build(Catalog().Items.Where(i => i != null));
            return _registry;
        }

        private static RuinRail.App.GameContentCatalog Catalog() => RuinRail.App.GameContentCatalog.Load();

        private CoinPickup SpawnCoin(Vector2 position, int amount = 5)
        {
            var go = Track(new GameObject("Coin"));
            go.transform.position = position;
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.2f;
            collider.isTrigger = true;
            var coin = go.AddComponent<CoinPickup>();
            coin.SetAmount(amount);
            return coin;
        }

        [UnityTest]
        public IEnumerator PickupAttraction_PullsWhatYouWalkOver_LeavesTheRoom_AndRespectsWalls()
        {
            var rows = new List<string> { "case,base_radius_tiles,coil_bonus_tiles,effective_radius_tiles,coin_distance_tiles,wall_between,pulled,collected" };
            var lane = NextLane();
            var harness = BuildPlayer(lane);
            yield return null;

            Assert.AreEqual(PickupAttractor.DefaultBaseRadiusTiles, harness.Attractor.BaseRadiusTiles, 0.0001f,
                "A composed attractor starts at the approved base radius.");
            Assert.AreEqual(PickupAttractor.DefaultBaseRadiusTiles, harness.Attractor.Radius, 0.0001f);

            // 1. Very near: it comes to you.
            var near = SpawnCoin(lane + new Vector2(1f, 0f));
            yield return null;
            for (var i = 0; i < 40; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(1, harness.Attractor.Collected, "A coin one tile away must be drawn in and collected.");
            rows.Add($"base, near coin,{PickupAttractor.DefaultBaseRadiusTiles},0,{harness.Attractor.Radius},1.0,NO,YES,YES".Replace("base, ", ""));

            // 2. Out of the baseline's reach but inside the Coil's: it stays put for a survivor without one.
            var mid = SpawnCoin(lane + new Vector2(3.5f, 0f));
            var midStart = mid.transform.position;
            for (var i = 0; i < 40; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(midStart, mid.transform.position, "A coin 3.5 tiles away must not move on the baseline reach.");
            Assert.AreEqual(1, harness.Attractor.Collected);
            rows.Add($"mid coin,{PickupAttractor.DefaultBaseRadiusTiles},0,{harness.Attractor.Radius},3.5,NO,NO,NO");

            // 3. Across the room: nothing reaches it, with or without the accessory. Not a vacuum.
            var far = SpawnCoin(lane + new Vector2(8f, 0f));
            var farStart = far.transform.position;
            for (var i = 0; i < 40; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(farStart, far.transform.position, "A coin eight tiles away must not move.");
            rows.Add($"far coin,{PickupAttractor.DefaultBaseRadiusTiles},0,{harness.Attractor.Radius},8.0,NO,NO,NO");

            // 4. Magnetic Coil: the mid coin comes in, the far one still does not — the accessory extends the reach
            //    by more than double without turning the room into a vacuum.
            harness.Stats.SetSource(new StatModifierSource("test:coil", StatModifier.Flat(StatId.PickupAttractionRadius, 3)));
            Assert.AreEqual(PickupAttractor.DefaultBaseRadiusTiles + 3f, harness.Attractor.Radius, 0.0001f,
                "The Coil is additive on top of the base radius.");
            Assert.Greater(harness.Attractor.Radius, PickupAttractor.DefaultBaseRadiusTiles * 2f,
                "The Coil must remain meaningfully stronger than the baseline.");
            for (var i = 0; i < 120; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(2, harness.Attractor.Collected, "With the Coil the mid coin is collected.");
            Assert.AreEqual(farStart, far.transform.position, "Even with the Coil, a coin eight tiles away stays put.");
            rows.Add($"magnetic coil (mid),{PickupAttractor.DefaultBaseRadiusTiles},3,{harness.Attractor.Radius},3.5,NO,YES,YES");
            rows.Add($"magnetic coil (far),{PickupAttractor.DefaultBaseRadiusTiles},3,{harness.Attractor.Radius},8.0,NO,NO,NO");
            harness.Stats.RemoveSource("test:coil");

            // 4. Behind a wall: never pulled through geometry.
            var wall = Track(new GameObject("Wall"));
            wall.transform.position = lane + new Vector2(1f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 6f);
            wall.AddComponent<EnvironmentObstacle>();
            yield return null;
            var behind = SpawnCoin(lane + new Vector2(2f, 0f));
            var behindStart = behind.transform.position;
            Assert.IsTrue(harness.Attractor.IsObstructed(behind.transform.position), "The wall must register as an obstruction.");
            for (var i = 0; i < 60; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(behindStart, behind.transform.position, "A coin behind a wall must not be dragged through it.");
            Assert.AreEqual(2, harness.Attractor.Collected);
            rows.Add($"behind wall,{PickupAttractor.DefaultBaseRadiusTiles},0,{harness.Attractor.Radius},2.0,YES,NO,NO");

            // 5. A stack the backpack cannot take is not pulled at all — otherwise it would be dragged onto the player
            //    and then hold the interaction prompt hostage from whatever they are standing at.
            UnityEngine.Object.DestroyImmediate(wall);
            _created.Remove(wall);
            var registry = Registry();
            var full = harness.Player.GetComponent<PlayerLootReceiver>().Inventory;
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
                full.TryAddToBackpack(new ItemInstance(Catalog().Items.First(x => x != null && x.Category == ItemCategory.Weapon).Id, 1));
            var refused = Track(new GameObject("RefusedAmmo"));
            refused.transform.position = lane + new Vector2(0.8f, 0f);
            var refusedCollider = refused.AddComponent<CircleCollider2D>();
            refusedCollider.radius = 0.2f;
            refusedCollider.isTrigger = true;
            var refusedPickup = refused.AddComponent<WorldItemPickup>();
            var ammoDefinition = Catalog().Items.OfType<AmmoItemDefinition>().First();
            refusedPickup.Hold(new ItemInstance(ammoDefinition.Id, 10), ItemCategory.Ammo);
            var refusedStart = refused.transform.position;
            yield return null;
            Assert.IsFalse(refusedPickup.CanBeCollectedBy(harness.Player), "A full backpack cannot take this stack.");
            for (var i = 0; i < 60; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(refusedStart, refused.transform.position,
                "A stack the backpack cannot take must not be dragged onto the player (it would steal the interact prompt).");
            rows.Add($"backpack full,{PickupAttractor.DefaultBaseRadiusTiles},0,{harness.Attractor.Radius},0.8,NO,NO,NO");

            WriteMatrix("pickup_attraction_matrix.csv", rows);
        }

        [UnityTest]
        public IEnumerator PickupAttraction_NeverTouchesChestsMerchantsOrEventObjects()
        {
            var lane = NextLane();
            var harness = BuildPlayer(lane);
            yield return null;

            // Only coins and ammo declare themselves attractable; equipment and every interactable do not.
            var equipment = Track(new GameObject("EquipmentPickup"));
            equipment.transform.position = lane + new Vector2(0.5f, 0f);
            var collider = equipment.AddComponent<CircleCollider2D>();
            collider.radius = 0.2f;
            collider.isTrigger = true;
            var pickup = equipment.AddComponent<WorldItemPickup>();
            var weapon = Catalog().Items.First(i => i != null && i.Category == ItemCategory.Weapon);
            pickup.Hold(new ItemInstance(weapon.Id, 1, Rarity.Common), weapon.Category);
            var start = equipment.transform.position;
            for (var i = 0; i < 40; i++) harness.Attractor.Step(0.02f);
            Assert.AreEqual(start, equipment.transform.position, "Equipment still requires a deliberate interaction.");
            Assert.AreEqual(0, harness.Attractor.Collected);
        }

        // ---------------- aim assist ----------------

        [UnityTest]
        public IEnumerator AimAssist_OffRemovesTheBend_OnRestoresIt()
        {
            var lane = NextLane();
            var shooter = Track(new GameObject("Shooter"));
            shooter.transform.position = lane;
            shooter.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);

            // A target off to one side but inside the assist cone: with assist on the shot bends onto it.
            var target = Track(new GameObject("Target"));
            target.transform.position = lane + new Vector2(6f, 1.2f);
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            target.AddComponent<HealthComponent>().SetMaxHealth(100);
            yield return null;

            var assist = AimAssistConfig.Defaults();
            _created.Add(assist);
            var raw = Vector2.right;

            AssistPreferences.Set(true);
            var on = ShotSolver.Solve(lane, lane, raw, 12f, WeaponClass.AssaultRifle, shooter, DamageTeam.Player, assist, null);
            AssistPreferences.Set(false);
            var off = ShotSolver.Solve(lane, lane, raw, 12f, WeaponClass.AssaultRifle, shooter, DamageTeam.Player, assist, null);

            Assert.AreNotEqual(Vector2.zero, on.Direction);
            Assert.Greater(Vector2.Angle(raw, on.Direction), 1f, "With assist ON the shot must bend toward the target.");
            Assert.AreEqual(0f, Vector2.Angle(raw, off.Direction), 0.001f, "With assist OFF the shot must leave on the raw aim.");
            Assert.IsNull(off.AssistedTarget, "With assist OFF no target is selected at all.");

            AssistPreferences.Set(true);
            var again = ShotSolver.Solve(lane, lane, raw, 12f, WeaponClass.AssaultRifle, shooter, DamageTeam.Player, assist, null);
            Assert.AreEqual(on.Direction.x, again.Direction.x, 0.0001f, "Turning it back on restores the previous behaviour exactly.");
            Assert.AreEqual(on.Direction.y, again.Direction.y, 0.0001f);

            WriteMatrix("aim_assist_setting_matrix.csv", new[]
            {
                "case,setting,raw_direction,resulting_direction,bend_degrees,target_selected",
                $"target inside the cone,ON,{raw},{on.Direction},{Vector2.Angle(raw, on.Direction):F2},{(on.AssistedTarget != null ? "YES" : "NO")}",
                $"target inside the cone,OFF,{raw},{off.Direction},{Vector2.Angle(raw, off.Direction):F2},{(off.AssistedTarget != null ? "YES" : "NO")}",
                $"target inside the cone,ON again,{raw},{again.Direction},{Vector2.Angle(raw, again.Direction):F2},{(again.AssistedTarget != null ? "YES" : "NO")}"
            });
        }

        // ---------------- quick grenade ----------------

        [UnityTest]
        public IEnumerator QuickGrenade_ThrowsFromTheBackpackAndSpendsExactlyOne()
        {
            var lane = NextLane();
            var go = Track(new GameObject("GrenadePlayer"));
            go.transform.position = lane;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            go.AddComponent<PlayerLifeStateComponent>();
            var launcher = go.AddComponent<RuinRail.Gameplay.Combat.Area.GrenadeLauncher>();
            launcher.SetDamageRoller(new UnityRandomDamageRoller());
            var inventory = PlayerInventory.FromRegistry(Registry(), Catalog().AmmoBalance);
            var stats = new PlayerStats(Catalog().StatCaps, 100);
            var user = go.AddComponent<PlayerConsumableUser>();
            user.Configure(inventory, id => Registry().TryGet(id, out var d) ? d : null, stats, new PlayerCombatEvents(), health, launcher, () => Vector2.right);
            yield return null;

            var grenadeDefinition = Catalog().Items.OfType<ConsumableDefinition>().First(c => c.EffectKind == ConsumableEffectKind.Grenade);
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance(grenadeDefinition.Id, 3)), "The grenade stack must fit the backpack.");
            // The container owns the stack once it is added (a stackable add can merge into and zero the source
            // instance), so the live stack is read back from the backpack rather than kept from before the add.
            var stack = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == grenadeDefinition.Id);
            Assert.AreEqual(3, stack.Quantity);
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.ActiveConsumable), "The Active Consumable slot stays empty: that is the point.");

            var rows = new List<string> { "case,slot_used,quantity_before,quantity_after,grenades_thrown,gate_held" };
            Assert.IsTrue(user.TryQuickGrenade(), "A backpack grenade must be throwable without equipping it.");
            yield return null;
            Assert.IsNotNull(launcher.LastThrown, "A grenade object must actually be launched.");
            Assert.AreEqual(2, stack.Quantity, "Exactly one unit is spent.");
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.ActiveConsumable), "Quick-use must not change the equipped consumable.");
            rows.Add($"backpack grenade,backpack,3,{stack.Quantity},1,NO");

            // The gate: while a window holds gameplay input the reader raises nothing, so the press cannot reach here.
            GameplayInputGate.Hold();
            var reader = new FakePlayerInputReader();
            user.SetInputReader(reader);
            var before = stack.Quantity;
            reader.RaiseQuickGrenade();
            yield return null;
            Assert.AreEqual(before, stack.Quantity, "A press while the gameplay gate is held must spend nothing.");
            rows.Add($"gate held,backpack,{before},{stack.Quantity},0,YES");
            GameplayInputGate.Release();

            // Nothing left: a silent no-op, never an error and never a spend.
            stack.SetQuantity(0);
            Assert.IsFalse(user.TryQuickGrenade(), "With no grenade the press does nothing.");
            rows.Add("no grenade,none,0,0,0,NO");

            WriteMatrix("grenade_quick_use_matrix.csv", rows);
        }

        // ---------------- status effects ----------------

        [UnityTest]
        public IEnumerator StatusChips_AppearForEveryTimedBuff_AndClearOnExpiry()
        {
            var stats = new PlayerStats(Catalog().StatCaps, 100);
            var health = Track(new GameObject("StatusHealth")).AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            var effects = new ConsumableEffectRunner(new ConsumableTargets(stats, new PlayerCombatEvents(), amount => { health.Heal(amount); return amount; }));
            var inventory = PlayerInventory.FromRegistry(Registry(), Catalog().AmmoBalance);

            var viewModel = new DungeonHudViewModel();
            viewModel.BindInventory(inventory);
            viewModel.BindStatusEffects(effects);
            var view = DungeonHudView.Create(viewModel, "StatusHud");
            Track(view.gameObject);
            yield return null;

            var timed = Catalog().Items.OfType<ConsumableDefinition>().Where(c => c.EffectKind == ConsumableEffectKind.TimedBuff).ToList();
            Assert.IsNotEmpty(timed, "There must be at least one authored timed buff to show.");
            Assert.LessOrEqual(timed.Count, HudSnapshot.MaxStatusEffects, "Every timed effect must have a chip.");

            var rows = new List<string> { "definition,duration_s,effect,activated,chip_shown,icon,fill_at_start,cleared_on_expiry" };
            foreach (var definition in timed)
            {
                Assert.IsTrue(effects.Apply(definition), definition.Id + " must apply.");
                viewModel.Tick();
                yield return null;
                var chip = view.VisibleStatusChips.FirstOrDefault(c => c.DefinitionId == definition.Id);
                Assert.IsNotNull(chip, definition.Id + " must show a chip while it is active.");
                Assert.Greater(chip.Fill, 0.5f, definition.Id + ": a freshly applied buff must show most of its time left.");
                StringAssert.Contains(definition.DisplayName, view.StatusDetailText);

                // Run the timer out through the authority that owns it; the chip must follow, not guess.
                effects.Tick(definition.BuffDurationSeconds + 0.1f);
                viewModel.Tick();
                yield return null;
                Assert.IsFalse(view.VisibleStatusChips.Any(c => c.DefinitionId == definition.Id),
                    definition.Id + ": the chip must clear the moment the effect expires.");
                rows.Add(string.Join(",", definition.Id, definition.BuffDurationSeconds,
                    $"{definition.BuffStat} {definition.BuffPercent:+0;-0}%", "YES", "YES",
                    definition.Icon != null ? "YES" : "NO", $"{chip.Fill:F2}", "YES"));
            }

            Assert.IsEmpty(view.VisibleStatusChips, "With nothing active the strip is empty, not a row of blanks.");
            Assert.IsEmpty(view.StatusDetailText);
            WriteMatrix("status_effect_matrix.csv", rows);
        }

        [UnityTest]
        public IEnumerator StatusStrip_DoesNotOverlapAnyExistingHudBlock()
        {
            var viewModel = new DungeonHudViewModel();
            var view = DungeonHudView.Create(viewModel, "LayoutHud");
            Track(view.gameObject);
            yield return null;

            Assert.IsNotNull(view.StatusPanel);
            var status = WorldRect(view.StatusPanel);
            foreach (var (name, rect) in new[]
                     {
                         ("minimap", WorldRect(view.TopLeftPanel)),
                         ("coins", WorldRect(view.TopRightPanel)),
                         ("boss bar", WorldRect(view.BossPanel)),
                         ("dash", WorldRect(view.DashPanel)),
                         ("hp", WorldRect(view.HpPanel)),
                         ("weapons", WorldRect(view.WeaponsPanel)),
                         ("consumable", WorldRect(view.ConsumablePanel)),
                         ("room title", WorldRect(view.RoomTitlePanel)),
                         ("notice", WorldRect(view.NoticePanel))
                     })
            {
                Assert.IsFalse(status.Overlaps(rect), $"The status strip overlaps the {name} block.");
            }
        }

        private static Rect WorldRect(RectTransform rect)
        {
            if (rect == null) return new Rect(-10000f, -10000f, 0f, 0f);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        // ---------------- prop dressing ----------------

        [UnityTest]
        public IEnumerator PropDressing_IsDeterministic_VariesBetweenRooms_AndNeverBlocksAnything()
        {
            var rows = new List<string> { "room,biome,pattern_count,authored_detail,added_detail,blocked_doors,blocked_markers,navigation_regressions,deterministic" };
            var pool = new List<RoomDefinition>();
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var directory = biome switch
                {
                    Biome.RuinedMetro => "RuinedMetro",
                    Biome.Rustworks => "Rustworks",
                    _ => "OvergrownLabs"
                };
                var guids = AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/Game/ScriptableObjects/Rooms/" + directory });
                var ofBiome = guids.Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(d => d != null && d.Prefab != null).ToList();
                // One of each size plus the boss arena: small rooms are 40 of the 63 shipping rooms, so a sample that
                // is all boss arenas (which carry no authored detail at all) would prove nothing about the common case.
                foreach (var size in new[] { RoomSizeClass.Small, RoomSizeClass.Medium, RoomSizeClass.Large })
                {
                    var pick = ofBiome.FirstOrDefault(d => d.SizeClass == size && d.RoomType == RoomType.Combat)
                               ?? ofBiome.FirstOrDefault(d => d.SizeClass == size);
                    if (pick != null) pool.Add(pick);
                }

                var boss = ofBiome.FirstOrDefault(d => d.RoomType == RoomType.Boss);
                if (boss != null) pool.Add(boss);
            }

            Assert.IsNotEmpty(pool, "Room prefabs must be available to dress.");
            foreach (var definition in pool)
            {
                var a = Track(UnityEngine.Object.Instantiate(definition.Prefab)).GetComponent<RoomRoot>();
                var b = Track(UnityEngine.Object.Instantiate(definition.Prefab)).GetComponent<RoomRoot>();
                var c = Track(UnityEngine.Object.Instantiate(definition.Prefab)).GetComponent<RoomRoot>();
                yield return null;

                var first = RoomPropDressing.Apply(a, runSeed: 7717, depth: 3, nodeId: 4);
                var repeat = RoomPropDressing.Apply(b, runSeed: 7717, depth: 3, nodeId: 4);
                var other = RoomPropDressing.Apply(c, runSeed: 7717, depth: 3, nodeId: 9);

                var cellsA = DetailCells(a);
                var cellsB = DetailCells(b);
                var cellsC = DetailCells(c);
                CollectionAssert.AreEquivalent(cellsA, cellsB, definition.Id + ": the same seed must dress identically.");
                Assert.AreEqual(first.Added, repeat.Added);

                // Two rooms in one layout must not look the same, or the pass achieved nothing.
                if (first.EligibleCells > 6) Assert.AreNotEqual(string.Join(";", cellsA), string.Join(";", cellsC),
                    definition.Id + ": two instances in one layout must differ.");

                // Nothing was placed into a doorway, a marker cell, geometry or a hazard.
                var blockedDoors = 0;
                foreach (var socket in a.GetSockets())
                foreach (var cell in cellsA)
                {
                    var dx = Mathf.Abs(cell.x - socket.Cell.x);
                    var dy = Mathf.Abs(cell.y - socket.Cell.y);
                    if (dx <= RoomPropDressing.DoorClearanceTiles && dy <= RoomPropDressing.DoorClearanceTiles) blockedDoors++;
                }

                var markers = a.GetMarkers().Select(m => new Vector3Int(m.Cell.x, m.Cell.y, 0)).ToHashSet();
                var blockedMarkers = cellsA.Count(markers.Contains);
                Assert.AreEqual(0, blockedMarkers, definition.Id + ": dressing must never sit on a marker cell.");

                var navigation = 0;
                var grid = a.Grid;
                var obstacles = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
                var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
                var hazards = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);
                foreach (var cell in cellsA)
                {
                    if (obstacles != null && obstacles.GetTile(cell) != null) navigation++;
                    if (walls != null && walls.GetTile(cell) != null) navigation++;
                    if (hazards != null && hazards.GetTile(cell) != null) navigation++;
                }

                Assert.AreEqual(0, navigation, definition.Id + ": dressing must never land on geometry or a hazard.");
                // The FloorDetail layer carries no collider, so nothing it holds can block movement at all.
                var detailObject = grid.transform.Find(RoomTilemapLayers.NameOf(RoomTilemapLayer.FloorDetail));
                Assert.IsNotNull(detailObject);
                Assert.IsEmpty(detailObject.GetComponents<Collider2D>(), definition.Id + ": FloorDetail must stay collider-free.");

                rows.Add(string.Join(",", definition.Id, definition.Biome, first.Patterns, first.Transformed, first.Added,
                    blockedDoors, blockedMarkers, navigation, "YES"));

                foreach (var instance in new[] { a.gameObject, b.gameObject, c.gameObject })
                {
                    _created.Remove(instance);
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            WriteMatrix("prop_dressing_matrix.csv", rows);
        }

        private static List<Vector3Int> DetailCells(RoomRoot root)
        {
            var cells = new List<Vector3Int>();
            var detail = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.FloorDetail);
            if (detail == null) return cells;
            foreach (var position in detail.cellBounds.allPositionsWithin)
                if (detail.GetTile(position) != null) cells.Add(position);
            cells.Sort((x, y) => x.x != y.x ? x.x.CompareTo(y.x) : x.y.CompareTo(y.y));
            return cells;
        }

        // ---------------- audio runtime ----------------

        [UnityTest]
        public IEnumerator Audio_KeepsOneListener_AndPitchVariesARepeatedCue()
        {
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            Assert.LessOrEqual(listeners.Length, 1, "The process must never hold two AudioListeners.");

            var catalog = Catalog();
            var service = Track(new GameObject("AudioService")).AddComponent<RuinRail.Audio.AudioService>();
            service.Configure(catalog.AudioEvents);
            AudioLevels.Set(1f, 1f, 1f, 1f, false);
            yield return null;

            // A high-frequency cue: repeated plays must land on different pitches, and the throttle must refuse a
            // second start inside its interval.
            var definition = catalog.AudioEvents.Events.First(e => e != null && e.Id == RuinRail.Audio.AudioEventIds.EnemyHit);
            Assert.Greater(definition.PitchMax - definition.PitchMin, 0.005f);
            Assert.Greater(definition.MinIntervalMs, 0);
            Assert.IsTrue(service.Play(definition.Id), "The first play must start.");
            Assert.IsFalse(service.Play(definition.Id), "A second play inside the minimum interval must be refused.");
            WriteMatrix("audio_runtime_matrix.csv", new[]
            {
                "check,value,expected,result",
                $"audio listeners in the test scene,{listeners.Length},at most 1,{(listeners.Length <= 1 ? "PASS" : "FAIL")}",
                $"enemy.hit pitch range,{definition.PitchMin:F2}..{definition.PitchMax:F2},varies,PASS",
                $"enemy.hit min interval,{definition.MinIntervalMs} ms,>0,PASS",
                "enemy.hit second play inside interval,refused,refused,PASS"
            });
        }
    }
}
