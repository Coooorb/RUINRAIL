using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The solo-death release scenario (`-smoke -smoke-scenario death -savedir fresh-dir`): independent of the full
    /// smoke's ordered stages. A fresh profile starts one expedition and dies in it; the Run Lost screen, the single
    /// failure transaction and the loss rules are checked (carried coins and at-risk gear gone; XP, ranks, banked coins,
    /// Storage and the deepest-depth record kept), the save is reloaded, and the Shelter can start the next run.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string DeathScenario = "death";

        private IEnumerator RunDeath()
        {
            _result.Scenario = DeathScenario;
            Application.logMessageReceived += OnLog;
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("death scenario: " + what); }
            try
            {
                yield return WaitFor(() => _composed.Contains(SceneNames.MainMenu), "main menu composed");
                Stage("death_menu");
                var menu = _app.Menu;
                var outcome = menu.Play();
                Check($"PLAY on an empty save directory creates a new profile ({outcome})", outcome == PlayOutcome.NewProfile);
                yield return WaitFor(() => _composed.Contains(SceneNames.Base), "base composed");
                var screen = FindFirstObjectByType<BaseHubScreen>();
                var session = menu.Session;
                if (screen == null || session == null) { Fail("death scenario: no Shelter"); yield break; }

                // Persistent progress that must survive the death: some XP and a rank, saved before the run starts.
                session.Progression.AddXp(LevelCurve.TotalXpForLevel(3));
                screen.Hub.Open(BaseStation.Character);
                yield return null;
                Check("a rank is bought before the run", screen.Hub.Character.Allocate(SkillId.Vitality));
                screen.Hub.Close();
                Check("the pre-run profile saves", session.SaveNow("smoke_death_prerun") == RuinRail.Persistence.SaveError.None);
                var xpBefore = session.Profile.TotalXp;
                var ranksBefore = SkillRules.All.Select(s => session.Profile.Skills.GetRank(s)).ToArray();
                var storageBefore = session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList();
                var deepestBefore = session.Profile.DeepestDepthReached;

                if (!screen.Hub.Multiplayer.SetReady(true)) { Fail("death scenario: ready refused"); yield break; }
                screen.Hub.Open(BaseStation.Transit);
                if (!screen.Hub.Transit.StartExpedition()) { Fail("death scenario: start refused: " + screen.Hub.Transit.Feedback.Text); yield break; }
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Dungeon + ";"), "dungeon composed");
                yield return null;
                var run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Rig?.Player == null) { Fail("death scenario: no player"); yield break; }
                _result.RunSeed = run.Expedition.State.RunSeed;
                _result.Biome = run.Expedition.State.Biome.ToString();
                _result.RoomsComposed = run.Rooms?.Count ?? 0;
                Stage("death");
                // The Run Lost screen, the one failure transaction, the loss committed and saved (shared with the full smoke).
                yield return DeathScreenChecks(run, menu);
                if (!string.IsNullOrEmpty(_result.Error)) yield break;

                session = menu.Session;
                Check($"XP is kept through the death ({xpBefore} → {session.Profile.TotalXp})", session.Profile.TotalXp >= xpBefore);
                Check("attribute ranks are kept", SkillRules.All.Select(s => session.Profile.Skills.GetRank(s)).SequenceEqual(ranksBefore));
                Check("Storage is untouched by the death", session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).SequenceEqual(storageBefore));
                Check("the deepest-depth record never goes backwards", session.Profile.DeepestDepthReached >= deepestBefore);
                var reloaded = _app.Saves.Load();
                Check("the saved profile reloads with the death committed: marker closed, XP and ranks kept, no migration",
                    reloaded.Success && !reloaded.WasMigrated && !reloaded.Slot.ActiveExpedition.IsOpen && reloaded.Slot.Profile.TotalXp == session.Profile.TotalXp
                    && SkillRules.All.Select(s => reloaded.Slot.Profile.Skills.GetRank(s)).SequenceEqual(ranksBefore));

                // No softlock: the Shelter can ready and start the next run straight away (the free Starter Loadout).
                var next = FindFirstObjectByType<BaseHubScreen>();
                Check("after the death the Shelter can ready the next run (Starter Loadout fallback)", next != null && next.Hub.Multiplayer.SetReady(true)
                    && session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) != null && next.Hub.Transit.CanStart);
                _result.BankedCoinsAfterReturn = session.Profile.BankedCoins;
                _result.TotalXpAfterReturn = session.Profile.TotalXp;
                _result.SaveReloaded = reloaded.Success;
                if (string.IsNullOrEmpty(_result.Error))
                {
                    _result.Success = true;
                    Stage("done");
                }
            }
            finally
            {
                _result.ReturningChecks = passed.ToArray();
                Debug.Log("[SMOKE] death scenario: " + string.Join("; ", passed));
                _result.ScenesComposed = _composed;
                Write();
                Application.logMessageReceived -= OnLog;
                Application.Quit(_result.Success ? 0 : 1);
            }
        }
    }
}
