using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 095: host executes weapon intents through the unchanged weapon components (no duplicate shots or ammo
    /// from spoofed fire), friendly fire is OFF for projectiles and melee, weapon state converges owner ↔ host,
    /// melee cancels on switch over the network, and clients never apply damage.
    /// </summary>
    public class NetworkCombatTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RangedWeaponDefinition Rifle(int magazine = 6, float fireRate = 10f)
        {
            var d = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(d);
            Set(d, "_id", "weapon_net_rifle");
            Set(d, "_damageMin", 10); Set(d, "_damageMax", 10);
            Set(d, "_fireRate", fireRate); Set(d, "_magazineSize", magazine); Set(d, "_reloadTime", 0.2f);
            Set(d, "_range", 12f); Set(d, "_projectileSpeed", 30f);
            Set(d, "_ammoType", AmmoType.Light); Set(d, "_ammoCostPerShot", 1);
            return d;
        }

        private (GameObject go, RangedWeapon weapon, RemoteIntentInputReader reader, AmmoReserve reserve, ProjectilePool pool) HostRangedFor(Vector2 position, RangedWeaponDefinition definition)
        {
            var go = new GameObject("HostPlayer");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.AddComponent<PlayerAiming>();
            var weapon = go.AddComponent<RangedWeapon>();
            var reader = new RemoteIntentInputReader();
            aiming.SetInputReader(reader);
            weapon.SetInputReader(reader);
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 30);
            weapon.SetProjectilePool(pool);
            weapon.SetAiming(aiming);
            weapon.SetAmmoReserve(reserve);
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 10 });
            weapon.SetDefinition(definition);
            return (go, weapon, reader, reserve, pool);
        }

        private (GameObject go, HealthComponent health) Target(Vector2 position, DamageTeam team)
        {
            var go = new GameObject($"Target_{team}");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(team);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(1000);
            return (go, health);
        }

        // ---- Acceptance 1: spoofed/duplicate fire cannot duplicate shots or ammo ----

        [UnityTest]
        public IEnumerator SpoofedFireIntents_CannotExceedTheWeaponsFireRate_OrDoubleConsumeAmmo()
        {
            var (_, weapon, reader, _, pool) = HostRangedFor(Vector2.zero, Rifle(magazine: 6, fireRate: 5f));
            uint seq = 0;
            // The "client" floods held-fire intents: 10 per frame for 12 frames (0.2 s at 60 fps => at most 2 shots at 5/s).
            for (var frame = 0; frame < 12; frame++)
            {
                for (var spam = 0; spam < 10; spam++) reader.Apply(MovementIntent.Create(++seq, Vector2.zero, Vector2.right, fireHeld: true));
                yield return null;
            }

            var elapsed = 12 * Time.deltaTime + 0.05f;
            Assert.LessOrEqual(pool.SpawnCount, Mathf.CeilToInt(elapsed * 5f) + 1, "Shots are bounded by the authoritative fire rate, never by intent count.");
            Assert.AreEqual(6 - pool.SpawnCount, weapon.MagazineAmmo, "Exactly one round per authoritative shot.");
            Assert.Greater(pool.SpawnCount, 0);
        }

        [UnityTest]
        public IEnumerator DuplicateReloadCommands_ReloadOnce_AndConsumeReserveOnce()
        {
            var (_, weapon, reader, reserve, _) = HostRangedFor(Vector2.zero, Rifle(magazine: 4, fireRate: 100f));
            reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return null;
            reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            yield return null;
            var fired = 4 - weapon.MagazineAmmo;
            Assert.Greater(fired, 0);

            Assert.IsTrue(reader.ApplyCommand(new WeaponCommand { Sequence = 1, Kind = WeaponCommandKind.Reload }));
            Assert.IsFalse(reader.ApplyCommand(new WeaponCommand { Sequence = 1, Kind = WeaponCommandKind.Reload }), "Duplicate command sequence is dropped.");
            Assert.IsFalse(reader.ApplyCommand(new WeaponCommand { Sequence = 0, Kind = WeaponCommandKind.Reload }), "Stale command is dropped.");
            Assert.IsTrue(weapon.IsReloading);
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(4, weapon.MagazineAmmo);
            Assert.AreEqual(30 - fired, reserve.Get(AmmoType.Light), "Reserve paid exactly once for the rounds loaded.");
            Assert.AreEqual(1, reader.AppliedCommands);
            Assert.AreEqual(2, reader.RejectedCommands);
        }

        // ---- Acceptance 2: friendly fire OFF ----

        [UnityTest]
        public IEnumerator PlayerProjectile_PassesThroughTeammates_AndHitsTheEnemyBehind()
        {
            var (_, weapon, reader, _, pool) = HostRangedFor(Vector2.zero, Rifle());
            var (_, teammate) = Target(new Vector2(2f, 0f), DamageTeam.Player);
            var (_, enemy) = Target(new Vector2(5f, 0f), DamageTeam.Enemy);
            reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return null;
            reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            Assert.AreEqual(1, pool.SpawnCount);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1000, teammate.CurrentHealth, "Player weapon damage never reduces teammate HP.");
            Assert.AreEqual(990, enemy.CurrentHealth, "The same shot still hits the enemy behind the teammate.");
        }

        [UnityTest]
        public IEnumerator PlayerMelee_NeverHitsTeammates()
        {
            var go = new GameObject("MeleePlayer");
            _created.Add(go);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var aiming = go.AddComponent<PlayerAiming>();
            var melee = go.AddComponent<MeleeWeapon>();
            var reader = new RemoteIntentInputReader();
            aiming.SetInputReader(reader);
            melee.SetInputReader(reader);
            var definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            _created.Add(definition);
            Set(definition, "_damageMin", 14); Set(definition, "_damageMax", 17); Set(definition, "_attackRate", 3.5f);
            Set(definition, "_attackRange", 1.5f); Set(definition, "_attackArcDegrees", 120f); Set(definition, "_windUpSeconds", 0.05f); Set(definition, "_recoverySeconds", 0.05f);
            melee.SetAiming(aiming);
            melee.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            melee.SetDefinition(definition);
            var (_, teammate) = Target(new Vector2(0.8f, 0.1f), DamageTeam.Player);
            var (_, enemy) = Target(new Vector2(0.8f, -0.4f), DamageTeam.Enemy);

            reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return null;
            reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1000, teammate.CurrentHealth, "Melee never lands on a teammate.");
            Assert.AreEqual(985, enemy.CurrentHealth, "...but lands on the enemy in the same arc.");
        }

        // ---- Acceptance 3: state convergence ----

        [UnityTest]
        public IEnumerator OwnerWeaponState_ConvergesToTheHostSnapshot()
        {
            var definition = Rifle(magazine: 6, fireRate: 100f);
            var hostSide = HostRangedFor(new Vector2(0f, 10f), definition);
            var ownerSide = HostRangedFor(new Vector2(0f, 20f), definition);
            var hostLoadout = hostSide.go.AddComponent<WeaponLoadout>();
            hostLoadout.SetPrimary(hostSide.weapon);
            hostLoadout.SetInputReader(hostSide.reader);
            hostLoadout.Initialize();
            var ownerLoadout = ownerSide.go.AddComponent<WeaponLoadout>();
            ownerLoadout.SetPrimary(ownerSide.weapon);
            ownerLoadout.SetInputReader(ownerSide.reader);
            ownerLoadout.Initialize();

            // Host fired twice; the owner's prediction (dropped intents) fired only once: drift.
            hostSide.reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return new WaitForSeconds(0.15f);
            hostSide.reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            ownerSide.reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return null;
            ownerSide.reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            yield return null;
            Assert.AreNotEqual(hostSide.weapon.MagazineAmmo, ownerSide.weapon.MagazineAmmo, "Fixture premise: the two drifted.");

            var snapshot = WeaponStateSync.Capture(hostLoadout, 2);
            Assert.AreEqual(hostSide.weapon.MagazineAmmo, snapshot.MagazineAmmo);
            Assert.AreEqual((int)WeaponSlot.Primary, snapshot.ActiveSlot);
            Assert.IsTrue(WeaponStateSync.Apply(ownerLoadout, snapshot));
            Assert.AreEqual(hostSide.weapon.MagazineAmmo, ownerSide.weapon.MagazineAmmo, "Owner adopts the authoritative magazine.");
            Assert.IsFalse(WeaponStateSync.Apply(ownerLoadout, snapshot), "Already converged: no change.");
        }

        // ---- Acceptance 4: melee cancel on switch over the network ----

        [UnityTest]
        public IEnumerator MeleeWindUp_IsCancelledByANetworkedSlotSwap_AndNeverLands()
        {
            var go = new GameObject("MeleeSwitcher");
            _created.Add(go);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var aiming = go.AddComponent<PlayerAiming>();
            var melee = go.AddComponent<MeleeWeapon>();
            var pool = go.AddComponent<ProjectilePool>();
            var rifle = go.AddComponent<RangedWeapon>();
            var reader = new RemoteIntentInputReader();
            aiming.SetInputReader(reader);
            melee.SetInputReader(reader);
            rifle.SetInputReader(reader);
            var definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            _created.Add(definition);
            Set(definition, "_damageMin", 14); Set(definition, "_damageMax", 17); Set(definition, "_attackRate", 3.5f);
            Set(definition, "_attackRange", 1.5f); Set(definition, "_attackArcDegrees", 120f); Set(definition, "_windUpSeconds", 0.3f); Set(definition, "_recoverySeconds", 0.1f);
            melee.SetAiming(aiming);
            melee.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            melee.SetDefinition(definition);
            rifle.SetProjectilePool(pool);
            rifle.SetAiming(aiming);
            rifle.SetAmmoReserve(new AmmoReserve());
            rifle.SetDefinition(Rifle());
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.SetPrimary(melee);
            loadout.SetSecondary(rifle);
            loadout.SetInputReader(reader);
            loadout.Initialize();
            var (_, enemy) = Target(new Vector2(0.8f, 0f), DamageTeam.Enemy);

            reader.Apply(MovementIntent.Create(1, Vector2.zero, Vector2.right, fireHeld: true));
            yield return null;
            reader.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right, fireHeld: false));
            Assert.AreEqual(MeleeAttackState.WindUp, melee.State);

            Assert.IsTrue(reader.ApplyCommand(new WeaponCommand { Sequence = 1, Kind = WeaponCommandKind.Swap }));
            Assert.AreEqual(WeaponSlot.Secondary, loadout.ActiveSlot);
            Assert.AreEqual(MeleeAttackState.Idle, melee.State, "Switching cancels the wind-up.");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1000, enemy.CurrentHealth, "The cancelled swing never lands.");
            Assert.IsFalse(melee.IsEquipped);
        }

        // ---- Requirement 2: clients never apply damage ----

        [Test]
        public void ClientProcess_NeverAppliesLocalDamage_ButAppliesReplicatedHealth()
        {
            var (_, health) = Target(Vector2.zero, DamageTeam.Enemy);
            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(health.TryApplyDamage(new DamageRequest(50)));
            Assert.AreEqual(1000, health.CurrentHealth);
            var damaged = 0;
            health.Damaged += d => damaged += d;
            health.ApplyReplicatedHealth(940, 1000);
            Assert.AreEqual(940, health.CurrentHealth);
            Assert.AreEqual(60, damaged);
            DamageAuthority.LocalIsAuthoritative = true;
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(50)));
            Assert.AreEqual(890, health.CurrentHealth);
        }
    }
}
