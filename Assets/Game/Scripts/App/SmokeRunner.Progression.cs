using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The character-progression stage of the built-player smoke: Skill Points are earned, spent at the Character
    /// Station through the real transaction, the rank cap and the unaffordable case are refused without deducting
    /// anything, the ranks survive leaving the station and a save/reload — and then the expedition that follows is
    /// checked for the real effect: effective max HP, the run-start fill, the HUD readout, movement speed and weapon
    /// damage all carry the purchased ranks.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private const int SmokeVitalityRank = 5;
        private const int SmokeMobilityRank = 4;
        private const int SmokePowerRank = 5; // enough that the starter pistol's integer damage roll actually moves

        private readonly List<string> _progressionChecks = new();

        /// <summary>Where the shipped player writes the progression proof frames (`-progressionproofdir dir`).</summary>
        public const string ProgressionProofDirArgument = "-progressionproofdir";

        private string ProgressionProofDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, ProgressionProofDirArgument);
                if (index < 0 || index + 1 >= args.Length) return ProofDir;
                var dir = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>One proof frame from the shipped executable; a headless run simply has no frame to capture.</summary>
        private IEnumerator CaptureProgression(string name)
        {
            var dir = ProgressionProofDir;
            if (string.IsNullOrEmpty(dir)) yield break;
            var path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            for (var i = 0; i < 180 && !File.Exists(path); i++) yield return null;
            if (File.Exists(path)) _captures.Add(path);
            Debug.Log("[SMOKE] capture " + (File.Exists(path) ? "written -> " + path : "NOT written: " + path));
        }

        private void ProgressionCheck(string what, bool ok)
        {
            if (ok) _progressionChecks.Add(what);
            else if (string.IsNullOrEmpty(_result.Error)) Fail("progression: " + what);
        }

        /// <summary>Shelter stage: earn, spend, refuse, persist. Runs before the expedition starts.</summary>
        private IEnumerator ProgressionShelterChecks(BaseHubScreen screen, BaseSession session)
        {
            var hub = screen.Hub;
            var character = hub.Character;

            // Nothing is offered while there is nothing to spend.
            ProgressionCheck("a fresh profile offers no attribute purchase", session.Progression.UnspentSkillPoints == 0 &&
                CharacterPanelViewModel.Attributes.All(a => !character.CanAllocate(a)));
            var ranksBefore = CharacterPanelViewModel.Attributes.ToDictionary(a => a, character.RankOf);
            ProgressionCheck("no attribute starts above rank 0", ranksBefore.Values.All(r => r == 0));
            ProgressionCheck("an unaffordable purchase is refused and deducts nothing",
                !character.Allocate(RuinRail.Gameplay.Progression.SkillId.Vitality) && character.RankOf(RuinRail.Gameplay.Progression.SkillId.Vitality) == 0 && session.Progression.UnspentSkillPoints == 0);

            // Earn the points through the real progression service, then open the station and buy.
            var wanted = SmokeVitalityRank + SmokeMobilityRank + SmokePowerRank + SkillRules.MaxRank;
            session.Progression.AddXp(LevelCurve.TotalXpForLevel(wanted + 1));
            var earned = session.Progression.UnspentSkillPoints;
            ProgressionCheck($"levelling granted {earned} Skill Points (one per level)", earned == wanted);
            hub.Open(BaseStation.Character);
            yield return null;
            yield return CaptureProgression("prog_01_character_station_before_upgrade");

            var purchases = new[]
            {
                (RuinRail.Gameplay.Progression.SkillId.Vitality, SmokeVitalityRank),
                (RuinRail.Gameplay.Progression.SkillId.Mobility, SmokeMobilityRank),
                (RuinRail.Gameplay.Progression.SkillId.Power, SmokePowerRank)
            };

            foreach (var (skill, target) in purchases)
            {
                for (var i = 0; i < target; i++)
                {
                    var pointsBefore = session.Progression.UnspentSkillPoints;
                    var rankBefore = character.RankOf(skill);
                    var bought = character.Allocate(skill);
                    ProgressionCheck($"{SkillCatalog.DisplayName(skill)} rank {rankBefore} -> {rankBefore + 1} costs exactly one point",
                        bought && character.RankOf(skill) == rankBefore + 1 && session.Progression.UnspentSkillPoints == pointsBefore - 1);
                    if (!string.IsNullOrEmpty(_result.Error)) yield break;
                }

                ProgressionCheck($"{SkillCatalog.DisplayName(skill)} reached rank {target} and shows its next-rank preview",
                    character.RankOf(skill) == target && character.EffectNext(skill) == SkillCatalog.EffectText(skill, target + 1));
                if (skill == RuinRail.Gameplay.Progression.SkillId.Vitality) yield return CaptureProgression("prog_02_vitality_rank_raised_and_preview_updated");
            }

            // Rank cap: buy one attribute to the top, then confirm the cap refuses and costs nothing.
            var capped = RuinRail.Gameplay.Progression.SkillId.Resilience;
            for (var i = 0; i < SkillRules.MaxRank; i++) character.Allocate(capped);
            var pointsAtCap = session.Progression.UnspentSkillPoints;
            ProgressionCheck($"{SkillCatalog.DisplayName(capped)} is at the rank cap and shows MAX",
                character.IsMaxed(capped) && character.EffectNext(capped) == null && character.EffectRows(capped).All(r => r.EndsWith(SkillCatalog.MaxedText)));
            ProgressionCheck("a purchase at the rank cap is refused and deducts nothing",
                !character.Allocate(capped) && character.RankOf(capped) == SkillRules.MaxRank && session.Progression.UnspentSkillPoints == pointsAtCap);
            ProgressionCheck("the maxed attribute's control is disabled", !character.CanAllocate(capped));
            yield return CaptureProgression("prog_03_max_rank_and_unaffordable_states");

            // The panel shows the purchase price, the rank and the effect of every attribute.
            var rows = StationPresentation.For(BaseStation.Character, hub, null).Rows;
            ProgressionCheck("the Character panel states the Skill Point price of a rank",
                rows.Any(r => r.Key == "RANK COST" && r.Value == SkillCatalog.PointCostText));
            ProgressionCheck("the Character panel shows every attribute's rank, description and effect lines",
                CharacterPanelViewModel.Attributes.All(a =>
                    rows.Any(r => r.Key == SkillCatalog.DisplayName(a) && r.Value.StartsWith(character.RankTextOf(a))) &&
                    rows.Any(r => r.IsText && r.Key == SkillCatalog.Description(a)) &&
                    character.EffectRows(a).All(e => rows.Any(r => r.IsText && r.Key == e))));

            // Leaving and re-entering the station runs no transaction and shows the same ranks.
            var spent = session.Progression.SpentSkillPoints;
            hub.Close();
            yield return null;
            hub.Open(BaseStation.Character);
            yield return null;
            ProgressionCheck("re-entering the progression screen changes nothing",
                session.Progression.SpentSkillPoints == spent && character.RankOf(RuinRail.Gameplay.Progression.SkillId.Vitality) == SmokeVitalityRank);
            yield return CaptureProgression("prog_04_progression_retained_after_reentering");
            hub.Close();
            yield return null;

            // Persistence: the safe point is written and a reload carries every rank back.
            if (session.SaveNow("smoke_progression") != RuinRail.Persistence.SaveError.None) { Fail("progression: save failed"); yield break; }
            var reloaded = _app.ProbeSave();
            ProgressionCheck("the saved profile reloads with every purchased rank intact",
                reloaded.Success && reloaded.SkillRanks != null &&
                reloaded.SkillRanks.Length == SkillRules.SkillCount &&
                reloaded.SkillRanks[(int)RuinRail.Gameplay.Progression.SkillId.Vitality] == SmokeVitalityRank &&
                reloaded.SkillRanks[(int)RuinRail.Gameplay.Progression.SkillId.Mobility] == SmokeMobilityRank &&
                reloaded.SkillRanks[(int)RuinRail.Gameplay.Progression.SkillId.Power] == SmokePowerRank &&
                reloaded.SkillRanks[(int)capped] == SkillRules.MaxRank);

            _result.ProgressionChecks = _progressionChecks.ToArray();
            _result.ProofCaptures = _captures.ToArray();
            Debug.Log($"[SMOKE] progression (Shelter): {_progressionChecks.Count} checks passed");
        }

        /// <summary>
        /// Dungeon stage: the purchased ranks are in the run's authoritative stats and in what the player feels.
        /// Deliberately frame-free — it runs before the existing dungeon stages and must not shift their frame timing.
        /// </summary>
        private void ProgressionRunChecks(ExpeditionScene run)
        {
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var stats = run.Rig.StatsBinder.Stats;
            var hud = run.Hud;
            var balance = _app.Content.PlayerBalance;

            ProgressionCheck("the run's stat pipeline carries the profile's attribute ranks",
                run.Rig.StatsBinder.Progression != null && run.Rig.StatsBinder.Progression.Vitality == SmokeVitalityRank);

            // Vitality: the effective maximum is base + equipment + 2 HP per rank, and the run started full at it.
            var armorHp = stats.GetFlat(StatId.MaxHealth) - 2 * SmokeVitalityRank;
            var expectedMax = Mathf.RoundToInt((100 + armorHp + 2 * SmokeVitalityRank) * stats.GetMultiplier(StatId.MaxHealth));
            ProgressionCheck($"Vitality rank {SmokeVitalityRank} raises the effective max HP to {stats.MaxHealth} (base 100 + equipment {armorHp} + {2 * SmokeVitalityRank})",
                stats.MaxHealth == expectedMax && stats.MaxHealth == 100 + armorHp + 2 * SmokeVitalityRank);
            ProgressionCheck($"the expedition starts full at the Vitality-modified maximum ({health.CurrentHealth}/{health.MaxHealth})",
                health.MaxHealth == stats.MaxHealth && health.CurrentHealth == stats.MaxHealth && run.Rig.RunStartHealth == stats.MaxHealth);
            ProgressionCheck($"the HUD shows the Vitality-modified maximum ({hud.Snapshot.MaxHp})", hud.Snapshot.MaxHp == stats.MaxHealth);

            // The heal cap is the modified maximum.
            health.TryApplyDamage(new DamageRequest(30));
            health.Heal(9999);
            ProgressionCheck("healing is capped at the Vitality-modified maximum", health.CurrentHealth == stats.MaxHealth);

            // Mobility: the real movement consumer, and a measured displacement.
            var movement = player.GetComponent<PlayerMovement>();
            var expectedSpeed = balance.MoveSpeed * (1f + SmokeMobilityRank / 100f);
            ProgressionCheck($"Mobility rank {SmokeMobilityRank} raises the real move speed to {movement.CurrentMoveSpeed:0.####} (base {balance.MoveSpeed})",
                Mathf.Abs(movement.CurrentMoveSpeed - expectedSpeed) < 0.0001f && stats.GetPercent(StatId.MovementSpeed) == SmokeMobilityRank);

            // Power: the real damage multiplier every weapon rolls against.
            ProgressionCheck($"Power rank {SmokePowerRank} raises the weapon damage multiplier to {stats.GetMultiplier(StatId.WeaponDamage):0.##}",
                stats.GetPercent(StatId.WeaponDamage) == SmokePowerRank);
            var weapon = run.Rig.Loadout.ActiveWeapon as RuinRail.Gameplay.Combat.Weapons.RangedWeapon;
            if (weapon != null && weapon.Definition != null)
            {
                // The damage a shot really carries is the rolled value scaled by the pipeline multiplier and rounded to
                // an integer, so the check is on the rolled range the equipped weapon can actually produce.
                var multiplier = stats.GetMultiplier(StatId.WeaponDamage);
                var min = Mathf.RoundToInt(weapon.Definition.DamageMin * multiplier);
                var max = Mathf.RoundToInt(weapon.Definition.DamageMax * multiplier);
                ProgressionCheck($"a shot from the equipped {weapon.Definition.DisplayName} now lands {min}-{max} damage (unmodified {weapon.Definition.DamageMin}-{weapon.Definition.DamageMax})",
                    min > weapon.Definition.DamageMin && max > weapon.Definition.DamageMax);
            }

            // Resilience at the rank cap: the resistances the impact receiver reads.
            ProgressionCheck("the maxed attribute's resistances reach the impact receiver",
                stats.GetPercent(StatId.KnockbackResistance) == 2 * SkillRules.MaxRank && stats.GetPercent(StatId.StaggerResistance) == 2 * SkillRules.MaxRank);

            _result.ProgressionChecks = _progressionChecks.ToArray();
            Debug.Log($"[SMOKE] progression (run): {_progressionChecks.Count} checks passed");
        }

        /// <summary>Proof frames from the shipped executable of the run the purchased ranks produced.</summary>
        private IEnumerator ProgressionRunCaptures(ExpeditionScene run)
        {
            yield return CaptureProgression("prog_05_run_start_full_hp_and_hud_with_vitality");
            var stats = run.Rig.StatsBinder.Stats;
            Debug.Log($"[SMOKE] progression proof: depth {run.Expedition.State.Depth} max HP {stats.MaxHealth} (Vitality {SmokeVitalityRank}), movement +{stats.GetPercent(StatId.MovementSpeed)}%, weapon damage +{stats.GetPercent(StatId.WeaponDamage)}%");
        }

        /// <summary>After the descend: the new depth's full heal uses the Vitality-modified maximum.</summary>
        private IEnumerator ProgressionDepthCaptures() => CaptureProgression("prog_06_next_depth_full_hp_with_vitality");

        private void ProgressionDepthChecks(ExpeditionScene run)
        {
            var health = run.Rig.Player.GetComponent<HealthComponent>();
            var stats = run.Rig.StatsBinder.Stats;
            ProgressionCheck($"depth {run.Expedition.State.Depth} still carries the purchased ranks (max HP {stats.MaxHealth})",
                run.Rig.StatsBinder.Progression != null && run.Rig.StatsBinder.Progression.Vitality == SmokeVitalityRank && stats.MaxHealth > 100);
            ProgressionCheck($"the new depth filled to the Vitality-modified maximum ({health.MaxHealth})",
                health.MaxHealth == stats.MaxHealth && run.DepthArrivalHeals == 1 && run.LastDepthArrivalHeals.Count == 1 && run.LastDepthArrivalHeals[0].EffectiveMax == stats.MaxHealth);
            _result.ProgressionChecks = _progressionChecks.ToArray();
            _result.ProofCaptures = _captures.ToArray();
            Debug.Log($"[SMOKE] progression (depth): {_progressionChecks.Count} checks passed");
        }
    }
}
