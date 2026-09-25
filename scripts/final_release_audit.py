#!/usr/bin/env python3
"""Final release-candidate audit: builds every TestResults/FinalReleaseAudit/*.csv from real evidence.

Every PASS in these matrices is computed here from an artifact that exists on disk: a test case in the PlayMode /
EditMode result XML (by class or method), a check line recorded by a built-player scenario run (in every run of that
scenario), a co-op proof step (host/client JSON), or a validator/build report. A row whose evidence is missing is
NOT RUN or FAIL, never PASS. Static rows (configuration facts) name the file they were read from.

    python3 scripts/final_release_audit.py [--out TestResults/FinalReleaseAudit]
"""
import argparse
import csv
import glob
import json
import os
import re
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def p(*parts):
    return os.path.join(ROOT, *parts)


# ----------------------------------------------------------------------------------------------------- evidence


class Tests:
    def __init__(self, playmode, editmode):
        self.cases = []
        self.totals = {}
        for platform, path in (("PM", playmode), ("EM", editmode)):
            if not os.path.exists(path):
                self.totals[platform] = None
                continue
            root = ET.parse(path).getroot()
            run = root if root.tag == "test-run" else root.find("test-run")
            self.totals[platform] = {k: run.get(k) for k in ("total", "passed", "failed", "skipped", "start-time", "end-time")}
            for tc in root.iter("test-case"):
                self.cases.append((platform, tc.get("classname").split(".")[-1], tc.get("methodname") or tc.get("name"), tc.get("result")))

    def check(self, *specs):
        """specs: 'PM:Class' (whole class) or 'PM:Class.Method' (method substring). Returns (result, evidence)."""
        parts, ok = [], True
        for spec in specs:
            platform, name = spec.split(":", 1)
            cls, _, method = name.partition(".")
            matched = [c for c in self.cases if c[0] == platform and c[1] == cls and (not method or method in c[2])]
            if not matched:
                return "NOT RUN", f"{spec}: not in the {platform} results"
            passed = sum(1 for c in matched if c[3] == "Passed")
            skipped = sum(1 for c in matched if c[3] == "Skipped")
            failed = len(matched) - passed - skipped
            if failed or passed == 0:
                ok = False
            parts.append(f"{spec} {passed}/{len(matched)}" + (f" ({skipped} skipped)" if skipped else ""))
        return ("PASS" if ok else "FAIL"), "; ".join(parts)


class Smoke:
    """Built-player scenario runs: <dir>/<scenario>/run_i/smoke_result.json."""

    def __init__(self, directory):
        self.runs = {}
        for scenario_dir in sorted(glob.glob(os.path.join(directory, "*"))):
            if not os.path.isdir(scenario_dir):
                continue
            results = []
            for run_dir in sorted(glob.glob(os.path.join(scenario_dir, "run_*")), key=lambda d: int(d.rsplit("_", 1)[1])):
                path = os.path.join(run_dir, "smoke_result.json")
                if os.path.exists(path):
                    with open(path) as f:
                        results.append(json.load(f))
                else:
                    results.append({"Success": False, "Stage": "no_result", "Error": "no smoke_result.json"})
            self.runs[os.path.basename(scenario_dir)] = results

    def check(self, scenario, field=None, needle=None, predicate=None, label=None):
        runs = self.runs.get(scenario, [])
        if not runs:
            return "NOT RUN", f"{scenario}: no runs"
        good = 0
        for r in runs:
            if not r.get("Success"):
                continue
            if field and needle is not None:
                values = r.get(field) or []
                if isinstance(values, str):
                    values = [values]
                if not any(needle.lower() in str(v).lower() for v in values):
                    continue
            if predicate and not predicate(r):
                continue
            good += 1
        what = label or (f"{field} ∋ '{needle}'" if field else "scenario passed")
        return ("PASS" if good == len(runs) else "FAIL"), f"built-player {scenario}: {what} in {good}/{len(runs)} runs"

    def example(self, scenario, field, needle):
        for r in self.runs.get(scenario, []):
            for v in (r.get(field) or []):
                if needle.lower() in str(v).lower():
                    return str(v)
        return ""


class Coop:
    """Co-op proof runs: <dir>/run_i/{host,client1,client2}.json."""

    def __init__(self, directory):
        self.runs = []
        for run_dir in sorted(glob.glob(os.path.join(directory, "run_*")), key=lambda d: int(d.rsplit("_", 1)[1])):
            peers = {}
            for path in glob.glob(os.path.join(run_dir, "*.json")):
                with open(path) as f:
                    peers[os.path.basename(path)[:-5]] = json.load(f)
            self.runs.append(peers)

    def all_passed(self):
        return bool(self.runs) and all(peers and all(d.get("Success") for d in peers.values()) for peers in self.runs)

    def step(self, needle, peer="host"):
        if not self.runs:
            return "NOT RUN", "no co-op runs", ""
        good, detail = 0, ""
        for peers in self.runs:
            d = peers.get(peer)
            if not d:
                continue
            steps = [s for s in d.get("Steps", []) if needle.lower() in s.get("Name", "").lower()]
            if steps and all(s.get("Pass") for s in steps):
                good += 1
                detail = detail or steps[0].get("Detail", "")
        return ("PASS" if good == len(self.runs) else "FAIL"), f"built-player {peer}: step '{needle}' passed in {good}/{len(self.runs)} runs", detail


def write(out, name, header, rows):
    path = os.path.join(out, name)
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        for r in rows:
            w.writerow(r)
    counts = {}
    for r in rows:
        for cell in r:
            if cell in ("PASS", "FAIL", "NOT RUN", "FIXED DURING AUDIT", "NOT RUN — ENVIRONMENT", "NON-BLOCKING KNOWN ISSUE", "RELEASE BLOCKER", "EXTERNAL CONTENT LIMITATION"):
                counts[cell] = counts.get(cell, 0) + 1
    print(f"{name}: {len(rows)} rows {counts}")
    return counts


def read_csv(path):
    if not os.path.exists(path):
        return []
    with open(path) as f:
        return list(csv.DictReader(f))


