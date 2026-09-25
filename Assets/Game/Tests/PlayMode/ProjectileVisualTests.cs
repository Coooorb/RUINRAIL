using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Visible in-flight projectiles. The presentation is a sprite parented to the pooled <see cref="Projectile"/>
    /// itself (<see cref="ProjectileVisual"/>), so it is where the authoritative shot is, points where it points,
    /// vanishes on the registered hit / wall / expiry and is reset before reuse; every ranged weapon family and every
    /// hostile projectile attack resolves a distinct, readable profile from the shipped catalog; nothing mechanical
    /// (speed, damage, range, collision) changes.
    /// </summary>
    public sealed class ProjectileVisualTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private ProjectileVisualCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            CombatLayers.Apply();
            _content = GameContentCatalog.Load();
            _catalog = _content.ProjectileVisuals;
            Assert.IsNotNull(_catalog, "the shipped content catalog binds the projectile visual catalog");
            ProjectileVisualCatalog.Active = _catalog;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            ProjectileVisualCatalog.Active = null;
        }

        private static readonly string[] RangedFamilies = { "proj_pistol", "proj_smg", "proj_rifle", "proj_battle_rifle", "proj_pellet", "proj_sniper", "proj_arrow", "proj_energy_bolt", "proj_rocket" };
        private static readonly string[] HostileProfiles = { "proj_enemy_round", "proj_enemy_sniper", "proj_enemy_rail", "proj_enemy_scrap", "proj_enemy_energy", "proj_boss_scrap", "proj_boss_arc", "proj_boss_furnace", "proj_boss_spore", "proj_boss_energy" };

        private IEnumerable<WeaponDefinition> RangedWeapons() => _content.Items.OfType<WeaponDefinition>().Where(w => w.WeaponClass != WeaponClass.Knife && w.WeaponClass != WeaponClass.Spear);

        // ---- catalog ----

        [Test]
        public void Catalog_IsCompleteAndProblemFree()
        {
            CollectionAssert.IsEmpty(_catalog.Problems());
            foreach (var id in RangedFamilies.Concat(HostileProfiles))
            {
                var profile = _catalog.Find(id);
                Assert.IsNotNull(profile, id);
                Assert.IsTrue(profile.IsValid, id + " has frames");
            }

            Assert.IsNotNull(_catalog.Find("proj_rocket").Trail, "the rocket carries an exhaust trail");
            Assert.Greater(_catalog.Find("proj_energy_bolt").Frames.Length, 1, "energy bolts animate");
        }

        [Test]
        public void EveryRangedWeapon_ResolvesAValidProfile_ByFamily_WithLegendaryOverrides()
        {
            var expectedFamily = new Dictionary<WeaponClass, string>
            {
                [WeaponClass.Pistol] = "proj_pistol", [WeaponClass.Smg] = "proj_smg", [WeaponClass.AssaultRifle] = "proj_rifle", [WeaponClass.BattleRifle] = "proj_battle_rifle",
                [WeaponClass.Shotgun] = "proj_pellet", [WeaponClass.Sniper] = "proj_sniper", [WeaponClass.Bow] = "proj_arrow", [WeaponClass.Blaster] = "proj_energy_bolt", [WeaponClass.RocketLauncher] = "proj_rocket"
            };
            var checkedWeapons = 0;
            var legendaries = 0;
            foreach (var weapon in RangedWeapons())
            {
                var id = ProjectileVisualCatalog.ResolveWeaponVisualId(weapon);
                Assert.IsFalse(string.IsNullOrEmpty(id), weapon.Id + " resolves a profile id");
                var profile = _catalog.Find(id);
                Assert.IsNotNull(profile, $"{weapon.Id}: '{id}' is a catalog profile");
                Assert.IsTrue(profile.IsValid, weapon.Id);
                var family = expectedFamily[weapon.WeaponClass];
                if (!string.IsNullOrEmpty(weapon.LegendaryMechanicId))
                {
                    Assert.AreEqual(family + "_legendary", id, weapon.Id + ": a Legendary weapon carries its family's Legendary variant");
                    Assert.AreNotEqual(_catalog.Find(family).Frames[0], profile.Frames[0], weapon.Id + ": the variant is its own sprite");
                    legendaries++;
                }
                else
                {
                    Assert.AreEqual(family, id, weapon.Id + ": family default");
                }

                checkedWeapons++;
            }

            Assert.AreEqual(27, checkedWeapons, "every ranged weapon in the catalog");
            Assert.AreEqual(9, legendaries, "the nine Legendary ranged weapons");
        }

        [Test]
        public void EveryTravellingEnemyProjectile_ResolvesAHostileProfile()
        {
            foreach (var enemy in _content.Enemies.Where(e => e.AttackKind == EnemyAttackKind.Projectile))
            {
                var profile = _catalog.Resolve(enemy.ProjectileVisualId, DamageTeam.Enemy);
                Assert.IsNotNull(profile, enemy.Id);
                StringAssert.StartsWith("proj_enemy", profile.Id, enemy.Id + " uses a hostile profile");
            }

            var attacks = AssetDatabase.FindAssets("t:EnemyAttackDefinition").Select(g => AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null && a.Motion == AttackMotion.Projectile).ToList();
            Assert.AreEqual(14, attacks.Count, "the shipped projectile attacks");
            foreach (var attack in attacks)
            {
                Assert.IsFalse(string.IsNullOrEmpty(attack.ProjectileVisualId), attack.Id + " names its profile");
                var profile = _catalog.Find(attack.ProjectileVisualId);
                Assert.IsNotNull(profile, attack.Id);
                Assert.IsTrue(profile.Id.StartsWith("proj_enemy") || profile.Id.StartsWith("proj_boss"), attack.Id + " is hostile-coded");
            }

            // Boss attacks carry their own, larger profiles: not the generic hostile round.
            foreach (var bossAttack in new[] { "scrapking_auto_burst", "conductor_burst_cannon", "titan_furnace_blast", "omega_spore_burst", "aegis_triple_burst" })
            {
                var attack = attacks.First(a => a.Id == bossAttack);
                StringAssert.StartsWith("proj_boss", attack.ProjectileVisualId, bossAttack);
            }
        }

        [Test]
        public void ProfilesAreDistinctAndReadable_SmallCrispWithABrightCoreAndADarkEdge()
        {
            var seen = new Dictionary<string, string>();
            foreach (var id in RangedFamilies.Concat(HostileProfiles).Concat(RangedFamilies.Select(f => f + "_legendary")))
            {
                var profile = _catalog.Find(id);
                Assert.IsNotNull(profile, id);
                var sprite = profile.Frames[0];
                Assert.AreEqual(FilterMode.Point, sprite.texture.filterMode, id + ": nearest-neighbour pixels");
                Assert.LessOrEqual(sprite.rect.width, 20f, id + ": no giant rectangles");
                Assert.LessOrEqual(sprite.rect.height, 7f, id);
                Assert.AreEqual(32f, sprite.pixelsPerUnit, id);
                var pixels = ReadFrame(sprite);
                var opaque = pixels.Where(p => p.a > 0.5f).ToList();
                Assert.Greater(opaque.Count, 2, id + " is drawn");
                Assert.IsTrue(opaque.Any(p => Luma(p) > 0.75f), id + ": a bright core pixel keeps it readable on dark floors");
                Assert.IsTrue(opaque.Any(p => Luma(p) < 0.3f), id + ": a dark edge/tail pixel keeps it readable on bright floors");
                var key = string.Join(",", pixels.Select(p => p.a > 0.5f ? ColorUtility.ToHtmlStringRGB(p) : "-"));
                Assert.IsFalse(seen.ContainsKey(key), $"{id} is pixel-identical to {(seen.TryGetValue(key, out var other) ? other : "?")}: families must differ");
                seen[key] = id;
                Assert.GreaterOrEqual(sprite.pivot.x / sprite.rect.width, 0.5f, id + ": pivot at or ahead of the middle so nothing draws past the physics point");
            }
        }

        private static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>The frame's pixels from the PNG on disk (sprites import without Read/Write, as every shipped texture does).</summary>
        private Color[] ReadFrame(Sprite sprite)
        {
            var path = AssetDatabase.GetAssetPath(sprite);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _created.Add(tex);
            Assert.IsTrue(tex.LoadImage(System.IO.File.ReadAllBytes(path)), path);
            return tex.GetPixels((int)sprite.rect.x, (int)sprite.rect.y, (int)sprite.rect.width, (int)sprite.rect.height);
        }

        // ---- runtime: follow / rotate / despawn / reset / no mechanical change ----

        private ProjectilePool Pool()
        {
            var go = new GameObject("Pool");
            _created.Add(go);
            return go.AddComponent<ProjectilePool>();
        }

        private GameObject Wall(Vector2 centre, Vector2 size)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = centre;
            wall.AddComponent<BoxCollider2D>().size = size;
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        [UnityTest]
        public IEnumerator Visual_SpawnsAtTheMuzzle_FollowsTheBody_PointsAlongVelocity_AndEndsExactlyOnTheWallHit()
        {
            var pool = Pool();
            Wall(new Vector2(206f, 300f), new Vector2(0.5f, 6f));
            var direction = new Vector2(1f, 0.5f).normalized;
            var data = new ProjectileSpawnData(7, 12f, 30f, 0f, 0f, direction, null, null, 0f, DamageTeam.Player, false, "proj_rifle");
            var projectile = pool.Spawn(new Vector2(200f, 300f), data);
            var visual = projectile.Visual;
            Assert.IsNotNull(visual);
            Assert.IsTrue(visual.IsVisible, "visible from the spawn frame");
            Assert.AreEqual("proj_rifle", visual.ProfileId);
            Assert.AreEqual(Vector3.zero, visual.Body.transform.localPosition, "the sprite sits on the projectile's own transform");
            Assert.AreEqual((Vector2)visual.Body.transform.position, (Vector2)projectile.transform.position);
            Assert.AreEqual(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, projectile.transform.eulerAngles.z, 0.01f, "oriented to the velocity");
            Assert.AreEqual(SortingLayers.Projectiles, visual.Body.sortingLayerName);
            Assert.AreEqual(7, projectile.Data.Damage); Assert.AreEqual(12f, projectile.Data.Speed); Assert.AreEqual(30f, projectile.Data.MaxRange);

            var lastBody = (Vector2)projectile.transform.position;
            for (var i = 0; i < 20 && !projectile.IsResolved; i++)
            {
                yield return new WaitForFixedUpdate();
                if (projectile.IsResolved) break;
                Assert.IsTrue(visual.IsVisible);
                var body = (Vector2)projectile.transform.position;
                Assert.AreEqual(body, (Vector2)visual.Body.transform.position, "the sprite is where the physics body is");
                Assert.GreaterOrEqual(Vector2.Dot(body - lastBody, direction), 0f, "moving with the shot");
                lastBody = body;
            }

            // Wall at x = 205.75 along a 26.6° path: the shot resolves against it and the sprite goes with it.
            var deadline = 200;
            while (!projectile.IsResolved && deadline-- > 0) yield return new WaitForFixedUpdate();
            Assert.IsTrue(projectile.IsResolved, "the shot hit the wall");
            Assert.IsFalse(projectile.gameObject.activeSelf, "returned to the pool");
            Assert.IsFalse(visual.IsVisible, "no sprite outlives the registered hit");
            Assert.IsNull(visual.Body.sprite);
            Assert.Less(projectile.transform.position.x, 206f, "and the body never went through the wall");
        }

        [UnityTest]
        public IEnumerator PooledReuse_ResetsFrameAndTrail_AndShowsTheNextShotsProfile()
        {
            var pool = Pool();
            var rocket = pool.Spawn(new Vector2(300f, 300f), new ProjectileSpawnData(1, 5f, 0.4f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player, false, "proj_rocket"));
            var visual = rocket.Visual;
            Assert.IsTrue(visual.Trail.enabled && visual.Trail.sprite != null, "the rocket shows its exhaust trail");
            Assert.Less(visual.Trail.transform.localPosition.x, 0f, "behind the body");
            yield return null; yield return null; yield return null;
            var deadline = 100;
            while (!rocket.IsResolved && deadline-- > 0) yield return new WaitForFixedUpdate();
            Assert.IsTrue(rocket.IsResolved, "range 0.4 expired");
            Assert.IsFalse(visual.IsVisible);
            Assert.IsFalse(visual.Trail.enabled, "no stale trail");

            var again = pool.Spawn(new Vector2(320f, 300f), new ProjectileSpawnData(1, 5f, 10f, 0f, 0f, Vector2.up, null, null, 0f, DamageTeam.Player, false, "proj_pellet"));
            Assert.AreSame(rocket, again, "the pooled instance was reused");
            Assert.AreEqual("proj_pellet", again.Visual.ProfileId);
            Assert.AreEqual(0, again.Visual.Frame, "frame reset");
            Assert.IsFalse(again.Visual.Trail.enabled, "a pellet has no trail: the rocket's did not leak");
            Assert.AreEqual(_catalog.Find("proj_pellet").Frames[0], again.Visual.Body.sprite);
            Assert.AreEqual(90f, again.transform.eulerAngles.z, 0.01f);
        }

        [Test]
        public void UnknownProfile_FallsBackToTheSidesDefault_AndNoCatalog_ShowsNothingButBreaksNothing()
        {
            var pool = Pool();
            var enemy = pool.Spawn(new Vector2(400f, 300f), new ProjectileSpawnData(1, 5f, 10f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Enemy, false, "no_such_profile"));
            Assert.AreEqual(_catalog.EnemyDefaultId, enemy.Visual.ProfileId, "hostile shots never fly invisible");
            var player = pool.Spawn(new Vector2(400f, 302f), new ProjectileSpawnData(1, 5f, 10f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player));
            Assert.AreEqual(_catalog.PlayerDefaultId, player.Visual.ProfileId, "an unnamed player shot shows the player default");

            ProjectileVisualCatalog.Active = null;
            var bare = pool.Spawn(new Vector2(400f, 304f), new ProjectileSpawnData(1, 5f, 10f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player, false, "proj_rifle"));
            Assert.IsFalse(bare.Visual.IsVisible);
            Assert.AreEqual(5f, bare.Data.Speed, "mechanics untouched by the absence of art");
        }

        [Test]
        public void AutomaticFireAndShotgunPellets_AllocateNothingBeyondThePool()
        {
            var pool = Pool();
            var spawned = new List<Projectile>();
            for (var i = 0; i < 40; i++) spawned.Add(pool.Spawn(new Vector2(500f + i, 300f), new ProjectileSpawnData(1, 1f, 100f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player, false, "proj_pellet")));
            var visuals = pool.GetComponentsInChildren<ProjectileVisual>(true);
            Assert.AreEqual(spawned.Count, visuals.Length, "one visual per pooled projectile, no extra bullets");
            var renderers = pool.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.AreEqual(spawned.Count * 2, renderers.Length, "body + trail renderer per projectile, nothing more");
            foreach (var p in spawned) Assert.AreEqual("proj_pellet", p.Visual.ProfileId);
        }

        // ---- the real emitters name the real profiles ----

        private (GameObject go, PlayerAiming aiming, FakePlayerInputReader input, ProjectilePool pool) Shooter(Vector2 at)
        {
            var go = new GameObject("Shooter");
            _created.Add(go);
            go.transform.position = at;
            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.AddComponent<PlayerAiming>();
            var input = new FakePlayerInputReader();
            aiming.SetInputReader(input);
            return (go, aiming, input, pool);
        }

        private T Weapon<T>(string id) where T : WeaponDefinition => (T)_content.Items.First(i => i.Id == id);

        [Test]
        public void PlayerWeapons_FireTheirFamilyProfiles_P9_Smg_Ar_BattleRifle_Shotgun_Sniper_Rocket_AndLegendary()
        {
            var cases = new (string id, string profile, int pellets)[]
            {
                ("weapon_p9_ranger", "proj_pistol", 1), ("weapon_rattler_9", "proj_smg", 1), ("weapon_ar_17", "proj_rifle", 1), ("weapon_hound_br", "proj_battle_rifle", 1),
                ("weapon_scatter_8", "proj_pellet", 0), ("weapon_longshot_s1", "proj_sniper", 1), ("weapon_pipe_launcher", "proj_rocket", 1), ("weapon_quickfang", "proj_pistol_legendary", 1)
            };
            foreach (var (id, profile, pellets) in cases)
            {
                var (go, aiming, input, pool) = Shooter(new Vector2(600f, 300f));
                var weapon = go.AddComponent<RangedWeapon>();
                var definition = Weapon<RangedWeaponDefinition>(id);
                weapon.SetInputReader(input); weapon.SetAiming(aiming); weapon.SetProjectilePool(pool); weapon.SetAmmoReserve(new AmmoReserve()); weapon.SetDamageRoller(new FixedDamageRoller()); weapon.SetDefinition(definition);
                Assert.IsTrue(weapon.TryFire(), id);
                var shot = weapon.LastSpawnedProjectile;
                Assert.AreEqual(profile, shot.Visual.ProfileId, id);
                Assert.IsTrue(shot.Visual.IsVisible, id);
                Assert.AreEqual(definition.ProjectileSpeed, shot.Data.Speed, id + ": speed unchanged");
                Assert.AreEqual(definition.Range, shot.Data.MaxRange, id + ": range unchanged");
                Assert.AreEqual(12, shot.Data.Damage, id + ": damage is the roller's value, unchanged by presentation");
                if (pellets == 0)
                {
                    var all = pool.GetComponentsInChildren<Projectile>().Where(p => p.gameObject.activeSelf).ToList();
                    Assert.AreEqual(definition.ProjectilesPerShot, all.Count, id + ": every pellet spawned");
                    Assert.IsTrue(all.All(p => p.Visual.ProfileId == "proj_pellet" && p.Visual.IsVisible), "every pellet is a visible pellet");
                }

                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BowAndBlaster_FireArrowAndEnergyBolt()
        {
            var (go, aiming, input, pool) = Shooter(new Vector2(620f, 300f));
            var bow = go.AddComponent<BowWeapon>();
            bow.SetInputReader(input); bow.SetAiming(aiming); bow.SetProjectilePool(pool); bow.SetDamageRoller(new FixedDamageRoller()); bow.SetDefinition(Weapon<BowWeaponDefinition>("weapon_recurve_bow"));
            Assert.IsTrue(bow.TryStartCharge());
            Assert.IsTrue(bow.TryRelease());
            Assert.AreEqual("proj_arrow", bow.LastSpawnedProjectile.Visual.ProfileId);

            var (go2, aiming2, input2, pool2) = Shooter(new Vector2(640f, 300f));
            var blaster = go2.AddComponent<BlasterWeapon>();
            blaster.SetInputReader(input2); blaster.SetAiming(aiming2); blaster.SetProjectilePool(pool2); blaster.SetDamageRoller(new FixedDamageRoller()); blaster.SetDefinition(Weapon<BlasterWeaponDefinition>("weapon_pulse_carbine_b1"));
            Assert.IsTrue(blaster.TryFire());
            Assert.AreEqual("proj_energy_bolt", blaster.LastSpawnedProjectile.Visual.ProfileId);

            var (go3, aiming3, input3, pool3) = Shooter(new Vector2(660f, 300f));
            var redline = go3.AddComponent<BlasterWeapon>();
            redline.SetInputReader(input3); redline.SetAiming(aiming3); redline.SetProjectilePool(pool3); redline.SetDamageRoller(new FixedDamageRoller()); redline.SetDefinition(Weapon<BlasterWeaponDefinition>("weapon_redline"));
            Assert.IsTrue(redline.TryFire());
            Assert.AreEqual("proj_energy_bolt_legendary", redline.LastSpawnedProjectile.Visual.ProfileId, "a Legendary blaster has its own bolt");
        }

        [Test]
        public void EnemyShooter_EliteEnergyBurst_AndBossSporeBurst_FireTheirHostileProfiles()
        {
            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = new Vector2(704f, 300f);
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            target.AddComponent<TestDamageableTarget>();

            var shooter = new DefaultEnemySpawner(_content.Stagger).Spawn(_content.Enemies.First(e => e.Id == "shooter"), new Vector2(700f, 300f), target.transform);
            _created.Add(shooter.gameObject);
            var attack = (EnemyProjectileAttack)shooter.Attack;
            Assert.IsTrue(attack.TryResolveAttack(target.transform));
            Assert.AreEqual(1, attack.SpawnedProjectiles.Count);
            Assert.AreEqual("proj_enemy_round", attack.SpawnedProjectiles[0].Visual.ProfileId);
            Assert.IsTrue(attack.SpawnedProjectiles[0].Visual.IsVisible);

            var resolverHost = new GameObject("Elite");
            _created.Add(resolverHost);
            resolverHost.transform.position = new Vector2(720f, 300f);
            resolverHost.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var pool = resolverHost.AddComponent<ProjectilePool>();
            var resolver = new AttackResolver(resolverHost.transform, resolverHost.GetComponent<Rigidbody2D>(), new FixedDamageRoller(), pool);
            var energy = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>("Assets/Game/ScriptableObjects/Enemies/Attacks/Attack_X7_EnergyBurst.asset");
            resolver.Begin(energy, Vector2.right);
            resolver.Tick(0.01f);
            Assert.IsTrue(resolver.SpawnedProjectiles.Count >= 1);
            Assert.AreEqual("proj_enemy_energy", resolver.SpawnedProjectiles[0].Visual.ProfileId);
            Assert.AreEqual(energy.ProjectileSpeed, resolver.SpawnedProjectiles[0].Data.Speed, "speed unchanged");
            resolver.Cancel();

            var spore = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>("Assets/Game/ScriptableObjects/Enemies/Attacks/Attack_Omega_SporeBurst.asset");
            resolver.Begin(spore, Vector2.right);
            resolver.Tick(0.01f);
            Assert.AreEqual(spore.ProjectileCount, resolver.SpawnedProjectiles.Count);
            Assert.IsTrue(resolver.SpawnedProjectiles.All(p => p.Visual.ProfileId == "proj_boss_spore" && p.Visual.IsVisible), "every spore is a visible boss spore");
        }
    }
}
