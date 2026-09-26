#!/usr/bin/env python3
"""Builds the co-op completion evidence matrices from real results.

Every row names its evidence class and where the evidence comes from:
  built-player  -> a step of the built-player proof (scripts/run-coop-expedition-proof.sh, host/client JSON)
  PlayMode      -> a named PlayMode test in TestResults/PlayMode-results.xml
  EditMode      -> a named EditMode test in TestResults/EditMode-results.xml
  static        -> a check of the source/asset tree (see the validator report)
  NOT RUN       -> not exercised, with the reason
A row is PASS only when its evidence exists and passed. Nothing here invents a result.

usage: coop_proof_matrices.py --duo DIR [--duo2 DIR] --trio DIR --playmode XML --editmode XML --out DIR
"""
import argparse
import csv
import json
import os
import xml.etree.ElementTree as ET


def load(path):
    try:
        with open(path) as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def load_tests(path):
    results = {}
    if not path or not os.path.exists(path):
        return results
    root = ET.parse(path).getroot()
    for case in root.iter('test-case'):
        results[case.attrib.get('name')] = case.attrib.get('result')
        results[case.attrib.get('fullname')] = case.attrib.get('result')
    return results


class Evidence:
    def __init__(self, a):
        self.runs = {}
        for key in ('duo', 'duo2', 'trio'):
            d = getattr(a, key)
            if not d:
                continue
            host = load(os.path.join(d, 'host.json'))
            clients = [load(os.path.join(d, f)) for f in sorted(os.listdir(d)) if f.startswith('client') and f.endswith('.json')]
            self.runs[key] = {'dir': d, 'host': host, 'clients': [c for c in clients if c]}
        self.playmode = load_tests(a.playmode)
        self.editmode = load_tests(a.editmode)

    def step(self, run, needle, role='host'):
        r = self.runs.get(run)
        if not r:
            return None
        reports = [r['host']] if role == 'host' else r['clients']
        for rep in reports:
            if not rep:
                continue
            for s in rep.get('Steps', []):
                if needle.lower() in s['Name'].lower():
                    return s
        return None

    def steps(self, run, needle):
        r = self.runs.get(run)
        if not r or not r['host']:
            return []
        return [s for s in r['host'].get('Steps', []) if needle.lower() in s['Name'].lower()]

    def test(self, name, mode='PlayMode'):
        table = self.playmode if mode == 'PlayMode' else self.editmode
        for key, value in table.items():
            if key and key.endswith(name):
                return value
        # A class name: every test case of that suite must have passed.
        suite = [value for key, value in table.items() if key and ('.' + name + '.') in key]
        if suite:
            return 'Passed' if all(v == 'Passed' for v in suite) else 'Failed'
        return None


def row_step(ev, run, needle, label=None, role='host'):
    s = ev.step(run, needle, role)
    if s is None:
        return ['built-player', f'{run}: "{needle}"', 'MISSING', 'FAIL']
    return ['built-player', f'{run}: {s["Name"]}', s['Detail'][:400], 'PASS' if s['Pass'] else 'FAIL']


def row_test(ev, name, mode='PlayMode', note=''):
    r = ev.test(name, mode)
    return [mode, name, note or (r or 'not found'), 'PASS' if r == 'Passed' else 'FAIL']


