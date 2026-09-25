using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The returning-profile release scenario (`-smoke -smoke-scenario returning -savedir dir`): a relaunch of the
    /// shipped player on a save directory a previous fresh-profile smoke left behind. Nothing is set up by debug code
    /// before the menu — the profile on disk is the precondition. The scenario proves the relaunch restores that
    /// profile exactly (XP, level, ranks, banked coins, Storage, deepest depth, tutorial and settings state, no
    /// migration churn), that the Character Station previews and every attribute's runtime effect hold, that Storage
    /// and the Shelter Trader move items and coins exactly once, and that a re-entered expedition carries the profile
    /// and returns it to disk intact.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string ScenarioArgument = "-smoke-scenario";
        public const string ReturningScenario = "returning";

        /// <summary>The screen-shake intensity the fresh-profile smoke saves through the Settings model as its last act.</summary>
        public const float FreshSmokeShakeIntensity = 0.5f;

        /// <summary>Ranks the returning scenario buys in the two attributes the fresh smoke leaves at zero.</summary>
        private const int ReturningRecoveryRank = 2;
        private const int ReturningHandlingRank = 3;

        private static string ScenarioName
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, ScenarioArgument);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : "fresh";
            }
        }

        private IEnumerator RunReturning()
        {
            _result.Scenario = ReturningScenario;
            Application.logMessageReceived += OnLog;
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("returning: " + what); }
            try
            {
                yield return WaitFor(() => _composed.Contains(SceneNames.MainMenu), "main menu composed");
                Stage("returning_menu");

                // ---- the profile on disk, read exactly as the boot would, before anything touches it ----
                var disk = _app.Saves.Load();
                Check("a saved profile exists and loads from the current document (no backup recovery)", disk.Success && !disk.WasRecovered);
                if (!disk.Success) yield break;
                var slot = disk.Slot;
                Check($"the save is already at the current schema (v{slot.SaveVersion}) and the boot migrates nothing", slot.SaveVersion == RuinRail.Persistence.SaveSlot.CurrentVersion && !disk.WasMigrated
                    && disk.Diagnostics.Entries.All(e => !e.Code.StartsWith("migration")));
                Check("nothing is quarantined", slot.Quarantine.Count == 0);
                var profile = slot.Profile;
                var ranks = SkillRules.All.ToDictionary(s => s, s => profile.Skills.GetRank(s));
                Check($"the precondition is a returning profile: XP {profile.TotalXp}, level {profile.Level}, ranks {string.Join("/", ranks.Values)}, banked {profile.BankedCoins}, deepest D{profile.DeepestDepthReached}",
                    profile.TotalXp > 0 && profile.Level > 1 && ranks.Values.Any(r => r > 0) && profile.BankedCoins > 0 && profile.DeepestDepthReached >= 2);
                Check($"the profile the fresh smoke created is the one on disk ('{profile.DisplayName}')", profile.DisplayName == SmokeDisplayName);
                Check("the expedition marker is closed (the last run resolved before the game closed)", !slot.ActiveExpedition.IsOpen);
                var storedIds = slot.Storage.Slots.Where(e => e.Item != null).Select(e => e.Item.InstanceId).ToList();
                Check($"Storage holds {storedIds.Count} items with no duplicated instance id", storedIds.Count > 0 && storedIds.Count == storedIds.Distinct().Count());
                var tutorialSeen = slot.FirstLaunch.TutorialPromptsSeen.ToList();
                Check($"tutorial state persisted ({tutorialSeen.Count} prompts seen, onboarding complete, name confirmed)",
                    tutorialSeen.Count > 0 && slot.FirstLaunch.ShelterOnboardingComplete && slot.FirstLaunch.DisplayNameConfirmed);
                Check($"settings persisted across the relaunch (screen-shake intensity {_app.Settings.Current.Accessibility.ScreenShakeIntensity:0.00})",
                    Mathf.Abs(_app.Settings.Current.Accessibility.ScreenShakeIntensity - FreshSmokeShakeIntensity) < 1e-3f && _app.Settings.LastError == RuinRail.Persistence.SaveError.None);

                // ---- PLAY continues the profile, it does not create one ----
                var menu = _app.Menu;
                var outcome = menu.Play();
                Check($"PLAY continues the saved profile ({outcome})", outcome == PlayOutcome.Continued && menu.AbandonedExpedition == null);
                yield return WaitFor(() => _composed.Contains(SceneNames.Base), "base composed");
                Stage("returning_base");
                var screen = FindFirstObjectByType<BaseHubScreen>();
                var session = menu.Session;
                if (screen == null || session == null) { Fail("returning: no Shelter"); yield break; }
                var hub = screen.Hub;
                Check("the live profile equals the saved one (name, XP, level, banked coins, deepest depth, ranks)",
                    session.Profile.DisplayName == profile.DisplayName && session.Profile.TotalXp == profile.TotalXp && session.Profile.Level == profile.Level
                    && session.Banked.Balance == profile.BankedCoins && session.Profile.DeepestDepthReached == profile.DeepestDepthReached
                    && SkillRules.All.All(s => hub.Character.RankOf(s) == ranks[s]));
                Check("Storage restored item for item", session.Storage.Items.Select(i => i.InstanceId).OrderBy(s => s).SequenceEqual(storedIds.OrderBy(s => s)));
                Check("no onboarding or first-kit grant on a returning profile", !session.GrantedFirstKit);
                Check($"level {session.Progression.Level} matches the XP curve and sits within the cap {LevelCurve.MaxLevel}",
                    session.Progression.Level == LevelCurve.LevelForTotalXp(session.Progression.TotalXp) && session.Progression.Level <= LevelCurve.MaxLevel);
                Check("points are conserved: spent + unspent = the points the XP curve has earned",
                    session.Progression.SpentSkillPoints + session.Progression.UnspentSkillPoints == LevelCurve.SkillPointsEarned(session.Progression.TotalXp));
                Check("every rank is within the cap", SkillRules.All.All(s => hub.Character.RankOf(s) <= SkillRules.MaxRank));

                // ---- Character Station: previews for every attribute, and the two untouched attributes bought now ----
                hub.Open(BaseStation.Character);
                yield return null;
                Check("the Character Station previews every attribute's current and next rank",
                    CharacterPanelViewModel.Attributes.All(a => hub.Character.EffectNow(a) == SkillCatalog.EffectText(a, hub.Character.RankOf(a))
                        && (hub.Character.IsMaxed(a) ? hub.Character.EffectNext(a) == null : hub.Character.EffectNext(a) == SkillCatalog.EffectText(a, hub.Character.RankOf(a) + 1))));
                var needed = ReturningRecoveryRank + ReturningHandlingRank - session.Progression.UnspentSkillPoints;
                if (needed > 0) session.Progression.AddXp(LevelCurve.TotalXpForLevel(session.Progression.Level + needed) - session.Progression.TotalXp);
                foreach (var (skill, target) in new[] { (SkillId.Recovery, ReturningRecoveryRank), (SkillId.Handling, ReturningHandlingRank) })
                    while (hub.Character.RankOf(skill) < target && hub.Character.Allocate(skill)) { }
                Check($"Recovery {ReturningRecoveryRank} and Handling {ReturningHandlingRank} bought at the station",
                    hub.Character.RankOf(SkillId.Recovery) == ReturningRecoveryRank && hub.Character.RankOf(SkillId.Handling) == ReturningHandlingRank);
                hub.Close();
                yield return null;

                // ---- Storage round trip: out to the backpack and back, nothing duplicated or lost ----
                hub.Open(BaseStation.Storage);
                yield return null;
                var moving = hub.Storage.Items.FirstOrDefault();
                var countBefore = hub.Storage.Count;
                var withdrawn = moving != null && hub.Storage.Withdraw(moving.InstanceId);
                Check("Storage → backpack moves exactly one item", withdrawn && hub.Storage.Count == countBefore - 1 && session.Loadout.Contains(moving.InstanceId) && session.Storage.Find(moving.InstanceId) == null);
                var deposited = moving != null && hub.Storage.Deposit(moving.InstanceId);
                Check("backpack → Storage puts it back, once", deposited && hub.Storage.Count == countBefore && !session.Loadout.Contains(moving.InstanceId) && session.Storage.Find(moving.InstanceId) != null);
                hub.Close();
                yield return null;

                // ---- Shelter Trader: one purchase and its resale, each settled exactly once in banked coins ----
                hub.Open(BaseStation.Trader);
                yield return null;
                var offer = hub.Trader.Offers.Where(o => !o.IsSold && o.Price <= hub.Trader.Banked).OrderBy(o => o.Price).FirstOrDefault();
                if (offer != null)
                {
                    var bankedBefore = hub.Trader.Banked;
                    var backpackBefore = session.Loadout.BackpackSlots.Where(i => i != null).Select(i => i.InstanceId).ToHashSet();
                    Check($"the Trader sells {offer.Definition.DisplayName} for {offer.Price} banked coins", hub.Trader.Buy(offer.Index) && hub.Trader.Banked == bankedBefore - offer.Price && offer.IsSold);
                    var bought = session.Loadout.BackpackSlots.FirstOrDefault(i => i != null && !backpackBefore.Contains(i.InstanceId) && i.DefinitionId == offer.Definition.Id);
                    Check("the purchase lands in the backpack once", bought != null && session.Loadout.BackpackSlots.Count(i => i != null && i.InstanceId == bought.InstanceId) == 1);
                    Check("a sold-out offer cannot be bought again", !hub.Trader.Buy(offer.Index) && hub.Trader.Banked == bankedBefore - offer.Price);
                    if (bought != null)
                    {
                        var quote = hub.Trader.QuoteSell(bought.InstanceId);
                        var beforeSale = hub.Trader.Banked;
                        Check($"reselling it pays the quoted {quote} coins once", hub.Trader.Sell(bought.InstanceId) && hub.Trader.Banked == beforeSale + quote && !session.Loadout.Contains(bought.InstanceId) && !hub.Trader.Sell(bought.InstanceId) && hub.Trader.Banked == beforeSale + quote);
                    }
                }
                else Check("the Trader has an affordable offer on a returning profile", false);
                hub.Close();
                yield return null;

                // ---- persist the Shelter changes; the relaunch-level probe sees them ----
                Check("the Shelter changes save", session.SaveNow("smoke_returning_shelter") == RuinRail.Persistence.SaveError.None);
                var afterShelter = _app.ProbeSave();
                Check("the saved profile carries the new ranks and the Trader's coin settlement", afterShelter.Success
                    && afterShelter.SkillRanks[(int)SkillId.Recovery] == ReturningRecoveryRank && afterShelter.SkillRanks[(int)SkillId.Handling] == ReturningHandlingRank
                    && afterShelter.BankedCoins == session.Banked.Balance);

                // ---- re-enter an expedition: the whole profile is in the run ----
                if (!hub.Multiplayer.SetReady(true)) { Fail("returning: ready refused"); yield break; }
                hub.Open(BaseStation.Transit);
                if (!hub.Transit.StartExpedition()) { Fail("returning: start refused: " + hub.Transit.Feedback.Text); yield break; }
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Dungeon + ";"), "dungeon composed");
                Stage("returning_run");
                for (var i = 0; i < 10; i++) yield return null;
                var run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Rig?.Player == null) { Fail("returning: no player in the re-entered run"); yield break; }
                var stats = run.Rig.StatsBinder.Stats;
                var progression = run.Rig.StatsBinder.Progression;
                var liveRanks = SkillRules.All.ToDictionary(s => s, s => session.Progression.Profile.Skills.GetRank(s));
                Check("the run's stat pipeline carries all six saved ranks", progression != null && SkillRules.All.All(s => progression.GetRank(s) == liveRanks[s]));
                var health = run.Rig.Player.GetComponent<HealthComponent>();
                var armorHp = stats.GetFlat(StatId.MaxHealth) - 2 * liveRanks[SkillId.Vitality];
                Check($"Vitality {liveRanks[SkillId.Vitality]}: max HP {stats.MaxHealth} = 100 + equipment {armorHp} + {2 * liveRanks[SkillId.Vitality]}, run starts full",
                    stats.MaxHealth == 100 + armorHp + 2 * liveRanks[SkillId.Vitality] && health.MaxHealth == stats.MaxHealth && health.CurrentHealth == stats.MaxHealth);
                Check($"Power {liveRanks[SkillId.Power]}: weapon damage +{stats.GetPercent(StatId.WeaponDamage)}%", stats.GetPercent(StatId.WeaponDamage) == liveRanks[SkillId.Power]);
                var movement = run.Rig.Player.GetComponent<RuinRail.Gameplay.Player.PlayerMovement>();
                Check($"Mobility {liveRanks[SkillId.Mobility]}: real move speed {movement.CurrentMoveSpeed:0.####}",
                    Mathf.Abs(movement.CurrentMoveSpeed - _app.Content.PlayerBalance.MoveSpeed * (1f + stats.GetPercent(StatId.MovementSpeed) / 100f)) < 0.0001f && stats.GetPercent(StatId.MovementSpeed) >= liveRanks[SkillId.Mobility]);
                Check($"Recovery {liveRanks[SkillId.Recovery]}: healing received +{stats.GetPercent(StatId.HealingReceived)}%", stats.GetPercent(StatId.HealingReceived) >= 2 * liveRanks[SkillId.Recovery] && liveRanks[SkillId.Recovery] == ReturningRecoveryRank);
                // Handling's reload bonus is the one with a runtime consumer; its weapon-switch share is an explicitly
                // deferred stat (StatConsumerIntegrity), so it is not asserted here.
                Check($"Handling {liveRanks[SkillId.Handling]}: reload speed +{stats.GetPercent(StatId.ReloadSpeed)}%",
                    stats.GetPercent(StatId.ReloadSpeed) >= liveRanks[SkillId.Handling] && liveRanks[SkillId.Handling] == ReturningHandlingRank);
                Check($"Resilience {liveRanks[SkillId.Resilience]}: knockback/stagger resistance +{stats.GetPercent(StatId.KnockbackResistance)}%/+{stats.GetPercent(StatId.StaggerResistance)}%",
                    stats.GetPercent(StatId.KnockbackResistance) >= 2 * liveRanks[SkillId.Resilience] && stats.GetPercent(StatId.StaggerResistance) >= 2 * liveRanks[SkillId.Resilience]);

                // ---- RETURN: the carried coins bank once, the record never goes backwards, nothing duplicates ----
                var bankedBeforeReturn = session.Banked.Balance;
                var deepestBefore = session.Profile.DeepestDepthReached;
                run.Expedition.AddCarriedCoins(40);
                var summary = run.Expedition.Return();
                Check($"RETURN extracts ({summary.Outcome}) and banks the carried coins once ({summary.CoinsExtracted})", summary.IsSuccess && summary.CoinsExtracted >= 40);
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Base + ";"), "base composed after return");
                Stage("returning_return");
                Check("banked coins rose by exactly the extracted amount", session.Banked.Balance == bankedBeforeReturn + summary.CoinsExtracted);
                Check($"the deepest-depth record is monotonic (D{deepestBefore} → D{session.Profile.DeepestDepthReached})", session.Profile.DeepestDepthReached >= deepestBefore);
                Check("the save after RETURN succeeds", session.SaveNow("smoke_returning_return") == RuinRail.Persistence.SaveError.None);
                var final = _app.Saves.Load();
                var ids = final.Success
                    ? final.Slot.Storage.Slots.Where(e => e.Item != null).Select(e => e.Item.InstanceId)
                        .Concat(final.Slot.Profile.SafeLoadout?.Equipped.Select(e => e.Item.InstanceId) ?? Enumerable.Empty<string>())
                        .Concat(final.Slot.Profile.SafeLoadout?.Backpack?.Where(e => e.Item != null).Select(e => e.Item.InstanceId) ?? Enumerable.Empty<string>()).ToList()
                    : new List<string>();
                Check($"the final save reloads: banked {final.Slot?.Profile.BankedCoins}, deepest D{final.Slot?.Profile.DeepestDepthReached}, marker closed, {ids.Count} items, no duplicate id, no migration",
                    final.Success && !final.WasMigrated && final.Slot.Profile.BankedCoins == session.Banked.Balance && final.Slot.Profile.DeepestDepthReached == session.Profile.DeepestDepthReached
                    && !final.Slot.ActiveExpedition.IsOpen && ids.Count == ids.Distinct().Count() && final.Slot.Quarantine.Count == 0);
                Check("tutorial state is not reset by the returning session", final.Success && tutorialSeen.All(final.Slot.FirstLaunch.TutorialPromptsSeen.Contains));
                Check("settings are untouched by the returning session", Mathf.Abs(_app.Settings.Current.Accessibility.ScreenShakeIntensity - FreshSmokeShakeIntensity) < 1e-3f);

                _result.BankedCoinsAfterReturn = session.Banked.Balance;
                _result.TotalXpAfterReturn = session.Profile.TotalXp;
                _result.DeepestDepthReached = session.Profile.DeepestDepthReached;
                _result.SaveReloaded = final.Success;
                if (string.IsNullOrEmpty(_result.Error))
                {
                    _result.Success = true;
                    Stage("done");
                }
            }
            finally
            {
                _result.ReturningChecks = passed.ToArray();
                Debug.Log("[SMOKE] returning: " + string.Join("; ", passed));
                _result.ScenesComposed = _composed;
                Write();
                Application.logMessageReceived -= OnLog;
                Application.Quit(_result.Success ? 0 : 1);
            }
        }
    }
}
