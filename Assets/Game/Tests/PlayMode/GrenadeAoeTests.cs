using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class GrenadeAoeTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var zone in Object.FindObjectsByType<SmokeZone>(FindObjectsSortMode.None)) Object.DestroyImmediate(zone.gameObject);
            foreach (var zone in Object.FindObjectsByType<BurnZone>(FindObjectsSortMode.None)) Object.DestroyImmediate(zone.gameObject);
            foreach (var g in Object.FindObjectsByType<ThrownGrenade>(FindObjectsSortMode.None)) Object.DestroyImmediate(g.gameObject);
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static GrenadeData Frag() => new() { Kind = GrenadeEffectKind.Frag, RadiusTiles = 2.5f, DamageMin = 35, DamageMax = 45, StaggerPower = 4f, ThrowRangeTiles = 6f, ThrowSpeed = 12f };
        private static GrenadeData Shock() => new() { Kind = GrenadeEffectKind.Shock, RadiusTiles = 2.5f, DamageMin = 15, DamageMax = 20, StaggerPower = 20f, ThrowRangeTiles = 6f, ThrowSpeed = 12f };
        private static GrenadeData Incendiary() => new() { Kind = GrenadeEffectKind.Incendiary, RadiusTiles = 2.5f, DamageMin = 12, DamageMax = 16, StaggerPower = 2f, BurnDamagePerSecond = 6, BurnDurationSeconds = 5f, ThrowRangeTiles = 6f, ThrowSpeed = 12f };
        private static GrenadeData Smoke() => new() { Kind = GrenadeEffectKind.Smoke, RadiusTiles = 4f, SmokeDurationSeconds = 6f, ThrowRangeTiles = 6f, ThrowSpeed = 12f };

        private HealthComponent Target(Vector2 position, DamageTeam team = DamageTeam.Enemy, int hp = 500, bool twoColliders = false)
        {
            var go = new GameObject("Target");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            if (twoColliders)
            {
                var child = new GameObject("Hurtbox");
                child.transform.SetParent(go.transform, false);
                child.AddComponent<CircleCollider2D>().radius = 0.6f;
            }

            go.AddComponent<TeamMember>().SetTeam(team);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(hp);
            return health;
        }

        private ThrownGrenade Throw(GrenadeData data, Vector2 origin, Vector2 landing, IDamageRoller roller = null)
        {
            var grenade = new GameObject("Grenade").AddComponent<ThrownGrenade>();
            grenade.Launch(data, origin, landing, DamageTeam.Player, roller);
            return grenade;
        }

        [UnityTest]
        public IEnumerator Frag_TravelsVisiblyThenExplodes_35To45_InRadius_OncePerTarget_NeverPlayers()
        {
            var inside = Target(new Vector2(6f, 0f));
            var edge = Target(new Vector2(8.4f, 0f), twoColliders: true);
            var outside = Target(new Vector2(9.2f, 0f));
            var player = Target(new Vector2(6.5f, 0.5f), DamageTeam.Player);
            var hits = new List<int>();
            inside.Damaged += d => hits.Add(d);
            yield return null;

            var grenade = Throw(Frag(), Vector2.zero, new Vector2(6f, 0f));
            grenade.Advance(0.25f);
            Assert.IsFalse(grenade.IsResolved);
            Assert.AreEqual(3f, grenade.transform.position.x, 0.01f, "Visible travel at 12 tiles/s.");
            Assert.AreEqual(500, inside.CurrentHealth, "No damage before landing.");
            grenade.Advance(0.3f);
            Assert.IsTrue(grenade.IsResolved);

            Assert.AreEqual(1, hits.Count, "Exactly one hit per target.");
            Assert.That(hits[0], Is.InRange(35, 45));
            Assert.Less(edge.CurrentHealth, 500, "Multi-collider target inside the radius is hit once.");
            Assert.LessOrEqual(500 - edge.CurrentHealth, 45, "Never double damage from two colliders.");
            Assert.AreEqual(500, outside.CurrentHealth, "3 tiles away: outside 2.5.");
            Assert.AreEqual(500, player.CurrentHealth, "Player-sourced explosions never hit Players (no PvP/friendly fire).");
        }

        [Test]
        public void AreaDamage_TagsExplosions_SoBlastSuitReductionApplies()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var config = ScriptableObject.CreateInstance<RuinRail.Gameplay.Player.PlayerBalanceConfig>();
            _created.Add(config);
            var victim = new GameObject("PlayerVictim");
            _created.Add(victim);
            victim.AddComponent<BoxCollider2D>().size = Vector2.one;
            victim.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = victim.AddComponent<HealthComponent>();
            var binder = victim.AddComponent<PlayerStatsBinder>();
            binder.Configure(caps, config);
            binder.Stats.SetSource(new StatModifierSource("blast_suit", StatModifier.Percent(StatId.GeneralDamageReduction, 40), StatModifier.Percent(StatId.ExplosionDamageReduction, 30)));
            Assert.AreEqual(100, health.CurrentHealth, "Binder syncs the pipeline's base 100 HP.");

            // An enemy-sourced explosion reaches the player and is reduced 50 → 30 → 21.
            var result = AreaDamageResolver.Apply(victim.transform.position, 1f, 50, 50, DamageKind.Explosion, 0f, DamageTeam.Enemy, new FixedDamageRoller { FixedValue = 50 });
            Assert.AreEqual(1, result.TargetsHit);
            Assert.AreEqual(100 - 21, health.CurrentHealth);

            AreaDamageResolver.Apply(victim.transform.position, 1f, 50, 50, DamageKind.Normal, 0f, DamageTeam.Enemy, new FixedDamageRoller { FixedValue = 50 });
            Assert.AreEqual(100 - 21 - 30, health.CurrentHealth, "Normal damage gets only general DR.");
        }

        [UnityTest]
        public IEnumerator Shock_Deals15To20_WithVeryHighStaggerTransported()
        {
            var probeObject = new GameObject("Probe");
            _created.Add(probeObject);
            probeObject.transform.position = new Vector2(5f, 0f);
            probeObject.AddComponent<BoxCollider2D>().size = Vector2.one;
            var probe = probeObject.AddComponent<DamageProbe>();
            var requests = new List<DamageRequest>();
            probe.Received += r => requests.Add(r);
            yield return null;

            var grenade = Throw(Shock(), Vector2.zero, new Vector2(5f, 0f));
            grenade.Advance(1f);

            Assert.IsTrue(grenade.IsResolved);
            Assert.AreEqual(1, requests.Count);
            Assert.That(requests[0].Amount, Is.InRange(15, 20));
            Assert.AreEqual(DamageKind.Explosion, requests[0].Kind);
            Assert.AreEqual(20f, requests[0].StaggerPower, 0.001f, "Very high stagger travels with the hit for the stagger foundation.");
        }

        [UnityTest]
        public IEnumerator Incendiary_ImpactThenFiveDeterministicBurnTicks_StoppingAfter5Seconds_AndNotAfterDeath()
        {
            var target = Target(new Vector2(5f, 0f), hp: 100);
            var fragile = Target(new Vector2(5.5f, 0.5f), hp: 20);
            yield return null;

            var grenade = Throw(Incendiary(), Vector2.zero, new Vector2(5f, 0f), new FixedDamageRoller { FixedValue = 14 });
            grenade.Advance(1f);
            Assert.IsTrue(grenade.IsResolved);
            Assert.AreEqual(86, target.CurrentHealth, "Impact 12–16 (fixed 14).");
            var burn = grenade.SpawnedZone.GetComponent<BurnZone>();
            Assert.IsNotNull(burn);
            Assert.AreEqual(5, burn.TotalTicks);
            Assert.AreEqual(2.5f, burn.Radius, 0.001f);

            burn.Advance(0.99f);
            Assert.AreEqual(0, burn.TicksDone, "First tick at 1.0s.");
            burn.Advance(0.02f);
            Assert.AreEqual(1, burn.TicksDone);
            Assert.AreEqual(80, target.CurrentHealth);
            burn.Advance(2.0f);
            Assert.AreEqual(3, burn.TicksDone, "Ticks at 2s and 3s: cadence is 1/s regardless of step size.");
            Assert.AreEqual(68, target.CurrentHealth);
            Assert.IsFalse(fragile.IsAlive, "20 HP: impact 14 + one tick 6 = dead after tick 1.");
            var fragileHealthAfterDeath = fragile.CurrentHealth;
            burn.Advance(2.0f);
            Assert.AreEqual(5, burn.TicksDone);
            Assert.IsTrue(burn.IsFinished);
            Assert.AreEqual(56, target.CurrentHealth, "Total 30 burn = 5 × 6.");
            Assert.AreEqual(fragileHealthAfterDeath, fragile.CurrentHealth, "Dead targets take no ticks.");
            yield return null;
            Assert.IsTrue(burn == null, "Zone cleans itself up after the last tick.");
        }

        [UnityTest]
        public IEnumerator Smoke_BlocksNormalEnemyTargeting_NotElitesOrBosses_For6Seconds()
        {
            var grenade = Throw(Smoke(), Vector2.zero, new Vector2(5f, 0f));
            grenade.Advance(1f);
            var smoke = grenade.SpawnedZone.GetComponent<SmokeZone>();
            Assert.IsNotNull(smoke);
            Assert.AreEqual(4f, smoke.Radius, 0.001f);
            Assert.AreEqual(6f, smoke.RemainingSeconds, 0.001f);

            var enemyPos = new Vector2(0f, 0f);
            var playerPos = new Vector2(10f, 0f);
            Assert.IsTrue(SmokeZone.IsLineOfSightBlocked(enemyPos, playerPos, ignoresSmoke: false), "Normal enemy: line through the cloud is blocked.");
            Assert.IsFalse(SmokeZone.IsLineOfSightBlocked(enemyPos, playerPos, ignoresSmoke: true), "Elite/Boss: not blinded.");
            Assert.IsFalse(SmokeZone.IsLineOfSightBlocked(new Vector2(0f, 10f), new Vector2(10f, 10f), ignoresSmoke: false), "Line clear of the cloud.");

            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            _created.Add(definition);
            definition.GetType().GetField("_baseHealth", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, 30);
            definition.GetType().GetField("_moveSpeed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, 3f);
            var grunt = new GameObject("Grunt");
            _created.Add(grunt);
            grunt.transform.position = enemyPos;
            grunt.AddComponent<CircleCollider2D>().radius = 0.4f;
            grunt.AddComponent<Rigidbody2D>();
            grunt.AddComponent<HealthComponent>();
            var controller = grunt.AddComponent<EnemyController>();
            controller.SetDefinition(definition);
            var player = new GameObject("PlayerTarget");
            _created.Add(player);
            player.transform.position = playerPos;
            controller.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Idle, controller.State, "Grunt cannot acquire the player through smoke.");
            Assert.IsFalse(controller.HasLineOfSightToTarget);

            smoke.Advance(6.1f);
            yield return null;
            Assert.IsTrue(smoke == null, "Smoke dissipates after 6s.");
            yield return null;
            Assert.AreEqual(EnemyState.Chase, controller.State, "Targeting resumes once the smoke is gone.");
        }

        [Test]
        public void GrenadeStack_ConsumesOnePerSuccessfulThrow_AndNothingWhenThrowingIsUnavailable()
        {
            var frag = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _created.Add(frag);
            Set(frag, "_id", "consumable_frag_grenade");
            Set(frag, "_category", ItemCategory.Consumable);
            Set(frag, "_isStackable", true);
            Set(frag, "_maxStack", 4);
            Set(frag, "_effectKind", ConsumableEffectKind.Grenade);
            Set(frag, "_grenade", Frag());
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { frag });
            var inventory = PlayerInventory.FromRegistry(registry, null);
            var stack = new ItemInstance("consumable_frag_grenade", 3);
            inventory.TryEquip(stack, EquippedSlot.ActiveConsumable);
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var stats = new PlayerStats(caps, 100);

            var noLauncher = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null, new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a)));
            Assert.AreEqual(ConsumableUseResult.UnsupportedEffect, noLauncher.TryUse());
            Assert.AreEqual(3, stack.Quantity, "No launcher: nothing thrown, nothing spent.");

            var launcherObject = new GameObject("Thrower");
            _created.Add(launcherObject);
            var launcher = launcherObject.AddComponent<GrenadeLauncher>();
            var thrown = 0;
            var withLauncher = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null,
                new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a, data => { thrown++; return launcher.Throw(data, Vector2.right) != null; })));
            Assert.AreEqual(ConsumableUseResult.Started, withLauncher.TryUse());
            Assert.AreEqual(1, thrown);
            Assert.AreEqual(2, stack.Quantity, "One per successful throw, immediately (no channel).");
            Assert.IsFalse(withLauncher.IsUsing);
            Assert.IsNotNull(launcher.LastThrown);
            Assert.AreEqual(GrenadeEffectKind.Frag, launcher.LastThrown.Data.Kind);
            Assert.AreEqual(6f, launcher.LastThrown.LandingPoint.x, 0.001f, "Thrown to full range along the aim.");

            var failingThrow = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null,
                new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a, _ => false)));
            failingThrow.TryUse();
            Assert.AreEqual(2, stack.Quantity, "A throw that did not happen costs nothing.");
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            info.SetValue(target, value);
        }

        private sealed class DamageProbe : MonoBehaviour, IDamageable
        {
            public event System.Action<DamageRequest> Received;
            public bool TryApplyDamage(DamageRequest request)
            {
                Received?.Invoke(request);
                return true;
            }
        }
    }
}

