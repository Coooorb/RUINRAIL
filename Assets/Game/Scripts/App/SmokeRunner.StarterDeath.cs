using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.RunEnd;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The Starter Loadout fallback and the Death / Run Lost screen in the built player: a profile that strips every
    /// equipped item into Storage still starts a run (Ready equips the free kit, once, storage untouched); dying in
    /// that run shows the Run Lost screen over the closed failure transaction, and RETURN TO SHELTER hands back to the
    /// Shelter with the loss committed and saved.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private IEnumerator StarterFallbackChecks(BaseHubScreen screen, BaseSession session)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("starter: " + what); }

            // Strip everything equipped into Storage: the player deliberately has no loadout.
            var stored = 0;
            foreach (var slot in new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.Accessory, EquippedSlot.ActiveConsumable })
            {
                var item = session.Loadout.Unequip(slot);
                if (item != null && session.Storage.TryAdd(item)) stored++;
            }

            yield return null;
            var storageBefore = session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).ToList();
            var member = session.Lobby.Get(BaseSession.LocalClientId);
            Check($"nothing equipped ({stored} items moved to Storage): the lobby reports the loadout invalid", member != null && !member.HasValidLoadout && session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) == null);
            screen.Hub.Open(BaseStation.Multiplayer); // the station READY lives in; its feedback line shows the notice
            yield return null;
            yield return CaptureHud("ecdp_05a_no_loadout_before_ready");

            var fallbacksBefore = session.StarterLoadoutFallbacks;
            // READY exactly as the MULTIPLAYER station's control activates it.
            var readyTask = screen.Terminal.ActivateAsync(RuinRail.UI.Multiplayer.TerminalAction.ToggleReady);
            while (!readyTask.IsCompleted) yield return null;
            yield return null;
            var pistol = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon);
            var knife = session.Loadout.GetEquipped(EquippedSlot.SecondaryWeapon);
            var vest = session.Loadout.GetEquipped(EquippedSlot.Armor);
            Check("READY with no loadout equips the free Starter Loadout instead of refusing", readyTask.Result.IsNone && member.IsReady && session.StarterLoadoutFallbacks == fallbacksBefore + 1 && member.HasValidLoadout);
            Check("the terminal shows the STARTER LOADOUT EQUIPPED notice (non-blocking, not an error)", screen.Terminal.Notice == RuinRail.UI.Multiplayer.TerminalViewModel.StarterLoadoutEquippedNotice && string.IsNullOrEmpty(screen.Terminal.ErrorText) && screen.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text == RuinRail.UI.Multiplayer.TerminalViewModel.StarterLoadoutEquippedNotice));
            Check("P9 Ranger / Field Knife / Scrap Vest equipped, Common, affix-free, unsellable",
                pistol?.DefinitionId == StarterKitService.PistolId && knife?.DefinitionId == StarterKitService.KnifeId && vest?.DefinitionId == StarterKitService.VestId
                && new[] { pistol, knife, vest }.All(i => i.Rarity == Rarity.Common && i.AffixRolls.Count == 0 && i.IsUnsellable));
            Check("Bandage active and 60 light ammo in the backpack", session.Loadout.GetEquipped(EquippedSlot.ActiveConsumable)?.DefinitionId == StarterKitService.BandageId && session.Loadout.Get(AmmoType.Light) >= StarterKitService.LightAmmoCount);
            Check("Storage untouched by the fallback (no duplication)", session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).SequenceEqual(storageBefore));
            yield return CaptureHud("ecdp_05b_starter_loadout_equipped");
            screen.Hub.Close();
            yield return null;

            screen.Hub.Open(BaseStation.Transit);
            var started = screen.Hub.Transit.StartExpedition();
            Check("START launches the run with the Starter Loadout and grants no second kit", started && session.StarterLoadoutFallbacks == fallbacksBefore + 1);
            _result.StarterFallbackChecks = passed.ToArray();
            Debug.Log("[SMOKE] starter fallback: " + string.Join("; ", passed));
        }

        private IEnumerator DeathScreenChecks(ExpeditionScene run, MainMenuViewModel menu)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("death: " + what); }
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var life = player.GetComponent<PlayerLifeStateComponent>();
            var expedition = run.Expedition;
            var screen = run.RunFailedScreen;
            var failed = run.RunFailed;
            if (screen == null || failed == null || life == null) { Check("Run Lost screen composed", false); yield break; }
            for (var i = 0; i < 20; i++) yield return null;
            run.Expedition.AddCarriedCoins(77);
            var ended = 0;
            expedition.ExpeditionEnded += _ => ended++;
            var bankedBefore = menu.Session.Profile.BankedCoins;
            Check("run active, player alive, no Run Lost screen before the death", expedition.IsExpeditionActive && life.IsAlive && !screen.IsShowing);

            // The death: lethal damage through the ordinary health path (solo: the last standing player dies outright).
            health.TryApplyDamage(new DamageRequest(999999));
            yield return null;
            yield return null;
            Check("solo death is conclusive: Dead, the failure transaction closed exactly once", life.IsDead && !expedition.IsExpeditionActive && ended == 1 && expedition.LastSummary != null && expedition.LastSummary.Outcome == ExpeditionOutcome.Failed);
            Check("the Run Lost screen is showing, once, with RUN LOST and the tracked figures", screen.IsShowing && failed.Shows == 1 && screen.TitleText == RunFailedViewModel.TitleText && screen.RowTexts.Any(r => r.label == "DEPTH REACHED" && r.value == "1") && screen.RowTexts.Any(r => r.label == "CARRIED COINS LOST" && r.value == "77") && screen.RowTexts.Any(r => r.label == "BIOME"));
            Check("gameplay input is held and the pointer cursor is up; Esc cannot open the pause menu over it", GameplayInputGate.IsHeld && PointerLayerOwnsCursor && !run.Pause.IsOpen);
            run.Pause.HandlePauseInput(); // the Esc path, as the reader delivers it
            yield return null;
            Check("pause stays closed under the Run Lost screen", !run.Pause.IsOpen && screen.IsShowing);
            Check("exactly one Run Lost layer, RETURN TO SHELTER focused first", FindObjectsByType<RunFailedScreen>(FindObjectsSortMode.None).Length == 1 && screen.List.Focused != null && screen.List.Focused.Id == "runfailed.shelter");
            yield return CaptureHud("ecdp_06_run_lost_screen");

            // RETURN TO SHELTER by mouse click on the real control.
            var control = screen.Controls.First(c => c.Id == "runfailed.shelter");
            control.SimulateHover(true);
            control.SimulateClick();
            yield return WaitFor(() => _composed.EndsWith(SceneNames.Base + ";"), "base composed after the run lost");
            yield return null;
            Check("RETURN TO SHELTER hands back to the Shelter; the choice resolved once", failed.Choice == RunFailedChoice.ReturnToShelter && menu.Session != null && !menu.Session.Expedition.IsExpeditionActive);
            var probe = _app.ProbeSave();
            Check("the loss is committed and saved: marker closed, carried coins gone, banked coins intact", probe.Success && !probe.ExpeditionMarkerOpen && probe.BankedCoins == bankedBefore && menu.Session.Profile.BankedCoins == bankedBefore);
            Check("back at the Shelter the loadout is empty again (the at-risk gear was lost with the run)", menu.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) == null);
            yield return CaptureHud("ecdp_07_return_to_shelter_after_run_lost");
            _result.DeathScreenChecks = passed.ToArray();
            Debug.Log("[SMOKE] death screen: " + string.Join("; ", passed));
        }
    }
}