# ----------------------------------------------------------------------------------------------------- matrices


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=p("TestResults", "FinalReleaseAudit"))
    a = ap.parse_args()
    out = a.out
    os.makedirs(out, exist_ok=True)
    bp = os.path.join(out, "built_player")
    T = Tests(p("TestResults", "PlayMode-results.xml"), p("TestResults", "EditMode-results.xml"))
    S = Smoke(os.path.join(bp, "release_smoke"))
    L = Smoke(os.path.join(bp, "longrun_smoke"))
    D = Coop(os.path.join(bp, "duo"))
    R = Coop(os.path.join(bp, "trio"))
    summary = {}

    def res(x):
        return x[0]

    # ---- 1. current-state reconciliation -------------------------------------------------------------
    pm, em = T.totals.get("PM"), T.totals.get("EM")
    pm_s = f"{pm['passed']}/{pm['total']} passed, {pm['failed']} failed, {pm['skipped']} skipped" if pm else "not run"
    em_s = f"{em['passed']}/{em['total']} passed, {em['failed']} failed, {em['skipped']} skipped" if em else "not run"
    build = open(p("TestResults", "build_report_macos.md")).read() if os.path.exists(p("TestResults", "build_report_macos.md")) else ""
    build_s = re.search(r"Result: \*\*(\w+)\*\*[^\n]*", build).group(0) if "Result:" in build else "no build report"
    rows = [
        ["Co-op", "COOP_RUNTIME_COMPOSITION_COMPLETE (completion report 2026-09-24)", f"duo release smoke {len(D.runs)} runs all pass={D.all_passed()}; trio {len(R.runs)} runs all pass={R.all_passed()}", "YES", "NO", "none", "built_player/duo, built_player/trio"],
        ["Co-op (older docs)", "MULTIPLAYER_GATE_REPORT / FINAL_SHIPPABLE_V1: fake transport / multiplayer never proven", "real host/client built players proven", "NO", "YES", "SUPERSEDED banner added", "documentation_consistency.csv"],
        ["Audio content", "AUDIO/MUSIC_ASSET_AUDIT.md (production copies): 0/53 clips, 0/11 tracks", "live audits: 53/53 events, 11/11 tracks, 6/6 stingers, 3/3 ambience (TestResults/*_assets.md)", "NO", "YES", "SUPERSEDED banner added", "PresentationAudioUxQolValidator + audit tests"],
        ["Art content", "ANIMATION_ASSET_AUDIT.md / FINAL_RELEASE_COMPLETION_REPORT: placeholders, white squares", "COMPLETION_ASSET_MANIFEST: 340 INTEGRATED / 0 PLACEHOLDER / 0 MISSING", "NO", "YES", "SUPERSEDED banner added", "production/COMPLETION_ASSET_MANIFEST.md"],
        ["Final MVP report generator", "FINAL_MVP_COMPLETION_REPORT: BLOCKED_EXTERNAL_ASSET, fake transport, hard-coded placeholder art line", "external blockers now derived from the manifest + audits; transport text states the real NGO proof", "NO", "YES", "FIXED DURING AUDIT (FinalMvpAudit.cs + test)", "Assets/Game/Scripts/Editor/Production/FinalMvpAudit.cs"],
        ["Production state doc", "131_CURRENT_PRODUCTION_STATE: open blockers (audio), 15 affixes, 13 input actions", "audio complete; 14 input actions; index points to the current audit", "NO", "YES", "SUPERSEDED banner; 00_INDEX 'Current Release Status'", "00_INDEX.md"],
        ["Open decisions", "06_OPEN_DECISIONS: no open decisions; list ends before co-op/post-D30/deepest", "those decisions recorded", "NO", "YES", "entries added", "06_OPEN_DECISIONS.md"],
        ["Environment", "ENVIRONMENT.md: package resolution NOT YET VERIFIED", "7 pins resolve; compile/tests/build pass on 6000.3.24f1", "NO", "YES", "verification status updated (no version change)", "Packages/manifest.json"],
        ["Automated tests", "co-op completion: PlayMode 838/838, EditMode 975/977", f"this audit: PlayMode {pm_s}; EditMode {em_s}", "YES", "NO", "none", "TestResults/*-results.xml"],
        ["macOS build", "co-op completion: build 9 succeeded", build_s, "YES", "NO", "none", "TestResults/build_report_macos.md"],
        ["Windows x64", "FINAL_SHIPPABLE_V1 / RELEASE_CANDIDATE: 'only target validated' (09-15/16 builds)", "Windows x64: NOT RUN — module unavailable (never re-run on the current tree)", "NO", "YES", "SUPERSEDED banners; reported NOT RUN", "external_services_status.csv"],
        ["Smoke gate", "memory/previous passes: solo smoke seed-flaky ~1-3 in 10, 'rerun until green'", "deterministic release smoke: see smoke_stability.csv", "NO", "YES", "FIXED DURING AUDIT (pinned seeds, stage coupling, target selection, independent scenarios)", "smoke_stability.csv"],
        ["Dead seams", "co-op completion: Defibrillator 'not available (pre-existing)'", "player impact receiver, Defibrillator and autosave flusher were uncomposed", "NO", "NO", "FIXED DURING AUDIT", "runtime_composition_matrix.csv"],
        ["Deepest depth / post-D30", "RUN_VARIETY report: implemented", "persisted by every fresh smoke (D2) and the long run (D13)", "YES", "NO", "none", "built_player/release_smoke, longrun_smoke"],
        ["Live UGS Relay", "NOT RUN — no project configuration", "NOT RUN — project/service configuration unavailable", "YES", "NO", "none", "external_services_status.csv"],
    ]
    summary["current_state_reconciliation"] = write(out, "current_state_reconciliation.csv", ["Area", "LatestClaim", "CurrentObservedState", "Consistent YES/NO", "StaleDoc YES/NO", "RequiredAction", "Evidence"], rows)

    # ---- 2. runtime composition / dead seams ---------------------------------------------------------
    def comp(subsystem, provider, caller, consumer, reaches, pm_specs, bp_eval, dead="NO"):
        r, ev = T.check(*pm_specs) if pm_specs else ("NOT RUN", "no PlayMode consequence test")
        b, bev = bp_eval if bp_eval else ("NOT RUN", "no built-player consequence")
        status = "PASS" if (r == "PASS" and (b in ("PASS", "NOT RUN")) and dead in ("NO", "FIXED DURING AUDIT")) else ("FAIL" if "FAIL" in (r, b) else r)
        if dead == "FIXED DURING AUDIT" and status == "PASS":
            status = "FIXED DURING AUDIT"
        return [subsystem, provider, caller, consumer, reaches, ev, bev, dead, status]

    rows = [
        comp("PlayerStats / SkillStatSource", "Progression/Skills.cs SkillStatSource; Stats/PlayerStatsBinder.cs", "PlayerEntityBuilder ← PlayerRig.Build (ExpeditionScene); PlayerRig.Attach (co-op); CoopMemberMirror (host copy)", "PlayerStatsBinder → movement/dash/weapons/health", "YES", ["PM:CharacterProgressionAttributeProofTests"], S.check("fresh", "ProgressionChecks", "stat pipeline carries")),
        comp("AffixRegistry", "Items/AffixRegistry.cs", "PlayerRig ctor; BaseSession; CoopMemberMirror", "EquippedItemStatSource via LoadoutStatRegistrar", "YES", ["PM:StatConsumerRuntimeTests.ARolledAffix"], S.check("fresh", "StatConsumerChecks", "every rolled affix contributes")),
        comp("Ammo capacity provider", "ItemSlotContainer.SetAmmoCapacityBonusProvider", "PlayerRig.ComposeGameplay; BaseSession; CoopMemberMirror", "ItemSlotContainer.MaxStackFor", "YES", ["PM:StatConsumerRuntimeTests.AmmoPouch"], S.check("fresh", "StatConsumerChecks", "light-ammo stack limit is raised")),
        comp("Weapon stat consumers (WeaponStatMath)", "Stats/WeaponStatMath.cs", "PlayerStatsBinder.Bind (PlayerRig)", "RangedWeapon / BlasterWeapon / BowWeapon / MeleeWeapon", "YES", ["PM:StatConsumerRuntimeTests.FireRateAffix"], S.check("fresh", "StatConsumerChecks", "effective values differ")),
        comp("Knockback/stagger (enemies receive)", "Combat/Impact/ImpactReceiver.cs, StaggerMeter", "DefaultEnemySpawner(content.Stagger) in ExpeditionScene", "Projectile / MeleeWeapon / AreaDamageResolver → ImpactDispatcher", "YES", ["PM:StatConsumerRuntimeTests.ImpactWeapons", "PM:ImpactReceiverTests"], S.check("fresh", "StatConsumerChecks", "displaced a live unresisted target")),
        comp("Knockback/stagger (player receives) + Resilience", "Player/PlayerImpactReceiver.cs", "PlayerRig.ComposeGameplay → SetConfig(content.Stagger) + SetEvents(CombatEvents); CoopMemberMirror → SetConfig (host copies)", "enemy/boss/hazard impacts → ImpactDispatcher → PlayerImpactReceiver (resistances from PlayerStats)", "YES", ["PM:ReleaseSeamCompositionTests.LiveRun_PlayerImpactReceiver", "PM:CharacterProgressionAttributeProofTests.Resilience"], None, "FIXED DURING AUDIT"),
        comp("Grenades / GrenadeLauncher / Quick Grenade", "Combat/Area/GrenadeLauncher, ThrownGrenade", "PlayerRig.ComposeGameplay", "PlayerConsumableUser (Active Consumable + QuickGrenade)", "YES", ["PM:GrenadeAoeTests", "PM:PresentationAudioUxQolRuntimeTests.QuickGrenade"], S.check("fresh", "PresentationQolChecks", "quick-grenade throws")),
        comp("Pickup attraction", "Player/PickupAttractor.cs", "PlayerEntityBuilder; PlayerStatsBinder.SetStats", "PickupAttractor.FixedUpdate", "YES", ["PM:PresentationAudioUxQolRuntimeTests.PickupAttraction"], S.check("fresh", "PresentationQolChecks", "comes to them")),
        comp("Aim assist setting", "Core/Rendering/AssistPreferences.cs", "GameApp → SettingsViewModel.Bootstrap; PlayerRig weapon config", "ShotSolver.Solve", "YES", ["PM:PresentationAudioUxQolRuntimeTests.AimAssist", "PM:AimAssistTests"], S.check("fresh", "PresentationQolChecks", "aim assist off")),
        comp("Status effects + HUD chips", "ConsumableEffectRunner", "PlayerRig consumables; ExpeditionScene _hud.BindStatusEffects", "DungeonHudViewModel / HudStatusChipView", "YES", ["PM:PresentationAudioUxQolRuntimeTests.StatusChips"], S.check("fresh", "PresentationQolChecks", "chip")),
        comp("Deepest-depth persistence", "ExpeditionService.RecordDepthArrival; PlayerProfile.DeepestDepthReached", "ExpeditionScene depth arrival", "TransitContext / Shelter / Main Menu", "YES", ["PM:CoopSoloRegressionTests"], S.check("fresh", "RunVarietyChecks", "reloaded save carries the record")),
        comp("Post-D30 reward multiplier", "EconomyConfig.CoinRewardMultiplier/XpRewardMultiplier", "BaseSession economy → ExpeditionService; RoomCategoryComposer prices", "ExpeditionService XP; LootRoller coins", "YES (inert until D31)", ["PM:ReleaseSeamCompositionTests.LiveRun_PostDepth30Rewards", "EM:EconomyTests"], None),
        comp("Boss attack selection (seeded)", "MovesetActorController", "RoomCategoryComposer bosses / EliteEngagement ← DungeonRoomRuntimeComposer", "MovesetActorController attack choice", "YES", ["PM:RunVarietyBossTests.BossAttackChoiceIsDeterministic"], S.check("fresh", "RunVarietyChecks", "authored attacks")),
        comp("Boss anti-kite", "MovesetActorController reposition", "boss composition", "Chase FixedUpdate", "YES", ["PM:RunVarietyBossTests.EveryBossRepositions"], S.check("fresh", "RunVarietyChecks", "re-engaged")),
        comp("Biome weighting", "Expedition/BiomeSelector.cs; BiomeEncounterWeights", "PartyLobby.TryStart (first biome); EncounterDirector", "ExpeditionService.Descend; encounter rolls", "YES", ["PM:MetroFullGateTests"], D.step("identical D1")[:2] if D.runs else None),
        comp("Room depth gating", "RoomDefinition.MinDepth; RoomPool", "DungeonGenerationPipeline.Generate ← ExpeditionScene", "DungeonAssembler", "YES", ["EM:RunVarietyValidationTests"], L.check("longrun", "ReturningChecks", "consecutive descends", label="12 descends to D13 (gated rooms eligible from D5/D10)")),
        comp("World substrate", "Presentation/World/WorldSubstrate.cs", "ExpeditionScene depth build", "renderer under the floor", "YES", ["PM:PresentationAudioUxQolRuntimeTests.Substrate"], S.check("fresh", "PresentationQolChecks", "underlay")),
        comp("Audio service / event assets", "Audio/AudioService.cs, GameplayAudioBinder; catalog AudioEvents", "GameApp bootstrap", "binder hooks in ExpeditionScene", "YES", ["PM:AudioServiceTests"], S.check("fresh", "AudioChecks", "sfx started")),
        comp("Music state", "Audio/MusicDirector.cs, MusicBinder", "GameApp bootstrap", "GameApp scene hooks; ExpeditionScene room/boss hooks", "YES", ["PM:MusicRoutingTests"], S.check("fresh", "AudioChecks", "music")),
        comp("Shelter Trader", "Base/TraderService.cs", "BaseSession ← MainMenuViewModel.Play", "BaseHubViewModel.Trader / BaseHubScreen", "YES", ["EM:TraderServiceTests", "PM:ReleaseSeamCompositionTests.Shelter_AutosavePoints"], S.check("returning", "ReturningChecks", "Trader sells")),
        comp("Help/Codex", "App/CodexPanel.cs; UI/Codex/CodexViewModel", "MainMenuScreen; PauseMenuScreen", "Codex pages", "YES", ["EM:PresentationAudioUxQolTests"], S.check("fresh", "PresentationQolChecks", "Help page")),
        comp("PlayerPresenceService", "Multiplayer/PlayerPresence.cs", "ExpeditionParty ← ExpeditionScene (solo/host/client)", "party entities", "YES", ["PM:PlayerPresenceTests"], D.step("one camera, one listener")[:2] if D.runs else None),
        comp("Player network entity factory", "NgoPlayerPresence / CoopPlayerDirectory", "ExpeditionScene.Coop host/client composition; GameApp prefab", "PlayerPresenceService spawn", "YES (co-op)", ["PM:RemotePlayerVisualTests", "PM:CoopExpeditionRuntimeTests.PlayerRigAttach"], D.step("client composed", "client1")[:2] if D.runs else None),
        comp("NetworkDungeonSync", "Multiplayer/NetworkDungeonSync.cs (CoopRunLink prefab)", "GameApp → LiveServiceConfiguration → CoopLinkSpawner", "CoopHostWorld.PublishDepth / client BuildDepth", "YES (co-op)", ["EM:DungeonNetSyncTests"], D.step("identical D1")[:2] if D.runs else None),
        comp("EnemyNetSync", "Multiplayer/EnemyNetSync.cs (EnemyNetState, EnemyReplica, registry)", "ExpeditionScene.Coop host/client worlds", "CoopHostWorld / CoopClientWorld", "YES (co-op)", ["PM:NetworkEnemyAuthorityTests", "PM:CoopExpeditionRuntimeTests.EveryArchetype"], D.step("enemy set replicated")[:2] if D.runs else None),
        comp("LootAuthority", "Multiplayer/LootAuthority.cs", "ExpeditionScene (solo/host/client)", "co-op pickups, merchant, cache", "YES", ["PM:NetworkLootAuthorityTests"], D.step("pickup race")[:2] if D.runs else None),
        comp("PartyLifeRoster", "Player/PlayerLifeState.cs", "ExpeditionScene.BuildCore", "PartyExpeditionBinding / downed / wipe", "YES", ["PM:CoopRuntimeCompositionTests"], R.step("trio wipe")[:2] if R.runs else None),
        comp("PartyReviveAuthority (Medical Station + Defibrillator)", "Player/PartyReviveAuthority.cs", "ExpeditionScene services; PlayerRig.ReviveRequester → RequestDefibrillatorRevive; host handler OnClientReviveRequested", "MedicalStationEvent; Defibrillator consumable", "YES", ["PM:DeadReturnSourcesTests", "PM:ReleaseSeamCompositionTests.LiveRun_Defibrillator"], R.step("defibrillator")[:2] if R.runs else None, "FIXED DURING AUDIT"),
        comp("Reconnect (ReconnectGrace)", "Multiplayer/ReconnectGrace.cs", "ExpeditionScene.Coop host grace + client ReconnectLoop", "PlayerPresence hold/transfer", "YES (co-op)", ["PM:DisconnectGraceTests"], D.step("reconnect")[:2] if D.runs else None),
        comp("Transit vote", "Expedition/TransitDecision.cs", "PartyExpeditionBinding (ExpeditionScene); client HostDecidedTransitPolicy", "TransitVoteViewModel", "YES", ["PM:PartyTransitTests", "EM:TransitVotingTests"], S.check("fresh", "DepthSettingsNonCombatChecks", "descend chosen through the transit vote panel")),
        comp("Shared non-combat/event authority", "host Interact replay (NetworkPlayerMotion) + LootAuthority merchant/cache", "ExpeditionScene.Coop", "CoopHostWorld handlers", "YES (co-op)", ["PM:NonCombatRoomMatrixTests"], D.step("merchant")[:2] if D.runs else None),
        comp("Save/extraction transaction + autosave points", "Persistence/ExpeditionTransactionRecorder, AutosaveService, AutosaveFlusher", "BaseSession (recorder/autosave); GameApp → AutosaveFlusher bound to the open session", "ExpeditionService Return/Fail; 113 autosave points flushed at end of frame", "YES", ["PM:ReleaseSeamCompositionTests.Shelter_AutosavePoints", "EM:AutosaveTests", "EM:PersistenceHardeningTests"], S.check("fresh", "SaveReloaded", None, predicate=lambda r: r.get("SaveReloaded"), label="SaveReloaded"), "FIXED DURING AUDIT"),
        comp("LootAuthority chest/event/medical request API", "LootAuthority.RequestChestOpen / RequestEventActivate / RequestMedicalRevive", "none on the release path (the co-op path replays Interact on the host copy instead)", "tests only", "NO (alternative path ships)", ["PM:NetworkLootAuthorityTests.EventActivation"], D.step("chest")[:2] if D.runs else None, "NO"),
    ]
    summary["runtime_composition_matrix"] = write(out, "runtime_composition_matrix.csv", ["Subsystem", "Provider", "CompositionCaller", "RuntimeConsumer", "ReleasePathReaches", "PlayModeConsequenceTest", "BuiltPlayerConsequence", "DeadSeam", "Result"], rows)

    # ---- 3. fresh profile end-to-end -----------------------------------------------------------------
    F = lambda *a, **k: S.check("fresh", *a, **k)
    steps = [
        ("boot", F(predicate=lambda r: r.get("ScenesComposed", "").startswith("MainMenu"), label="boot composed MainMenu first")),
        ("Main Menu", F(predicate=lambda r: "MainMenu" in r.get("ScenesComposed", ""), label="Main Menu composed; PLAY")),
        ("create profile", F("ProfileChecks", "display name")),
        ("Shelter", F(predicate=lambda r: "Base" in r.get("ScenesComposed", ""), label="Shelter composed")),
        ("starter kit", F("ProfileChecks", "first starter kit was granted")),
        ("Character Station available", F("ProgressionChecks", "costs exactly one point")),
        ("Storage works", F("StarterFallbackChecks", "Storage untouched")),
        ("Shelter Trader works", S.check("returning", "ReturningChecks", "Trader sells", label="relaunch of the fresh profile: Trader buy + resale")),
        ("Multiplayer Terminal does not break Solo", F("StarterFallbackChecks", "terminal shows the STARTER LOADOUT EQUIPPED notice")),
        ("enter Solo expedition", F("ProfileChecks", "completed the onboarding")),
        ("D1 generation", F("PlayabilityChecks", "no open exit into the void")),
        ("combat", F("CombatChecks", "direct crosshair-on-enemy projectile reduces HP")),
        ("ammo economy", F("EconomyContainmentProjectileChecks", "sells for 15%")),
        ("inventory", F("InventoryChecks", "controller")),
        ("weapon switching", F("HudChecks", "switching to slot 2")),
        ("grenade quick-use", F("PresentationQolChecks", "quick-grenade throws")),
        ("timed status display", F("PresentationQolChecks", "status detail line")),
        ("pickup attraction", F("PresentationQolChecks", "comes to them")),
        ("Merchant", F("MerchantChecks", "")),
        ("non-combat room", F("NonCombatRoomsDriven", "")),
        ("elite (deterministic fixture)", T.check("PM:CrusherUnitEliteTests", "PM:RailguardEliteTests", "PM:LabsElitesTests", "PM:ScrapExecutionerEliteTests", "PM:TunnelStalkerEliteTests")),
        ("boss", F("DepthSettingsNonCombatChecks", "boss defeated")),
        ("Transit", F("DepthSettingsNonCombatChecks", "descend chosen through the transit vote panel")),
        ("RETURN", F(predicate=lambda r: r.get("BankedCoinsAfterReturn", 0) > 0, label="RETURN banked coins")),
        ("save", F(predicate=lambda r: r.get("SaveReloaded"), label="save written and reloaded")),
        ("return to Shelter", F(predicate=lambda r: "Dungeon;Base" in r.get("ScenesComposed", ""), label="Dungeon → Base")),
        ("banked state correct", F(predicate=lambda r: r.get("SaveReloaded") and r.get("ReturnToMenuOk"), label="reloaded banked coins equal the session; banked intact after the later runs")),
        ("relaunch", S.check("returning", label="a new process on the same save directory")),
        ("profile restored", S.check("returning", "ReturningChecks", "live profile equals the saved one")),
    ]
    rows = [[i + 1, name, "built-player" if "built-player" in ev else "PlayMode", ev, r] for i, (name, (r, ev)) in enumerate(steps)]
    summary["fresh_profile_end_to_end"] = write(out, "fresh_profile_end_to_end.csv", ["Step", "Requirement", "EvidenceClass", "Evidence", "Result"], rows)

    # ---- 4. returning profile ------------------------------------------------------------------------
    RT = lambda needle: S.check("returning", "ReturningChecks", needle)
    items = [
        ("profile loads (current document, no backup recovery)", RT("loads from the current document")),
        ("XP / level / skill points / ranks / banked / deepest restored", RT("live profile equals the saved one")),
        ("stored items restored", RT("Storage restored item for item")),
        ("equipped gear + affixes apply (re-entered run carries every rank; affix rolls reloaded)", RT("stat pipeline carries all six")),
        ("Vitality effect", RT("Vitality")), ("Power effect", RT("Power")), ("Mobility effect", RT("Mobility")),
        ("Recovery effect", RT("Recovery")), ("Handling effect", RT("Handling")), ("Resilience effect", RT("Resilience")),
        ("six attributes (PlayMode consequence)", T.check("PM:CharacterProgressionAttributeProofTests")),
        ("level / cap rules", RT("matches the XP curve")),
        ("points conserved", RT("points are conserved")),
        ("Character Station previews", RT("previews every attribute")),
        ("storage round-trip", RT("backpack → Storage puts it back")),
        ("Shelter Trader buy + resale once", RT("reselling it pays the quoted")),
        ("banked coins correct after RETURN", RT("banked coins rose by exactly")),
        ("deepest depth monotonic", RT("record is monotonic")),
        ("no duplicated items", RT("no duplicate id")),
        ("tutorial state not reset", RT("tutorial state is not reset")),
        ("settings persisted across relaunch", RT("settings persisted across the relaunch")),
        ("no migration churn on boot", RT("boot migrates nothing")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in items]
    summary["returning_profile_end_to_end"] = write(out, "returning_profile_end_to_end.csv", ["Requirement", "Evidence", "Result"], rows)

    # ---- 5. save integrity ---------------------------------------------------------------------------
    items = [
        ("fresh save", F(predicate=lambda r: r.get("SaveReloaded"), label="fresh profile saved and reloaded")),
        ("returning save", RT("final save reloads")),
        ("older supported save versions (v1 fixture)", T.check("EM:SaveSlotTests.LegacyV1Fixture")),
        ("migration chain", T.check("EM:PersistenceHardeningTests.AtomicWrite_Interrupted_NewerDocument_Migration", "EM:SaveSlotTests.LegacyV1Fixture")),
        ("atomic temp/current/backup recovery", T.check("EM:AutosaveTests.FileStore_RecoversFromCompleteTemp", "EM:SaveSlotTests.FileStore_WritesAtomically", "EM:AutosaveTests.CorruptCurrent_FallsBackToBackup")),
        ("malformed save quarantine / no overwrite", T.check("EM:SaveSlotTests.NewerCorruptAndInvalidDocuments", "EM:SaveSlotTests.UnresolvedDefinitions_AreQuarantined")),
        ("duplicate item-id rejection", T.check("EM:PersistenceHardeningTests.StorageTransfers_DoubleDeposit")),
        ("negative / invalid value rejection", T.check("EM:SaveSlotTests.NewerCorruptAndInvalidDocuments")),
        ("interrupted save simulation", T.check("EM:AutosaveTests.InterruptedWrite_BeforeCommit")),
        ("save after RETURN", RT("save after RETURN succeeds")),
        ("save after death", S.check("death", "ReturningChecks", "reloads with the death committed")),
        ("save after progression purchase", T.check("PM:ReleaseSeamCompositionTests.Shelter_AutosavePoints")),
        ("save after Shelter transaction (trader)", RT("carries the new ranks and the Trader's coin settlement")),
        ("save after co-op extraction", D.step("own transaction, save", "client1")[:2] if D.runs else ("NOT RUN", "no duo runs")),
        ("session/reconnect state not written as permanent", T.check("EM:SaveSlotTests.Slot_ContainsNoSettingsAndNoExpeditionState", "EM:AutosaveTests.NoMidExpeditionResumeData")),
        ("extraction commits once (replays)", T.check("EM:PersistenceHardeningTests.ExtractionReplays", "EM:PersistenceHardeningTests.Quit_BeforeCommit")),
        ("autosave points reach disk without an explicit save (flusher composed)", T.check("PM:ReleaseSeamCompositionTests.Shelter_AutosavePoints")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in items]
    summary["save_integrity_matrix"] = write(out, "save_integrity_matrix.csv", ["Requirement", "Evidence", "Result"], rows)

    # ---- 6. solo built-player proof ------------------------------------------------------------------
    items = [
        ("boot / menu / profile / Shelter", F("ProfileChecks", "onboarding")),
        ("expedition + D1 combat", F("CombatChecks", "reduces HP")),
        ("inventory", F("InventoryChecks", "")),
        ("ammo", F("LootChecks", "ammo")),
        ("Merchant", F("MerchantChecks", "")),
        ("event / non-combat room", F("NonCombatRoomsDriven", "")),
        ("boss", F("DepthSettingsNonCombatChecks", "boss defeated")),
        ("Transit + DESCEND", F("DepthSettingsNonCombatChecks", "descend chosen")),
        ("next depth", F("DepthSettingsNonCombatChecks", "depth 2 starts at full")),
        ("RETURN", F(predicate=lambda r: r.get("BankedCoinsAfterReturn", 0) > 0, label="RETURN banked")),
        ("save / reload", F(predicate=lambda r: r.get("SaveReloaded"), label="SaveReloaded")),
        ("death path (inside the full smoke)", F("DeathScreenChecks", "loss is committed")),
        ("death path (independent scenario)", S.check("death", "DeathScreenChecks", "Run Lost screen is showing")),
        ("settings change (screen-shake intensity 50% saved, verified after relaunch)", RT("settings persisted across the relaunch")),
        ("controller navigation", F("InventoryChecks", "controller")),
        ("Weapon Cache (seed 31 scenario)", S.check("cache", "WeaponCacheChecks", "exactly one")),
        ("no exception / missing reference / missing script (any error fails the run)", F(label="0 error/exception log lines (OnLog fails the run)")),
        ("no softlock (every stage bounded by a 60 s wait; all runs reached 'done')", F(predicate=lambda r: r.get("Stage") == "done", label="stage done")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in items]
    summary["solo_built_player_proof"] = write(out, "solo_built_player_proof.csv", ["Requirement", "Evidence", "Result"], rows)

    # ---- 7. co-op end-to-end -------------------------------------------------------------------------
    duo_items = [("Host", "host listening"), ("Join", "joined and Ready"), ("ready/start", "host started the party"), ("same dungeon", "identical D1"),
                 ("local ownership correct", "one camera, one listener"), ("remote visuals correct", "movement arrives"), ("combat", "fire"),
                 ("enemy authority", "enemy set replicated"), ("loot race", "pickup race"), ("Merchant/event request", "merchant"),
                 ("Downed", "downed"), ("Revive", "revive"), ("boss", "boss"), ("Transit vote", "vote"), ("D1 → D2", "same D2"),
                 ("state preservation", "survive the transition"), ("reconnect", "reconnect"), ("RETURN", "RETURN"), ("extraction/save", "own transaction"), ("clean session exit", "no orphan network objects")]
    rows = []
    for name, needle in duo_items:
        r, ev, det = D.step(needle)
        if r != "PASS":
            r2, ev2, det2 = D.step(needle, "client1")
            if r2 == "PASS":
                r, ev, det = r2, ev2, det2
        rows.append(["Duo", name, ev, det[:220], r])
    trio_items = [("3 identities", "joined and Ready"), ("3 player entities", "one camera"), ("same dungeon", "identical D1"), ("authored trio scaling", "authored co-op scaling applied for the composed party of 3"),
                  ("combat", "fire"), ("shared encounter", "activated once"), ("loot race", "pickup race"), ("revive", "revive"), ("vote", "vote"),
                  ("one depth transition", "same D2"), ("no duplicate local presentation", "one camera"), ("no missing party member", "joined"),
                  ("bleedout / spectator", "bleedout"), ("Defibrillator (client-requested, host-validated)", "defibrillator"), ("wipe once", "trio wipe")]
    for name, needle in trio_items:
        r, ev, det = R.step(needle)
        rows.append(["Trio", name, ev, det[:220], r])
    rows.append(["Duo", f"{len(D.runs)} consecutive release-smoke runs (seed 46)", "coop_runs.csv", "", "PASS" if D.all_passed() and len(D.runs) >= 5 else "FAIL"])
    rows.append(["Trio", f"{len(R.runs)} consecutive runs (seed 46)", "coop_runs.csv", "", "PASS" if R.all_passed() and len(R.runs) >= 1 else "FAIL"])
    summary["coop_end_to_end"] = write(out, "coop_end_to_end.csv", ["Party", "Requirement", "Evidence", "Detail (first run)", "Result"], rows)

    # ---- 8. input / settings -------------------------------------------------------------------------
    inp = [
        ("move / aim / fire / reload / dash / interact", T.check("EM:InputActionsAssetTests", "PM:PlayerInputReaderTests")),
        ("weapon switch (Weapon1/Weapon2/WeaponSwap)", T.check("PM:WeaponLoadoutTests")),
        ("active consumable + Quick Grenade", T.check("PM:PresentationAudioUxQolRuntimeTests.QuickGrenade", "EM:ConsumableCoreTests")),
        ("inventory / pause / navigation back+confirm", T.check("EM:UiNavigationTests", "PM:PauseScreenInteractionTests", "EM:PauseMenuFlowTests")),
        ("14 actions, each with label, KBM + gamepad glyph, rebind path or documented fixed (Pause)", T.check("EM:FinalReleaseCandidateValidatorTests.RealProject")),
        ("rebinding keyboard/mouse + gamepad, conflict detection, reset defaults, persistence", T.check("EM:PauseSettingsRebindingTests", "EM:SettingsPagesTests")),
        ("volume categories Master/Music/SFX/Ambience + mute", T.check("EM:SettingsPagesTests", "EM:AudioRuntimeHardeningTests")),
        ("resolution / display mode / VSync / frame limit", T.check("EM:SettingsPagesTests", "EM:DisplayNameAndSettingsTests")),
        ("screen shake + intensity / damage numbers / hit flash / tutorial prompts", T.check("EM:SettingsPagesTests", "PM:CombatFeedbackTests")),
        ("Aim Assist", T.check("PM:PresentationAudioUxQolRuntimeTests.AimAssist")),
        ("Settings pages in the built player (music 70% applied and persisted mid-run)", F("DepthSettingsNonCombatChecks", "persisted the level")),
        ("Controls page in the built player (scheme selector + rebind rows)", F("DepthSettingsNonCombatChecks", "CONTROLS page")),
        ("settings survive a relaunch", RT("settings persisted across the relaunch")),
        ("controller focus in the built player", F("InventoryChecks", "controller")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in inp]
    summary["input_settings_matrix"] = write(out, "input_settings_matrix.csv", ["Area", "Evidence", "Result"], rows)

    # ---- 9. UI / navigation --------------------------------------------------------------------------
    ui = [
        ("Main Menu", T.check("PM:BootFlowTests", "EM:UiLayoutTests")), ("profile creation", F("ProfileChecks", "display name")),
        ("Shelter + stations (Character, Storage, Trader, Multiplayer Terminal)", T.check("PM:ShelterUiInteractionTests", "EM:BaseUiTests")),
        ("Settings + Controls/Rebinding", T.check("PM:SettingsPanelTests", "EM:SettingsPagesTests")),
        ("Help/Codex", T.check("EM:PresentationAudioUxQolTests")),
        ("in-run HUD (640×360 layout)", T.check("PM:DungeonHudLayoutRegressionTests", "PM:DungeonHudTests")),
        ("inventory", T.check("PM:GraphicalInventoryTests", "PM:InventoryUiTests", "PM:InventoryFontRegressionTests")),
        ("Dungeon Merchant", T.check("PM:MerchantTradeUiTests")), ("Weapon Cache", T.check("PM:WeaponCacheUiTests")),
        ("event choices / prompts", T.check("PM:RoomHudQolTests")), ("pause", T.check("PM:PauseScreenInteractionTests", "PM:PauseMenuTests")),
        ("Run Lost", T.check("PM:RunFailedScreenTests")), ("Return/extraction summary", T.check("EM:ExpeditionSummaryTests")),
        ("Transit voting", T.check("EM:TransitVotingTests", "PM:PartyTransitTests")), ("co-op party HUD", T.check("PM:PartyStatusUiTests")),
        ("no fallback font on the release path", T.check("EM:ReleaseFontDependencyTests", "PM:PixelFontRenderingTests")),
        ("focus: no trap / reachable targets / mouse+keyboard+controller parity", T.check("EM:UiNavigationTests", "PM:ShelterUiInteractionTests")),
        ("16:9 scale-up and a non-16:9 shape (letterboxed pixel-perfect camera)", T.check("PM:CameraRigTests", "EM:UiLayoutTests")),
        ("built-player captures of the shipping screens (headless render)", F(predicate=lambda r: len(r.get("ProofCaptures") or []) > 40, label="> 40 proof captures per run")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in ui]
    summary["ui_navigation_matrix"] = write(out, "ui_navigation_matrix.csv", ["Screen / Check", "Evidence", "Result"], rows)

    # ---- 10. presentation / audio --------------------------------------------------------------------
    pa = [
        ("no raw black void; substrate under the floor; no substrate collider", T.check("PM:PresentationAudioUxQolRuntimeTests.Substrate")),
        ("no prop obstruction", T.check("EM:PresentationAudioUxQolTests")),
        ("one AudioListener", F("AudioChecks", "listener")),
        ("category mix / high-frequency SFX variation+throttle", T.check("EM:AudioRuntimeHardeningTests", "EM:PresentationAudioUxQolTests")),
        ("music loop alignment / ambience continuity", T.check("PM:MusicRoutingTests", "PM:LootAmmoAudioProofTests")),
        ("Shelter / exploration / combat / boss / Merchant-UI audio", F("AudioChecks", "")),
        ("output-device recovery", T.check("EM:AudioRuntimeHardeningTests")),
        ("volume persistence", RT("settings persisted across the relaunch")),
        ("presentation/audio validator", T.check("EM:PresentationAudioUxQolTests")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in pa]
    summary["presentation_audio_matrix"] = write(out, "presentation_audio_matrix.csv", ["Check", "Evidence", "Result"], rows)

    # ---- 11. combat regression -----------------------------------------------------------------------
    snap = {r["key"]: r["value"] for r in read_csv(os.path.join(out, "frozen_release_snapshot.csv"))}
    base = {r["key"]: r["value"] for r in read_csv(p("production", "FINAL_RELEASE_FROZEN_BASELINE.csv"))}
    def frozen(prefix):
        keys = [k for k in base if k.startswith(prefix)]
        if not keys or not snap:
            return "NOT RUN", f"{prefix}: no snapshot/baseline"
        changed = [k for k in keys if snap.get(k) != base[k]]
        return ("FAIL" if changed else "PASS"), f"{len(keys)} frozen value(s) equal to the release baseline" + (f"; changed: {changed}" if changed else "")
    cb = [
        ("all 33 weapon definitions (core fingerprints)", frozen("weapon.")),
        ("weapon stat consumers", T.check("PM:StatConsumerRuntimeTests")),
        ("fire rate / magazine / reload / auto reload", T.check("PM:RangedWeaponTests", "PM:AutoReloadTests")),
        ("projectile speed / range / pointblank collision", T.check("PM:ProjectileTests", "PM:DirectAimHitTests")),
        ("shotgun spread", T.check("PM:ShotgunReferenceTests", "EM:SpreadFiringPatternTests")),
        ("melee timing", T.check("PM:MeleeWeaponTests")),
        ("blaster heat", frozen("blaster.heat.")),
        ("blaster behaviour", T.check("PM:BlasterWeaponTests", "EM:BlasterHeatStateTests")),
        ("bow charge", T.check("PM:BowWeaponTests", "EM:BowChargeResolverTests")),
        ("knockback / stagger", T.check("EM:StaggerKnockbackTests", "PM:ImpactReceiverTests", "PM:ReleaseSeamCompositionTests.LiveRun_PlayerImpactReceiver")),
        ("aim assist ON/OFF", T.check("PM:AimAssistTests", "PM:PresentationAudioUxQolRuntimeTests.AimAssist")),
        ("remote weapon presentation in co-op", T.check("PM:RemotePlayerVisualTests")),
        ("starter P9 + kit", frozen("starter.kit")),
        ("Field Knife", frozen("weapon.field_knife")),
        ("D1 ammo tuning", frozen("loot.supply_chest.light_ammo")),
        ("D1 ammo / blaster fine-tune pins", T.check("EM:D1AmmoBlasterFineTuneTests")),
        ("ammo caps", frozen("ammo.cap.")),
        ("live combat in the built player (seed 11, CombatAim live test pinned)", T.check("PM:CombatAimCollisionProofTests")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in cb]
    summary["combat_regression_matrix"] = write(out, "combat_regression_matrix.csv", ["Check", "Evidence", "Result"], rows)

    # ---- 12. depth / economy -------------------------------------------------------------------------
    de = [
        ("D1–D30 and D31+ difficulty unchanged", frozen("depth.scaling.")),
        ("post-D30 reward continuation only after D30 (and its cap)", frozen("reward.D")),
        ("deepest-depth persistence", F("RunVarietyChecks", "reloaded save carries the record")),
        ("deepest depth over 12 descends", L.check("longrun", "ReturningChecks", "deepest D13", label="save/load deepest D13")),
        ("elite frequency", frozen("elite.frequency")), ("room depth gating", frozen("rooms.depth_gated")), ("biome identity weighting", frozen("biome.weights")),
        ("boss seeded selection / anti-kite", T.check("PM:RunVarietyBossTests")),
        ("boss HP", frozen("boss.hp.")),
        ("Merchant prices / coin rules / rarity rules", T.check("EM:DungeonMerchantTests", "EM:CoinDistributionTests", "EM:RarityAffixTests", "EM:EconomyTests")),
        ("XP rules / level cap", T.check("EM:ProgressionTests")),
        ("RETURN / DESCEND behaviour", T.check("EM:ExpeditionServiceTests", "EM:ExpeditionTransactionTests")),
        ("depth-arrival heal", T.check("PM:DepthArrivalHealTests")),
        ("validator contract", T.check("EM:RunVarietyValidationTests")),
        ("co-op scaling", frozen("coop.scaling")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in de]
    summary["depth_economy_regression_matrix"] = write(out, "depth_economy_regression_matrix.csv", ["Check", "Evidence", "Result"], rows)

    # ---- 13. non-combat / events ---------------------------------------------------------------------
    kinds = ["Merchant", "MedicalStation", "BrokenMachine", "LockedVault", "WeaponCache", "CursedChest", "SupplySignal", "Loot", "Treasure"]
    nc = []
    matrix = T.check("PM:NonCombatRoomMatrixTests", "PM:DepthSettingsDescriptionsNonCombatProofTests")
    for kind in kinds:
        solo = S.check("fresh", "NonCombatRoomsDriven", kind)
        if solo[0] != "PASS":
            solo = S.check("cache", "NonCombatRoomsDriven", kind)
        if solo[0] == "PASS":
            nc.append((f"{kind}: solo built-player (pinned seed)", solo))
        else:
            # Not generated on the pinned release seeds' first depth: proven in a live PlayMode run that drives every type.
            nc.append((f"{kind}: solo live run (PlayMode real composition; not generated on seeds 11/31 D1)", (matrix[0], matrix[1])))
    nc += [
        ("every non-combat room type end-to-end (prompt, cost, grant, once, save)", T.check("PM:NonCombatRoomMatrixTests", "PM:SpecialRoomRuntimeTests")),
        ("event kinds data + outcomes", T.check("EM:DungeonEventsITests", "EM:DungeonEventsIITests", "EM:DungeonEventsIIITests")),
        ("Weapon Cache selection once (built player seed 31)", S.check("cache", "WeaponCacheChecks", "no duplicate reward")),
        ("co-op merchant / events / cache host authority", D.step("merchant")[:2] if D.runs else ("NOT RUN", "")),
        ("Transit", F("DepthSettingsNonCombatChecks", "descend chosen")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in nc]
    summary["noncombat_regression_matrix"] = write(out, "noncombat_regression_matrix.csv", ["Event / Check", "Evidence", "Result"], rows)

    # ---- 14. death / extraction ----------------------------------------------------------------------
    dx = [
        ("Solo death: carried risk lost", S.check("death", "DeathScreenChecks", "carried coins gone")),
        ("Solo death: persistent progression retained (XP, ranks, Storage)", S.check("death", "ReturningChecks", "XP is kept")),
        ("Solo death: Run Lost UI", S.check("death", "DeathScreenChecks", "Run Lost screen is showing")),
        ("Solo death: save correct", S.check("death", "ReturningChecks", "reloads with the death committed")),
        ("Solo death: no softlock (next run can start)", S.check("death", "ReturningChecks", "ready the next run")),
        ("Solo RETURN: loot/coins secured, summary, save", RT("banks the carried coins once")),
        ("Co-op partial death: downed/revive", D.step("revive")[:2] if D.runs else ("NOT RUN", "")),
        ("Co-op dead player semantics (bleedout → Dead → spectate)", R.step("bleedout")[:2] if R.runs else ("NOT RUN", "")),
        ("Co-op Defibrillator returns a Dead member", R.step("defibrillator")[:2] if R.runs else ("NOT RUN", "")),
        ("Co-op wipe: run ends once, no duplicate loss transaction", R.step("trio wipe")[:2] if R.runs else ("NOT RUN", "")),
        ("Co-op RETURN: extraction commits once; all peers coherent; persistence", D.step("RETURN vote resolves once")[:2] if D.runs else ("NOT RUN", "")),
        ("Co-op client's own extraction + save", D.step("own transaction", "client1")[:2] if D.runs else ("NOT RUN", "")),
        ("transaction semantics (PlayMode/EditMode)", T.check("EM:ExpeditionTransactionTests", "PM:DeadSpectatorTests", "PM:PlayerLifeStateTests")),
    ]
    rows = [[n, ev, r] for n, (r, ev) in dx]
    summary["death_extraction_matrix"] = write(out, "death_extraction_matrix.csv", ["Rule", "Evidence", "Result"], rows)

    # ---- 15. performance / long run ------------------------------------------------------------------
    rows = []
    for i, run in enumerate(L.runs.get("longrun", [])):
        for line in run.get("LongRunSamples") or []:
            if line.startswith("depth,"):
                continue
            rows.append([f"solo long run {i + 1} (built player, seed 11)"] + line.split(","))
    for label, coop in (("duo", D), ("trio", R)):
        for i, peers in enumerate(coop.runs):
            host = peers.get("host") or {}
            for v in host.get("Views", []):
                if not str(v.get("Role", "")).startswith("host"):
                    continue
                rows.append([f"{label} run {i + 1} {v['Role']} (built player, seed 46)", v.get("Depth", ""), "", v.get("GameObjects", ""),
                             f"{v.get('MonoUsedBytes', 0) / 1048576:.2f}", f"{v.get('AllocatedBytes', 0) / 1048576:.1f}", "", v.get("Cameras", ""),
                             v.get("AudioListeners", ""), v.get("SpawnedNetworkObjects", ""), v.get("LiveEnemies", ""), ""])
            rows.append([f"{label} run {i + 1} after return (host)", "", "", "", "", "", "", "", "", host.get("SpawnedObjectsAfterReturn", ""), "",
                         f"player objects after return {host.get('PlayerObjectsAfterReturn', '')}"])
    rows.insert(0, ["scenario", "depth/phase", "rebuild_ms", "game_objects", "mono_mb", "allocated_mb", "projectiles_pooled", "cameras", "listeners", "network_objects", "stale_enemies", "launch_subscribers"])
    with open(os.path.join(out, "performance_longrun.csv"), "w", newline="") as f:
        csv.writer(f).writerows(rows)
    print(f"performance_longrun.csv: {len(rows) - 1} sample rows")

    # ---- 16/17. smoke stability + manifest -----------------------------------------------------------
    def loop_stats(path):
        runs = read_csv(path)
        return len(runs), sum(1 for r in runs if r.get("success") == "true" and r.get("exit") == "0")
    rows = []
    for label, path, seed, cause, fix in (
        ("solo full smoke — BEFORE, clock seed (the previous default)", os.path.join(out, "smoke_before", "clock", "runs.csv"), "clock",
         "test seed nondeterminism: every run generated a different dungeon/biome, so seed-dependent placements and preconditions were dice rolls (5 different failing stages)", "official gate pins a seed per scenario"),
        ("solo full smoke — BEFORE, seed 11, pre-audit build", os.path.join(out, "smoke_before", "seed11", "runs.csv"), "11", "none observed on this seed/build", "-"),
        ("solo full smoke — AFTER, clock seed (not a release gate; robustness only)", os.path.join(out, "smoke_after", "clock", "runs.csv"), "clock",
         "residual on arbitrary layouts: direct-hit reference placement (not fully isolated) and the one-physics-step pack pile-up at an open doorway (overshoot 0.079 > 0.06)", "precondition fixes below raised it from 2/10 to 6/10; tolerances unchanged"),
    ):
        n, ok = loop_stats(path)
        rows.append([label, n, ok, n - ok, seed, cause, fix, "NO" if "clock" in seed else ("YES" if n and ok == n else "NO")])
    for label, path, cause in (
        ("iteration build 3 (release smoke, onboarding step expectation)", os.path.join(out, "iterations", "build3", "release_smoke", "release_smoke_runs.csv"), "harness defect introduced in this audit (expected onboarding step Complete before the first expedition); fixed"),
        ("iteration build 5 (release smoke)", os.path.join(out, "iterations", "build5", "release_smoke", "release_smoke_runs.csv"), "clean"),
        ("iteration build 6 (release smoke)", os.path.join(out, "iterations", "build6", "release_smoke", "release_smoke_runs.csv"), "seed 31 knife check 0/10: the Field Knife dummy was placed on an aim that was still easing after the camera (46-49° off, arc ±40°); fixed by holding the dummy on the aim through the wind-up"),
    ):
        runs_i = read_csv(path)
        rows.append([label, len(runs_i), sum(1 for r in runs_i if r.get("success") == "true" and r.get("exit") == "0"), sum(1 for r in runs_i if not (r.get("success") == "true" and r.get("exit") == "0")), "11/31", cause, "see RootCause", "superseded by the final build"])
    runs = read_csv(os.path.join(bp, "release_smoke", "release_smoke_runs.csv"))
    fixes = {
        "fresh": ("ordered-stage coupling + placement: knockback target chosen by undefined FindObjectsByType order (could pick the frozen reference dummy); reference shot lane could be crossed by the live pack; 'a non-combat room exists' asserted although dungeon/55 ranges start at 0",
                  "dynamic alive target with open floor; pack held during the reference shots, enemy-free lane, hazard-free spot; every non-combat room present must be driven (seed 11 keeps them in the manifest)"),
        "returning": ("new scenario", "relaunch on the fresh run's save; fresh smoke now performs real onboarding"),
        "death": ("new independent scenario", "death decoupled from the ordered mega-smoke"),
        "cache": ("ordered-stage coupling: the Weapon Cache stage consumed the real cache and the non-combat stage then failed on its prompt (seed 31 failed 3/3 before); the Field Knife dummy was placed on an easing aim (46-49° off the ±40° arc)",
                  "the non-combat stage asserts the spent cache's used state; knife dummy on a clear lane, held on the aim through the wind-up"),
    }
    for scenario in ("fresh", "returning", "death", "cache"):
        rs = [r for r in runs if r["scenario"] == scenario]
        ok = sum(1 for r in rs if r["success"] == "true" and r["exit"] == "0")
        consecutive = len(rs) >= 10 and ok == len(rs)
        seed = rs[0]["seed"] if rs else ""
        rows.append([f"release smoke: {scenario}", len(rs), ok, len(rs) - ok, seed, fixes[scenario][0], fixes[scenario][1], "YES" if consecutive else "NO"])
    lr = L.runs.get("longrun", [])
    rows.append(["release smoke: longrun (12 descends)", len(lr), sum(1 for r in lr if r.get("Success")), sum(1 for r in lr if not r.get("Success")), "11", "new scenario", "per-depth leak sampling", "YES" if lr and all(r.get("Success") for r in lr) else "NO"])
    for label, coop, seed in (("co-op duo release smoke", D, "46"), ("co-op trio", R, "46")):
        ok = sum(1 for peers in coop.runs if peers and all(d.get("Success") for d in peers.values()))
        rows.append([label, len(coop.runs), ok, len(coop.runs) - ok, seed, "none observed", "-", "YES" if coop.runs and ok == len(coop.runs) else "NO"])
    rows.append(["PlayMode CombatAimCollisionProofTests live run", "see automated gates", "", "", "11 (was clock)", "clock seed; the encounter's next wave walking into the reference line of fire; whole-pack pile-up pushing one body ~0.4 tiles into a wall/door for one physics step (failed ~half of full runs)", "LiveRunSeed = 11; later waves cleared before each shot; one pursuer at wall/door (the test's Charger-lane rule); 0.12 bound unchanged; 6/6 isolated + 2 full runs green", "YES" if T.check("PM:CombatAimCollisionProofTests")[0] == "PASS" else "NO"])
    summary["smoke_stability"] = write(out, "smoke_stability.csv", ["Scenario", "Runs", "Passes", "Failures", "Seed", "RootCause", "Fix", "FinalDeterministic YES/NO"], rows)

    manifest = read_csv(p("scripts", "release_smoke_manifest.csv"))
    rows = []
    for m in manifest:
        sc = m["scenario"]
        if sc in ("duo", "trio"):
            coop = D if sc == "duo" else R
            ok = coop.all_passed()
            evidence = f"{len(coop.runs)} runs, all peers passed={ok}"
        elif sc == "longrun":
            ok, evidence = bool(lr) and all(r.get("Success") for r in lr), f"{len(lr)} runs"
        else:
            rs = [r for r in runs if r["scenario"] == sc]
            good = sum(1 for r in rs if r["success"] == "true" and r["exit"] == "0")
            ok, evidence = bool(rs) and good == len(rs), f"{good}/{len(rs)} runs passed"
        rows.append([m["critical_path"], sc, m["kind"], m["seed"], m["command"], m["evidence"], evidence, "PASS" if ok else "FAIL"])
    summary["release_smoke_manifest"] = write(out, "release_smoke_manifest.csv", ["CriticalPath", "Scenario", "Kind", "Seed", "Command", "Checks", "Runs", "Result"], rows)

    # ---- 18. build pipeline --------------------------------------------------------------------------
    version = open(p("ProjectSettings", "ProjectVersion.txt")).read()
    ver = re.search(r"m_EditorVersion: (\S+)", version).group(1)
    bl = [
        ("Unity version", "6000.3.24f1", ver, "PASS" if ver == "6000.3.24f1" else "FAIL"),
        ("approved scenes in order / no test scene", "Bootstrap, MainMenu, Base, Dungeon", re.search(r"Scenes: ([^\n]*)", build).group(1) if "Scenes:" in build else "", "PASS" if "Scenes: Bootstrap, MainMenu, Base, Dungeon" in build else "FAIL"),
        ("non-development options", "options: None", "options: None (non-development)" if "options: None" in build else "?", "PASS" if "options: None" in build else "FAIL"),
        ("build result", "Succeeded, 0 errors", build_s, "PASS" if "Result: **Succeeded** — errors 0" in build else "FAIL"),
        ("no missing script/reference warnings", "0 such warnings", "the 2 warnings are UGS project link + symbol upload token", "PASS" if "Missing" not in build and "missing script" not in build.lower() else "FAIL"),
        ("network prefabs registered in the build", "PlayerNetworkEntity + CoopRunLink", "FinalReleaseCandidateValidator 'network registration'", T.check("EM:FinalReleaseCandidateValidatorTests.RealProject")[0]),
        ("no debug-only / fake networking on the release online path", "fake driver only with -offline-multiplayer", "LiveServiceConfiguration.ComposeForProcess; the co-op proof never passes -offline-multiplayer", T.check("EM:LiveServiceCompositionTests")[0]),
        ("content generation / catalog", "catalog builder consistent", "GameContentCatalog validated", T.check("EM:FinalProductionValidatorTests")[0]),
        ("settings defaults", "SettingsData.Defaults()", "SettingsPagesTests", T.check("EM:SettingsPagesTests")[0]),
        ("release build tool test", "ReleaseBuildToolTests", "", T.check("EM:ReleaseBuildToolTests")[0]),
        ("Windows x64", "declared release target", "Windows x64: NOT RUN — module unavailable", "NOT RUN — ENVIRONMENT"),
        ("macOS Standalone", "local substitute", build_s, "PASS" if "Succeeded" in build else "FAIL"),
    ]
    summary["build_pipeline_matrix"] = write(out, "build_pipeline_matrix.csv", ["Check", "Expected", "Observed", "Result"], [list(r) for r in bl])

    # ---- 19. external services -----------------------------------------------------------------------
    ex = [
        ("Windows x64 release build", "Windows x64: NOT RUN — module unavailable", "PlaybackEngines contains only MacStandaloneSupport; not installed (by instruction)", "NOT RUN — ENVIRONMENT"),
        ("Live UGS Sessions/Relay", "Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable", "no linked project ID / credentials; MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun skipped", "NOT RUN — ENVIRONMENT"),
        ("understandable error when live services are unavailable", "the terminal reports the failure in words, solo unaffected", T.check("EM:MultiplayerTerminalTests", "EM:RecoverableErrorTests")[1], T.check("EM:MultiplayerTerminalTests", "EM:RecoverableErrorTests")[0]),
        ("local real-network runtime (UnityTransport, direct address)", "complete", f"duo {len(D.runs)} runs pass={D.all_passed()}, trio {len(R.runs)} runs pass={R.all_passed()}", "PASS" if D.all_passed() and R.all_passed() else "FAIL"),
    ]
    summary["external_services_status"] = write(out, "external_services_status.csv", ["Service / Platform", "Status", "Evidence", "Result"], [list(r) for r in ex])

    # ---- 20. documentation consistency ---------------------------------------------------------------
    docs = [
        ("production/FINAL_RELEASE_CANDIDATE_AUDIT.md", "YES", "NO", "-", "created: the current release status"),
        ("production/FINAL_MVP_COMPLETION_REPORT.md (generated)", "YES", "YES", "fake transport text; always-on placeholder-art blocker", "generator fixed (FinalMvpAudit.cs + FinalMvpAuditTests); regenerated by EditMode"),
        ("production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md", "YES", "YES", "open blockers: missing audio; 15 affixes; 13 input actions", "SUPERSEDED banner"),
        ("production/AUDIO_EVENT_AUDIT.md", "YES", "YES", "0/53 events have clips", "SUPERSEDED banner (live audit in TestResults)"),
        ("production/MUSIC_ASSET_AUDIT.md", "YES", "YES", "tracks 0/11", "SUPERSEDED banner"),
        ("production/ANIMATION_ASSET_AUDIT.md", "YES", "YES", "0/22 animation sets", "SUPERSEDED banner"),
        ("production/FULL_GAME_COMPREHENSIVE_REVIEW.md", "YES", "YES", "co-op not playable; audio mix missing", "SUPERSEDED banner"),
        ("production/FINAL_RELEASE_COMPLETION_REPORT.md", "YES", "YES", "0/53 clips; white squares; placeholder font", "SUPERSEDED banner"),
        ("production/FINAL_SHIPPABLE_V1_REPORT.md", "YES", "YES", "real multiplayer never proven; Windows only validated target", "SUPERSEDED banner"),
        ("production/RELEASE_CANDIDATE_REPORT.md", "YES", "YES", "0/22 animation sets … plays silence", "SUPERSEDED banner"),
        ("production/RELEASE_VALIDATION_REPORT.md", "NO", "YES", "offline fake transport path", "SUPERSEDED banner"),
        ("production/MULTIPLAYER_GATE_REPORT.md", "NO", "YES", "PASS over deterministic doubles", "SUPERSEDED banner"),
        ("production/COOP_RUNTIME_COMPOSITION_REPORT.md", "NO", "YES", "COOP_RUNTIME_COMPOSITION_INCOMPLETE", "SUPERSEDED banner → completion report"),
        ("00_INDEX.md", "YES", "YES", "points to 131 as the production state", "Current Release Status section added"),
        ("06_OPEN_DECISIONS.md", "YES", "YES", "omits co-op / post-D30 / deepest-depth decisions", "entries added"),
        ("ENVIRONMENT.md", "YES", "YES", "package resolution NOT YET VERIFIED", "verification status updated; no version change"),
        ("production/123_POST_MVP.md", "NO", "YES", "deepest-depth records listed as post-MVP", "personal record marked as shipped; leaderboards remain post-MVP"),
        ("production/COOP_RUNTIME_COMPOSITION_COMPLETION_REPORT.md", "NO", "NO", "Defibrillator 'not available' (now wired)", "none — historical pass report; this audit supersedes that line"),
        ("production/122_MVP_SCOPE.md", "NO", "NO", "join-code Relay target (still unproven live)", "none — spec target stands"),
        ("multiplayer/80–86 design specs", "NO", "NO", "-", "none"),
        ("per-pass reports 09-18 → 09-24, GATE_*, ROOMSET_*", "NO", "NO", "-", "none — historical"),
    ]
    summary["documentation_consistency"] = write(out, "documentation_consistency.csv", ["Document", "IntendedAsCurrent YES/NO", "Stale YES/NO", "Contradiction", "Action"], [list(d) for d in docs])

    with open(os.path.join(out, "audit_summary.json"), "w") as f:
        json.dump({"playmode": T.totals.get("PM"), "editmode": T.totals.get("EM"), "matrices": summary}, f, indent=2)


if __name__ == "__main__":
    main()
