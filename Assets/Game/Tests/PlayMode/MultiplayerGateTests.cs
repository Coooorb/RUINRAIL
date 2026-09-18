using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 108 gate: one host-authoritative expedition flow per party size (solo / duo / trio) over the deterministic
    /// local network doubles — session join, ready/start, movement/dash/combat authority, shared dungeon, loot race and
    /// coin split, scaling caps, boss, downed/revive/dead, vote, disconnect grace — with item/Coin/XP conservation
    /// asserted at every step. Writes TestResults/gate_108_multiplayer.md.
    /// </summary>
    public class MultiplayerGateTests
    {
        private const string ReportPath = "TestResults/gate_108_multiplayer.md";

        private readonly List<UnityEngine.Object> _created = new();
        private readonly List<string> _report = new();
        private PlayerBalanceConfig _balance;
        private DisplayNamePolicy _policy;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private DepthScalingConfig _scaling;
        private RoomPool _pool;
        private DungeonGraphRules _rules;
        private BossDefinition _tunnelMaw;
        private List<EnemyDefinition> _archetypes;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        private sealed class Authority : IAuthorityContext
        {
            public Authority(NetworkRole role) { Role = role; }
            public NetworkRole Role { get; }
            public bool IsAuthority => Role != NetworkRole.Client;
        }

        private sealed class Peer
        {
            public string Id;
            public ulong ClientId;
            public NetworkSessionController Session;
            public FakeNetworkDriver Driver;
            public ExpeditionService Expedition;
            public PlayerProfile Profile;
            public PartyExpeditionBinding Binding;
            public ExpeditionStartCoordinator Coordinator = new();
            public GameObject Entity;
            public PlayerLifeStateComponent Life;
            public HealthComponent Health;
            public PlayerInventory Inventory;
            public CoinWallet Wallet => Expedition.State.CarriedWallet;
            public int XpAwarded;
        }

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _scaling = AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            _tunnelMaw = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset");
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" }).Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var metro = AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/Game/ScriptableObjects/Rooms/RuinedMetro" }).Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _pool = RoomPool.Build(metro, Biome.RuinedMetro);
            _rules = DungeonGraphRules.CreateDefault();
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("Spawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(Resolve);
            FakeMultiplayerServices.ResetRegistry();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            foreach (var boss in UnityEngine.Object.FindObjectsByType<BossController>(FindObjectsSortMode.None)) if (boss != null) UnityEngine.Object.DestroyImmediate(boss.transform.parent != null ? boss.transform.parent.gameObject : boss.gameObject);
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            foreach (var movement in UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)) if (movement != null) UnityEngine.Object.DestroyImmediate(movement.gameObject);
            if (_rules != null) UnityEngine.Object.DestroyImmediate(_rules);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        private void Check(string name, bool pass, string detail = null)
        {
            _report.Add($"| {name} | {(pass ? "PASS" : "FAIL")} | {detail ?? string.Empty} |");
            Assert.IsTrue(pass, $"{name}: {detail}");
        }

        private static InventorySnapshot Loadout(string instanceId) => new()
        {
            Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
            Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("consumable_bandage", 3).ToSnapshot() } }
        };

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        // =====================================================================================================
        // The scenario
        // =====================================================================================================

        private IEnumerator RunScenario(int partySize)
        {
            _report.Add($"\n### Party size {partySize}\n");
            _report.Add("| Check | Result | Detail |");
            _report.Add("|---|---|---|");

            // ---- 1. Session join (join code, private, max 3) ----
            var peers = new List<Peer>();
            var hostSession = new NetworkSessionController(new FakeMultiplayerServices(), new FakeNetworkDriver());
            var hostResult = Wait(hostSession.HostAsync(new SessionRequest(3)));
            Check("session: host creates a private join-code session", hostResult.Success && hostSession.Session != null && hostSession.Session.IsHost && hostSession.Session.MaxPlayers == 3 && new SessionRequest(3).IsPrivate, hostSession.Session?.JoinCode);
            peers.Add(new Peer { Id = "P0", ClientId = 0, Session = hostSession });
            for (var i = 1; i < partySize; i++)
            {
                var driver = new FakeNetworkDriver();
                var client = new NetworkSessionController(new FakeMultiplayerServices(), driver);
                var joined = Wait(client.JoinAsync(hostSession.Session.JoinCode));
                Check($"session: client {i} joins by code", joined.Success && client.State == NetworkLifecycleState.Connected && client.Role == NetworkRole.Client, joined.Message);
                peers.Add(new Peer { Id = $"P{i}", ClientId = (ulong)i, Session = client, Driver = driver });
            }

            var fourth = new NetworkSessionController(new FakeMultiplayerServices(), new FakeNetworkDriver());
            var overflow = Wait(fourth.JoinAsync(hostSession.Session.JoinCode));
            Check("session: no 4th member beyond the party limit", partySize < 3 ? overflow.Success : !overflow.Success && overflow.Error == ServiceErrorKind.SessionFull, overflow.Error.ToString());
            if (partySize < 3) Wait(fourth.LeaveAsync());

            // ---- 2. Ready / start: exactly one start snapshot, party size captured, one expedition per peer ----
            var lobby = new PartyLobby(0, Resolve);
            foreach (var peer in peers)
            {
                lobby.Join(peer.ClientId, peer.Id);
                lobby.SetLoadout(peer.ClientId, Loadout(peer.Id));
                Check($"lobby: {peer.Id} valid loadout + ready", lobby.SetReady(peer.ClientId, true));
            }

            if (partySize > 1) Check("lobby: a client cannot start", lobby.TryStart(1, 4242, Biome.RuinedMetro, out _) == LobbyStartError.NotHost);
            Check("lobby: host starts when everyone is ready", lobby.TryStart(0, 4242, Biome.RuinedMetro, out var snapshot) == LobbyStartError.None && snapshot.PartySize == partySize);
            Check("lobby: duplicate start is idempotent", lobby.TryStart(0, 4242, Biome.RuinedMetro, out var again) == LobbyStartError.AlreadyStarted && ReferenceEquals(again, snapshot));

            var roster = new PartyLifeRoster();
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            foreach (var peer in peers)
            {
                peer.Profile = new PlayerProfile { SafeLoadout = snapshot.Members.First(m => m.ClientId == peer.ClientId).Loadout };
                peer.Expedition = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance, null, peer.Id);
                var state = peer.Coordinator.Apply(snapshot, peer.Expedition, peer.Profile);
                peer.Coordinator.Apply(snapshot, peer.Expedition, peer.Profile);
                Check($"start: {peer.Id} starts once with party size {partySize}", state != null && peer.Coordinator.Applied == 1 && peer.Coordinator.Ignored == 1 && state.StartingPartySize == partySize && state.RunSeed == 4242);
                peer.Inventory = state.Inventory;
                Check($"start: {peer.Id} safe loadout became at-risk (no safe copy left)", peer.Profile.SafeLoadout == null && peer.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null && peer.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).IsAtRisk);
            }

            var startingInstanceIds = peers.ToDictionary(p => p.Id, p => AllInstanceIds(p.Inventory));
            Check("start: item instance ids unique across the party", startingInstanceIds.Values.SelectMany(s => s).Distinct().Count() == startingInstanceIds.Values.Sum(s => s.Count));

            // ---- 3. Presence: one owned entity per member; life roster ----
            var connection = new FakeConnectionEvents(0, isHost: true);
            var grace = new ReconnectGraceService(60f, () => peers[0].Expedition.IsExpeditionActive, () => _now);
            var presence = new PlayerPresenceService(connection, new LocalPlayerEntityFactory(_balance, null, id => new Vector2(id.ClientId * 3f, 0f)), new SessionRoster(_policy), grace);
            foreach (var peer in peers) connection.Connect(peer.ClientId, peer.Id);
            Check("presence: exactly one entity per member", presence.Entities.Count == partySize && presence.SpawnCount == partySize);
            foreach (var peer in peers)
            {
                peer.Entity = presence.Entities[peer.ClientId].GameObject;
                peer.Life = peer.Entity.GetComponent<PlayerLifeStateComponent>();
                peer.Health = peer.Entity.GetComponent<HealthComponent>();
                peer.Life.SetParticipantId(peer.Id);
                peer.Life.SetRoster(roster);
                peer.Entity.GetComponent<PlayerLootReceiver>().SetInventory(peer.Inventory);
                peer.Entity.GetComponent<PlayerLootReceiver>().SetWallet(peer.Wallet);
                peer.Binding = new PartyExpeditionBinding(peer.Expedition, roster, peer.Life);
            }

            Check("presence: only the host's entity has local input; replicas are input-isolated", PlayerEntityBuilder.IsInputIsolated(peers[0].Entity) == false && peers.Skip(1).All(p => PlayerEntityBuilder.IsInputIsolated(p.Entity)));
            Check("life: roster is co-op iff party > 1", roster.IsCoop == (partySize > 1) && roster.Count == partySize);

            // ---- 4. Movement / dash / combat authority ----
            var mover = peers[partySize - 1];
            var remote = new RemoteIntentInputReader();
            mover.Entity.GetComponent<PlayerMovement>().SetInputReader(remote);
            mover.Entity.GetComponent<PlayerDash>().SetInputReader(remote);
            var start = mover.Entity.transform.position;
            remote.Apply(MovementIntent.Create(1, Vector2.right, Vector2.right));
            yield return new WaitForSeconds(0.3f);
            remote.Apply(MovementIntent.Create(2, Vector2.zero, Vector2.right));
            yield return new WaitForFixedUpdate();
            Check("motion: host simulates the owner's intent", mover.Entity.transform.position.x > start.x + 0.5f, $"moved {mover.Entity.transform.position.x - start.x:0.00} tiles");
            Check("motion: stale intent is ignored", !remote.Apply(MovementIntent.Create(1, Vector2.left, Vector2.left)));
            var validator = new HostDashValidator(mover.Entity.GetComponent<PlayerDash>());
            var d1 = validator.Validate(new DashRequest { Sequence = 1, Direction = Vector2.up });
            var d2 = validator.Validate(new DashRequest { Sequence = 1, Direction = Vector2.up });
            var d3 = validator.Validate(new DashRequest { Sequence = 2, Direction = Vector2.up });
            Check("dash: host validates once per request (duplicate + cooldown rejected)", d1 == DashVerdict.Accepted && d2 == DashVerdict.RejectedDuplicate && (d3 == DashVerdict.RejectedAlreadyDashing || d3 == DashVerdict.RejectedCooldown) && validator.Accepted == 1, $"{d1}/{d2}/{d3}");
            yield return new WaitForSeconds(0.25f);

            DamageAuthority.LocalIsAuthoritative = false;
            var clientHit = peers[0].Health.TryApplyDamage(new DamageRequest(10));
            var clientHeal = peers[0].Health.Heal(10);
            DamageAuthority.LocalIsAuthoritative = true;
            Check("combat: a client process cannot mutate health", !clientHit && !clientHeal && peers[0].Health.CurrentHealth == peers[0].Health.MaxHealth);
            Check("combat: the host applies damage", peers[0].Health.TryApplyDamage(new DamageRequest(10)) && peers[0].Health.CurrentHealth == peers[0].Health.MaxHealth - 10);
            peers[0].Health.Heal(10);

            // ---- 5. Shared dungeon ----
            var (payload, hostGen) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pool, 4242, 1);
            var identical = true;
            for (var i = 1; i < partySize; i++)
            {
                var rebuilt = DungeonSync.ClientRebuild(payload, new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), RoomPool.Build(_pool.Rooms, Biome.RuinedMetro));
                identical &= rebuilt.Success && rebuilt.Layout.Signature() == hostGen.Layout.Signature();
            }

            Check("dungeon: every client rebuilds the host's layout from the payload", hostGen.Success && identical, $"{hostGen.Layout.Placements.Count} rooms");

            // ---- 6. Scaling: threat, caps, HP ----
            var plan = EncounterDirector.Compose(new EncounterContext(4242, 1, partySize, Biome.RuinedMetro, 3), _archetypes);
            Check("scaling: threat multiplier + active cap", Mathf.Approximately(plan.BudgetMax, ThreatBudgetTable.SoloBudget(1).max * PartyScaling.ThreatMultiplier(partySize)) && plan.TotalCount <= PartyScaling.ActiveNormalCap(partySize), $"x{PartyScaling.ThreatMultiplier(partySize)} cap {PartyScaling.ActiveNormalCap(partySize)} spawned {plan.TotalCount}");
            var normalBase = _archetypes.First().BaseHealth;
            Check("scaling: normal HP on the normal curve, damage unscaled", DepthScaling.ScaledHealth(normalBase, 1, partySize, false, _scaling) == Mathf.Max(1, Mathf.RoundToInt(normalBase * PartyScaling.NormalEnemyHealthMultiplier(partySize))) && DepthScaling.ScaledDamage(10, 12, 1, _scaling) == (10, 12));

            // ---- 7. Loot race + coin split (host-authoritative, conserved) ----
            var authority = new LootAuthorityService(new Authority(NetworkRole.Host));
            authority.SetDropService(new ItemDropService(_spawner));
            foreach (var peer in peers) authority.RegisterParticipant(new LootParticipant(peer.ClientId, peer.Id, new BackpackContainer(peer.Inventory), peer.Wallet, peer.Entity));
            var pickup = _spawner.CreateItemPickup(Vector2.zero);
            var prize = new ItemInstance("weapon_kestrel_12") { Rarity = Rarity.Rare };
            pickup.Hold(prize, ItemCategory.Weapon);
            var itemsBefore = peers.Sum(p => AllInstanceIds(p.Inventory).Count) + 1;
            var results = peers.Select(p => authority.RequestPickup($"pick-{p.Id}", p.ClientId, pickup)).ToList();
            var retries = peers.Select(p => authority.RequestPickup($"pick-{p.Id}", p.ClientId, pickup)).ToList();
            yield return null;
            var accepted = results.Count(r => r.Verdict == LootVerdict.Accepted);
            var holders = peers.Count(p => p.Inventory.BackpackSlots.Any(i => i != null && i.InstanceId == prize.InstanceId));
            Check("loot: simultaneous pickup has exactly one winner", accepted == 1 && holders == 1 && pickup.IsConsumed, string.Join(",", results.Select(r => r.Verdict)));
            Check("loot: retried requests replay the ledger (no second execution)", retries.Select((r, i) => ReferenceEquals(r, results[i])).All(x => x));
            Check("loot: item count conserved (backpacks + ground)", peers.Sum(p => AllInstanceIds(p.Inventory).Count) + _ground.Count == itemsBefore, $"{itemsBefore} before");
            var clientAuthority = new LootAuthorityService(new Authority(NetworkRole.Client));
            Check("loot: a client cannot resolve loot", clientAuthority.RequestPickup("x", 1, pickup).Verdict == LootVerdict.NotAuthority);

            var pile = _spawner.CreateCoinPickup(new Vector2(1f, 0f));
            pile.SetAmount(101);
            var walletsBefore = peers.Sum(p => p.Wallet.Balance);
            var coin = authority.RequestCoins("coins-1", 0, pile);
            authority.RequestCoins("coins-1", 0, pile);
            var walletsAfter = peers.Sum(p => p.Wallet.Balance);
            Check("coins: pile split evenly across the party, once, conserved", coin.Verdict == LootVerdict.Accepted && walletsAfter - walletsBefore == 101 && authority.LastDistribution.Shares.Count == partySize && authority.LastDistribution.Shares.Max(s => s.Amount) - authority.LastDistribution.Shares.Min(s => s.Amount) <= 1, string.Join("/", authority.LastDistribution.Shares.Select(s => s.Amount)));
            yield return null;

            // ---- 8. XP: each peer records the same kill once ----
            foreach (var peer in peers) { peer.Expedition.RecordEnemyDefeated(12); peer.XpAwarded += 12; }
            Check("xp: committed once per peer, permanent", peers.All(p => p.Profile.TotalXp == p.XpAwarded && p.Expedition.State.Stats.XpEarned == p.XpAwarded));

            // ---- 9. Downed / revive / dead (co-op only) ----
            if (partySize > 1)
            {
                var victim = peers[1];
                var reviver = peers[0];
                Kill(victim.Health);
                Check("life: co-op 0 HP -> Downed with 20 s bleedout", victim.Life.IsDowned && Mathf.Approximately(victim.Life.BleedoutRemaining, 20f) && !victim.Life.CanAct);
                Check("life: downed player refused by the loot authority", authority.RequestDrop("drop-downed", victim.ClientId, victim.Entity.GetComponent<PlayerLootReceiver>().CarriedContainers, victim.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId, 1, Vector2.zero).Verdict == LootVerdict.Rejected);
                reviver.Entity.transform.position = victim.Entity.transform.position + new Vector3(0.5f, 0f);
                var reviverReader = new FakePlayerInputReader { InteractHeld = true };
                reviver.Entity.GetComponent<PlayerReviver>().SetInputReader(reviverReader);
                for (var t = 0f; t < 4.05f; t += 0.1f) reviver.Entity.GetComponent<PlayerReviver>().Step(0.1f);
                reviverReader.InteractHeld = false;
                Check("life: 4 s hold revives at 30% Max HP with protection", victim.Life.IsAlive && victim.Health.CurrentHealth == Mathf.RoundToInt(victim.Health.MaxHealth * 0.3f) && victim.Health.IsInvulnerable && roster.Revives.Completed == 1);
                victim.Entity.GetComponent<ReviveProtection>().Tick(2f);

                Kill(victim.Health);
                victim.Life.Tick(20.01f);
                Check("life: bleedout -> Dead, gear kept, no drop", victim.Life.IsDead && victim.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null && _ground.Count == 0 && !victim.Entity.GetComponent<PlayerLootReceiver>().TryDrop(victim.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId).Success);
                Check("life: dead spectator follows a living teammate", victim.Entity.GetComponent<DeadSpectatorFollow>().IsSpectating && victim.Entity.GetComponent<DeadSpectatorFollow>().FollowTarget != victim.Entity.transform);
                var revives = new PartyReviveAuthority(roster, _balance);
                var defib = new ItemInstance("consumable_defibrillator", 1, Rarity.Legendary);
                reviver.Inventory.TryEquip(defib, EquippedSlot.ActiveConsumable);
                Check("life: defibrillator revives the dead teammate", revives.ReviveWithDefibrillator(reviver.Life, new RuinRail.Gameplay.Items.Consumables.ReviveRequest("consumable_defibrillator", 30)) && victim.Life.IsAlive);
                reviver.Inventory.Unequip(EquippedSlot.ActiveConsumable);
                victim.Entity.GetComponent<ReviveProtection>().Tick(2f);
            }
            else
            {
                var soloExpedition = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance, null, "solo");
                var soloProfile = new PlayerProfile { SafeLoadout = Loadout("solo") };
                soloExpedition.Start(soloProfile, 1, Biome.RuinedMetro, 1);
                var soloRoster = new PartyLifeRoster();
                var soloGo = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "solo", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, LifeRoster = soloRoster, ParticipantId = "solo" });
                _created.Add(soloGo);
                using var soloBinding = new PartyExpeditionBinding(soloExpedition, soloRoster, soloGo.GetComponent<PlayerLifeStateComponent>());
                Kill(soloGo.GetComponent<HealthComponent>());
                Check("life: solo 0 HP -> Dead + immediate failure (never Downed)", soloGo.GetComponent<PlayerLifeStateComponent>().IsDead && !soloExpedition.IsExpeditionActive && soloExpedition.LastSummary.Outcome == ExpeditionOutcome.Failed && soloProfile.SafeLoadout == null);
            }

            // ---- 10. Boss (boss curve) + vote Descend ----
            var boss = new DefaultBossSpawner(new[] { _tunnelMaw }).Spawn(_tunnelMaw, new Vector2(50f, 0f), null);
            _created.Add(boss.gameObject);
            boss.Boss.Health.SetMaxHealth(DepthScaling.ScaledHealth(_tunnelMaw.BaseHealth, 1, partySize, true, _scaling));
            Check("boss: HP on the boss curve", boss.Boss.Health.MaxHealth == Mathf.RoundToInt(1150 * PartyScaling.BossHealthMultiplier(partySize)), boss.Boss.Health.MaxHealth.ToString());
            boss.Boss.Health.TryApplyDamage(new DamageRequest(999999));
            yield return null;
            Check("boss: defeated once", boss.IsDefeated);
            var transits = peers.ToDictionary(p => p.Id, p => p.Expedition.RecordBossDefeated(700));
            foreach (var peer in peers) peer.XpAwarded += 700;
            Check("vote: opened for living players only", transits.Values.All(t => t.State == TransitDecisionState.Open && t.LivingPlayers.Count == partySize));
            foreach (var voter in peers)
            {
                foreach (var t in transits.Values) t.Submit(voter.Id, TransitChoice.DescendDeeper);
            }

            Check("vote: unanimous Descend moves every peer to Depth 2", peers.All(p => p.Expedition.State.Depth == 2 && p.Expedition.IsExpeditionActive));

            // ---- 11. Disconnect grace (co-op): held, reclaimed, same entity ----
            if (partySize > 1)
            {
                var dropped = peers[1];
                var token = presence.Entities[dropped.ClientId].ReconnectToken;
                var entityBefore = dropped.Entity;
                connection.Disconnect(dropped.ClientId);
                Check("disconnect: character held at risk during the expedition", grace.Pending.Count == 1 && entityBefore != null && dropped.Health.TryApplyDamage(new DamageRequest(1)));
                _now += 20;
                connection.Reconnect(11, token, dropped.Id);
                Check("disconnect: reconnect rebinds the same entity once (no duplicate)", presence.ReconnectCount == 1 && ReferenceEquals(presence.Entities[11].GameObject, entityBefore) && presence.SpawnCount == partySize && grace.Pending.Count == 0);
                dropped.ClientId = 11;
                dropped.Health.Heal(1);
            }

            // ---- 12. Second boss + Return: living peers extract, conservation ----
            foreach (var peer in peers)
            {
                var t = peer.Expedition.RecordBossDefeated(700);
                peer.XpAwarded += 700;
                transits[peer.Id] = t;
            }

            var warnings = transits.Values.Select(t => t.RequiresReturnWarning).ToList();
            Check("vote: no dead-return warning while everyone lives", warnings.All(w => !w));
            foreach (var t in transits.Values) t.Submit(peers[0].Id, TransitChoice.ReturnToShelter);
            Check("vote: one Return returns the whole party", peers.All(p => !p.Expedition.IsExpeditionActive && p.Expedition.LastSummary.Outcome == ExpeditionOutcome.Extracted));
            foreach (var peer in peers)
            {
                var summary = peer.Expedition.LastSummary;
                var secured = summary.SecuredItems.Select(i => i.InstanceId).ToList();
                Check($"extract: {peer.Id} secured exactly its carried items once", secured.Distinct().Count() == secured.Count && secured.OrderBy(x => x).SequenceEqual(SnapshotIds(peer.Profile.SafeLoadout).OrderBy(x => x)) && summary.CoinsLost == 0);
                Check($"extract: {peer.Id} banked its carried coins once", peer.Profile.BankedCoins == summary.CoinsExtracted && summary.CoinsExtracted == authority.LastDistribution.Shares.First(s => s.ParticipantId == peer.Id).Amount);
                Check($"extract: {peer.Id} XP permanent and exact", peer.Profile.TotalXp == peer.XpAwarded && summary.XpEarned == peer.XpAwarded, peer.XpAwarded.ToString());
                Check($"extract: {peer.Id} replayed Return is the same summary", ReferenceEquals(peer.Expedition.Return(), summary) && ReferenceEquals(peer.Expedition.Fail(), summary));
            }

            var allSecured = peers.SelectMany(p => p.Expedition.LastSummary.SecuredItems.Select(i => i.InstanceId)).ToList();
            Check("extract: no item instance secured by two peers", allSecured.Distinct().Count() == allSecured.Count);
            Check("extract: the prize ended with exactly one peer", allSecured.Count(id => id == prize.InstanceId) == 1);

            // ---- 13. Host loss after extraction changes nothing; mid-run host loss fails everyone (TASK 106 suite covers the live drop) ----
            var bindings = peers.Select(p => new SessionExpeditionBinding(p.Session, p.Expedition)).ToList();
            foreach (var peer in peers) peer.Driver?.Drop();
            Check("session: dropping after extraction cannot re-open or fail a closed transaction", bindings.All(b => b.Failures == 0) && peers.All(p => p.Expedition.LastSummary.Outcome == ExpeditionOutcome.Extracted));
            foreach (var b in bindings) b.Dispose();
            foreach (var peer in peers) peer.Binding.Dispose();
            presence.Dispose();
            Wait(hostSession.LeaveAsync());
        }

        private double _now = 5000.0;

        private static List<string> AllInstanceIds(PlayerInventory inventory)
        {
            var ids = new List<string>();
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var item = inventory.GetEquipped(slot);
                if (item != null) ids.Add(item.InstanceId);
            }

            ids.AddRange(inventory.BackpackSlots.Where(i => i != null).Select(i => i.InstanceId));
            return ids;
        }

        private static List<string> SnapshotIds(InventorySnapshot snapshot)
        {
            if (snapshot == null) return new List<string>();
            return (snapshot.Equipped ?? Array.Empty<InventorySnapshot.Entry>()).Concat(snapshot.Backpack ?? Array.Empty<InventorySnapshot.Entry>()).Where(e => e?.Item != null).Select(e => e.Item.InstanceId).ToList();
        }

        [UnityTest]
        public IEnumerator SoloDuoTrio_HostAuthoritativeExpeditionFlow_PassesEveryGateCheck()
        {
            _report.Clear();
            _report.Add("# TASK 108 — Multiplayer gate (local deterministic doubles)");
            _report.Add($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z by MultiplayerGateTests.");
            var failed = false;
            foreach (var party in new[] { 1, 2, 3 })
            {
                var scenario = RunScenario(party);
                while (true)
                {
                    object current;
                    try
                    {
                        if (!scenario.MoveNext()) break;
                        current = scenario.Current;
                    }
                    catch (Exception e)
                    {
                        failed = true;
                        _report.Add($"| scenario {party}: exception | FAIL | {e.Message} |");
                        break;
                    }

                    yield return current;
                }

                TearDown();
                SetUp();
            }

            _report.Add("\n### Live services\n");
            _report.Add("| Check | Result | Detail |");
            _report.Add("|---|---|---|");
            _report.Add("| Unity Relay / Sessions live smoke | NOT RUN | requires RUINRAIL_LIVE_SERVICES=1 with signed-in Unity Gaming Services credentials; not available in the autonomous run |");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, string.Join("\n", _report) + "\n", Encoding.UTF8);
            Assert.IsFalse(failed, "A scenario threw; see the report.");
        }
    }
}
