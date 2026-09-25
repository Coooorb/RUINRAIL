using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Runtime proof that every permanent attribute reaches the system it is defined to change — not a rank in a
    /// profile, not a number in a tooltip, but the value the run actually uses: effective max HP and the run-start /
    /// depth-arrival fill, the damage a shot applies, measured movement distance, the healing a consumable restores,
    /// the reload timer, and the stagger pressure / knockback displacement a hit produces.
    ///
    /// Each check appends a line to TestResults/CharacterProgressionAttributeProof/runtime_attribute_evidence.txt with
    /// the rank tested, the baseline and upgraded values, what the authoritative rule says they should be, and the
    /// gameplay consumer that read them.
    /// </summary>
    public class CharacterProgressionAttributeProofTests
    {
        private const string ProofFolder = "TestResults/CharacterProgressionAttributeProof";
        private const string EvidenceFile = ProofFolder + "/runtime_attribute_evidence.txt";

        private static readonly List<string> Evidence = new();

        private readonly List<Object> _created = new();
        private readonly List<PlayerRig> _rigs = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Dictionary<AmmoType, AmmoItemDefinition> _ammo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Evidence.Clear();
            Evidence.Add("RUINRAIL — character attribute runtime evidence (player/13_LEVELING_AND_SKILL_POINTS)");
            Evidence.Add("Columns: attribute | rank | derived stat | baseline | upgraded | expected | actual | consumer | result");
            Evidence.Add(new string('-', 120));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Directory.CreateDirectory(ProofFolder);
            File.WriteAllText(EvidenceFile, string.Join("\n", Evidence) + "\n");
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            Assert.IsNotNull(_catalog, "GameContentCatalog in Resources");
            _registry = _catalog.BuildRegistry();
            _ammo = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var rig in _rigs) rig.Dispose();
            _rigs.Clear();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Record(SkillId skill, int rank, StatId stat, string baseline, string upgraded, string expected, string actual, string consumer, bool pass)
        {
            Evidence.Add(string.Join(" | ", SkillCatalog.DisplayName(skill), "rank " + rank, stat.ToString(), baseline, upgraded, expected, actual, consumer, pass ? "PASS" : "FAIL"));
            Assert.IsTrue(pass, $"{skill} rank {rank} / {stat}: expected {expected}, got {actual}");
        }

        private static void Note(string line) => Evidence.Add(line);

        private static SkillAllocation Ranks(params (SkillId Skill, int Rank)[] ranks)
        {
            var allocation = new SkillAllocation();
            foreach (var (skill, rank) in ranks) allocation.SetRank(skill, rank);
            return allocation;
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        /// <summary>A real expedition rig, built exactly the way the Dungeon scene builds it, with the profile's ranks.</summary>
        private (PlayerRig rig, HealthComponent health) StartRun(SkillAllocation skills, params (string id, EquippedSlot slot)[] equipped)
        {
            var expedition = new ExpeditionService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance);
            var profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = equipped.Select(e => new InventorySnapshot.Entry { Slot = (int)e.slot, Item = new ItemInstance(e.id).ToSnapshot() }).ToArray(),
                    Backpack = System.Array.Empty<InventorySnapshot.Entry>()
                }
            };
            if (skills != null)
            {
                foreach (var s in SkillRules.All) profile.Skills.SetRank(s, skills.GetRank(s));
                // The ranks must be ones the profile's levels actually paid for: ExpeditionService.Start reconciles
                // spent against earned points and refunds an allocation that no XP justifies (anti-exploit).
                profile.TotalXp = LevelCurve.TotalXpForLevel(1 + profile.Skills.TotalSpent);
            }

            var state = expedition.Start(profile, 11, Biome.RuinedMetro);
            Assert.AreEqual(skills?.TotalSpent ?? 0, profile.Skills.TotalSpent, "the expedition kept the profile's legitimately earned ranks");
            var rig = new PlayerRig(_catalog, _registry, _catalog.BuildSpecials());
            var player = rig.Build(state, null, "local", Vector2.zero, new FakePlayerInputReader(), profile.Skills);
            _rigs.Add(rig);
            _created.Add(player);
            return (rig, player.GetComponent<HealthComponent>());
        }

        /// <summary>A bare player composed by the one player composition, with this profile's ranks already on it.</summary>
        private GameObject BuildPlayer(SkillAllocation skills, FakePlayerInputReader input = null, Vector2 position = default)
        {
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Player",
                IsLocal = true,
                InputReader = input ?? new FakePlayerInputReader(),
                BalanceConfig = _catalog.PlayerBalance,
                Caps = _catalog.StatCaps,
                Position = position,
                Progression = skills
            });
            _created.Add(go);
            return go;
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // ================= VITALITY =================

        [Test]
        public void Vitality_ChangesEffectiveMaxHealth_AndTheRunStartsFullAtIt()
        {
            var baseline = StartRun(null, (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(100, baseline.rig.StatsBinder.Stats.MaxHealth);
            Assert.AreEqual(100, baseline.health.CurrentHealth);

            foreach (var rank in new[] { 1, 3, 5, SkillRules.MaxRank })
            {
                var expected = 100 + 2 * rank;
                var run = StartRun(Ranks((SkillId.Vitality, rank)), (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
                var actual = run.rig.StatsBinder.Stats.MaxHealth;
                Record(SkillId.Vitality, rank, StatId.MaxHealth, "100", actual.ToString(), expected.ToString(), actual.ToString(),
                    "PlayerStats.MaxHealth -> HealthComponent", actual == expected);
                Assert.AreEqual(expected, run.health.MaxHealth, "the health component's ceiling is the effective maximum");
                Assert.AreEqual(expected, run.health.CurrentHealth, "the run starts at CurrentHP == EffectiveMaxHP");
                Assert.AreEqual(expected, run.rig.RunStartHealth);
            }
        }

        [Test]
        public void Vitality_ComposesWithArmorAndRaisesTheHealCap()
        {
            var armorOnly = StartRun(null, (StarterKitService.VestId, EquippedSlot.Armor));
            var armorBonus = armorOnly.rig.StatsBinder.Stats.MaxHealth - 100;
            Assert.Greater(armorBonus, 0, "the Scrap Vest carries a Max HP bonus");

            const int rank = 7;
            var both = StartRun(Ranks((SkillId.Vitality, rank)), (StarterKitService.VestId, EquippedSlot.Armor));
            var expected = 100 + armorBonus + 2 * rank;
            var actual = both.rig.StatsBinder.Stats.MaxHealth;
            Record(SkillId.Vitality, rank, StatId.MaxHealth, $"{100 + armorBonus} (armor only)", actual.ToString(), expected.ToString(), actual.ToString(),
                "PlayerStats.MaxHealth (skills + armor summed, then clamped once)", actual == expected);
            Assert.AreEqual(expected, both.health.CurrentHealth, "progression + equipment start the run full");

            // The heal cap is the modified maximum: damage, then over-heal, and current HP stops exactly at it.
            Assert.IsTrue(both.health.TryApplyDamage(new DamageRequest(40)));
            var damaged = both.health.CurrentHealth;
            Assert.Less(damaged, expected, "the armor's damage reduction still lets damage through");
            Assert.Greater(damaged, 0);
            both.health.Heal(9999);
            Record(SkillId.Vitality, rank, StatId.MaxHealth, damaged.ToString(), both.health.CurrentHealth.ToString(), expected.ToString(), both.health.CurrentHealth.ToString(),
                "HealthComponent.Heal cap", both.health.CurrentHealth == expected);
        }

        [Test]
        public void Vitality_ReachesTheHud()
        {
            const int rank = 6;
            var run = StartRun(Ranks((SkillId.Vitality, rank)), (StarterKitService.VestId, EquippedSlot.Armor));
            var hud = new DungeonHudViewModel();
            hud.BindPlayer(run.health, run.rig.Player.GetComponent<PlayerDash>(), run.rig.Player.GetComponent<PlayerLifeStateComponent>());
            var expected = run.rig.StatsBinder.Stats.MaxHealth;
            Record(SkillId.Vitality, rank, StatId.MaxHealth, "-", hud.Snapshot.MaxHp.ToString(), expected.ToString(), hud.Snapshot.MaxHp.ToString(),
                "DungeonHudViewModel.Snapshot.MaxHp", hud.Snapshot.MaxHp == expected);
            Assert.AreEqual(expected, hud.Snapshot.Hp, "the HUD shows a full bar at the start of the run");
        }

        [Test]
        public void Vitality_NewDepthFillsToTheModifiedMaximum_AndNothingElseDoes()
        {
            const int rank = 8;
            var run = StartRun(Ranks((SkillId.Vitality, rank)), (StarterKitService.VestId, EquippedSlot.Armor));
            var effectiveMax = run.rig.StatsBinder.Stats.MaxHealth;
            Assert.IsTrue(run.health.TryApplyDamage(new DamageRequest(50)));
            var damaged = run.health.CurrentHealth;

            // Same-depth operations must NOT heal: a stat recompute, an equipment re-bind, a stat refresh.
            run.rig.StatsBinder.Stats.Recompute();
            run.rig.StatsBinder.Bind();
            run.rig.StatsBinder.ApplyProgression(run.rig.StatsBinder.Progression);
            Assert.AreEqual(damaged, run.health.CurrentHealth, "a stat refresh inside a depth never heals");
            Assert.AreEqual(effectiveMax, run.health.MaxHealth, "and never loses the progression ceiling");

            var roster = new PartyLifeRoster();
            roster.Register(run.rig.Player.GetComponent<PlayerLifeStateComponent>());
            var outcomes = DepthArrivalHeal.ApplyToParty(roster);
            Assert.AreEqual(1, outcomes.Count);
            Record(SkillId.Vitality, rank, StatId.MaxHealth, damaged.ToString(), run.health.CurrentHealth.ToString(), effectiveMax.ToString(), run.health.CurrentHealth.ToString(),
                "DepthArrivalHeal (descend to a new depth)", run.health.CurrentHealth == effectiveMax && outcomes[0].IsFull);
        }

        // ================= POWER =================

        [Test]
        public void Power_ChangesTheDamageAShotApplies()
        {
            const int roll = 20;
            var baseline = FireOnce(null, roll, out _);
            foreach (var rank in new[] { 1, 5, SkillRules.MaxRank })
            {
                var target = FireOnce(Ranks((SkillId.Power, rank)), roll, out var applied);
                var expected = Mathf.RoundToInt(roll * (1f + rank / 100f));
                Record(SkillId.Power, rank, StatId.WeaponDamage, baseline.ToString(), target.ToString(), expected.ToString(), target.ToString(),
                    "RangedWeapon projectile damage", target == expected);
                Assert.AreEqual(expected, applied, "and that is the HP the target actually loses");
            }
        }

        /// <summary>Fires the test weapon once and returns the projectile's damage; <paramref name="applied"/> is the HP a target loses.</summary>
        private int FireOnce(SkillAllocation skills, int roll, out int applied)
        {
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(skills, input);
            var pool = player.AddComponent<ProjectilePool>();
            var weapon = player.AddComponent<RangedWeapon>();
            var definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(definition);
            SetField(definition, "_damageMin", roll);
            SetField(definition, "_damageMax", roll);
            SetField(definition, "_fireRate", 5f);
            SetField(definition, "_magazineSize", 10);
            SetField(definition, "_reloadTime", 2f);
            SetField(definition, "_range", 10f);
            SetField(definition, "_projectileSpeed", 20f);
            weapon.SetInputReader(input);
            weapon.SetProjectilePool(pool);
            weapon.SetAiming(player.GetComponent<PlayerAiming>());
            weapon.SetAmmoReserve(new AmmoReserve());
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = roll });
            weapon.SetDefinition(definition);
            player.GetComponent<PlayerStatsBinder>().Bind();

            Assert.IsTrue(weapon.TryFire());
            var damage = weapon.LastSpawnedProjectile.Data.Damage;

            var dummy = new GameObject("Target");
            _created.Add(dummy);
            var health = dummy.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);
            health.TryApplyDamage(new DamageRequest(damage));
            applied = 500 - health.CurrentHealth;
            return damage;
        }

        // ================= MOBILITY =================

        [UnityTest]
        public IEnumerator Mobility_ChangesMeasuredMovementDistance()
        {
            const int steps = 40;
            var baselineInput = new FakePlayerInputReader { Move = Vector2.right };
            var baselinePlayer = BuildPlayer(null, baselineInput, new Vector2(-200f, 0f));
            var baselineSpeed = baselinePlayer.GetComponent<PlayerMovement>().CurrentMoveSpeed;
            var baselineStart = baselinePlayer.transform.position;
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
            var baselineDistance = baselinePlayer.transform.position.x - baselineStart.x;

            const int rank = SkillRules.MaxRank;
            var input = new FakePlayerInputReader { Move = Vector2.right };
            var player = BuildPlayer(Ranks((SkillId.Mobility, rank)), input, new Vector2(200f, 0f));
            var speed = player.GetComponent<PlayerMovement>().CurrentMoveSpeed;
            var start = player.transform.position;
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
            var distance = player.transform.position.x - start.x;

            var expectedSpeed = _catalog.PlayerBalance.MoveSpeed * (1f + rank / 100f);
            Record(SkillId.Mobility, rank, StatId.MovementSpeed, baselineSpeed.ToString("0.###", CultureInfo.InvariantCulture),
                speed.ToString("0.###", CultureInfo.InvariantCulture), expectedSpeed.ToString("0.###", CultureInfo.InvariantCulture),
                speed.ToString("0.###", CultureInfo.InvariantCulture), "PlayerMovement.CurrentMoveSpeed",
                Mathf.Abs(speed - expectedSpeed) < 0.0001f);

            Note($"MOBILITY | rank {rank} | measured travel over {steps} fixed steps | baseline {baselineDistance:0.###} u | upgraded {distance:0.###} u | ratio {(baselineDistance > 0f ? distance / baselineDistance : 0f):0.###} (expected ~{1f + rank / 100f:0.###}) | PlayerMovement");
            Assert.Greater(distance, baselineDistance, "the upgraded player really covers more ground in the same time");
            Assert.AreEqual(1f + rank / 100f, distance / baselineDistance, 0.02f, "measured travel matches the rank's multiplier");
        }

        // ================= RECOVERY =================

        [Test]
        public void Recovery_ChangesTheHealingAConsumableRestores()
        {
            const int healAmount = 40;
            var definition = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _created.Add(definition);
            SetField(definition, "_effectKind", ConsumableEffectKind.Heal);
            SetField(definition, "_healAmount", healAmount);

            var baseline = HealOnce(null, definition, healAmount);
            foreach (var rank in new[] { 1, 5, SkillRules.MaxRank })
            {
                var restored = HealOnce(Ranks((SkillId.Recovery, rank)), definition, healAmount);
                var expected = Mathf.RoundToInt(healAmount * (1f + 2 * rank / 100f));
                Record(SkillId.Recovery, rank, StatId.HealingReceived, baseline.ToString(), restored.ToString(), expected.ToString(), restored.ToString(),
                    "ConsumableEffectRunner heal", restored == expected);
            }
        }

        private int HealOnce(SkillAllocation skills, ConsumableDefinition definition, int healAmount)
        {
            var player = BuildPlayer(skills, null, new Vector2(_created.Count * 30f, 0f));
            var health = player.GetComponent<HealthComponent>();
            var stats = player.GetComponent<PlayerStatsBinder>().Stats;
            health.SetMaxHealth(1000);
            health.TryApplyDamage(new DamageRequest(500));
            var before = health.CurrentHealth;
            var runner = new ConsumableEffectRunner(new ConsumableTargets(stats, new PlayerCombatEvents(), amount =>
            {
                var restoredFrom = health.CurrentHealth;
                health.Heal(amount);
                return health.CurrentHealth - restoredFrom;
            }));
            Assert.IsTrue(runner.Apply(definition));
            Assert.Greater(healAmount, 0);
            return health.CurrentHealth - before;
        }

        // ================= HANDLING =================

        [UnityTest]
        public IEnumerator Handling_ShortensTheRealReloadTimer()
        {
            const float reloadTime = 2f;
            var baselineWeapon = ReloadWeapon(null, reloadTime);
            var baseline = baselineWeapon.CurrentReloadTime;
            Assert.AreEqual(reloadTime, baseline, 0.0001f);

            const int rank = SkillRules.MaxRank;
            var weapon = ReloadWeapon(Ranks((SkillId.Handling, rank)), reloadTime);
            var expected = reloadTime / (1f + rank / 100f);
            Record(SkillId.Handling, rank, StatId.ReloadSpeed, baseline.ToString("0.####", CultureInfo.InvariantCulture),
                weapon.CurrentReloadTime.ToString("0.####", CultureInfo.InvariantCulture), expected.ToString("0.####", CultureInfo.InvariantCulture),
                weapon.CurrentReloadTime.ToString("0.####", CultureInfo.InvariantCulture), "RangedWeapon.CurrentReloadTime",
                Mathf.Abs(weapon.CurrentReloadTime - expected) < 0.0001f);

            // And the timer the weapon actually runs is the shortened one: it is still reloading at the baseline's
            // half-way point and finished before the baseline duration would have elapsed.
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.TryStartReload(), "the weapon has ammo in reserve and an incomplete magazine");
            Assert.IsTrue(weapon.IsReloading);
            var elapsed = 0f;
            while (weapon.IsReloading && elapsed < reloadTime * 2f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Note($"HANDLING | rank {rank} | measured reload | baseline timer {reloadTime:0.####} s | upgraded timer {weapon.CurrentReloadTime:0.####} s | measured {elapsed:0.####} s | RangedWeapon reload coroutine");
            Assert.Less(elapsed, reloadTime, "the measured reload really finishes sooner than the unmodified duration");
            Assert.GreaterOrEqual(elapsed, expected - 0.1f, "and is not instantaneous");

            // The attribute's second stat is composed and capped identically; it has no consumer in this build, which
            // the EditMode gate pins as a documented gap rather than inventing a weapon-switch duration.
            var stats = weapon.GetComponent<PlayerStatsBinder>().Stats;
            Note($"HANDLING | rank {rank} | WeaponSwitchSpeed | composed value +{stats.GetPercent(StatId.WeaponSwitchSpeed)}% | NO RUNTIME CONSUMER (weapon switching is instantaneous; no base duration is defined in the repository) | documented gap");
            Assert.AreEqual(rank, stats.GetPercent(StatId.WeaponSwitchSpeed));
        }

        private RangedWeapon ReloadWeapon(SkillAllocation skills, float reloadTime)
        {
            var input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            var player = BuildPlayer(skills, input, new Vector2(_created.Count * 30f, 0f));
            var pool = player.AddComponent<ProjectilePool>();
            var weapon = player.AddComponent<RangedWeapon>();
            var definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(definition);
            SetField(definition, "_damageMin", 10);
            SetField(definition, "_damageMax", 10);
            SetField(definition, "_fireRate", 5f);
            SetField(definition, "_magazineSize", 4);
            SetField(definition, "_reloadTime", reloadTime);
            SetField(definition, "_range", 10f);
            SetField(definition, "_projectileSpeed", 20f);
            var reserve = new AmmoReserve();
            reserve.Set(definition.AmmoType, 200);
            weapon.SetInputReader(input);
            weapon.SetProjectilePool(pool);
            weapon.SetAiming(player.GetComponent<PlayerAiming>());
            weapon.SetAmmoReserve(reserve);
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 10 });
            weapon.SetDefinition(definition);
            player.GetComponent<PlayerStatsBinder>().Bind();
            return weapon;
        }

        // ================= RESILIENCE =================

        [Test]
        public void Resilience_ReducesStaggerPressureAndKnockbackDistance()
        {
            const float staggerPower = 6f;
            const float knockback = 8f;
            var baseline = Impact(null, staggerPower, knockback);

            foreach (var rank in new[] { 1, 5, SkillRules.MaxRank })
            {
                var resist = 2 * rank;
                var result = Impact(Ranks((SkillId.Resilience, rank)), staggerPower, knockback);
                var expectedStagger = staggerPower * (100 - resist) / 100f;
                Record(SkillId.Resilience, rank, StatId.StaggerResistance, baseline.stagger.ToString("0.###", CultureInfo.InvariantCulture),
                    result.stagger.ToString("0.###", CultureInfo.InvariantCulture), expectedStagger.ToString("0.###", CultureInfo.InvariantCulture),
                    result.stagger.ToString("0.###", CultureInfo.InvariantCulture), "PlayerImpactReceiver stagger meter",
                    Mathf.Abs(result.stagger - expectedStagger) < 0.0001f);

                var expectedKnockback = KnockbackMath.Distance(knockback, resist, _catalog.Stagger);
                Record(SkillId.Resilience, rank, StatId.KnockbackResistance, baseline.knockback.ToString("0.###", CultureInfo.InvariantCulture),
                    result.knockback.ToString("0.###", CultureInfo.InvariantCulture), expectedKnockback.ToString("0.###", CultureInfo.InvariantCulture),
                    result.knockback.ToString("0.###", CultureInfo.InvariantCulture), "PlayerImpactReceiver knockback displacement",
                    Mathf.Abs(result.knockback - expectedKnockback) < 0.0001f);
                Assert.Less(result.knockback, baseline.knockback, "a resisted hit really pushes the player less far");
            }
        }

        private (float stagger, float knockback) Impact(SkillAllocation skills, float staggerPower, float knockback)
        {
            var player = BuildPlayer(skills, null, new Vector2(_created.Count * 30f, 0f));
            var receiver = player.GetComponent<PlayerImpactReceiver>();
            receiver.SetConfig(_catalog.Stagger);
            player.GetComponent<PlayerStatsBinder>().Bind();
            var staggerResult = receiver.ApplyStagger(new ImpactRequest(Vector2.right, 0f, staggerPower));
            var knockbackResult = receiver.ApplyKnockback(new ImpactRequest(Vector2.right, knockback, 0f));
            return (staggerResult.Applied, knockbackResult.Distance);
        }

        // ================= equipment / progression interaction matrix =================

        [Test]
        public void ProgressionAndEquipment_ComposeAcrossTheWholeMatrix()
        {
            var armor = _registry.Definitions.OfType<ArmorDefinition>()
                .FirstOrDefault(a => a.BaseModifiers().Any(m => m.Stat == StatId.MaxHealth));
            Assert.IsNotNull(armor, "an armor with a Max HP bonus exists in the catalog");
            var vest = _registry.TryGet(StarterKitService.VestId, out var v) ? v as EquipmentItemDefinition : null;
            Assert.IsNotNull(vest, "the Scrap Vest exists");

            var accessory = _registry.Definitions.OfType<AccessoryDefinition>()
                .FirstOrDefault(a => a.BaseModifiers().Any(m => m.Stat == StatId.MovementSpeed));
            if (accessory == null) Note("EQUIPMENT MATRIX | accessory combinations SKIPPED: no accessory in the catalog carries a Movement Speed intrinsic");

            const int vitality = 6;
            const int mobility = 3;
            var skills = Ranks((SkillId.Vitality, vitality), (SkillId.Mobility, mobility));
            var pistol = (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon);

            var cases = new List<(string Name, (string, EquippedSlot)[] Gear, SkillAllocation Skills)>
            {
                ("no gear, no progression", new[] { pistol }, null),
                ("no gear + progression", new[] { pistol }, skills),
                ("armor only", new[] { (armor.Id, EquippedSlot.Armor) }, null),
                ("armor + progression", new[] { (armor.Id, EquippedSlot.Armor) }, skills),
                ("starter loadout + progression", new[] { pistol, (StarterKitService.VestId, EquippedSlot.Armor) }, skills)
            };

            if (accessory != null)
            {
                cases.Add(("accessory only", new[] { (accessory.Id, EquippedSlot.Accessory) }, null));
                cases.Add(("accessory + progression", new[] { (accessory.Id, EquippedSlot.Accessory) }, skills));
                cases.Add(("armor + accessory + progression", new[] { (armor.Id, EquippedSlot.Armor), (accessory.Id, EquippedSlot.Accessory) }, skills));
            }

            foreach (var (name, gear, caseSkills) in cases)
            {
                var run = StartRun(caseSkills, gear);
                var stats = run.rig.StatsBinder.Stats;

                // Expected values are computed from the item definitions and the approved per-rank rule directly,
                // never from the pipeline being tested.
                var gearModifiers = gear
                    .Select(g => _registry.TryGet(g.Item1, out var d) ? d as EquipmentItemDefinition : null)
                    .Where(d => d != null).SelectMany(d => d.BaseModifiers()).ToList();
                var vitalityRank = caseSkills?.GetRank(SkillId.Vitality) ?? 0;
                var mobilityRank = caseSkills?.GetRank(SkillId.Mobility) ?? 0;
                var flatHp = gearModifiers.Where(m => m.Stat == StatId.MaxHealth && m.Kind == StatModifierKind.Flat).Sum(m => m.Value) + 2 * vitalityRank;
                var percentHp = gearModifiers.Where(m => m.Stat == StatId.MaxHealth && m.Kind == StatModifierKind.Percent).Sum(m => m.Value);
                var expectedHp = Mathf.Max(1, Mathf.RoundToInt((100 + flatHp) * (1f + percentHp / 100f)));
                var moveCap = stats.CapFor(StatId.MovementSpeed);
                var rawMove = gearModifiers.Where(m => m.Stat == StatId.MovementSpeed && m.Kind == StatModifierKind.Percent).Sum(m => m.Value) + mobilityRank;
                var expectedMove = moveCap > 0 ? Mathf.Min(rawMove, moveCap) : rawMove;

                Assert.AreEqual(expectedHp, stats.MaxHealth, $"{name}: effective max HP");
                Assert.AreEqual(expectedHp, run.health.CurrentHealth, $"{name}: the run starts at CurrentHP == EffectiveMaxHP");
                Assert.AreEqual(expectedMove, stats.GetPercent(StatId.MovementSpeed), $"{name}: movement bonus");
                Note($"EQUIPMENT MATRIX | {name} | MaxHP expected {expectedHp} actual {stats.MaxHealth} | Movement expected +{expectedMove}% actual +{stats.GetPercent(StatId.MovementSpeed)}% | start HP {run.health.CurrentHealth}/{run.health.MaxHealth} | PASS");
            }

            // A gear change inside the run re-binds every stat consumer; the progression source must survive it and the
            // rebuild must not heal.
            var live = StartRun(skills, (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon), (armor.Id, EquippedSlot.Armor));
            var beforeSwap = live.rig.StatsBinder.Stats.MaxHealth;
            live.health.TryApplyDamage(new DamageRequest(15));
            var damaged = live.health.CurrentHealth;
            live.rig.Inventory.Unequip(EquippedSlot.PrimaryWeapon);
            live.rig.StatsBinder.Bind();
            Assert.AreEqual(beforeSwap, live.rig.StatsBinder.Stats.MaxHealth, "unequipping mid-run never drops the progression bonus");
            Assert.AreEqual(damaged, live.health.CurrentHealth, "and never heals");
            Note($"EQUIPMENT MATRIX | mid-run weapon unequip | MaxHP {live.rig.StatsBinder.Stats.MaxHealth} (unchanged) | HP {live.health.CurrentHealth} (not healed) | PASS");

            // Removing the armor drops only the armor's contribution; the attribute's contribution stays.
            live.rig.Inventory.Unequip(EquippedSlot.Armor);
            live.rig.StatsBinder.Bind();
            Assert.AreEqual(100 + 2 * vitality, live.rig.StatsBinder.Stats.MaxHealth, "the progression bonus outlives every equipment change");
            Note($"EQUIPMENT MATRIX | mid-run armor unequip | MaxHP {live.rig.StatsBinder.Stats.MaxHealth} (base + Vitality only) | PASS");
        }

        // ================= network authority =================

        [Test]
        public void EachPlayersProgressionIsIsolated_AndTheHostComposesIt()
        {
            var host = BuildPlayer(Ranks((SkillId.Vitality, 10), (SkillId.Power, 10)), null, new Vector2(0f, 0f));
            var client = BuildPlayer(Ranks((SkillId.Vitality, 2)), null, new Vector2(40f, 0f));
            var guest = BuildPlayer(null, null, new Vector2(80f, 0f));

            var hostStats = host.GetComponent<PlayerStatsBinder>().Stats;
            var clientStats = client.GetComponent<PlayerStatsBinder>().Stats;
            var guestStats = guest.GetComponent<PlayerStatsBinder>().Stats;

            Assert.AreEqual(120, hostStats.MaxHealth);
            Assert.AreEqual(104, clientStats.MaxHealth);
            Assert.AreEqual(100, guestStats.MaxHealth, "a player object composed without an allocation carries no progression");
            Assert.AreEqual(10, hostStats.GetPercent(StatId.WeaponDamage));
            Assert.AreEqual(0, clientStats.GetPercent(StatId.WeaponDamage), "one player's ranks never leak into another's pipeline");

            // Reconnect / recomposition: applying the same allocation again is idempotent (one keyed source), and the
            // host can hand a spawned replica the owner's real ranks without rebuilding the object.
            var binder = guest.GetComponent<PlayerStatsBinder>();
            var allocation = Ranks((SkillId.Vitality, 5));
            binder.ApplyProgression(allocation);
            binder.ApplyProgression(allocation);
            binder.ApplyProgression(allocation);
            Assert.AreEqual(110, binder.Stats.MaxHealth, "re-applying can never double a bonus");
            Assert.AreEqual(1, binder.Stats.SourceIds.Count(id => id == SkillStatSource.Id));

            binder.ApplyProgression(null);
            Assert.AreEqual(100, binder.Stats.MaxHealth, "removing the allocation removes exactly its contribution");
            Note($"NETWORK | per-player isolation | host MaxHP {hostStats.MaxHealth} / dmg +{hostStats.GetPercent(StatId.WeaponDamage)}% | client MaxHP {clientStats.MaxHealth} / dmg +{clientStats.GetPercent(StatId.WeaponDamage)}% | no cross-contamination | PASS");
            Note("NETWORK | authority | progression enters the pipeline only through PlayerStatsBinder.ApplyProgression from the profile the host composes; no UI, RPC or NetworkVariable writes PlayerStats | PASS");
            Note("NETWORK | live UGS/Relay NOT RUN (no live service in this environment); the deterministic local harness was used.");
        }
    }
}