namespace RuinRail.Tests
{
    /// <summary>
    /// Consumable world effects in the shipped runtime (boot → Shelter → dungeon): each grenade is thrown through the
    /// player's real quick-grenade path, drawn along its flight, and shows its landing effect at the gameplay's radius for
    /// the gameplay's duration. Captures: TestResults/RegressionProof/consumable_vfx_*.png.
    /// </summary>
    public sealed class ConsumableVfxLiveTests
    {
        private string _saveDir;
        private RuinRail.App.GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_cvfx_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory("TestResults/RegressionProof");
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = null;
            RuinRail.Core.Input.GameplayInputGate.Reset();
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static List<RuinRail.Presentation.Vfx.PooledEffect> Live(string kind) =>
            Object.FindObjectsByType<RuinRail.Presentation.Vfx.PooledEffect>(FindObjectsSortMode.None).Where(e => e.IsActive && e.Kind == kind).ToList();

        [UnityTest]
        public IEnumerator EveryGrenade_IsDrawnInFlight_AndItsLandingEffectMatchesTheRealAreaAndDuration()
        {
            _app = RuinRail.App.GameApp.Ensure(RuinRail.App.GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            UnityEngine.SceneManagement.SceneManager.LoadScene(RuinRail.Core.SceneNames.MainMenu);
            for (var t = Time.realtimeSinceStartup + 30f; _app.ComposedScene != RuinRail.Core.SceneNames.MainMenu;) { Assert.Less(Time.realtimeSinceStartup, t); yield return null; }
            _app.Menu.Play();
            for (var t = Time.realtimeSinceStartup + 30f; _app.ComposedScene != RuinRail.Core.SceneNames.Base;) { Assert.Less(Time.realtimeSinceStartup, t); yield return null; }
            var hub = Object.FindFirstObjectByType<RuinRail.App.BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Thrower");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            for (var t = Time.realtimeSinceStartup + 30f; _app.ComposedScene != RuinRail.Core.SceneNames.Dungeon;) { Assert.Less(Time.realtimeSinceStartup, t); yield return null; }
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<RuinRail.App.ExpeditionScene>();
            var player = run.Rig.Player;
            var inventory = run.Rig.Inventory;
            var start = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Start).InteriorWorldBounds;
            var from = new Vector2(start.xMin + 1.5f, start.center.y);
            var grenades = _app.Content.Items.OfType<ConsumableDefinition>().Where(c => c.EffectKind == ConsumableEffectKind.Grenade).OrderBy(c => c.Grenade.Kind).ToList();
            Assert.AreEqual(4, grenades.Count, "Frag, Shock, Incendiary, Smoke");

            foreach (var definition in grenades)
            {
                var data = definition.Grenade;
                player.transform.position = from;
                player.GetComponent<Rigidbody2D>().position = from;
                inventory.Unequip(EquippedSlot.ActiveConsumable);
                for (var slot = 0; slot < inventory.BackpackSlots.Count; slot++)
                    if (inventory.BackpackSlots[slot] != null && inventory.BackpackSlots[slot].DefinitionId.Contains("grenade")) inventory.RemoveFromBackpack(slot);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance(definition.Id, 1), EquippedSlot.ActiveConsumable), definition.Id);
                yield return null;

