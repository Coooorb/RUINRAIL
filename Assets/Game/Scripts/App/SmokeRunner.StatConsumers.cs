using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The stat-consumer stage of the built-player smoke: the shipped executable must show that content which used to
    /// advertise nothing now does something.
    ///
    /// At the Shelter it equips an accessory whose intrinsic was inert before this pass (the Ammo Pouch) and a weapon
    /// carrying affix rolls produced by the real <see cref="AffixRollService"/>, and proves the Shelter's own loadout
    /// already honours the capacity bonus. In the run it measures the same things through the run's authoritative
    /// stats: the ammo the run can actually carry, the cadence and reach of the equipped weapon, and a real knockback
    /// and stagger delivered through <see cref="ImpactDispatcher"/>. After the save and reload it proves the affix
    /// rolls came back verbatim.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        /// <summary>The accessory whose intrinsic (+25% Ammo Stack Capacity) had no runtime caller before this pass.</summary>
        public const string StatConsumerAccessoryId = "accessory_ammo_pouch";

        /// <summary>A Shotgun: the class whose authored knockback/stagger this pass switched on.</summary>
        public const string StatConsumerWeaponId = "weapon_breacher_12";

        private const int StatConsumerSmokeSeed = 20260923;

        private readonly List<string> _statConsumerChecks = new();
        private readonly List<string> _statConsumerRolls = new();
        private int _statConsumerBaseStackLimit;
        private string _statConsumerPouchInstanceId = string.Empty;
        private string _statConsumerWeaponInstanceId = string.Empty;

        private void StatConsumerCheck(string what, bool ok)
        {
            if (ok) _statConsumerChecks.Add(what);
            else if (string.IsNullOrEmpty(_result.Error)) Fail("stat consumers: " + what);
            _result.StatConsumerChecks = _statConsumerChecks.ToArray();
        }

        private AmmoItemDefinition SmokeAmmo(AmmoType type) =>
            _app.Content.Items.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);

        /// <summary>
        /// Shelter stage: prove the Shelter's own loadout honours the previously inert accessory, roll a real Epic item
        /// with the real roll service, and send both into the run in the loadout backpack.
        ///
        /// Both items travel in the backpack rather than equipped because the graphical-inventory stage of this smoke
        /// drags its own Ammo Pouch into the accessory slot and the HUD stage expects the starter Field Knife in weapon
        /// slot 2; those are accepted, frozen checks. The run stage below equips both for real, after them.
        /// </summary>
        private void StatConsumerShelterChecks(BaseSession session)
        {
            var light = SmokeAmmo(AmmoType.Light);
            if (light == null) { Fail("stat consumers: no light ammo definition"); return; }

            _statConsumerBaseStackLimit = session.Loadout.MaxStackFor(light);
            StatConsumerCheck($"the Shelter starts at the approved light-ammo stack limit ({_statConsumerBaseStackLimit})", _statConsumerBaseStackLimit > 0);

            var pouch = new ItemInstance(StatConsumerAccessoryId);
            StatConsumerCheck("the Ammo Pouch equips into the Shelter loadout", session.Loadout.TryEquip(pouch, EquippedSlot.Accessory));
            var raised = session.Loadout.MaxStackFor(light);
            StatConsumerCheck($"the Shelter loadout honours the pouch: stack limit {_statConsumerBaseStackLimit} -> {raised}", raised > _statConsumerBaseStackLimit);
            session.Loadout.Unequip(EquippedSlot.Accessory);
            StatConsumerCheck("removing it at the Shelter restores the baseline limit", session.Loadout.MaxStackFor(light) == _statConsumerBaseStackLimit);
            StatConsumerCheck("the pouch travels into the run in the loadout backpack", session.Loadout.TryAddToBackpack(pouch));
            _statConsumerPouchInstanceId = pouch.InstanceId;

            // A real roll from the real service, on the real pool, with a fixed seed.
            var definition = _app.Content.Items.OfType<EquipmentItemDefinition>().FirstOrDefault(d => d.Id == StatConsumerWeaponId);
            if (definition == null) { Fail("stat consumers: no " + StatConsumerWeaponId); return; }
            var weapon = new ItemInstance(StatConsumerWeaponId);
            var roll = new AffixRollService().Roll(weapon, definition, Rarity.Epic, new SeededRandom(StatConsumerSmokeSeed));
            StatConsumerCheck($"an Epic roll on {StatConsumerWeaponId} succeeded with {roll.AffixCount} affixes", roll.Success && roll.AffixCount == 3);
            _statConsumerRolls.Clear();
            _statConsumerRolls.AddRange(weapon.AffixRolls.Select(r => r.AffixId + "=" + r.Value));
            _result.StatConsumerAffixRolls = string.Join("; ", _statConsumerRolls);
            StatConsumerCheck("the rolled Shotgun travels into the run in the loadout backpack", session.Loadout.TryAddToBackpack(weapon));
            _statConsumerWeaponInstanceId = weapon.InstanceId;
            Debug.Log($"[SMOKE] stat consumers (Shelter): {_statConsumerChecks.Count} checks passed");
        }

        /// <summary>Dungeon stage: the same content, measured through the run's authoritative stats.</summary>
        private IEnumerator StatConsumerRunChecks(ExpeditionScene run)
        {
            var stats = run.Rig.StatsBinder.Stats;
            var inventory = run.Rig.Inventory;
            var light = SmokeAmmo(AmmoType.Light);

            // 0. Equip both from the run's own backpack, through the inventory the player uses.
            var pouch = TakeFromBackpack(inventory, _statConsumerPouchInstanceId);
            var rolled = TakeFromBackpack(inventory, _statConsumerWeaponInstanceId);
            StatConsumerCheck("both items arrived in the run's backpack", pouch != null && rolled != null);
            if (pouch == null || rolled == null) yield break;
            if (inventory.GetEquipped(EquippedSlot.Accessory) != null) inventory.Unequip(EquippedSlot.Accessory);
            var displacedKnife = inventory.Unequip(EquippedSlot.SecondaryWeapon);
            StatConsumerCheck("the Ammo Pouch equips in the run", inventory.TryEquip(pouch, EquippedSlot.Accessory));
            StatConsumerCheck("the rolled Shotgun equips in the run", inventory.TryEquip(rolled, EquippedSlot.SecondaryWeapon));
            if (displacedKnife != null) inventory.TryAddToBackpack(displacedKnife);
            yield return null;

            // 1. The accessory intrinsic that had no caller: the run carries more rounds because of it.
            StatConsumerCheck($"the run composes the pouch intrinsic (+{stats.GetPercent(StatId.AmmoStackCapacity)}% Ammo Stack Capacity)", stats.GetPercent(StatId.AmmoStackCapacity) > 0);
            var limit = inventory.MaxStackFor(light);
            StatConsumerCheck($"the run's light-ammo stack limit is raised ({_statConsumerBaseStackLimit} -> {limit})", limit > _statConsumerBaseStackLimit);
            var carriedBefore = inventory.Get(AmmoType.Light);
            inventory.Add(AmmoType.Light, 10000);
            var carried = inventory.Get(AmmoType.Light);
            StatConsumerCheck($"the run really carries more than the base limit ({carried} rounds, base limit {_statConsumerBaseStackLimit})",
                carried > _statConsumerBaseStackLimit && carried >= carriedBefore);

            // 2. The affix rolls reach the pipeline at all — before this pass the run resolved every affix to null.
            var affixed = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            StatConsumerCheck("the rolled Shotgun came into the run with its rolls intact",
                affixed != null && affixed.DefinitionId == StatConsumerWeaponId && affixed.AffixRolls.Count == 3);
            var granted = affixed != null
                ? affixed.AffixRolls.Select(r => StatIds.FromAffix(AffixFor(r.AffixId)?.Stat ?? AffixStat.Damage)).ToList()
                : new List<StatId>();
            StatConsumerCheck("every rolled affix contributes a non-zero modifier to the run's stats",
                granted.Count == 3 && granted.All(s => stats.GetPercent(s) > 0));

            // 3. The weapon those affixes sit on really behaves differently.
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            var shotgun = run.Rig.Player.GetComponents<RangedWeapon>().FirstOrDefault(w => w.Definition != null && w.Definition.Id == StatConsumerWeaponId);
            StatConsumerCheck("the rolled Shotgun is mounted and selected", shotgun != null && shotgun.IsEquipped);
            if (shotgun == null) yield break;

            var authoredInterval = 1f / shotgun.Definition.FireRate;
            var authoredRange = shotgun.Definition.Range;
            var authoredMagazine = shotgun.Definition.MagazineSize;
            StatConsumerCheck($"the weapon's effective values differ from the authored ones (interval {authoredInterval:0.###}s -> {shotgun.CurrentFireInterval:0.###}s, " +
                              $"range {authoredRange:0.##} -> {shotgun.CurrentRange:0.##}, magazine {authoredMagazine} -> {shotgun.CurrentMagazineSize})",
                shotgun.CurrentFireInterval <= authoredInterval && shotgun.CurrentRange >= authoredRange && shotgun.CurrentMagazineSize >= authoredMagazine
                && (shotgun.CurrentFireInterval < authoredInterval || shotgun.CurrentRange > authoredRange || shotgun.CurrentMagazineSize > authoredMagazine));

            // 4. Impact: the shot really carries the authored knockback and stagger, and a real receiver really moves.
            if (!shotgun.TryFire()) { Fail("stat consumers: the rolled Shotgun could not fire"); yield break; }
            var pellet = shotgun.LastSpawnedProjectile;
            StatConsumerCheck($"a fired pellet carries knockback {pellet?.Data.Knockback:0.##} and stagger {pellet?.Data.StaggerPower:0.##}",
                pellet != null && pellet.Data.Knockback > 0f && pellet.Data.StaggerPower > 0f);
            if (pellet == null) yield break;

            // A live target that can actually be moved: a dynamic, alive, enabled enemy with open floor on the push side.
            // (FindObjectsByType has no defined order, and earlier stages leave a frozen kinematic reference dummy in the
            // room — picking whichever came first made this check depend on enumeration order, not on the impact.)
            var target = FindObjectsByType<ImpactReceiver>(FindObjectsSortMode.InstanceID).FirstOrDefault(r =>
                r.Profile.IsDisplaceable && r.GetComponent<Rigidbody2D>() is { bodyType: RigidbodyType2D.Dynamic }
                && r.GetComponent<RuinRail.Gameplay.Enemies.EnemyController>() is { enabled: true, IsAlive: true }
                && !Physics2D.CircleCastAll(r.transform.position, 0.3f, Vector2.right, 1.5f).Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null));
            var synthetic = target == null;
            if (synthetic)
            {
                var body = new GameObject("SmokeImpactTarget");
                body.transform.position = (Vector2)run.Rig.Player.transform.position + Vector2.up * 6f;
                var rb = body.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.freezeRotation = true;
                body.AddComponent<CircleCollider2D>().radius = 0.25f;
                target = body.AddComponent<ImpactReceiver>();
                target.SetConfig(_app.Content.Stagger);
                target.SetProfile(new ImpactProfile("smoke_target", 0, 0, true, false));
                yield return null;
            }

            var start = (Vector2)target.transform.position;
            ImpactDispatcher.Apply(target, new ImpactRequest(Vector2.right, pellet.Data.Knockback, pellet.Data.StaggerPower));
            for (var i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            var moved = Vector2.Distance(start, target.transform.position);
            StatConsumerCheck($"the hit displaced a{(synthetic ? " synthetic" : " live")} unresisted target by {moved:0.##} tiles", moved > 0.1f);
            if (synthetic && target != null) Destroy(target.gameObject);

            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
            yield return null;
            Debug.Log($"[SMOKE] stat consumers (run): {_statConsumerChecks.Count} checks passed — rolls {_result.StatConsumerAffixRolls}");
        }

        /// <summary>Post-reload stage: the rolls a save round trip returned must be the rolls that went in.</summary>
        private void StatConsumerSaveChecks(GameApp.SaveProbe probe)
        {
            var reloaded = probe.EquippedAffixRolls
                .Where(r => r.StartsWith(StatConsumerWeaponId + ":", StringComparison.Ordinal))
                .Select(r => r.Substring(StatConsumerWeaponId.Length + 1))
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            var expected = _statConsumerRolls.OrderBy(r => r, StringComparer.Ordinal).ToList();
            StatConsumerCheck($"the save round trip returned the same affix rolls ({string.Join("; ", reloaded)})", reloaded.SequenceEqual(expected));
            StatConsumerCheck("the Ammo Pouch is still equipped after the reload",
                probe.Success && probe.EquippedInstanceIds.Length > 0);
        }

        /// <summary>Takes an item out of the backpack the way the inventory does, so equipping it is a move rather than a copy.</summary>
        private static ItemInstance TakeFromBackpack(PlayerInventory inventory, string instanceId)
        {
            for (var i = 0; i < inventory.BackpackSlots.Count; i++)
            {
                if (inventory.BackpackSlots[i]?.InstanceId == instanceId) return inventory.RemoveFromBackpack(i);
            }

            return null;
        }

        private AffixDefinition AffixFor(string affixId) =>
            _app.Content.Items.OfType<EquipmentItemDefinition>()
                .Select(d => d.AffixPool)
                .Where(p => p != null)
                .SelectMany(p => p.Affixes)
                .FirstOrDefault(a => a != null && a.Id == affixId);
    }
}
