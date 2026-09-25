using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Final release cleanup: the incoming-impact armor passives (Anchored / Riot Armor, Shock Absorber / Blast Suit,
    /// Exo Lock / Reinforced Exo-Rig) through the real composition — the solo/host rig (now ticked, with DamageTaken
    /// raised from the authoritative health) and the host's copy of a remote co-op member (CoopMemberMirror), which runs
    /// exactly these three from the member's mirrored equipment while the member's own rig runs none of them.
    /// </summary>
    public sealed class CoopReactivePassiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_reactive_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CursorService.SetApplier(_ => true);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            CursorService.Reset();
            DamageAuthority.LocalIsAuthoritative = true;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time.");
                yield return null;
            }
        }

        private IEnumerator EnterRun()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Passive Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static ImpactRequest Stagger() => new(Vector2.right, 0f, 1000f);
        private static ImpactRequest Explosion() => new(Vector2.left, 3f, 0f, DamageKind.Explosion);

        private static InventorySnapshot Wearing(string armorId, string instanceId = null)
        {
            var item = new ItemInstance(armorId, 1, Rarity.Legendary);
            var snapshot = item.ToSnapshot();
            if (instanceId != null) snapshot.InstanceId = instanceId;
            return new InventorySnapshot { Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.Armor, Item = snapshot } }, Backpack = new InventorySnapshot.Entry[0] };
        }

        /// <summary>The host's copy of a remote member, composed exactly as ComposeHostParty does.</summary>
        private (GameObject body, CoopMemberMirror mirror) RemoteMember(ulong clientId, InventorySnapshot loadout, Vector2 at)
        {
            var body = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Remote_" + clientId, IsLocal = false, BalanceConfig = _app.Content.PlayerBalance, Caps = _app.Content.StatCaps,
                Position = at, LifeRoster = new PartyLifeRoster(), ParticipantId = "remote_" + clientId
            });
            var mirror = new CoopMemberMirror(body, clientId, _app.Content, _app.Registry, new CoopMemberProfile { ClientId = clientId, Loadout = loadout });
            return (body, mirror);
        }

        [UnityTest]
        public IEnumerator Solo_IncomingImpactPassives_FollowTheirAuthoredTiming()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var receiver = player.GetComponent<PlayerImpactReceiver>();
            var inventory = run.Rig.Inventory;
            var stats = run.Rig.StatsBinder.Stats;
            var health = player.GetComponent<HealthComponent>();
            void Wear(string id) { inventory.Unequip(EquippedSlot.Armor); Assert.IsTrue(inventory.TryEquip(new ItemInstance(id, 1, Rarity.Legendary), EquippedSlot.Armor)); }
            Assert.IsNotNull(player.GetComponent<EquipmentPassiveTicker>(), "the rig ticks its passives");

            // Anchored: one negation, refused on cooldown, negates again once the authored 8 s have passed.
            Wear("armor_riot_armor");
            Assert.AreEqual("anchored", run.Rig.Passives.GetActive(EquippedSlot.Armor)?.Id);
            var n0 = receiver.StaggersNegated;
            receiver.ApplyStagger(Stagger());
            Assert.AreEqual(n0 + 1, receiver.StaggersNegated, "first stagger negated");
            receiver.ApplyStagger(Stagger());
            Assert.AreEqual(n0 + 1, receiver.StaggersNegated, "on cooldown: the next stagger lands");
            yield return new WaitForSeconds(AnchoredPassive.CooldownSeconds + 0.4f);
            receiver.ApplyStagger(Stagger());
            Assert.AreEqual(n0 + 2, receiver.StaggersNegated, "after 8 s the ticked cooldown has elapsed: negated again (before the fix: never)");

            // Shock Absorber: every explosion knockback ignored, ordinary knockback untouched.
            Wear("armor_blast_suit");
            var k0 = receiver.KnockbacksNegated;
            receiver.ApplyKnockback(Explosion());
            receiver.ApplyKnockback(Explosion());
            Assert.AreEqual(k0 + 2, receiver.KnockbacksNegated);
            Assert.Greater(receiver.ApplyKnockback(new ImpactRequest(Vector2.left, 3f, 0f)).Distance, 0f, "ordinary knockback still moves the wearer");
            Assert.AreEqual(k0 + 2, receiver.KnockbacksNegated);

            // Exo Lock: below the threshold nothing; a 20+ hit raises resistance for 5 s; the 8 s cooldown blocks a re-trigger.
            Wear("armor_reinforced_exo_rig");
            var exo = (ExoLockPassive)run.Rig.Passives.GetActive(EquippedSlot.Armor);
            health.Heal(health.MaxHealth);
            health.TryApplyDamage(new DamageRequest(ExoLockPassive.DamageThreshold - 1));
            Assert.IsFalse(exo.IsBuffActive, "a hit below the threshold does nothing");
            var r0 = stats.GetPercent(StatId.StaggerResistance);
            health.TryApplyDamage(new DamageRequest(25));
            Assert.IsTrue(exo.IsBuffActive, "a 25 hit triggers Exo Lock (before the fix: DamageTaken had no producer)");
            Assert.Greater(stats.GetPercent(StatId.StaggerResistance), r0);
            yield return new WaitForSeconds(1f);
            health.TryApplyDamage(new DamageRequest(25));
            yield return new WaitForSeconds(ExoLockPassive.DurationSeconds - 0.5f);
            Assert.IsFalse(exo.IsBuffActive, "the buff ends 5 s after the first hit: the second hit inside the cooldown did not extend it");
            Assert.AreEqual(r0, stats.GetPercent(StatId.StaggerResistance));
        }

        [UnityTest]
        public IEnumerator RemoteMember_HostCopy_RunsExactlyItsIncomingImpactPassive_OnceAndOnlyOnItsOwnImpacts()
        {
            yield return EnterRun();
            var (bodyA, mirrorA) = RemoteMember(7, Wearing("armor_riot_armor"), new Vector2(400f, 400f));
            var (bodyB, mirrorB) = RemoteMember(8, Wearing("armor_blast_suit"), new Vector2(420f, 400f));
            var (bodyC, mirrorC) = RemoteMember(9, Wearing("armor_reinforced_exo_rig"), new Vector2(440f, 400f));
            var (bodyD, mirrorD) = RemoteMember(10, Wearing("armor_heavy_plate"), new Vector2(460f, 400f));
            Assert.AreEqual("anchored", mirrorA.Passives.GetActive(EquippedSlot.Armor)?.Id);
            Assert.AreEqual("shock_absorber", mirrorB.Passives.GetActive(EquippedSlot.Armor)?.Id);
            Assert.AreEqual("exo_lock", mirrorC.Passives.GetActive(EquippedSlot.Armor)?.Id);
            Assert.AreEqual("last_stand", mirrorD.Passives.GetActive(EquippedSlot.Armor)?.Id, "Last Stand reduces damage the host computes on the member's body: host-resolved");

            var ra = bodyA.GetComponent<PlayerImpactReceiver>();
            var rb = bodyB.GetComponent<PlayerImpactReceiver>();
            // One impact each way: each passive acts on its own member only, exactly once.
            ImpactDispatcher.Apply(bodyA.transform, Stagger());
            ImpactDispatcher.Apply(bodyB.transform, Stagger());
            ImpactDispatcher.Apply(bodyA.transform, Explosion());
            ImpactDispatcher.Apply(bodyB.transform, Explosion());
            Assert.AreEqual(1, ra.StaggersNegated, "Anchored negated A's stagger once");
            Assert.AreEqual(0, rb.StaggersNegated, "no Anchored on B");
            Assert.AreEqual(1, rb.KnockbacksNegated, "Shock Absorber ignored B's explosion once");
            Assert.AreEqual(0, ra.KnockbacksNegated, "no Shock Absorber on A");
            ImpactDispatcher.Apply(bodyA.transform, Stagger());
            Assert.AreEqual(1, ra.StaggersNegated, "A's Anchored is on its host-owned cooldown");

            var exo = (ExoLockPassive)mirrorC.Passives.GetActive(EquippedSlot.Armor);
            bodyC.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(25));
            Assert.IsTrue(exo.IsBuffActive, "Exo Lock triggered from the host copy's authoritative damage");
            Assert.IsFalse(((ExoLockPassive)mirrorC.Passives.GetActive(EquippedSlot.Armor)) == null);
            bodyA.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(25));
            Assert.AreEqual(1, bodyC.GetComponent<EquipmentPassiveTicker>().Count, "one passive runtime per member body");

            // The host ticks the member's passives: A's Anchored recharges on the host's clock.
            yield return new WaitForSeconds(AnchoredPassive.CooldownSeconds + 0.4f);
            ImpactDispatcher.Apply(bodyA.transform, Stagger());
            Assert.AreEqual(2, ra.StaggersNegated);
            foreach (var m in new[] { mirrorA, mirrorB, mirrorC, mirrorD }) m.Dispose();
            foreach (var b in new[] { bodyA, bodyB, bodyC, bodyD }) Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator RemoteMember_Snapshots_SwapWithoutStalePassives_AndResendingTheSameArmor_KeepsItsCooldown()
        {
            yield return EnterRun();
            var riot = Wearing("armor_riot_armor", "riot_instance");
            var (body, mirror) = RemoteMember(7, riot, new Vector2(400f, 400f));
            var receiver = body.GetComponent<PlayerImpactReceiver>();
            ImpactDispatcher.Apply(body.transform, Stagger());
            Assert.AreEqual(1, receiver.StaggersNegated);

            // The member re-sends the very same equipment (reconnect resync, an unrelated backpack change): no reset.
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 2, Inventory = Wearing("armor_riot_armor", "riot_instance") }));
            ImpactDispatcher.Apply(body.transform, Stagger());
            Assert.AreEqual(1, receiver.StaggersNegated, "re-sending the same armor does not reset Anchored's cooldown");

            // Swap to Blast Suit: Anchored is gone (no stale passive), Shock Absorber is live.
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 3, Inventory = Wearing("armor_blast_suit") }));
            Assert.AreEqual("shock_absorber", mirror.Passives.GetActive(EquippedSlot.Armor)?.Id);
            yield return new WaitForSeconds(0.1f);
            var negated = receiver.StaggersNegated;
            receiver.ApplyStagger(Stagger());
            Assert.AreEqual(negated, receiver.StaggersNegated, "no stale Anchored after the swap");
            ImpactDispatcher.Apply(body.transform, Explosion());
            Assert.AreEqual(1, receiver.KnockbacksNegated);

            // Unequip: nothing left.
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 4, Inventory = new InventorySnapshot { Equipped = new InventorySnapshot.Entry[0], Backpack = new InventorySnapshot.Entry[0] } }));
            Assert.AreEqual(0, mirror.Passives.Active.Count);
            ImpactDispatcher.Apply(body.transform, Explosion());
            Assert.AreEqual(1, receiver.KnockbacksNegated, "no stale Shock Absorber after unequip");
            // A stale (older) snapshot is ignored entirely.
            Assert.IsFalse(mirror.Apply(new InventorySnapshotMessage { Version = 3, Inventory = Wearing("armor_blast_suit") }));
            Assert.AreEqual(0, mirror.Passives.Active.Count);
            mirror.Dispose();
            Object.DestroyImmediate(body);
        }

        [Test]
        public void MemberRig_LeavesTheIncomingImpactPassivesToTheHost_AndKeepsEveryOther()
        {
            var catalog = GameContentCatalog.Load();
            var registry = ItemDefinitionRegistry.Build(catalog.Items.Where(i => i != null));
            ItemDefinition Resolve(string id) => registry.TryGet(id, out var d) ? d : null;
            var stats = new PlayerStats(catalog.StatCaps, 100);
            foreach (var (armor, expected) in new[] { ("armor_riot_armor", 0), ("armor_blast_suit", 0), ("armor_reinforced_exo_rig", 0), ("armor_heavy_plate", 0), ("armor_scout_rig", 1) })
            {
                var inventory = PlayerInventory.FromRegistry(registry, catalog.AmmoBalance);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance(armor, 1, Rarity.Legendary), EquippedSlot.Armor));
                using var member = new EquipmentPassiveRegistrar(inventory, Resolve, new PassiveContext(stats, new PlayerCombatEvents(), () => 100, () => 100, _ => false, null), EquipmentPassiveRegistrar.IsMemberRigMechanic);
                using var solo = new EquipmentPassiveRegistrar(inventory, Resolve, new PassiveContext(new PlayerStats(catalog.StatCaps, 100), new PlayerCombatEvents(), () => 100, () => 100, _ => false, null));
                Assert.AreEqual(expected, member.Active.Count, armor + " on a co-op client's own rig");
                Assert.AreEqual(1, solo.Active.Count, armor + " on a solo/host rig (unfiltered)");
            }

            // The client → host message kinds carry no passive trigger a member could spoof.
            foreach (var kind in new[] { "req.passive", "req.stagger", "req.negate", "passive.trigger" }) Assert.IsFalse(CoopKinds.IsClientToHost(kind));
        }
    }
}
