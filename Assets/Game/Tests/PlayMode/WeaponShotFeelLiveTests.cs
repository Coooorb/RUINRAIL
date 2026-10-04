using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Weapon shot feel in a live run at 640×360, composed by the shipping seams: one representative weapon of every
    /// ranged class (and two Legendaries) equipped through the run's inventory and fired by the live rig at a frozen
    /// target down a clear lane — captured on the muzzle frame, in flight and on the impact — then a busy fight with
    /// live attackers. Captures: TestResults/WeaponShotFeel.
    /// </summary>
    public sealed class WeaponShotFeelLiveTests
    {
        private const string Folder = "TestResults/WeaponShotFeel";
        private static readonly string[] Weapons =
        {
            "weapon_p9_ranger", "weapon_rattler_9", "weapon_ar_17", "weapon_hound_br", "weapon_breacher_12", "weapon_longshot_s1",
            "weapon_pulse_carbine_b1", "weapon_pipe_launcher", "weapon_vanguard", "weapon_redline"
        };

        private GameApp _app;
        private string _saveDir;
        private readonly StringBuilder _evidence = new();

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_shotfeel_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            File.AppendAllText(Path.Combine(Folder, "shot_feel_evidence.txt"), _evidence.ToString());
            _evidence.Clear();
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
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
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 10; i++) yield return null;
        }

        /// <summary>A horizontal lane of <paramref name="length"/> tiles inside the room with nothing solid along it.</summary>
        private static (Vector2 from, Vector2 to)? ClearLane(Rect interior, float length)
        {
            for (var y = interior.center.y; y < interior.yMax - 1f; y += 0.5f)
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var row = interior.center.y + (y - interior.center.y) * sign;
                for (var x = interior.xMin + 1.2f; x + length < interior.xMax - 0.8f; x += 0.5f)
                {
                    var from = new Vector2(x, row);
                    var to = from + Vector2.right * length;
                    if (!RoomRuntime.IsSpawnClear(from) || !RoomRuntime.IsSpawnClear(to)) continue;
                    if (Physics2D.CircleCastAll(from, 0.45f, Vector2.right, length).Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<EnvironmentObstacle>() != null)) continue;
                    return (from, to);
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_EveryRangedClass_ShowsMuzzleFlightAndImpact_AndHoldsUpInABusyFight([Values(11, 27)] int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Shot Feel");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var biome = run.Expedition.State.Biome;
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var room = run.Rooms[run.Generation.Graph.StartId];
            var lane = ClearLane(room.InteriorWorldBounds, 5f);
            Assert.IsTrue(lane.HasValue, "a clear 5-tile lane in the start room");
            var (stand, targetAt) = lane.Value;
            yield return Teleport(run, stand);

            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var ortho = LiveDungeonCapture.Height / (2f * ppu);
            var centre = (stand + targetAt) * 0.5f;
            _evidence.AppendLine($"biome {biome} seed {seed} lane {stand}->{targetAt}");

            // A frozen, sturdy target at the end of the lane.
            var targetDefinition = content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset);
            var target = new DefaultEnemySpawner(content.Stagger).Spawn(targetDefinition, targetAt, player.transform);
            room.BindEncounterBounds(target.gameObject);
            run.BindEnemyPresentation(target);
            target.enabled = false;
            var targetBody = target.GetComponent<Rigidbody2D>();
            targetBody.bodyType = RigidbodyType2D.Kinematic;
            var targetHealth = target.GetComponent<HealthComponent>();
            targetHealth.SetMaxHealth(100000);
            targetHealth.Heal(100000);

            var aiming = player.GetComponent<PlayerAiming>();
            var reader = new FakePlayerInputReader { IsAimFromPointer = true };
            aiming.SetInputReader(reader);
            IEnumerator AimAt(Vector2 world)
            {
                // The camera eases toward the aim: hold the pointer on the spot until the crosshair settles (a time deadline,
                // batch PlayMode is uncapped).
                var until = Time.time + 2f;
                while (Time.time < until)
                {
                    reader.Aim = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
                    yield return null;
                    if (Vector2.Distance(aiming.AimWorldPoint, world) < 0.05f && Time.time > until - 1.6f) break;
                }
            }

            var inventory = run.Rig.Inventory;
            var feedback = run.Feedback;
            var missed = new List<string>();
            foreach (var id in Weapons)
            {
                inventory.Unequip(EquippedSlot.PrimaryWeapon);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance(id, 1, Rarity.Rare), EquippedSlot.PrimaryWeapon), id);
                yield return null;
                run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
                yield return null;
                var weapon = run.Rig.Loadout.ActiveWeapon;
                if (weapon is RangedWeapon r) { r.SetAimAssist(null); r.ApplyAuthoritativeState(r.Definition.MagazineSize, false); }
                if (weapon is BlasterWeapon b) b.SetAimAssist(null);
                targetBody.position = targetAt;
                target.transform.position = targetAt;
                yield return AimAt(targetAt + Vector2.up * 0.4f);
                // Let any earlier effect play out so every capture shows this weapon's shot alone.
                var settle = Time.time + 0.6f;
                while (Time.time < settle) yield return null;

                var driver = player.GetComponent<RuinRail.Presentation.Animation.WeaponVisualDriver>();
                var kicksBefore = driver.ShotsShown;
                var magBefore = (weapon as RangedWeapon)?.MagazineAmmo ?? -1;
                var muzzleBefore = feedback.CountOf("muzzle");
                var landedBefore = feedback.CountOf("impact") + feedback.CountOf("explosion");
                var definition = weapon switch { RangedWeapon rw => (WeaponDefinition)rw.Definition, BlasterWeapon bw => bw.Definition, _ => null };
                var profile = ProjectileVisualCatalog.Active.Find(ProjectileVisualCatalog.ResolveWeaponVisualId(definition));
                Assert.IsNotNull(profile, id + " resolves a projectile profile");
                Assert.IsFalse(string.IsNullOrEmpty(profile.MuzzleKind) || string.IsNullOrEmpty(profile.ImpactKind), id + ": its profile names a muzzle flash and an impact");
                var flashSeen = false;
                var blastChecked = false;
                var hpBefore = targetHealth.CurrentHealth;
                Assert.IsTrue(Fire(weapon), id + " fired");
                var fired = Time.time;
                var shots = new HashSet<string>();
                var impactSeen = false;
                while (Time.time < fired + 1.2f)
                {
                    yield return null;
                    var age = Time.time - fired;
                    if (!shots.Contains("muzzle") && feedback.CountOf("muzzle") > muzzleBefore)
                    {
                        // The profile's own flash, at native pixel size (never resampled), where the shot really left.
                        var flash = Object.FindObjectsByType<PooledEffect>(FindObjectsSortMode.None).FirstOrDefault(e => e.IsActive && e.Kind == profile.MuzzleKind);
                        flashSeen = flash != null && flash.transform.localScale == Vector3.one
                            && Vector2.Distance(flash.transform.position, ((weapon as RangedWeapon)?.LastShot ?? ((BlasterWeapon)weapon).LastShot).SpawnPosition) < 0.01f;
                        Shot(id, "1_muzzle"); shots.Add("muzzle");
                    }
                    else if (!shots.Contains("flight") && shots.Contains("muzzle") && age > 0.06f) { Shot(id, "2_flight"); shots.Add("flight"); }
                    if (!impactSeen && targetHealth.CurrentHealth < hpBefore)
                    {
                        impactSeen = true;
                        Shot(id, "3_impact");
                        if (definition is RangedWeaponDefinition rd && rd.ExplosionRadiusTiles > 0f)
                        {
                            // The blast is drawn at native pixels with its rim on the true radius: never a stretched sprite.
                            var radiusPx = Mathf.RoundToInt(rd.ExplosionRadiusTiles * 32f);
                            var blast = Object.FindObjectsByType<PooledEffect>(FindObjectsSortMode.None).FirstOrDefault(e => e.IsActive && e.Kind == "explosion_r" + radiusPx);
                            Assert.IsNotNull(blast, id + ": the radius-true blast plays");
                            Assert.AreEqual(Vector3.one, blast.transform.localScale, id + ": the blast is not resampled");
                            Assert.AreEqual(radiusPx * 2 + 2, Mathf.RoundToInt(blast.Renderer.sprite.rect.width), id + ": the blast sheet spans the true diameter");
                            blastChecked = true;
                        }
                    }
                }

                _evidence.AppendLine($"{id}: muzzle {feedback.CountOf("muzzle") - muzzleBefore} hit {hpBefore - targetHealth.CurrentHealth} shots [{string.Join(",", shots)}] impact={impactSeen} kicks={driver.ShotsShown - kicksBefore} mag={magBefore}->{(weapon as RangedWeapon)?.MagazineAmmo ?? -1} aim={aiming.AimWorldPoint} dir={aiming.AimDirection} target={targetAt}");
                if (!impactSeen) missed.Add(id);
                Assert.AreEqual(1, feedback.CountOf("muzzle") - muzzleBefore, id + ": one trigger pull, one muzzle flash");
                if (definition is RangedWeaponDefinition re && re.ExplosionRadiusTiles > 0f) Assert.IsTrue(blastChecked, id + ": the blast was checked");
                Assert.IsTrue(flashSeen, id + $": the '{profile.MuzzleKind}' flash plays at native size on the shot's spawn point");
                Assert.Greater(feedback.CountOf("impact") + feedback.CountOf("explosion"), landedBefore, id + ": the landing shows an impact (or the rocket's blast)");
                if (health.IsAlive) health.Heal(health.MaxHealth);
            }

            // ---- an environment hit: the rifle into the room's far wall, the target out of the lane ----
            {
                target.gameObject.SetActive(false);
                inventory.Unequip(EquippedSlot.PrimaryWeapon);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_hound_br", 1, Rarity.Rare), EquippedSlot.PrimaryWeapon));
                yield return null;
                run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
                yield return null;
                var rifle = (RangedWeapon)run.Rig.Loadout.ActiveWeapon;
                rifle.SetAimAssist(null);
                rifle.ApplyAuthoritativeState(rifle.Definition.MagazineSize, false);
                var wallAim = new Vector2(room.InteriorWorldBounds.xMax + 0.3f, stand.y + 0.4f);
                yield return AimAt(wallAim);
                var before = feedback.CountOf("impact");
                Assert.IsTrue(rifle.TryFire());
                var until = Time.time + 1.5f;
                while (Time.time < until && feedback.CountOf("impact") == before) yield return null;
                Assert.Greater(feedback.CountOf("impact"), before, "the wall hit shows an impact");
                yield return null;
                LiveDungeonCapture.Capture(Folder, $"{biome}_hound_br_4_wall", cam, new Vector2(room.InteriorWorldBounds.xMax - 4f, stand.y), ortho, ppu, includeUi: false);
                target.gameObject.SetActive(true);
            }

            void Shot(string id, string phase) => LiveDungeonCapture.Capture(Folder, $"{biome}_{id.Replace("weapon_", string.Empty)}_{phase}", cam, centre, ortho, ppu, includeUi: false);

            // ---- busy fight: live attackers round the target, rapid and heavy fire into them ----
            void Live(string enemyId, Vector2 offset)
            {
                var definition = content.Enemies.First(d => d.Id == enemyId);
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, targetAt + offset, player.transform);
                if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                room.BindEncounterBounds(enemy.gameObject);
                run.BindEnemyPresentation(enemy);
                enemy.GetComponent<HealthComponent>().SetMaxHealth(100000);
                enemy.GetComponent<HealthComponent>().Heal(100000);
            }

            Live("shooter", new Vector2(0.5f, 2f));
            Live("charger", new Vector2(-1f, -1.6f));
            Live(content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Lob).Id, new Vector2(1f, -2f));
            foreach (var id in new[] { "weapon_rattler_9", "weapon_breacher_12", "weapon_pulse_carbine_b1", "weapon_pipe_launcher" })
            {
                inventory.Unequip(EquippedSlot.PrimaryWeapon);
                Assert.IsTrue(inventory.TryEquip(new ItemInstance(id, 1, Rarity.Rare), EquippedSlot.PrimaryWeapon), id);
                yield return null;
                run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
                yield return null;
                var weapon = run.Rig.Loadout.ActiveWeapon;
                if (weapon is RangedWeapon ra) ra.SetAimAssist(null);
                if (weapon is BlasterWeapon ba) ba.SetAimAssist(null);
                yield return AimAt(targetAt + Vector2.up * 0.4f);
                var end = Time.time + 1.4f;
                var next = Time.time + 0.35f;
                var n = 0;
                while (Time.time < end)
                {
                    if (health.IsAlive) health.Heal(health.MaxHealth);
                    if (weapon is RangedWeapon rr) rr.ApplyAuthoritativeState(rr.Definition.MagazineSize, false);
                    Fire(weapon);
                    yield return null;
                    if (Time.time >= next && n < 3) { n++; next += 0.35f; LiveDungeonCapture.Capture(Folder, $"{biome}_busy_{id.Replace("weapon_", string.Empty)}_{n}", cam, centre, ortho, ppu, includeUi: true); }
                }
            }

            Assert.IsEmpty(missed, "every weapon's shot reached the target down the clear lane");
        }

        private static bool Fire(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r => r.TryFire(),
            BlasterWeapon b => b.TryFire(),
            _ => false
        };
    }
}