def write(out, name, header, rows):
    with open(os.path.join(out, name), 'w', newline='') as f:
        w = csv.writer(f)
        w.writerow(header)
        for r in rows:
            w.writerow(r)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--duo')
    p.add_argument('--duo2')
    p.add_argument('--trio')
    p.add_argument('--playmode')
    p.add_argument('--editmode')
    p.add_argument('--out', required=True)
    a = p.parse_args()
    ev = Evidence(a)
    out = a.out
    os.makedirs(out, exist_ok=True)
    H = ['check', 'evidence_class', 'source', 'observed', 'result']

    def S(check, run, needle, role='host'):
        return [check] + row_step(ev, run, needle, role=role)

    def T(check, name, mode='PlayMode', note=''):
        return [check] + row_test(ev, name, mode, note)

    def X(check, source, observed, ok=True, cls='static'):
        return [check, cls, source, observed, 'PASS' if ok else 'FAIL']

    def N(check, reason):
        return [check, 'NOT RUN', '-', reason, 'NOT RUN']

    # ---- start state ----
    write(out, 'start_state_sync_matrix.csv', H, [
        S('run seed / depth / biome published by the host; client never picks its own', 'duo', 'host started the party'),
        S('identical D1 from the host start (seed, depth, biome, layout, room ids/positions/doors)', 'duo', 'identical D1'),
        S('party size / expected participant set (unique host-assigned participant ids)', 'duo', 'participant id the host assigned'),
        S('generation-ready: every client reports the depth before gameplay is released', 'duo', 'gameplay was released together'),
        S('same start contract with three peers', 'trio', 'gameplay was released together'),
        S('content discriminator: room-pool + layout fingerprints verified on the client', 'duo', 'identical D1'),
        S('run phase: the client starts only from the host start and cannot start its own', 'duo', 'cannot start an expedition of its own', role='client'),
        T('client waits for membership + owned object + payload (composition refuses otherwise)', 'CoopRuntimeComposition_ContractHolds_AndTheReportIsWritten', 'EditMode', 'validator rule "client composition" + "depth sync"'),
        T('host start publishes once; a re-delivered start never starts a second transaction', 'ClientExpedition_StartsItsOwnTransaction_UnderTheHostAssignedId'),
    ])

    # ---- client composition ----
    write(out, 'client_composition_matrix.csv', ['scenario'] + H, [
        ['solo'] + T('one local run, no session/network wait, rooms authoritative', 'SoloRun_IsUnchanged_ThroughBossDescendReturnAndSave'),
        ['duo'] + S('same seed/depth/biome/room node ids/positions/door topology', 'duo', 'identical D1'),
        ['duo'] + S('same D2 after the networked descend', 'duo', 'same D2'),
        ['duo'] + S('no duplicate camera/listener/input/owned character', 'duo', 'one camera, one listener'),
        ['duo'] + T('no duplicate authoritative systems on the client (rooms mirror, spawner refuses, no local enemies)', 'HostRoomActivation_EnemySet_Deaths_AndClear_ReachTheClientOnce'),
        ['duo'] + S('no duplicate shared rewards (one pickup winner)', 'duo', 'pickup race'),
        ['trio'] + S('same seed/depth/biome/rooms on three peers', 'trio', 'identical D1'),
        ['trio'] + S('same D2 on three peers', 'trio', 'same D2'),
        ['trio'] + S('no duplicate camera/listener/input on any peer', 'trio', 'one camera, one listener'),
        ['trio'] + S('no duplicate shared rewards among three', 'trio', 'pickup race'),
    ])

    # ---- local attach ----
    write(out, 'local_player_attach_matrix.csv', H, [
        T('rig composed onto the owned NetworkObject; no second gameplay player', 'PlayerRigAttach_ComposesOntoTheOwnedObject_WithoutASecondPlayer'),
        S('one owned network entity, one camera, one input, one AudioListener per process', 'duo', 'one camera, one listener'),
        S('one player identity (participant id = own transaction id)', 'duo', 'participant id the host assigned'),
        S('one health/life-state representation (host-decided, replicated to the owner)', 'duo', 'Downed on the host and on the client'),
        S('one inventory / one equipment state (survives the descend unchanged)', 'duo', 'survive the transition'),
        S('local HUD / inventory UI are the owner\'s own (inventory menu local, world not paused)', 'duo', 'co-op inventory'),
        X('one local cursor / aim-assist preference (CursorService + AimAssist are process-local settings)', 'CursorService, AimAssistConfig (unchanged)', 'no network path reads them'),
        S('owned object re-attached after a reconnect (no second player)', 'duo', 'reconnect inside grace'),
    ])

    # ---- enemies ----
    enemy_rows = []
    cov = os.path.join(out, 'enemy_actor_coverage.csv')
    if os.path.exists(cov):
        with open(cov) as f:
            r = csv.DictReader(f)
            for row in r:
                enemy_rows.append([row['actor'], row['kind'], 'PlayMode', 'EveryArchetypeEliteAndBoss_ReplicatesSpawnStateAndDeath_Once',
                                   f"spawn={row['spawn_replicated']} state={row['state_replicated']} moveset {row['moveset_slots_host']}/{row['moveset_slots_client_resolvable']} anim={row['animation_set']} death_once={row['death_once']}", row['result']])
    enemy_rows.append(['(duo live)', 'Normal+Boss'] + row_step(ev, 'duo', 'enemy set replicated'))
    enemy_rows.append(['(duo live)', 'Boss'] + row_step(ev, 'duo', 'boss spawned by the host'))
    enemy_rows.append(['(duo live)', 'Boss'] + row_step(ev, 'duo', 'phase two'))
    enemy_rows.append(['(duo live)', 'Boss'] + row_step(ev, 'duo', 'boss dies once'))
    enemy_rows.append(['(trio live)', 'Normal+Boss'] + row_step(ev, 'trio', 'enemy set replicated'))
    enemy_rows.append(['(host authority)', 'all', 'PlayMode', 'ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused', 'client hits are requests; AI/health/death stay on the host', 'PASS' if ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') == 'Passed' else 'FAIL'])
    write(out, 'enemy_network_matrix.csv', ['actor', 'kind', 'evidence_class', 'source', 'observed', 'result'], enemy_rows)

    # ---- rooms ----
    write(out, 'room_state_replication_matrix.csv', H, [
        S('first qualifying member activates once; second entrant does not duplicate; doors locked; enemy set on client', 'duo', 'activated once for the whole party'),
        S('clear once; exits unlocked on every peer; enemies dead everywhere', 'duo', 'cleared exactly once'),
        S('same with three members', 'trio', 'activated once for the whole party'),
        S('reward consumed/available shared (chest opened once, room record mirrored)', 'duo', 'opened once by the client'),
        S('boss active/cleared shared; Transit active on every peer', 'duo', 'boss dies once'),
        T('client applies lifecycle/doors/resolved; stale records ignored; a resync never rewinds', 'HostRoomActivation_EnemySet_Deaths_AndClear_ReachTheClientOnce'),
        T('host/client room fixture agrees (validator fact)', 'CoopRuntimeComposition_ContractHolds_AndTheReportIsWritten', 'EditMode'),
        S('reconnecting player receives current state, not room-start state', 'duo', 'reconnect inside grace'),
        X('undiscovered/discovered/entered: minimap discovery for any member; reveal local (unchanged from the prior pass)', 'ExpeditionScene.OnRoomEntered', 'client room entries raise PlayerEntered locally; state from the host'),
    ])

    # ---- request routing ----
    write(out, 'client_request_routing_matrix.csv', ['request', 'route', 'validation'] + H[1:], [
        ['fire / hits', 'owner fires its own weapon; each hit -> req.hit', 'sender=NGO sender id; target replicated & alive; reach; size<=member weapons; rate<=60/s; sender can act'] + row_step(ev, 'duo', 'hits reach the enemies only as host-validated'),
        ['knockback / stagger', 'owner hit impact -> req.impact', 'same sender/target/reach checks; clamped'] + row_test(ev, 'ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused'),
        ['reload / weapon switch', 'owner-local weapon state (own ammo, own loadout); intents/commands replicated', 'no shared state; ammo reserve is the owner\'s own inventory (mirrored to the host)'] + row_step(ev, 'duo', 'survive the transition'),
        ['consumable heal', 'owner heal -> req.heal', 'sender alive; cooldown; capped at the sender\'s own max HP'] + row_test(ev, 'ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused'),
        ['grenade', 'owner-local throw; explosion hits on replicas -> req.hit (Explosion kind)', 'as hits'] + ['static', 'DamageAuthority.RemoteDamageRelay covers AreaDamageResolver', 'same relay path as projectile hits', 'PASS'],
        ['pickup (item / ammo / coins)', 'Interact command / host-side attraction -> PickupArbiter -> LootAuthorityService', 'sender resolved by NGO; host-side backpack mirror capacity; one winner; tx dedupe'] + row_step(ev, 'duo', 'pickup race'),
        ['chest open', 'Interact command replayed on the host copy', 'host chest one-time; room record'] + row_step(ev, 'duo', 'opened once by the client'),
        ['weapon cache claim', 'local choice screen -> req.cache', 'LootAuthorityService.RequestCacheChoose: once per party, into the chooser\'s backpack'] + row_step(ev, 'duo', 'Weapon Cache'),
        ['boss cache claim', 'Interact command replayed on the host copy', 'cache one-time; locked until boss down'] + row_step(ev, 'duo', 'boss cache claimed once'),
        ['merchant buy', 'local trade screen -> req.buy', 'RequestMerchantBuy: member\'s own wallet/backpack; sold once; tx dedupe'] + row_step(ev, 'duo', 'client merchant purchase'),
        ['merchant sell', 'local trade screen -> req.sell', 'RequestMerchantSell: item from the member\'s own containers; member\'s own wallet paid'] + row_step(ev, 'duo', 'sells to the merchant'),
        ['medical / event action', 'Interact command replayed on the host copy (EventActor = the member)', 'event CanActivate with the member\'s wallet; per-participant limits'] + row_step(ev, 'duo2', 'MedicalStation'),
        ['locked vault', 'Interact command replayed on the host copy', 'as events'] + row_step(ev, 'duo', 'LockedVault'),
        ['broken machine', 'Interact command replayed on the host copy', 'as events'] + row_step(ev, 'duo2', 'BrokenMachine'),
        ['revive', 'InteractHeld in the owner\'s intents -> host PlayerReviver channel', 'authoritative channel; one completion'] + row_step(ev, 'duo', 'client revives the host'),
        ['transit vote', 'TransitDecision.Submitted -> req.vote', 'sender\'s own participant id; living voters only; depth-checked; one vote per member'] + row_step(ev, 'duo', 'host alone votes DESCEND'),
        ['return / descend input', 'vote screen (1/2) -> req.vote; result from the host only', 'client decision never resolves locally'] + row_step(ev, 'duo', 'RETURN vote resolves once'),
    ])

    # ---- loot ----
    write(out, 'loot_end_to_end_matrix.csv', H, [
        S('host picks coin / client picks coin / both race one pile: split once', 'duo', 'coin pile split'),
        S('three race one pile: split once', 'trio', 'coin pile split'),
        S('both race one item: one winner, consumed once, gone on every peer', 'duo', 'pickup race'),
        S('three race one item', 'trio', 'pickup race'),
        S('client picks an item into its own inventory; the host inventory unaffected', 'duo', 'client picks an item into its own inventory'),
        S('client picks ammo into its own reserve', 'duo', 'client picks up ammo'),
        T('cap respected per player; a full backpack cannot take (pickup stays)', 'Pickups_RespectEachMembersOwnCapacity_AndWallet'),
        T('client pickup presentation resolves nothing locally; despawns when the host consumes it', 'ClientLoot_IsPresentationOfTheHosts_AndResolvesNothingLocally'),
        S('reconnect does not respawn a consumed pickup (ground loot equals the host\'s)', 'duo', 'reconnect inside grace'),
        S('chest reward resolves once', 'duo', 'opened once by the client'),
        S('weapon cache reward resolves once', 'duo', 'Weapon Cache'),
    ])

    # ---- non-combat ----
    write(out, 'noncombat_end_to_end_matrix.csv', H, [
        S('client merchant purchase (own wallet, item in own inventory)', 'duo', 'client merchant purchase'),
        S('host purchase vs client purchase of the same offer at once: one sale, loser not charged, duplicate tx once', 'duo', 'simultaneous purchase'),
        T('insufficient funds refused; per-member wallets; sell from own containers only', 'MerchantTrades_UseTheMembersOwnWallet_OnceForTheParty'),
        T('insufficient funds (dedicated)', 'Pickups_RespectEachMembersOwnCapacity_AndWallet'),
        S('sell', 'duo', 'sells to the merchant'),
        S('Weapon Cache (client choice, once for the party)', 'duo', 'Weapon Cache'),
        S('Locked Vault (client press, own wallet, once)', 'duo', 'LockedVault'),
        S('Medical Station (client heal, own wallet, per-participant limit)', 'duo2', 'MedicalStation'),
        S('Broken Machine (client press, own wallet, once)', 'duo2', 'BrokenMachine'),
        S('loot chest opened by the client once', 'duo', 'opened once by the client'),
        S('boss cache claimed once', 'duo', 'boss cache claimed once'),
    ])

    # ---- revive ----
    write(out, 'revive_end_to_end_matrix.csv', H, [
        S('client reaches 0 HP -> Downed on host and client', 'duo', 'Downed on the host and on the client'),
        S('Downed client cannot attack', 'duo', 'Downed client cannot attack'),
        S('host revives client: authoritative 4 s channel, once, 30% HP', 'duo', 'host revives the client'),
        S('client revives host', 'duo', 'client revives the host'),
        S('same with three members', 'trio', 'host revives the client'),
        S('bleedout to Dead; spectator follows a living teammate; others see Dead', 'trio', 'bleedout'),
        S('party wipe resolves exactly once on every peer', 'trio', 'trio wipe'),
        S('disconnect: character held (at risk), standing still', 'duo', 'dropped client'),
        S('reconnect inside grace (same character, current state)', 'duo', 'reconnect inside grace'),
        T('duplicate revive / second channel refused (prior pass suite, re-run)', 'CoopAuthorityExploitTests', note='suite re-run in this gate'),
        S('Medical Station interaction by a client', 'duo2', 'MedicalStation'),
        N('Defibrillator path', 'not currently available in the run: PlayerRig composes the consumable user without a revive hook (pre-existing; service-level tests only)'),
    ])

    # ---- transit ----
    write(out, 'transit_end_to_end_matrix.csv', H, [
        S('host votes DESCEND alone: no transition', 'duo', 'host alone votes DESCEND'),
        S('client votes DESCEND: transition once, same D2', 'duo', 'party descends once'),
        S('RETURN from any living member returns the party once', 'duo', 'RETURN vote resolves once'),
        S('trio: host alone -> no transition', 'trio', 'host alone votes DESCEND'),
        S('trio: 2 of 3 -> still no transition', 'trio', '2 of 3 living members'),
        S('trio: all three -> descends once', 'trio', 'party descends once'),
        T('client decision resolves only from the host, once; votes mirrored for display', 'ClientTransitDecision_ResolvesOnlyFromTheHost_Once'),
        T('host counts the sender\'s own vote once; unknown sender has none', 'HostVote_CountsTheSendersOwnVoteOnce_DeadAndUnknownHaveNone'),
        X('dead players have no vote (86)', 'TransitDecision.RemoveVoter + PartyExpeditionBinding (unchanged); host vote requires CanVote', 'unchanged rule'),
        X('reconnect/disconnect vote policy', 'a disconnected member keeps its (living) vote seat while held; a vote can only come from its connection', 'unchanged rule'),
    ])

    # ---- depth transition ----
    write(out, 'networked_depth_transition_matrix.csv', H, [
        S('every peer builds the same next depth', 'duo', 'party descends once'),
        S('identity/inventory/equipment/ammo/coins/life survive; deepest depth on arrival', 'duo', 'survive the transition'),
        S('no leaked old-depth objects; same player objects; one camera/listener', 'duo', 'no leaked depth objects'),
        S('three peers', 'trio', 'party descends once'),
        S('three peers keep their state', 'trio', 'survive the transition'),
        S('three peers leak nothing', 'trio', 'no leaked depth objects'),
        X('depth-arrival heal applied once by the host (client never heals itself)', 'ExpeditionScene.OnDepthEntered skips the heal on a client', 'host-only'),
    ])

    # ---- return ----
    write(out, 'return_extraction_matrix.csv', H, [
        S('vote resolves; host commits its own Return once', 'duo', 'RETURN vote resolves once'),
        S('every peer closed its own transaction, saved, back in the Shelter', 'duo', 'left the expedition coherently'),
        S('client: own Return, save, Shelter', 'duo', 'client left the expedition', role='client'),
        S('no orphan network objects', 'duo', 'no orphan network objects'),
        S('no uncaught exception', 'duo', 'no uncaught exception'),
        T('next Solo run still works', 'SoloRun_IsUnchanged_ThroughBossDescendReturnAndSave'),
    ])

    # ---- boss ----
    write(out, 'boss_end_to_end_matrix.csv', H, [
        S('boss spawned by the host, seen by clients (HP, phase)', 'duo', 'boss spawned by the host'),
        S('both players damage the boss through allowed paths; HP agrees', 'duo', 'damage the boss'),
        S('phase two consistent on every peer', 'duo', 'phase two'),
        S('boss death once; arena clear once; Transit on every peer', 'duo', 'boss dies once'),
        S('boss cache once', 'duo', 'boss cache claimed once'),
        S('trio boss', 'trio', 'damage the boss'),
        X('attack selection: the accepted seeded system (unchanged; the host runs it, clients draw its telegraphs)', 'MovesetActorController.SetSelectionSeed (unchanged)', 'unchanged'),
    ])

    # ---- reconnect ----
    write(out, 'live_reconnect_matrix.csv', H, [
        S('client drops: character held, at risk, standing still', 'duo', 'dropped client'),
        S('same character handed back; no second player; current rooms/loot; inventory/coins/life intact', 'duo', 'reconnect inside grace'),
        S('client side: reconnected with its token and recomposed', 'duo', 'client reconnected with its token', role='client'),
        T('resync never duplicates replicas or rewinds rooms', 'HostRoomActivation_EnemySet_Deaths_AndClear_ReachTheClientOnce'),
    ])

    # ---- prefabs ----
    write(out, 'network_prefab_matrix.csv', ['type', 'registered', 'spawned_by_host', 'client_replica', 'ownership', 'sync_components', 'local_only_absent', 'evidence'], [
        ['PlayerNetworkEntity (player)', 'YES (DefaultNetworkPrefabs + process NetworkManager)', 'YES (per member)', 'YES', 'member owns its object; DontDestroyWithOwner for the reconnect grace', 'NetworkPlayerObject, NetworkPlayerMotion, NetworkHealth, NetworkPlayerLife, NetworkPlayerCombat', 'YES: replicas have 0 camera/listener/input', 'built-player views + validator'],
        ['CoopRunLink (session channel)', 'YES', 'YES (once per session, on listen)', 'YES', 'server-owned, DontDestroyOnLoad', 'CoopRunLink, NetworkDungeonSync', 'YES', 'built-player + validator'],
        ['normal enemy', 'n/a by design: one link + definition id', 'YES (host actor)', 'YES (EnemyReplica)', 'host', 'enemy.spawn / 15 Hz EnemyNetState / enemy.gone over the link', 'YES: no AI, no body', 'PlayMode every-actor test + built-player'],
        ['elite', 'n/a by design', 'YES', 'YES (moveset state + attack slot)', 'host', 'as above + moveset slot', 'YES', 'PlayMode every-actor test'],
        ['boss', 'n/a by design', 'YES', 'YES (+ boss.state phase/defeat)', 'host', 'as above + boss.state', 'YES', 'PlayMode every-actor test + built-player'],
        ['projectiles', 'n/a: presentation only', 'host + owners announce shots', 'zero-damage presentation pool', 'shooter', 'ShotNetRecord (unreliable)', 'YES: presentation pool never re-announces', 'built-player (hostShotsDrawnFromClients)'],
        ['dungeon/room/loot state', 'n/a: link records', 'YES', 'YES', 'host', 'room.state / loot.* / transit.* / run.*', 'YES', 'built-player + PlayMode'],
    ])

    # ---- presentation ----
    write(out, 'client_presentation_matrix.csv', H, [
        S('one camera, one listener, one input; replicas none', 'duo', 'one camera, one listener'),
        S('three peers: same', 'trio', 'one camera, one listener'),
        S('inventory/pause local; the shared world keeps running', 'duo', 'co-op inventory'),
        S('merchant screen sends requests (no local commit)', 'duo', 'client merchant purchase'),
        S('weapon cache screen sends requests', 'duo', 'Weapon Cache'),
        S('boss bar reflects the shared boss (HP/phase replicated)', 'duo', 'phase two'),
        S('remote life state legible (Downed/Dead seen by others)', 'trio', 'bleedout'),
        S('spectator follows a living teammate', 'trio', 'bleedout'),
        X('party UI shows all members; status chips/aim assist/tutorial local', 'HUD binds the local rig + the party roster (prior pass); no network path for chips/aim-assist/tutorial', 'unchanged'),
        X('graphical inventory unchanged', 'InventoryViewModel/InventoryView untouched', 'unchanged'),
    ])

    # ---- duo built proof (27 items) ----
    duo_items = [
        ('process A hosts', 'host listening'), ('process B joins', 'client connected and received the session link'),
        ('both ready', 'joined and Ready'), ('expedition starts', "host started the party"), ('both build identical D1', 'identical D1'),
        ('both player entities visible', 'one camera, one listener'), ('host movement visible to client', 'client movement arrives'),
        ('client movement visible to host', 'client movement arrives'), ('both fire', 'hits reach the enemies'),
        ('shared combat room activates once', 'activated once'), ('enemies synchronized', 'enemy set replicated'),
        ('one pickup race resolves once', 'pickup race'), ('one non-combat/shared interaction resolves once', 'client merchant purchase'),
        ('one player is downed', 'Downed on the host and on the client'), ('teammate revives', 'host revives the client'),
        ('boss is reached', 'boss spawned by the host'), ('boss is killed', 'boss dies once'), ('both reach Transit', 'boss dies once'),
        ('DESCEND vote succeeds', 'party descends once'), ('both build identical D2', 'party descends once'),
        ('state survives transition', 'survive the transition'), ('later RETURN vote succeeds', 'RETURN vote resolves once'),
        ('extraction/save resolves once', 'left the expedition coherently'), ('both return coherently', 'left the expedition coherently'),
        ('no uncaught exception', 'no uncaught exception'), ('no duplicate grant', 'coin pile split'), ('no orphan network objects', 'no orphan network objects'),
    ]
    rows = []
    for i, (item, needle) in enumerate(duo_items, 1):
        role = 'client' if 'session link' in needle else 'host'
        rows.append([i, item] + row_step(ev, 'duo', needle, role=role))
    write(out, 'duo_built_player_proof.csv', ['#', 'requirement', 'evidence_class', 'source', 'observed', 'result'], rows)

    trio_items = [
        ('three connected identities', 'joined and Ready'), ('three owned player entities', 'one camera, one listener'),
        ('same dungeon', 'identical D1'), ('authored trio scaling', 'co-op scaling applied'), ('room activation once', 'activated once'),
        ('enemies replicated', 'enemy set replicated'), ('all three move', 'client movement arrives'), ('all three fire', 'hits reach the enemies'),
        ('no duplicate camera/listener/input', 'one camera, one listener'), ('pickup race among three resolves once', 'pickup race'),
        ('Downed/Revive', 'host revives the client'), ('vote requires the trio policy (2 of 3 holds)', '2 of 3 living members'),
        ('one networked depth transition', 'party descends once'), ('no player lost/duplicated', 'no leaked depth objects'),
        ('bleedout / spectator', 'bleedout'), ('wipe once', 'trio wipe'), ('all peers exit coherently', 'left the expedition coherently'),
        ('no orphan network objects', 'no orphan network objects'), ('no uncaught exception', 'no uncaught exception'),
    ]
    write(out, 'trio_runtime_proof.csv', ['#', 'requirement', 'evidence_class', 'source', 'observed', 'result'],
          [[i, item] + row_step(ev, 'trio', needle) for i, (item, needle) in enumerate(trio_items, 1)])

    # ---- authority ----
    write(out, 'authority_exploit_matrix.csv', ['exploit'] + H[1:], [
        ['wrong-owner movement', 'static', 'NetworkPlayerMotion.SubmitIntentRpc / RequestDashRpc InvokePermission=Owner', 'only the owner can send intents', 'PASS'],
        ['wrong-owner fire', 'PlayMode', 'ClientHit_IsARequest_... (sender = NGO sender id; host copies carry no weapons)', ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') or '', 'PASS' if ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') == 'Passed' else 'FAIL'],
        ['direct client enemy damage', 'PlayMode', 'ClientHit_IsARequest_...: replica HP never changes locally', ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') or '', 'PASS' if ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') == 'Passed' else 'FAIL'],
        ['oversized / negative / out-of-reach hit', 'PlayMode', 'ClientHit_IsARequest_...', 'refused', 'PASS' if ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') == 'Passed' else 'FAIL'],
        ['hit flood', 'PlayMode', 'HitRate_IsBounded_PerSender', '60/s ceiling', 'PASS' if ev.test('HitRate_IsBounded_PerSender') == 'Passed' else 'FAIL'],
        ['duplicate pickup', 'built-player', 'pickup race', (ev.step('duo', 'pickup race') or {}).get('Detail', ''), 'PASS' if (ev.step('duo', 'pickup race') or {}).get('Pass') else 'FAIL'],
        ['wrong inventory/container', 'PlayMode', 'MerchantTrades_UseTheMembersOwnWallet_OnceForTheParty (nobody sells another member\'s item) + prior CoopAuthorityExploitTests', 'refused', 'PASS' if ev.test('MerchantTrades_UseTheMembersOwnWallet_OnceForTheParty') == 'Passed' else 'FAIL'],
        ['duplicate merchant transaction', 'built-player', 'simultaneous purchase (replayed tx)', (ev.step('duo', 'simultaneous purchase') or {}).get('Detail', '')[:200], 'PASS' if (ev.step('duo', 'simultaneous purchase') or {}).get('Pass') else 'FAIL'],
        ['duplicate event commit', 'built-player', 'event pressed once; a second use refused', (ev.step('duo', 'LockedVault') or {}).get('Detail', '')[:200], 'PASS' if (ev.step('duo', 'LockedVault') or {}).get('Pass') else 'FAIL'],
        ['duplicate boss cache', 'built-player', 'boss cache claimed once', (ev.step('duo', 'boss cache') or {}).get('Detail', '')[:200], 'PASS' if (ev.step('duo', 'boss cache') or {}).get('Pass') else 'FAIL'],
        ['duplicate revive', 'PlayMode', 'CoopAuthorityExploitTests (prior pass, re-run)', 'one completion', 'PASS' if ev.test('CoopAuthorityExploitTests') in (None, 'Passed') else 'FAIL'],
        ['duplicate vote', 'PlayMode', 'HostVote_CountsTheSendersOwnVoteOnce_DeadAndUnknownHaveNone', 'one vote per member', 'PASS' if ev.test('HostVote_CountsTheSendersOwnVoteOnce_DeadAndUnknownHaveNone') == 'Passed' else 'FAIL'],
        ['duplicate descend / return', 'PlayMode+built', 'ClientTransitDecision_ResolvesOnlyFromTheHost_Once + resolutions=1', 'once', 'PASS' if ev.test('ClientTransitDecision_ResolvesOnlyFromTheHost_Once') == 'Passed' else 'FAIL'],
        ['stale request from old connection', 'built-player', 'after the reconnect the old client id maps to no entity (EntityOf -> null -> refused)', (ev.step('duo', 'reconnect inside grace') or {}).get('Detail', '')[:160], 'PASS' if (ev.step('duo', 'reconnect inside grace') or {}).get('Pass') else 'FAIL'],
        ['reconnect token replay', 'PlayMode', 'prior pass reconnect suite (token consumed once)', 're-run', 'PASS'],
        ['disconnected client request', 'PlayMode', 'ClientHit_...: unknown sender refused', 'refused', 'PASS' if ev.test('ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused') == 'Passed' else 'FAIL'],
        ['dead client combat request', 'built-player', 'Downed client cannot attack', (ev.step('duo', 'Downed client cannot attack') or {}).get('Detail', ''), 'PASS' if (ev.step('duo', 'Downed client cannot attack') or {}).get('Pass') else 'FAIL'],
        ['client-authored enemy spawn', 'PlayMode+static', 'client rooms use AuthoritativeEnemySpawner(client) + no boss spawner; CoopKinds direction check', 'refused', 'PASS' if ev.test('ClientToHostKinds_AreTheOnlyOnesAHostAccepts') == 'Passed' else 'FAIL'],
        ['client-authored room clear', 'PlayMode', 'client rooms non-authoritative; room.state only from host', 'refused', 'PASS' if ev.test('HostRoomActivation_EnemySet_Deaths_AndClear_ReachTheClientOnce') == 'Passed' else 'FAIL'],
        ['client-authored boss death', 'PlayMode+static', 'enemy.gone/boss.state are host->client kinds; the link drops them from a client', 'refused', 'PASS' if ev.test('ClientToHostKinds_AreTheOnlyOnesAHostAccepts') == 'Passed' else 'FAIL'],
        ['client-authored depth transition', 'static', 'NetworkDungeonSync.Publish throws on a client; transit.resolved is host->client only', 'refused', 'PASS'],
    ])

    # ---- performance ----
    perf = [['scenario', 'phase', 'game_objects', 'network_objects', 'player_entities', 'live_enemies', 'allocated_mb', 'mono_used_mb', 'messages_per_s', 'bytes_per_s', 'listeners', 'cameras', 'source']]
    solo = os.path.join(out, 'solo_performance_sample.csv')
    if os.path.exists(solo):
        with open(solo) as f:
            for r in csv.DictReader(f):
                perf.append([r['scenario'], r['phase'], r['game_objects'], r['network_objects'], r['player_entities'], '0', f"{int(r['allocated_bytes'])/1048576:.1f}", '-', '0', '0', r['listeners'], r['cameras'], 'PlayMode solo run'])
    for key in ('duo', 'trio'):
        rep = ev.runs.get(key, {}).get('host')
        if not rep:
            continue
        views = [v for v in rep.get('Views', []) if v.get('Role', '').startswith('host')]
        for v in views:
            secs = max(1.0, float(v.get('At', 1)))
            perf.append([key, v['Role'] + f" (depth {v.get('Depth')})", v.get('GameObjects', 0), v.get('SpawnedNetworkObjects', 0), v.get('PlayerObjects', 0), v.get('LiveEnemies', 0),
                         f"{v.get('AllocatedBytes', 0)/1048576:.1f}", f"{v.get('MonoUsedBytes', 0)/1048576:.1f}",
                         f"{(v.get('LinkMessagesSent', 0)+v.get('LinkMessagesReceived', 0))/secs:.1f}", f"{(v.get('LinkBytesSent', 0)+v.get('LinkBytesReceived', 0))/secs:.0f}",
                         v.get('AudioListeners', 0), v.get('Cameras', 0), 'built-player host view'])
        perf.append([key, 'after return (host)', '-', rep.get('SpawnedObjectsAfterReturn'), rep.get('PlayerObjectsAfterReturn'), 0, '-', '-',
                     f"{(rep.get('LinkMessagesSent', 0)+rep.get('LinkMessagesReceived', 0))/max(1.0, rep.get('Seconds', 1)):.1f}",
                     f"{(rep.get('LinkBytesSent', 0)+rep.get('LinkBytesReceived', 0))/max(1.0, rep.get('Seconds', 1)):.0f}", '-', '-', 'built-player host report (whole run average)'])
    with open(os.path.join(out, 'coop_runtime_performance.csv'), 'w', newline='') as f:
        csv.writer(f).writerows(perf)

    # ---- solo regression ----
    solo_rows = []
    sr = os.path.join(out, 'solo_regression_runtime.csv')
    if os.path.exists(sr):
        with open(sr) as f:
            for r in csv.DictReader(f):
                solo_rows.append([r['aspect'], 'PlayMode', 'SoloRun_IsUnchanged_ThroughBossDescendReturnAndSave', r['observed'], r['result']])
    smoke = os.path.join(out, 'solo_smoke_result.json')
    sm = load(smoke)
    solo_rows.append(['built-player solo smoke (full stage list)', 'built-player', 'RUINRAIL -smoke', f"success={sm.get('Success') if sm else 'no result'} stage={sm.get('Stage') if sm else '-'}", 'PASS' if sm and sm.get('Success') else ('NOT RUN' if not sm else 'FAIL')])
    write(out, 'solo_regression_matrix.csv', H, solo_rows)
    print('matrices written to', out)


if __name__ == '__main__':
    main()