                var launcher = player.GetComponent<GrenadeLauncher>();
                Assert.IsTrue(run.Rig.Consumables.TryQuickGrenade(), definition.Id + " thrown through the quick-grenade path");
                var grenade = launcher.LastThrown;
                yield return null;
                var inFlight = Live("grenade");
                Assert.AreEqual(1, inFlight.Count, $"{definition.Id}: the canister is drawn in flight");
                Assert.Less(Vector2.Distance(inFlight[0].transform.position, grenade.transform.position), 0.05f, "and follows the real grenade");
                var landing = grenade.LandingPoint;
                for (var t = Time.time + 3f; grenade != null && Time.time < t;) yield return null;
                yield return null;
                Assert.AreEqual(0, Live("grenade").Count, $"{definition.Id}: the canister is gone on landing");

                string area = data.Kind switch { GrenadeEffectKind.Shock => "shock", GrenadeEffectKind.Smoke => "smoke_cloud", GrenadeEffectKind.Incendiary => "fire_zone", _ => "explosion" };
                var effect = Live(area).SingleOrDefault();
                Assert.IsNotNull(effect, $"{definition.Id}: {area} shows where it landed");
                Assert.Less(Vector2.Distance(effect.transform.position, landing), 0.01f, "at the landing point");
                if (area != "explosion")
                    Assert.AreEqual(data.RadiusTiles * 2f, effect.Renderer.bounds.size.x, 0.05f, $"{definition.Id}: drawn exactly as wide as the gameplay area");
                if (data.Kind == GrenadeEffectKind.Incendiary) Assert.AreEqual(1, Live("explosion").Count, "the Incendiary also bursts");
                if (data.Kind == GrenadeEffectKind.Frag || data.Kind == GrenadeEffectKind.Incendiary)
                    Assert.AreEqual(data.RadiusTiles * 2f, Live("explosion")[0].Renderer.bounds.size.x, 0.2f, "the blast reads its real radius");

