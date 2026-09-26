using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Input-rate exploits: no press/release pattern — held fire, deliberate use, or a press/release toggle every frame
    /// (thousands of clicks a second in the uncapped batch loop) — nor weapon swapping between attacks may beat a
    /// weapon's authored cadence. Real shipped definitions on the shipped player composition, driven through each
    /// weapon's real Update by the input reader, measured over a fixed window of game time.
    /// </summary>
    public class WeaponInputCadenceTests
    {
        private const float Window = 2f;

        private readonly List<Object> _created = new();
        private GameContentCatalog _content;

        private sealed class MidpointDamageRoller : IDamageRoller
        {
            public int Roll(int minInclusive, int maxInclusive) => (minInclusive + maxInclusive) / 2;
        }

        private enum Pattern { Hold, Spam }

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            if (_app == null) return;
            Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
            _app = null;
        }

        private GameApp _app;
        private string _saveDir;

        private IEnumerator EnterRun()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_cadence_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(_content, _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != SceneNames.MainMenu) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            _app.Menu.Play();
            while (_app.ComposedScene != SceneNames.Base) { Assert.Less(Time.realtimeSinceStartup, deadline + 30f); yield return null; }
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Cadence Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            while (_app.ComposedScene != SceneNames.Dungeon) { Assert.Less(Time.realtimeSinceStartup, deadline + 60f); yield return null; }
            for (var i = 0; i < 12; i++) yield return null;
        }

        /// <summary>
        /// Feel, measured on the shipped composition: the Recurve Bow equipped through the run's inventory on the live
        /// rig. A tap fires on its release frame; a full draw can be followed by the next draw on the very next frame;
        /// spammed taps settle at one shot per quick-shot recovery; a tap during recovery lands as soon as it ends.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveRun_Bow_StaysResponsive_ForTapsFullDrawsAndSpam()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var inventory = run.Rig.Inventory;
            inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_recurve_bow"), EquippedSlot.PrimaryWeapon));
            yield return null;
            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
            var bow = (BowWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            bow.SetInputReader(input);
            yield return null;

            // 1. A tap from rest: the arrow leaves on the release frame.
            input.FireHeld = true; yield return null;
            input.FireHeld = false; yield return null;
            Assert.AreEqual(1, bow.ShotsFired, "tap → arrow on the release frame");
            var recovery = bow.RecoveryRemaining;

            // 2. A tap during the recovery lands the moment it ends (measured delay ≈ what was left of it).
            input.FireHeld = true; yield return null;
            input.FireHeld = false;
            var queuedAt = Time.time;
            var left = bow.RecoveryRemaining;
            while (bow.ShotsFired == 1) yield return null;
            var bufferedDelay = Time.time - queuedAt;
            Assert.LessOrEqual(bufferedDelay, left + 0.05f, "the buffered tap fires as soon as recovery ends");

            // 3. Deliberate full draws: the next draw starts on the frame after the release.
            while (bow.IsRecovering) yield return null;
            input.FireHeld = true;
            while (!bow.IsFullyCharged) yield return null;
            input.FireHeld = false; yield return null;
            input.FireHeld = true; yield return null;
            Assert.IsTrue(bow.IsCharging, "after a full draw the next draw begins at once");
            input.FireHeld = false; yield return null;

            // 4. Sustained spam: steady cadence at the quick-shot recovery.
            while (bow.IsRecovering) yield return null;
            var start = bow.ShotsFired;
            var t0 = Time.time;
            yield return Drive(input, Pattern.Spam, 2f);
            var spamRate = (bow.ShotsFired - start) / Mathf.Max(0.001f, Time.time - t0);
            Debug.Log($"[CADENCE] live Recurve Bow: tap recovery {recovery:0.###}s, buffered tap landed after {bufferedDelay:0.###}s (recovery left {left:0.###}s), full draw → next draw next frame, spam {spamRate:0.##} shots/s (cap {1f / recovery:0.##}/s)");
            Assert.LessOrEqual(spamRate, 1f / recovery + 0.5f, "spam stays at the recovery cadence in the live run");
            Assert.GreaterOrEqual(spamRate, 1f / recovery - 1.2f, "spam is not throttled below the recovery cadence");
        }

        private T Definition<T>(string id) where T : WeaponDefinition => (T)_content.Items.OfType<WeaponDefinition>().First(w => w.Id == id);

        private (GameObject Player, FakePlayerInputReader Input) BuildPlayer(float x)
        {
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "CadencePlayer", IsLocal = true, InputReader = input,
                BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps, Position = new Vector2(x, 900f)
            });
            _created.Add(go);
            return (go, input);
        }

        /// <summary>Runs the window: Hold keeps Fire down, Spam flips it every frame. <paramref name="perFrame"/> observes.</summary>
        private static IEnumerator Drive(FakePlayerInputReader input, Pattern pattern, float seconds, System.Action perFrame = null)
        {
            input.FireHeld = false;
            yield return null;
            var until = Time.time + seconds;
            while (Time.time < until)
            {
                input.FireHeld = pattern == Pattern.Hold || !input.FireHeld;
                yield return null;
                perFrame?.Invoke();
            }

            input.FireHeld = false;
            yield return null;
        }

        // ================= Bow: the exploit =================

        private BowWeapon Bow(string id, out FakePlayerInputReader input, float x)
        {
            var (player, reader) = BuildPlayer(x);
            input = reader;
            var bow = player.AddComponent<BowWeapon>();
            bow.SetInputReader(reader);
            bow.SetAiming(player.GetComponent<PlayerAiming>());
            bow.SetProjectilePool(player.AddComponent<ProjectilePool>());
            bow.SetDamageRoller(new MidpointDamageRoller());
            bow.SetDefinition(Definition<BowWeaponDefinition>(id));
            bow.SetStats(player.GetComponent<PlayerStatsBinder>().Stats);
            return bow;
        }

        [UnityTest]
        public IEnumerator Bow_ClickSpam_CannotOutDamageDeliberateFullDraws_ForEveryShippedBow()
        {
            foreach (var id in new[] { "weapon_recurve_bow", "weapon_compound_bow", "weapon_stormstring" })
            {
                var definition = Definition<BowWeaponDefinition>(id);
                const float window = 3f;

                // Deliberate: draw to full, release, draw again at once (a full draw owes no recovery).
                var bow = Bow(id, out var input, 0f);
                yield return null;
                var fullDamage = 0;
                var fullShots = 0;
                var until = Time.time + window;
                while (Time.time < until)
                {
                    input.FireHeld = true;
                    yield return null;
                    if (!bow.IsFullyCharged) continue;
                    input.FireHeld = false;
                    var before = bow.ShotsFired;
                    yield return null;
                    Assert.AreEqual(before + 1, bow.ShotsFired, $"{id}: a full draw released and fired");
                    Assert.IsFalse(bow.IsRecovering, $"{id}: a full draw owes no recovery");
                    fullDamage += bow.LastSpawnedProjectile.Data.Damage;
                    fullShots++;
                }

                input.FireHeld = false;

                // Spam: a press/release every frame.
                var spam = Bow(id, out var spamInput, 50f);
                yield return null;
                var spamDamage = 0;
                var seen = 0;
                yield return Drive(spamInput, Pattern.Spam, window, () =>
                {
                    if (spam.ShotsFired == seen) return;
                    seen = spam.ShotsFired;
                    spamDamage += spam.LastSpawnedProjectile.Data.Damage;
                });

                var fullDps = (definition.FullDrawDamageMin + definition.FullDrawDamageMax) / 2 / definition.FullChargeSeconds;
                var quickRecovery = BowChargeResolver.RecoverySeconds(definition, 0f, definition.FullChargeSeconds);
                var capShots = Mathf.FloorToInt(window / quickRecovery) + 1;
                Debug.Log($"[CADENCE] {id}: deliberate {fullShots} full draws = {fullDamage} dmg; spam {spam.ShotsFired} shots = {spamDamage} dmg (cap {capShots} shots, quick recovery {quickRecovery:0.###}s)");
                Assert.Greater(spam.ShotsFired, 1, $"{id}: spam still fires (responsive), just not faster than the cap");
                Assert.LessOrEqual(spam.ShotsFired, capShots, $"{id}: {spam.ShotsFired} spammed shots in {window}s beats the quick-shot recovery cap of {capShots}");
                Assert.LessOrEqual(spamDamage, fullDps * window + definition.QuickDamageMax, $"{id}: spam dealt {spamDamage} in {window}s, more than full draws can ({fullDps * window:0})");
                Assert.GreaterOrEqual(fullShots, Mathf.FloorToInt(window / definition.FullChargeSeconds) - 1, $"{id}: deliberate full draws were slowed down");
                Assert.IsTrue(fullDamage / (float)fullShots >= definition.FullDrawDamageMin, $"{id}: full draws keep full-draw damage");
            }
        }

        [UnityTest]
        public IEnumerator Bow_TapDuringRecovery_IsBufferedAndFiresWhenRecoveryEnds_AndSwappingDoesNotClearRecovery()
        {
            var bow = Bow("weapon_recurve_bow", out var input, 0f);
            var knife = bow.gameObject.AddComponent<MeleeWeapon>();
            knife.SetInputReader(input);
            knife.SetAiming(bow.GetComponent<PlayerAiming>());
            knife.SetDefinition(Definition<MeleeWeaponDefinition>("weapon_field_knife"));
            var loadout = bow.gameObject.AddComponent<WeaponLoadout>();
            loadout.SetInputReader(input);
            loadout.SetPrimary(bow);
            loadout.SetSecondary(knife);
            loadout.Initialize();
            yield return null;

            // Responsive: a tap from rest fires on the release frame.
            input.FireHeld = true; yield return null;
            input.FireHeld = false; yield return null;
            Assert.AreEqual(1, bow.ShotsFired, "the first tap fires immediately");
            Assert.IsTrue(bow.IsRecovering, "a quick shot owes recovery");

            // Swapping out and straight back does not clear what the quick shot owes.
            loadout.SelectSlot(WeaponSlot.Secondary);
            loadout.SelectSlot(WeaponSlot.Primary);
            Assert.IsTrue(bow.IsRecovering, "a swap must not clear the recovery");
            Assert.IsFalse(bow.TryStartCharge(), "no draw while recovering");

            // A tap during recovery is kept and fires as soon as recovery ends, with no further input.
            input.FireHeld = true; yield return null;
            input.FireHeld = false; yield return null;
            Assert.AreEqual(1, bow.ShotsFired, "the buffered tap waits for recovery");
            var deadline = Time.time + 1f;
            while (bow.ShotsFired == 1 && Time.time < deadline) yield return null;
            Assert.AreEqual(2, bow.ShotsFired, "the buffered tap fired when recovery ended");

            // A press held through recovery becomes a draw the moment recovery ends.
            input.FireHeld = true;
            deadline = Time.time + 1f;
            while (!bow.IsCharging && Time.time < deadline) yield return null;
            Assert.IsTrue(bow.IsCharging, "a held press starts drawing once recovery ends");
            Assert.AreEqual(2, bow.ShotsFired, "and does not fire until released");
            input.FireHeld = false;
        }

        // ================= audit: the other archetypes =================

        [UnityTest]
        public IEnumerator Firearms_ClickSpamNeverBeatsHeldFire_AndAmmoSpentEqualsShots()
        {
            // Pistol (semi-auto feel), SMG (high rate), Shotgun (pellets), Sniper (slow), Rocket Launcher (multi-ammo).
            foreach (var id in new[] { "weapon_p9_ranger", "weapon_rattler_9", "weapon_breacher_12", "weapon_longshot_s1", "weapon_pipe_launcher" })
            {
                var definition = Definition<RangedWeaponDefinition>(id);
                var counts = new Dictionary<Pattern, int>();
                foreach (var pattern in new[] { Pattern.Hold, Pattern.Spam })
                {
                    var (player, input) = BuildPlayer(pattern == Pattern.Hold ? 100f : 150f);
                    var weapon = player.AddComponent<RangedWeapon>();
                    weapon.SetInputReader(input);
                    weapon.SetAiming(player.GetComponent<PlayerAiming>());
                    weapon.SetProjectilePool(player.AddComponent<ProjectilePool>());
                    weapon.SetDamageRoller(new MidpointDamageRoller());
                    weapon.SetDefinition(definition);
                    var reserve = new AmmoReserve();
                    reserve.Add(definition.AmmoType, 999);
                    weapon.SetAmmoReserve(reserve);
                    weapon.SetStats(player.GetComponent<PlayerStatsBinder>().Stats);
                    yield return null;

                    var shots = 0;
                    var magazine = weapon.MagazineAmmo;
                    var reserveBefore = reserve.Get(definition.AmmoType);
                    var reloadsAmmo = 0;
                    yield return Drive(input, pattern, Window, () =>
                    {
                        if (weapon.MagazineAmmo < magazine) shots += magazine - weapon.MagazineAmmo;
                        else if (weapon.MagazineAmmo > magazine) reloadsAmmo += weapon.MagazineAmmo - magazine;
                        magazine = weapon.MagazineAmmo;
                    });

                    counts[pattern] = shots;
                    var cap = Mathf.FloorToInt(Window * definition.FireRate) + 1;
                    Assert.LessOrEqual(shots, cap, $"{id} {pattern}: {shots} shots in {Window}s beats {definition.FireRate}/s");
                    // Every accepted shot took exactly one magazine round; the reserve only fed reloads.
                    Assert.AreEqual(reserveBefore - reloadsAmmo * definition.AmmoCostPerShot, reserve.Get(definition.AmmoType),
                        $"{id} {pattern}: reserve moved by something other than reloads");
                    Object.DestroyImmediate(player);
                }

                Debug.Log($"[CADENCE] {id} ({definition.FireRate}/s): hold {counts[Pattern.Hold]} spam {counts[Pattern.Spam]}");
                Assert.LessOrEqual(counts[Pattern.Spam], counts[Pattern.Hold] + 1, $"{id}: click spam out-fired held fire");
                Assert.Greater(counts[Pattern.Spam], 0, $"{id}: clicking still fires");
            }
        }

        [UnityTest]
        public IEnumerator Blaster_ClickSpamNeverBeatsHeldFire()
        {
            var definition = Definition<BlasterWeaponDefinition>("weapon_pulse_carbine_b1");
            var counts = new Dictionary<Pattern, int>();
            foreach (var pattern in new[] { Pattern.Hold, Pattern.Spam })
            {
                var (player, input) = BuildPlayer(pattern == Pattern.Hold ? 200f : 250f);
                var blaster = player.AddComponent<BlasterWeapon>();
                blaster.SetInputReader(input);
                blaster.SetAiming(player.GetComponent<PlayerAiming>());
                blaster.SetProjectilePool(player.AddComponent<ProjectilePool>());
                blaster.SetDamageRoller(new MidpointDamageRoller());
                blaster.SetDefinition(definition);
                blaster.SetStats(player.GetComponent<PlayerStatsBinder>().Stats);
                yield return null;
                yield return Drive(input, pattern, Window);
                counts[pattern] = blaster.ShotsFired;
                Assert.LessOrEqual(blaster.ShotsFired, Mathf.FloorToInt(Window / blaster.CurrentFireInterval) + 1,
                    $"{pattern}: {blaster.ShotsFired} blaster shots beat the fire interval");
                Object.DestroyImmediate(player);
            }

            Debug.Log($"[CADENCE] {definition.Id}: hold {counts[Pattern.Hold]} spam {counts[Pattern.Spam]}");
            Assert.LessOrEqual(counts[Pattern.Spam], counts[Pattern.Hold] + 1, "click spam out-fired held fire");
        }

        [UnityTest]
        public IEnumerator Melee_ClickSpamAndSwapCancel_NeverBeatTheAttackInterval()
        {
            foreach (var id in new[] { "weapon_field_knife", "weapon_scrap_spear" })
            {
                var definition = Definition<MeleeWeaponDefinition>(id);
                foreach (var swapCancel in new[] { false, true })
                {
                    var (player, input) = BuildPlayer(swapCancel ? 300f : 350f);
                    var melee = player.AddComponent<MeleeWeapon>();
                    melee.SetInputReader(input);
                    melee.SetAiming(player.GetComponent<PlayerAiming>());
                    melee.SetDamageRoller(new MidpointDamageRoller());
                    melee.SetDefinition(definition);
                    melee.SetStats(player.GetComponent<PlayerStatsBinder>().Stats);
                    var pistol = player.AddComponent<RangedWeapon>();
                    pistol.SetDefinition(Definition<RangedWeaponDefinition>("weapon_p9_ranger"));
                    var loadout = player.AddComponent<WeaponLoadout>();
                    loadout.SetInputReader(input);
                    loadout.SetPrimary(melee);
                    loadout.SetSecondary(pistol);
                    loadout.Initialize();
                    yield return null;

                    // Swap-cancel: as soon as a swing has landed (recovery), swap out and straight back in, then keep clicking.
                    yield return Drive(input, Pattern.Spam, Window, () =>
                    {
                        if (!swapCancel || melee.State != MeleeAttackState.Recovery) return;
                        loadout.SelectSlot(WeaponSlot.Secondary);
                        loadout.SelectSlot(WeaponSlot.Primary);
                    });

                    var cap = Mathf.FloorToInt(Window * melee.CurrentAttackRate) + 1;
                    Debug.Log($"[CADENCE] {id} ({melee.CurrentAttackRate:0.##}/s) spam{(swapCancel ? " + swap-cancel" : string.Empty)}: {melee.AttacksStarted} swings (cap {cap})");
                    Assert.Greater(melee.AttacksStarted, 1, $"{id}: clicking still swings");
                    Assert.LessOrEqual(melee.AttacksStarted, cap, $"{id}{(swapCancel ? " swap-cancel" : " spam")}: {melee.AttacksStarted} swings in {Window}s beats {melee.CurrentAttackRate:0.##}/s");
                    Object.DestroyImmediate(player);
                }
            }
        }
    }
}
