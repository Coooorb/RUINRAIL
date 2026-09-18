using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 131: the Dungeon HUD (91) — reference layout without overlap, live weapon/resource display, co-op life states, observer-only binding.</summary>
    public class DungeonHudTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private static Rect ReferenceRect(RectTransform rect)
        {
            // Anchored to a single point inside a 640x360 reference canvas: position = anchor * reference + offset - pivot * size.
            var reference = new Vector2(DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight);
            var origin = Vector2.Scale(rect.anchorMin, reference) + rect.anchoredPosition - Vector2.Scale(rect.pivot, rect.sizeDelta);
            return new Rect(origin, rect.sizeDelta);
        }

        private (GameObject go, FakePlayerInputReader reader, WeaponLoadout loadout, RangedWeapon rifle, BlasterWeapon blaster, AmmoReserve reserve) PlayerWithWeapons(PartyLifeRoster roster = null, string name = "Local")
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, LifeRoster = roster, ParticipantId = name });
            _created.Add(go);
            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.GetComponent<PlayerAiming>();
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.SetInputReader(reader);
            var rifle = go.AddComponent<RangedWeapon>();
            rifle.SetInputReader(reader);
            var rifleDefinition = (RangedWeaponDefinition)Resolve("weapon_p9_ranger");
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 36);
            rifle.SetProjectilePool(pool);
            rifle.SetAiming(aiming);
            rifle.SetAmmoReserve(reserve);
            rifle.SetDefinition(rifleDefinition);
            var blaster = go.AddComponent<BlasterWeapon>();
            blaster.SetInputReader(reader);
            blaster.SetDefinition((BlasterWeaponDefinition)_registry.Definitions.OfType<BlasterWeaponDefinition>().First());
            loadout.SetPrimary(rifle);
            loadout.SetSecondary(blaster);
            loadout.Initialize();
            return (go, reader, loadout, rifle, blaster, reserve);
        }

        // ---- Acceptance 1: reference layout, every element present, no overlap/cut-off ----

        [Test]
        public void Layout_PlacesEveryApprovedElementInsideTheReferenceResolution_WithoutOverlap()
        {
            var vm = new DungeonHudViewModel();
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            Assert.AreEqual(new Vector2(640f, 360f), view.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution, "101: 640x360 reference.");
            Assert.IsTrue(view.Canvas.pixelPerfect);

            var panels = new Dictionary<string, RectTransform>
            {
                ["HP (bottom-left)"] = view.HpPanel,
                ["Weapons (bottom-centre)"] = view.WeaponsPanel,
                ["Consumable (bottom-right)"] = view.ConsumablePanel,
                ["Minimap (top-left)"] = view.TopLeftPanel,
                ["Coins (top-right)"] = view.TopRightPanel,
                ["Party"] = view.PartyPanel
            };
            var bounds = new Rect(0f, 0f, 640f, 360f);
            var rects = panels.ToDictionary(p => p.Key, p => ReferenceRect(p.Value));
            foreach (var (name, rect) in rects.Select(kv => (kv.Key, kv.Value)))
            {
                Assert.IsTrue(bounds.Contains(rect.min) && bounds.Contains(rect.max), $"{name} {rect} inside 640x360.");
            }

            var hp = rects["HP (bottom-left)"];
            var weapons = rects["Weapons (bottom-centre)"];
            var consumable = rects["Consumable (bottom-right)"];
            var topLeft = rects["Minimap (top-left)"];
            var coins = rects["Coins (top-right)"];
            Assert.Less(hp.yMax, 180f, "HP in the bottom half");
            Assert.Less(hp.xMax, 213f, "HP in the left third");
            Assert.Less(Mathf.Abs(weapons.center.x - 320f), 1f, "Weapons centred");
            Assert.Less(weapons.yMax, 180f);
            Assert.Greater(consumable.xMin, 426f, "Consumable in the right third");
            Assert.Greater(topLeft.yMin, 180f, "Minimap top-left");
            Assert.Less(topLeft.xMax, 320f);
            Assert.Greater(coins.yMin, 180f);
            Assert.Greater(coins.xMin, 426f, "Coins top-right");

            var keys = rects.Keys.ToList();
            for (var i = 0; i < keys.Count; i++)
            for (var j = i + 1; j < keys.Count; j++)
            {
                Assert.IsFalse(rects[keys[i]].Overlaps(rects[keys[j]]), $"{keys[i]} {rects[keys[i]]} overlaps {keys[j]} {rects[keys[j]]}.");
            }

            Assert.IsFalse(string.IsNullOrEmpty(view.HpText));
            Assert.IsFalse(string.IsNullOrEmpty(view.BiomeText), "the biome identity is the top-left line under the minimap");
            Assert.IsFalse(string.IsNullOrEmpty(view.CoinsText));
        }

        // ---- Acceptance 2: weapon switching updates active weapon/resource immediately ----

        [UnityTest]
        public IEnumerator WeaponSwitching_UpdatesActiveWeaponAndResource_Immediately()
        {
            var (go, reader, loadout, rifle, blaster, reserve) = PlayerWithWeapons();
            var vm = new DungeonHudViewModel();
            vm.BindPlayer(go.GetComponent<HealthComponent>(), go.GetComponent<PlayerDash>(), go.GetComponent<PlayerLifeStateComponent>());
            vm.BindWeapons(loadout, rifle, blaster, t => reserve.Get(t));
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            yield return null;

            Assert.IsTrue(vm.Snapshot.Primary.IsActive);
            Assert.AreEqual(HudResourceKind.Ammo, vm.Snapshot.Primary.Resource);
            Assert.AreEqual(rifle.MagazineAmmo, vm.Snapshot.Primary.Magazine);
            Assert.AreEqual(36, vm.Snapshot.Primary.Reserve);
            Assert.IsTrue(view.PrimarySlot.IsActive && view.PrimarySlot.BracketsVisible, "slot 1 carries the active highlight");
            StringAssert.Contains($"{rifle.MagazineAmmo} / 36", view.PrimaryText);
            Assert.IsFalse(view.SecondarySlot.IsActive);
            Assert.AreEqual(HudResourceKind.Heat, vm.Snapshot.Secondary.Resource);

            var rendersBefore = view.Renders;
            reader.RaiseWeaponSwapped();
            Assert.AreEqual(WeaponSlot.Secondary, loadout.ActiveSlot);
            Assert.IsTrue(vm.Snapshot.Secondary.IsActive, "Updated synchronously through the loadout event.");
            Assert.IsFalse(vm.Snapshot.Primary.IsActive);
            Assert.Greater(view.Renders, rendersBefore);
            Assert.IsTrue(view.SecondarySlot.IsActive && !view.PrimarySlot.IsActive, "the active highlight moved to slot 2");
            StringAssert.Contains("HEAT 0%", view.SecondaryText);

            // Firing the rifle changes the magazine; the HUD follows on the next tick without polling anything else.
            reader.RaiseWeaponSwapped();
            reader.Aim = Vector2.right;
            Assert.IsTrue(rifle.TryFire());
            vm.Tick();
            Assert.AreEqual(rifle.MagazineAmmo, vm.Snapshot.Primary.Magazine);
            StringAssert.Contains($"{rifle.MagazineAmmo} / 36", view.PrimaryText);

            // Bow: draw presentation; empty rifle: NO AMMO.
            var bow = go.AddComponent<BowWeapon>();
            bow.SetInputReader(reader);
            bow.SetDefinition((BowWeaponDefinition)_registry.Definitions.OfType<BowWeaponDefinition>().First());
            loadout.SetSecondary(bow);
            vm.BindWeapons(loadout, rifle, bow, t => reserve.Get(t));
            Assert.AreEqual(HudResourceKind.Charge, vm.Snapshot.Secondary.Resource);
            Assert.AreEqual("READY", vm.Snapshot.Secondary.ResourceText);
            loadout.SelectSlot(WeaponSlot.Secondary);
            bow.TryStartCharge();
            bow.AdvanceCharge(0.4f);
            vm.Tick();
            Assert.IsTrue(vm.Snapshot.Secondary.IsCharging);
            StringAssert.StartsWith("DRAW ", vm.Snapshot.Secondary.ResourceText);
            reserve.Consume(AmmoType.Light, 36);
            var cleared = new AmmoReserve();
            vm.BindWeapons(loadout, rifle, bow, t => cleared.Get(t));
            Assert.AreEqual(0, vm.Snapshot.Primary.Reserve, "Reserve reads the player's ammo through the resolver.");
            Assert.IsFalse(vm.Snapshot.Primary.NoAmmo, "Magazine still loaded: not NO AMMO yet.");
        }

        // ---- Acceptance 3: co-op life-state / HP display follows the authoritative state ----

        [Test]
        public void Party_ShowsNamesLifeStateHp_DownedTimer_Dead_AndDisconnected()
        {
            var roster = new PartyLifeRoster();
            var (local, _, _, _, _, _) = PlayerWithWeapons(roster, "Local");
            var mate = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Mate", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, LifeRoster = roster, ParticipantId = "Mate", Position = new Vector2(3f, 0f) });
            _created.Add(mate);
            var vm = new DungeonHudViewModel();
            vm.BindPlayer(local.GetComponent<HealthComponent>(), local.GetComponent<PlayerDash>(), local.GetComponent<PlayerLifeStateComponent>());
            vm.BindParty(roster);
            vm.SetDisplayName("Mate", "Rook");
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);

            Assert.AreEqual(1, vm.Snapshot.Party.Count, "The local player is not listed as a teammate.");
            Assert.AreEqual("Rook", vm.Snapshot.Party[0].Name);
            Assert.AreEqual(HudLifeState.Alive, vm.Snapshot.Party[0].State);
            Assert.AreEqual("100/100", vm.Snapshot.Party[0].StateText);
            CollectionAssert.AreEqual(new[] { "Rook  100/100" }, view.PartyTexts);

            var mateHealth = mate.GetComponent<HealthComponent>();
            mateHealth.TryApplyDamage(new DamageRequest(30));
            Assert.AreEqual("Rook  70/100", view.PartyTexts[0], "HP follows the authoritative health event.");

            mateHealth.TryApplyDamage(new DamageRequest(999));
            var mateLife = mate.GetComponent<PlayerLifeStateComponent>();
            Assert.AreEqual(PlayerLifeState.Downed, mateLife.State);
            Assert.AreEqual(HudLifeState.Downed, vm.Snapshot.Party[0].State);
            Assert.AreEqual("Rook  DOWNED 20s", view.PartyTexts[0]);
            mateLife.Tick(5.5f);
            vm.Tick();
            Assert.AreEqual("Rook  DOWNED 15s", view.PartyTexts[0], "Bleedout timer counts down.");
            mateLife.Tick(20f);
            Assert.AreEqual(HudLifeState.Dead, vm.Snapshot.Party[0].State);
            Assert.AreEqual("Rook  DEAD", view.PartyTexts[0]);

            vm.SetMemberConnected("Mate", false);
            Assert.AreEqual("Rook  DISCONNECTED", view.PartyTexts[0]);
            vm.SetMemberConnected("Mate", true);
            Assert.AreEqual("Rook  DEAD", view.PartyTexts[0]);
        }

        // ---- Top info: depth/biome/objective/coins; consumable ----

        [Test]
        public void TopInfoAndConsumable_FollowExpeditionAndInventoryEvents()
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            var profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[]
                    {
                        new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() },
                        new InventorySnapshot.Entry { Slot = (int)EquippedSlot.ActiveConsumable, Item = new ItemInstance("consumable_bandage", 3).ToSnapshot() }
                    },
                    Backpack = System.Array.Empty<InventorySnapshot.Entry>()
                }
            };
            var vm = new DungeonHudViewModel();
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            vm.BindExpedition(expedition);
            var state = expedition.Start(profile, 11, Biome.Rustworks);
            vm.BindInventory(state.Inventory);

            Assert.AreEqual("RUSTWORKS", view.BiomeText, "the biome identity sits under the minimap; the depth is the minimap's own chip");
            Assert.AreEqual("0", view.CoinsText, "the coin readout is the token plus the number");
            Assert.IsTrue(view.CoinView.HasIconSprite, "the coin token is bound from the UI skin");
            Assert.AreEqual("x3", view.ConsumableText, "the consumable is an icon slot with a stack chip, not a text line");
            Assert.IsTrue(view.ConsumableSlot.IconVisible && !view.ConsumableSlot.IsEmpty, "the Bandage icon is shown");

            expedition.AddCarriedCoins(75);
            Assert.AreEqual("75", view.CoinsText, "Coins follow the wallet event.");
            state.Inventory.Unequip(EquippedSlot.ActiveConsumable);
            Assert.AreEqual(string.Empty, view.ConsumableText, "Only the active stack is shown; nothing when none is equipped.");
            Assert.IsTrue(view.ConsumableSlot.IsEmpty && !view.ConsumableSlot.IconVisible, "the slot reads as empty (neutral plate, no icon)");

            expedition.RecordBossDefeated(0);
            expedition.Descend();
            Assert.AreEqual(2, vm.Snapshot.Depth, "the depth follows the expedition");
            Assert.AreEqual(HudSnapshot.BiomeName(state.Biome), view.BiomeText);
        }

        // ---- Acceptance 4 + Req 5: observer only, no searches, integer/no-crit presentation ----

        [Test]
        public void Hud_NeverMutatesGameplay_NeverSearchesTheScene_AndShowsNoGearScoreOrCrit()
        {
            var (go, _, loadout, rifle, _, reserve) = PlayerWithWeapons();
            var health = go.GetComponent<HealthComponent>();
            var vm = new DungeonHudViewModel();
            vm.BindPlayer(health, go.GetComponent<PlayerDash>(), go.GetComponent<PlayerLifeStateComponent>());
            vm.BindWeapons(loadout, rifle, null, t => reserve.Get(t));
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            var hpBefore = health.CurrentHealth;
            var magazineBefore = rifle.MagazineAmmo;
            var reserveBefore = reserve.Get(AmmoType.Light);
            for (var i = 0; i < 50; i++) vm.Tick();
            Assert.AreEqual(hpBefore, health.CurrentHealth);
            Assert.AreEqual(magazineBefore, rifle.MagazineAmmo);
            Assert.AreEqual(reserveBefore, reserve.Get(AmmoType.Light));
            Assert.AreEqual(WeaponSlot.Primary, loadout.ActiveSlot);

            var sources = Directory.GetFiles("Assets/Game/Scripts/UI/Hud", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
            Assert.IsTrue(sources.Count >= 2);
            foreach (var source in sources)
            {
                Assert.IsFalse(Regex.IsMatch(source, @"FindObjectsByType|FindObjectOfType|FindFirstObjectByType|FindAnyObjectByType|GameObject\.Find|FindObjectsOfType"), "HUD never searches the scene.");
                Assert.IsFalse(Regex.IsMatch(source, @"TryApplyDamage|\.Heal\(|TryFire|TryEquip|Unequip\(|SelectSlot\(|Credit\(|Debit\(|TryStartDash"), "HUD never calls a gameplay mutator.");
                Assert.IsFalse(Regex.IsMatch(source, @"GearScore|Gear Score|PowerScore|CritChance|CriticalHit|IsCrit|\bCRIT\b"), "No Gear Score / crit UI (90/91).");
            }

            Assert.IsTrue(Regex.IsMatch(view.HpText, @"^\d+ / \d+"), "Integer HP readout.");
        }
    }
}
