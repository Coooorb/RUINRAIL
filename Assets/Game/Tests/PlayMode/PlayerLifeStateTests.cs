using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 102: co-op Downed at 0 HP (20 s bleedout, crawl only), solo dies outright, no down counter (84/15).</summary>
    public class PlayerLifeStateTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        private sealed class ProbeInteractable : MonoBehaviour, IInteractable
        {
            public int Interactions;
            public bool CanInteract(GameObject interactor) => true;
            public bool Interact(GameObject interactor) { Interactions++; return true; }
        }

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            Assert.IsNotNull(_balance);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
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

        private (GameObject go, FakePlayerInputReader reader, PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position = default)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position, LifeRoster = roster });
            _created.Add(go);
            return (go, reader, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        // ---- Approved values / no down counter ----

        [Test]
        public void ApprovedValues_AndNoDownCounterExists()
        {
            Assert.AreEqual(20f, _balance.DownedBleedoutSeconds, 0.0001f, "84: initial bleedout 20 s.");
            Assert.Greater(_balance.DownedCrawlSpeedMultiplier, 0f);
            Assert.Less(_balance.DownedCrawlSpeedMultiplier, 1f, "Crawl is slower than walking.");
            CollectionAssert.AreEquivalent(new[] { "Alive", "Downed", "Dead" }, System.Enum.GetNames(typeof(PlayerLifeState)), "15: exactly three life states.");

            foreach (var type in new[] { typeof(PlayerLifeStateComponent), typeof(PartyLifeRoster), typeof(PlayerBalanceConfig) })
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(m => m.Name.ToLowerInvariant());
                Assert.IsFalse(members.Any(n => n.Contains("downcount") || n.Contains("downsremaining") || n.Contains("strike") || n.Contains("timesdowned")), $"{type.Name}: no down counter / three-strikes member.");
            }
        }

        // ---- Acceptance 1 + 3: co-op enters Downed with an Alive teammate; solo never does ----

        [Test]
        public void Solo_ZeroHp_IsDeadImmediately_NeverDowned()
        {
            var (_, _, life, health) = Player("Solo", null);
            var downed = 0; var died = 0;
            life.Downed += _ => downed++;
            life.Died += _ => died++;
            Kill(health);
            Assert.AreEqual(PlayerLifeState.Dead, life.State);
            Assert.AreEqual(0, downed);
            Assert.AreEqual(1, died);
            Assert.IsFalse(life.CanAct);

            // A party of one is solo semantics as well.
            var roster = new PartyLifeRoster();
            var (_, _, single, singleHealth) = Player("PartyOfOne", roster);
            Assert.IsFalse(roster.IsCoop);
            Kill(singleHealth);
            Assert.AreEqual(PlayerLifeState.Dead, single.State);
        }

        [Test]
        public void Coop_ZeroHp_WithAliveTeammate_EntersDowned_WithTwentySecondBleedout()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster);
            var (_, _, b, _) = Player("B", roster, new Vector2(5f, 0f));
            Assert.IsTrue(roster.IsCoop);
            var changes = new List<(PlayerLifeState from, PlayerLifeState to)>();
            roster.MemberStateChanged += (m, from, to) => changes.Add((from, to));

            Kill(healthA);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(20f, a.BleedoutRemaining, 0.0001f);
            Assert.AreEqual(PlayerLifeState.Alive, b.State);
            CollectionAssert.AreEqual(new[] { (PlayerLifeState.Alive, PlayerLifeState.Downed) }, changes);
            Assert.IsFalse(roster.IsWiped);
        }

        [Test]
        public void Coop_ZeroHp_WhenNoTeammateCanAct_IsDead_AndRaisesTeamWipeOnce()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster);
            var (_, _, b, healthB) = Player("B", roster, new Vector2(5f, 0f));
            var wipes = 0;
            roster.TeamWiped += _ => wipes++;

            Kill(healthA);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Kill(healthB);
            Assert.AreEqual(PlayerLifeState.Dead, b.State, "The last Alive player at 0 HP dies: a Downed teammate cannot revive.");
            Assert.IsTrue(roster.IsWiped);
            Assert.AreEqual(1, wipes, "84 Team Wipe raised immediately, without waiting for A's bleedout.");

            a.Tick(25f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State);
            Assert.AreEqual(1, wipes, "Bleedout completing after the wipe raises nothing new.");
        }

        // ---- Acceptance 2: authoritative timer and restricted actions ----

        [Test]
        public void Bleedout_IsAuthoritative_AndReachesDeadAtZero()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster);
            Player("B", roster, new Vector2(5f, 0f));
            Kill(healthA);
            var died = 0;
            a.Died += _ => died++;

            a.Tick(19.9f);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(0.1f, a.BleedoutRemaining, 0.001f);

            // A client never advances the timer.
            DamageAuthority.LocalIsAuthoritative = false;
            a.Tick(5f);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(0.1f, a.BleedoutRemaining, 0.001f);
            DamageAuthority.LocalIsAuthoritative = true;

            a.Tick(0.1f + 0.01f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State);
            Assert.AreEqual(0f, a.BleedoutRemaining);
            Assert.AreEqual(1, died);
            a.Tick(1f);
            Assert.AreEqual(1, died, "Dead is terminal for the timer.");
        }

        [Test]
        public void Client_NeverDecidesLocally_ButAppliesReplicatedState()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster);
            Player("B", roster, new Vector2(5f, 0f));
            DamageAuthority.LocalIsAuthoritative = false;
            healthA.ApplyReplicatedHealth(0, 100);
            Assert.AreEqual(PlayerLifeState.Alive, a.State, "0 HP replicated: the life state waits for the authority's verdict.");

            a.ApplyReplicatedState(PlayerLifeState.Downed, 12.5f);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(12.5f, a.BleedoutRemaining, 0.0001f);
            Assert.IsFalse(a.CanAct);
            a.ApplyReplicatedState(PlayerLifeState.Dead, 0f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State);
        }

        [UnityTest]
        public IEnumerator Downed_CrawlsSlowly_ButCannotDashAttackReloadSwapUseItemsOrInteract()
        {
            var roster = new PartyLifeRoster();
            var (go, reader, a, healthA) = Player("A", roster);
            Player("B", roster, new Vector2(8f, 0f));
            var body = go.GetComponent<Rigidbody2D>();
            var dash = go.GetComponent<PlayerDash>();
            var interactor = go.GetComponent<PlayerInteractor>();
            var probe = new GameObject("Probe").AddComponent<ProbeInteractable>();
            _created.Add(probe.gameObject);
            probe.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
            probe.transform.position = go.transform.position + new Vector3(0.6f, 0f);

            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.GetComponent<PlayerAiming>();
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.SetInputReader(reader);
            var rifle = go.AddComponent<RangedWeapon>();
            rifle.SetInputReader(reader);
            var definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(definition);
            Set(definition, "_damageMin", 10); Set(definition, "_damageMax", 12); Set(definition, "_fireRate", 5f); Set(definition, "_magazineSize", 12);
            Set(definition, "_reloadTime", 1.2f); Set(definition, "_range", 10f); Set(definition, "_projectileSpeed", 20f);
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 30);
            rifle.SetProjectilePool(pool);
            rifle.SetAiming(aiming);
            rifle.SetAmmoReserve(reserve);
            rifle.SetDefinition(definition);
            loadout.SetPrimary(rifle);
            loadout.Initialize();
            yield return null;

            reader.Aim = Vector2.right;
            Assert.IsTrue(rifle.TryFire(), "Alive: fires.");
            var magazineAfterOneShot = rifle.MagazineAmmo;
            Assert.IsTrue(interactor.TryInteract());
            Assert.AreEqual(1, probe.Interactions);

            Kill(healthA);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);

            // Restricted actions, both via input events and via the direct APIs the host validates against.
            reader.Move = Vector2.right;
            reader.RaiseDash();
            Assert.IsFalse(dash.IsDashing);
            Assert.IsFalse(dash.TryStartDash(Vector2.right));
            Assert.AreEqual(0, dash.DashesStarted);
            Assert.IsFalse(rifle.TryFire());
            Assert.IsFalse(rifle.TryStartReload());
            reader.FireHeld = true;
            yield return null;
            yield return null;
            Assert.AreEqual(magazineAfterOneShot, rifle.MagazineAmmo, "Held fire does nothing while Downed.");
            Assert.IsFalse(rifle.IsReloading);
            reader.FireHeld = false;
            reader.RaiseInteract();
            Assert.IsFalse(interactor.TryInteract());
            Assert.AreEqual(1, probe.Interactions, "No interaction while Downed.");
            reader.RaiseWeaponSwapped();
            Assert.AreEqual(WeaponSlot.Primary, loadout.ActiveSlot, "No weapon swap while Downed.");
            Assert.IsFalse(go.GetComponent<PlayerConsumableUser>() != null && go.GetComponent<PlayerConsumableUser>().TryUse());

            // Crawl: the body moves, slowly (life state owns the body; PlayerMovement yields).
            Assert.IsTrue(go.GetComponent<PlayerMovement>().IsOverridden);
            var start = go.transform.position;
            yield return new WaitForSeconds(0.5f);
            var crawled = Vector2.Distance(start, go.transform.position);
            Assert.Greater(crawled, 0.05f, "Downed players can crawl.");
            var expected = _balance.MoveSpeed * _balance.DownedCrawlSpeedMultiplier * 0.5f;
            Assert.Less(crawled, _balance.MoveSpeed * 0.5f * 0.8f, "...but well below walking speed.");
            Assert.AreEqual(expected, crawled, expected * 0.5f);
            Assert.AreEqual(a.CrawlSpeed, body.linearVelocity.magnitude, 0.01f);

            // Dead: no movement at all.
            a.Tick(20f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 0.001f);
        }

        // ---- Revive seam (used by TASK 103) ----

        [Test]
        public void ReturnToAlive_RestoresHealthAndActions_OnlyOnTheAuthority()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster);
            Player("B", roster, new Vector2(5f, 0f));
            Kill(healthA);
            Assert.IsFalse(healthA.Heal(10), "Generic heal cannot resurrect.");

            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(a.ReturnToAlive(30));
            DamageAuthority.LocalIsAuthoritative = true;
            Assert.IsTrue(a.ReturnToAlive(30));
            Assert.AreEqual(PlayerLifeState.Alive, a.State);
            Assert.AreEqual(30, healthA.CurrentHealth);
            Assert.IsTrue(a.CanAct);
            Assert.IsFalse(a.ReturnToAlive(30), "Already Alive.");

            // Downed again later: a fresh 20 s, never a shortened 'second down'.
            Kill(healthA);
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(20f, a.BleedoutRemaining, 0.0001f);
        }
    }
}
