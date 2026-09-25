using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The co-op runtime composition contract, checked against the real project and against a real composed party: the
    /// validator's static rules plus the composition facts it can only get by composing 1-, 2- and 3-player parties
    /// here. A rule that cannot be evidenced is a failure, never a pass.
    /// </summary>
    public class CoopRuntimeCompositionValidatorTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object { _created.Add(o); return o; }

        private ExpeditionParty Compose(int size, int expected, Vector2 origin, out PartyLifeRoster roster,
            out RuinRail.Networking.LootAuthorityService loot, out PartyCompositionError error)
        {
            var content = GameContentCatalog.Load();
            var registry = ItemDefinitionRegistry.Build(content.Items.Where(i => i != null));
            roster = new PartyLifeRoster();
            loot = new RuinRail.Networking.LootAuthorityService(RuinRail.Networking.LocalAuthorityContext.Instance);
            var local = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "LocalPlayer", IsLocal = false, BalanceConfig = content.PlayerBalance, Caps = content.StatCaps,
                Position = origin, LifeRoster = roster, ParticipantId = "p0"
            }));
            local.GetComponent<PlayerLootReceiver>().SetInventory(PlayerInventory.FromRegistry(registry, content.AmmoBalance));
            var members = Enumerable.Range(0, expected)
                .Select(i => new PartyMemberDescriptor((ulong)i, i == 0 ? "Host" : null, "p" + i, i == 0)).ToList();
            var party = ExpeditionParty.Compose(new PartyCompositionRequest
            {
                Members = members, LocalClientId = 0, LocalEntity = local, LocalParticipantId = "p0", IsHost = true,
                Balance = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = roster, Loot = loot,
                Items = registry, Ammo = content.AmmoBalance, DisplayNamePolicy = content.DisplayNamePolicy,
                // An expected count above the composable count models a lobby that promised members the host cannot
                // admit; the composition must refuse rather than scale for a party that is not there (83).
                SpawnPosition = identity => identity.ClientId < (ulong)size ? origin + new Vector2(2f * (identity.ClientId + 1), 0f) : origin,
                RemoteFactory = size < expected ? new RefusingFactory(size) : null
            }, out error);
            if (party != null) foreach (var member in party.Members) if (member.GameObject != local) Track(member.GameObject);
            return party;
        }

        // ---------------- completion pass fixtures ----------------

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            System.Reflection.FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RuinRail.Dungeon.Runtime.RoomRuntime Room(RuinRail.Dungeon.Rooms.RoomType type, int node, Vector2 offset)
        {
            var definition = Track(ScriptableObject.CreateInstance<RuinRail.Dungeon.Rooms.RoomDefinition>());
            Set(definition, "_id", "fixture_" + type);
            Set(definition, "_roomType", type);
            var go = Track(new GameObject("FixtureRoom_" + type));
            go.transform.position = offset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RuinRail.Dungeon.Rooms.RoomRoot>();
            root.Configure(definition, new Vector2Int(10, 8));
            var runtime = go.AddComponent<RuinRail.Dungeon.Runtime.RoomRuntime>();
            runtime.Configure(root, node, 1, 2);
            return runtime;
        }

        /// <summary>
        /// Host/client runtime facts from real fixtures: the host world and the client world over the loopback bus, the
        /// client-side party view over replicated objects, a resync, and a client hit that must stay a request.
        /// </summary>
        private void SupplyCompletionFacts(CoopRuntimeCompositionValidator.CompositionFacts facts)
        {
            var content = GameContentCatalog.Load();
            var hostBus = RuinRail.Networking.LoopbackCoopBus.CreateHost(out var network);
            var clientBus = RuinRail.Networking.LoopbackCoopBus.Join(network, 1);
            var hostRooms = new Dictionary<int, RuinRail.Dungeon.Runtime.RoomRuntime> { [1] = Room(RuinRail.Dungeon.Rooms.RoomType.Start, 1, new Vector2(0f, 5000f)), [2] = Room(RuinRail.Dungeon.Rooms.RoomType.Loot, 2, new Vector2(20f, 5000f)) };
            var clientRooms = new Dictionary<int, RuinRail.Dungeon.Runtime.RoomRuntime> { [1] = Room(RuinRail.Dungeon.Rooms.RoomType.Start, 1, new Vector2(0f, 6000f)), [2] = Room(RuinRail.Dungeon.Rooms.RoomType.Loot, 2, new Vector2(20f, 6000f)) };
            var roster = new PartyLifeRoster();
            var hostPlayer = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "FixtureHost", IsLocal = false, BalanceConfig = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = roster, ParticipantId = "h" }));
            var host = new RuinRail.Networking.CoopHostWorld(hostBus, new RuinRail.Networking.CoopHostParty { EntityOf = id => id == 0 ? hostPlayer : null }, null, null, null);
            var client = new RuinRail.Networking.CoopClientWorld(clientBus);
            try
            {
                client.BindDepth(1, clientRooms, null, null);
                host.BindDepth(1, hostRooms, null, new RuinRail.Networking.DungeonSyncPayload { IsValid = true, Depth = 1 }, hostPlayer);
                client.ReportDepth(1, true, "fp", null);
                host.Tick(0.01f);
                hostRooms[1].NotifyPlayerEntered(hostPlayer);
                hostRooms[2].NotifyPlayerEntered(hostPlayer);
                hostRooms[2].State.MarkResolved("chest:0");
                host.Tick(1f);
                int Divergences() => hostRooms.Keys.Count(k => hostRooms[k].Lifecycle != clientRooms[k].Lifecycle || !hostRooms[k].State.Resolved.SequenceEqual(clientRooms[k].State.Resolved) || hostRooms[k].DoorsLocked != clientRooms[k].DoorsLocked);
                facts.RoomStateDivergences = Divergences();

                var before = client.Replicas.Replicas.Count;
                var statesBefore = clientRooms.Values.Select(r => r.Lifecycle).ToList();
                host.ServeResync(1);
                facts.ResyncDuplicates = (client.Replicas.Replicas.Count - before) + clientRooms.Values.Select(r => r.Lifecycle).Zip(statesBefore, (a, b) => a != b ? 1 : 0).Sum() + Divergences();

                var registry = new RuinRail.Networking.EnemyReplicaRegistry();
                var replica = registry.Spawn(new RuinRail.Networking.EnemySpawnRecord { NetId = 9, DefinitionId = "grunt", Position = new Vector2(0f, 7000f) });
                Track(replica.gameObject);
                replica.ApplyInitialHealth(40, 40);
                var local = Track(new GameObject("FixtureLocal"));
                client.InstallRelays(local);
                RuinRail.Gameplay.Combat.DamageAuthority.LocalIsAuthoritative = false;
                replica.Health.TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(15));
                RuinRail.Gameplay.Combat.DamageAuthority.LocalIsAuthoritative = true;
                facts.ClientLocalHitsApplied = 40 - replica.Health.CurrentHealth;
            }
            finally
            {
                RuinRail.Gameplay.Combat.DamageAuthority.LocalIsAuthoritative = true;
                host.Dispose();
                client.Dispose();
                RuinRail.Core.Input.GameplayInputGate.Reset();
            }

            // A client's party view adopts the host's replicas: exactly the run's members, nothing local added.
            int View(int size, out int presentation)
            {
                var replicas = Enumerable.Range(0, size).Select(i => Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Replica" + i, IsLocal = false, BalanceConfig = content.PlayerBalance, Caps = content.StatCaps, ParticipantId = "r" + i }))).ToList();
                var viewRoster = new PartyLifeRoster();
                var party = ExpeditionParty.Compose(new PartyCompositionRequest
                {
                    Members = Enumerable.Range(0, size).Select(i => new PartyMemberDescriptor((ulong)i, "M" + i, "r" + i, i == 0)).ToList(),
                    LocalClientId = 1, LocalEntity = replicas[1], LocalParticipantId = "r1", IsHost = true,
                    Balance = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = viewRoster,
                    RemoteFactory = new RuinRail.Networking.ReplicatedPlayerFactory(id => replicas[(int)id], viewRoster),
                    DisplayNamePolicy = content.DisplayNamePolicy
                }, out _);
                presentation = party == null ? -1 : party.RemoteMembers.Sum(m => m.GameObject.GetComponentsInChildren<Camera>(true).Length + m.GameObject.GetComponentsInChildren<AudioListener>(true).Length + m.GameObject.GetComponentsInChildren<PlayerInput>(true).Length);
                var composed = party?.ComposedPartySize ?? 0;
                party?.Dispose();
                return composed;
            }

            facts.ClientDuoComposed = View(2, out var duoPresentation);
            facts.ClientTrioComposed = View(3, out var trioPresentation);
            facts.ClientRemoteLocalPresentation = duoPresentation + trioPresentation;
        }

        [Test]
        public void CoopCompletionRules_FailOnADeliberatelyBrokenFixture()
        {
            // The real sources with the client composition, the depth publication and the client spawner guard removed,
            // plus facts from a run whose host and client diverged: every one of those rules must fail.
            var overrides = new Dictionary<string, string>
            {
                [CoopRuntimeCompositionValidator.ExpeditionCoopPath] = File.ReadAllText(CoopRuntimeCompositionValidator.ExpeditionCoopPath).Replace("BuildWhenClientReady", "BuildLater").Replace("RequestResync()", "Nothing()"),
                [CoopRuntimeCompositionValidator.ExpeditionScenePath] = File.ReadAllText(CoopRuntimeCompositionValidator.ExpeditionScenePath).Replace("new AuthoritativeEnemySpawner(roomSpawner, new CoopClientAuthority())", "roomSpawner").Replace("BuildWhenClientReady", "BuildLater"),
                [CoopRuntimeCompositionValidator.HostWorldPath] = File.ReadAllText(CoopRuntimeCompositionValidator.HostWorldPath).Replace("_bus.PublishDepth(", "_bus.Ignore("),
                [CoopRuntimeCompositionValidator.ProofPath] = string.Empty
            };
            var broken = new CoopRuntimeCompositionValidator.CompositionFacts
            {
                ComposablePartySizes = new[] { 1, 2, 3 }, TrioExpected = 3, TrioComposed = 3, RefusesUnderfilledStart = true,
                RosterMembersForTrio = 3, LootParticipantsForTrio = 3, CoopFlagFromComposedParty = true, SoloFlagStaysSolo = true,
                RoomStateDivergences = 1, ClientDuoComposed = 1, ClientTrioComposed = 3, ClientRemoteLocalPresentation = 1, ResyncDuplicates = 2, ClientLocalHitsApplied = 5
            };
            var report = CoopRuntimeCompositionValidator.Validate(broken, overrides);
            Assert.IsFalse(report.Pass);
            var failed = report.Lines.Where(l => !l.Pass).Select(l => l.Rule).ToList();
            foreach (var rule in new[] { "client composition", "depth sync", "enemy sync", "live reconnect", "room state fixture", "client party view", "resync fixture" })
                CollectionAssert.Contains(failed, rule, $"the broken fixture must fail '{rule}'");
        }

        /// <summary>A factory that can only produce the first <c>limit</c> members — an underfilled start.</summary>
        private sealed class RefusingFactory : RuinRail.Networking.IPlayerEntityFactory
        {
            private readonly int _limit;
            public RefusingFactory(int limit) { _limit = limit; }
            public GameObject Spawn(RuinRail.Networking.PlayerIdentity identity, bool isLocalOwner)
                => identity.ClientId < (ulong)_limit ? new GameObject("Member_" + identity.ClientId) : null;
            public void Despawn(GameObject entity) { if (entity != null) Object.DestroyImmediate(entity); }
        }

        [Test]
        public void CoopRuntimeComposition_ContractHolds_AndTheReportIsWritten()
        {
            var facts = new CoopRuntimeCompositionValidator.CompositionFacts();
            var composable = new List<int>();

            for (var size = 1; size <= 3; size++)
            {
                var party = Compose(size, size, new Vector2(0f, 3000f + size * 100f), out var roster, out var loot, out var error);
                Assert.IsNotNull(party, $"{size}-player composition failed: {error}");
                Assert.AreEqual(size, party.ComposedPartySize);
                composable.Add(size);

                if (size == 1) facts.SoloFlagStaysSolo = !party.IsCoop;
                if (size == 3)
                {
                    facts.TrioExpected = party.ExpectedPartySize;
                    facts.TrioComposed = party.ComposedPartySize;
                    facts.RosterMembersForTrio = roster.Count;
                    facts.LootParticipantsForTrio = loot.Participants.Count;
                    facts.CoopFlagFromComposedParty = party.IsCoop;
                    var remotes = party.RemoteMembers.ToList();
                    facts.RemoteCameras = remotes.Sum(m => m.GameObject.GetComponentsInChildren<Camera>(true).Length);
                    facts.RemoteAudioListeners = remotes.Sum(m => m.GameObject.GetComponentsInChildren<AudioListener>(true).Length);
                    facts.RemoteLocalInputReaders = remotes.Sum(m => m.GameObject.GetComponentsInChildren<PlayerInput>(true).Length);
                    facts.RemoteHuds = remotes.Sum(m => m.GameObject.GetComponentsInChildren<RuinRail.UI.Hud.DungeonHudView>(true).Length);
                }

                party.Dispose();
            }

            facts.ComposablePartySizes = composable;

            // 83: a start that cannot compose what the lobby promised is refused, not scaled down silently. The host
            // reports each member it could not spawn, which is the error this expects.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("No player entity could be spawned"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("No player entity could be spawned"));
            var underfilled = Compose(1, 3, new Vector2(0f, 3400f), out _, out _, out var underfilledError);
            Assert.IsNull(underfilled, "An underfilled start must be refused.");
            Assert.AreEqual(PartyCompositionError.Underfilled, underfilledError);
            facts.RefusesUnderfilledStart = true;

            SupplyCompletionFacts(facts);
            var report = CoopRuntimeCompositionValidator.Validate(facts);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CoopRuntimeCompositionValidator.ReportPath)) ?? ".");
            File.WriteAllText(CoopRuntimeCompositionValidator.ReportPath, report.ToMarkdown());

            Assert.Greater(report.Lines.Count, 15, "Every contract rule is checked.");
            Assert.IsTrue(report.Pass, "Co-op runtime composition contract:\n" + string.Join("\n",
                report.Lines.Where(l => !l.Pass).Select(l => $"- {l.Rule} / {l.Subject}: {string.Join("; ", l.Problems)}")));
        }
    }
}