                var cam = run.Camera.Camera.transform;
                cam.position = new Vector3(landing.x, landing.y, cam.position.z);
                for (var i = 0; i < 3; i++) yield return null;
                RuinRail.Tests.LiveDungeonCapture.Capture("TestResults/RegressionProof", "consumable_vfx_" + data.Kind, run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: false);

                // Lasting areas: the effect lives exactly as long as the zone that does the gameplay.
                if (data.Kind == GrenadeEffectKind.Smoke || data.Kind == GrenadeEffectKind.Incendiary)
                {
                    var zone = data.Kind == GrenadeEffectKind.Smoke ? (Component)Object.FindObjectsByType<SmokeZone>(FindObjectsSortMode.None).Single() : Object.FindObjectsByType<BurnZone>(FindObjectsSortMode.None).Single();
                    Assert.Less(Vector2.Distance(zone.transform.position, effect.transform.position), 0.01f, "one area, one effect, same place");
                    var seconds = data.Kind == GrenadeEffectKind.Smoke ? data.SmokeDurationSeconds : data.BurnDurationSeconds;
                    var landedAt = Time.time;
                    for (var t = Time.time + seconds + 2f; zone != null && Time.time < t;) yield return null;
                    var zoneEnded = Time.time - landedAt;
                    for (var t = Time.time + 1f; effect.IsActive && Time.time < t;) yield return null;
                    var effectEnded = Time.time - landedAt;
                    Assert.IsTrue(zone == null, "the zone ended");
                    Assert.AreEqual(zoneEnded, effectEnded, 0.25f, $"{definition.Id}: the effect ends with the area ({zoneEnded:0.00}s vs {effectEnded:0.00}s)");
                }
                else
                {
                    for (var t = Time.time + 1.5f; effect.IsActive && Time.time < t;) yield return null;
                    Assert.IsFalse(effect.IsActive, "the burst is brief");
                }
            }
            // A stim's buff (the composed ConsumableEffectRunner binding): one status marker over the user.
            inventory.Unequip(EquippedSlot.ActiveConsumable);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("consumable_combat_stim", 1), EquippedSlot.ActiveConsumable));
            var statusBefore = Object.FindFirstObjectByType<RuinRail.Presentation.Vfx.CombatFeedback>().CountOf("status");
            Assert.IsTrue(run.Rig.Consumables.TryUse(), "the stim is used");
            for (var t = Time.time + 5f; Object.FindFirstObjectByType<RuinRail.Presentation.Vfx.CombatFeedback>().CountOf("status") == statusBefore && Time.time < t;) yield return null;
            Assert.AreEqual(statusBefore + 1, Object.FindFirstObjectByType<RuinRail.Presentation.Vfx.CombatFeedback>().CountOf("status"), "the buff shows its marker once");
        }
    }
}
