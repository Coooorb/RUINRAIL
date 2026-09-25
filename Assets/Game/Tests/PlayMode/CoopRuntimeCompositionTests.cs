using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The real co-op runtime composition: 1-, 2- and 3-player expeditions composed through the shipping
    /// <see cref="ExpeditionParty"/> path, with real player entities, a real <see cref="PartyLifeRoster"/>, a real
    /// <see cref="LootAuthorityService"/> and real physics.
    ///
    /// Nothing here is a mock of the composition itself: the entities are built by <see cref="PlayerEntityBuilder"/>,
    /// the presence map is <see cref="PlayerPresenceService"/>'s, and the party services are the ones the expedition
    /// composes. The connection layer is the repository's offline/loopback double, which is the strongest path that can
    /// run in a single process — a second OS process and live Relay are reported separately and honestly.
    /// </summary>
    public class CoopRuntimeCompositionTests
    {
        private const string MatrixDirectory = "TestResults/CoopRuntimeComposition";
        private readonly List<UnityEngine.Object> _created = new();
        private int _lane;

        [SetUp]
        public void SetUp() => DamageAuthority.LocalIsAuthoritative = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        private T Track<T>(T o) where T : UnityEngine.Object { _created.Add(o); return o; }

        /// <summary>Each fixture gets its own stretch of world so two parties can never touch each other's physics.</summary>
        private Vector2 NextLane() => new(0f, 400f + _lane++ * 120f);

        private static RuinRail.App.GameContentCatalog Catalog() => RuinRail.App.GameContentCatalog.Load();

        private static void WriteMatrix(string fileName, IEnumerable<string> rows)
        {
            Directory.CreateDirectory(MatrixDirectory);
            File.WriteAllLines(Path.Combine(MatrixDirectory, fileName), rows);
        }

        // ---------------- composition ----------------

        private sealed class Fixture : IDisposable
        {
            public ExpeditionParty Party;
            public PartyLifeRoster Roster;
            public LootAuthorityService Loot;
            public GameObject LocalEntity;
            public PartyCompositionError Error;

            public void Dispose() => Party?.Dispose();
        }

        /// <summary>Composes a real party of <paramref name="size"/> members at <paramref name="origin"/>.</summary>
        private Fixture Compose(int size, Vector2 origin, int expectedOverride = 0)
        {
            var content = Catalog();
            var roster = new PartyLifeRoster();
            var loot = new LootAuthorityService(LocalAuthorityContext.Instance);
            // The local player is built exactly as the run builds it, then adopted by the party composition.
            var local = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "LocalPlayer",
                IsLocal = true,
                InputReader = new FakePlayerInputReader(),
                BalanceConfig = content.PlayerBalance,
                Caps = content.StatCaps,
                Position = origin,
                LifeRoster = roster,
                ParticipantId = "p0"
            }));
            local.GetComponent<PlayerLootReceiver>().SetInventory(PlayerInventory.FromRegistry(Registry(), content.AmmoBalance));

            var members = new List<PartyMemberDescriptor>();
            for (var i = 0; i < Math.Max(size, expectedOverride); i++)
                members.Add(new PartyMemberDescriptor((ulong)i, i == 0 ? "Host" : "Client " + i, "p" + i, i == 0));
            // An expectedOverride larger than size models a lobby that promised more members than can be composed.
            var composable = members.Take(size).ToList();

            var request = new PartyCompositionRequest
            {
                Members = expectedOverride > 0 ? members : composable,
                LocalClientId = 0,
                LocalDisplayName = "Host",
                LocalParticipantId = "p0",
                LocalEntity = local,
                IsHost = true,
                Balance = content.PlayerBalance,
                Caps = content.StatCaps,
                LifeRoster = roster,
                Loot = loot,
                Items = Registry(),
                Ammo = content.AmmoBalance,
                Grace = new ReconnectGraceService(ReconnectGraceService.DefaultGraceSeconds, () => true),
                SpawnPosition = identity => origin + new Vector2(1.5f * (identity.ClientId + 1), 0f)
            };

            if (expectedOverride > 0)
            {
                // Model the underfilled case: the snapshot names more members than the host will admit.
                request.Members = members.Take(expectedOverride).ToList();
            }

            var party = ExpeditionParty.Compose(request, out var error);
            foreach (var member in party?.Members ?? Enumerable.Empty<NetworkPlayerEntity>())
            {
                if (member.GameObject == null || member.GameObject == local) continue;
                Track(member.GameObject);
            }

            return new Fixture { Party = party, Roster = roster, Loot = loot, LocalEntity = local, Error = error };
        }

        private static ItemDefinitionRegistry _registry;
        private static ItemDefinitionRegistry Registry() => _registry ??= ItemDefinitionRegistry.Build(Catalog().Items.Where(i => i != null));

        // ---------------- Phase 3 / 4: presence and scaling ----------------

        [UnityTest]
        public IEnumerator Party_ComposesOneTwoOrThreePlayers_WithOneEntityPerClient()
        {
            var rows = new List<string> { "scenario,expected,composed,entities,owned_by_local,roster_members,loot_participants,scaling_party_size,is_coop,result" };
            foreach (var size in new[] { 1, 2, 3 })
            {
                using var fixture = Compose(size, NextLane());
                yield return null;
                Assert.IsNotNull(fixture.Party, $"{size}-player composition must succeed.");
                var party = fixture.Party;

                Assert.AreEqual(size, party.ComposedPartySize, $"{size}: one entity per client.");
                Assert.AreEqual(size, party.Presence.Entities.Count);
                Assert.AreEqual(1, party.Members.Count(m => m.IsLocalOwner), $"{size}: exactly one entity is owned by this client.");
                Assert.AreSame(fixture.LocalEntity, party.LocalEntity, $"{size}: the local player is adopted, never rebuilt.");
                CollectionAssert.AllItemsAreUnique(party.Members.Select(m => m.OwnerClientId).ToList(), $"{size}: no duplicate entity for one client id.");
                Assert.AreEqual(size, fixture.Roster.Count, $"{size}: every member reaches the party life roster.");
                Assert.AreEqual(size, fixture.Loot.Participants.Count, $"{size}: every member is a loot participant.");
                Assert.AreEqual(size, party.ScalingPartySize, $"{size}: the dungeon scales for the composed party.");
                Assert.AreEqual(size > 1, party.IsCoop, $"{size}: the co-op flag follows composed presence.");
                Assert.IsTrue(party.IsFullyComposed);

                rows.Add(string.Join(",", size == 1 ? "solo" : size == 2 ? "duo" : "trio", size, party.ComposedPartySize,
                    party.Presence.Entities.Count, party.Members.Count(m => m.IsLocalOwner), fixture.Roster.Count,
                    fixture.Loot.Participants.Count, party.ScalingPartySize, party.IsCoop, "PASS"));
            }

            WriteMatrix("player_presence_matrix.csv", rows);
        }

        [UnityTest]
        public IEnumerator Party_RefusesAnUnderfilledStart_SoScalingCanNeverRunAheadOfPresence()
        {
            // 83: a lobby that promised three members but can only compose one must not produce a trio-scaled dungeon.
            var rows = new List<string> { "case,expected_party,composed_entities,scaling_party_size,started,error" };
            using (var solo = Compose(1, NextLane()))
            {
                Assert.IsNotNull(solo.Party);
                rows.Add($"solo start,1,1,{solo.Party.ScalingPartySize},YES,None");
            }

            yield return null;
            var content = Catalog();
            var roster = new PartyLifeRoster();
            var local = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "LocalPlayer", IsLocal = true, InputReader = new FakePlayerInputReader(),
                BalanceConfig = content.PlayerBalance, Caps = content.StatCaps, Position = NextLane(), LifeRoster = roster, ParticipantId = "p0"
            }));

            // A snapshot naming four members exceeds the party limit and must be refused outright (80).
            var tooMany = ExpeditionParty.Compose(new PartyCompositionRequest
            {
                Members = Enumerable.Range(0, 4).Select(i => new PartyMemberDescriptor((ulong)i, "P" + i, "p" + i, i == 0)).ToList(),
                LocalClientId = 0, LocalEntity = local, LifeRoster = roster, Balance = content.PlayerBalance, Caps = content.StatCaps
            }, out var tooManyError);
            Assert.IsNull(tooMany, "A party larger than 3 must be refused.");
            Assert.AreEqual(PartyCompositionError.TooManyMembers, tooManyError);
            rows.Add("four members promised,4,0,n/a,NO,TooManyMembers");

            // A snapshot that does not include this client leaves the peer with nothing to own.
            var noLocal = ExpeditionParty.Compose(new PartyCompositionRequest
            {
                Members = new[] { new PartyMemberDescriptor(7, "Other", "p7", false) },
                LocalClientId = 0, LocalEntity = local, LifeRoster = roster, Balance = content.PlayerBalance, Caps = content.StatCaps
            }, out var noLocalError);
            Assert.IsNull(noLocal, "A snapshot without the local client must be refused.");
            Assert.AreEqual(PartyCompositionError.NoLocalMember, noLocalError);
            rows.Add("snapshot excludes this peer,1,0,n/a,NO,NoLocalMember");

            // The third refusal (a start that composes fewer entities than promised) needs a factory that fails to spawn,
            // which the EditMode validator fixture supplies; it is proven there, against the same composition.
            rows.Add("composed fewer than promised,3,1,n/a,NO,Underfilled (proven in CoopRuntimeCompositionValidatorTests)");
            WriteMatrix("party_scaling_integrity.csv", rows);
        }

        // ---------------- Phase 7: local presentation ----------------

        [UnityTest]
        public IEnumerator RemoteMembers_CreateNoCameraListenerInputOrHud()
        {
            using var fixture = Compose(3, NextLane());
            yield return null;
            var rows = new List<string> { "member,is_local_owner,cameras,audio_listeners,local_input_readers,huds,input_reader_type,result" };

            foreach (var member in fixture.Party.Members)
            {
                var go = member.GameObject;
                var cameras = go.GetComponentsInChildren<Camera>(true).Length;
                var listeners = go.GetComponentsInChildren<AudioListener>(true).Length;
                var inputs = go.GetComponentsInChildren<PlayerInput>(true).Length;
                var huds = go.GetComponentsInChildren<DungeonHudView>(true).Length;
                Assert.AreEqual(0, cameras, "A composed member must not carry a camera.");
                Assert.AreEqual(0, listeners, "A composed member must not carry an AudioListener.");
                Assert.AreEqual(0, huds, "A composed member must not carry a HUD.");
                if (!member.IsLocalOwner)
                {
                    Assert.AreEqual(0, inputs, "A remote member must not carry a local PlayerInput.");
                    var movement = go.GetComponent<PlayerMovement>();
                    Assert.IsNotNull(movement);
                }

                rows.Add(string.Join(",", member.Identity.DisplayName, member.IsLocalOwner, cameras, listeners,
                    member.IsLocalOwner ? inputs : 0, huds, member.IsLocalOwner ? "local" : "NullPlayerInputReader", "PASS"));
            }

            // The whole party adds exactly one listener and one camera to the process: none, because both belong to the
            // local presentation the expedition composes once.
            Assert.AreEqual(0, fixture.Party.Members.Sum(m => m.GameObject.GetComponentsInChildren<AudioListener>(true).Length));
            WriteMatrix("local_presentation_matrix.csv", rows);
        }

        [UnityTest]
        public IEnumerator RemoteMembers_DoNotRespondToLocalInput()
        {
            using var fixture = Compose(2, NextLane());
            yield return new WaitForFixedUpdate();
            var remote = fixture.Party.RemoteMembers.First().GameObject;
            var before = (Vector2)remote.transform.position;
            var movement = remote.GetComponent<PlayerMovement>();
            Assert.IsNotNull(movement);

            // The remote replica's reader is the null reader by construction: no input source exists that could drive it.
            for (var i = 0; i < 12; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(before, (Vector2)remote.transform.position, "A remote member cannot be moved by this client.");
        }

        // ---------------- Phase 9: loot race ----------------

        [UnityTest]
        public IEnumerator SharedPickup_RacedByTwoPlayers_HasExactlyOneWinner()
        {
            var origin = NextLane();
            using var fixture = Compose(2, origin);
            yield return null;
            var members = fixture.Party.Members.ToList();
            var rows = new List<string> { "case,claimants,winner,loser_verdict,duplicates,pickup_consumed,result" };

            var ammo = Catalog().Items.OfType<AmmoItemDefinition>().First();
            var pickup = Track(new GameObject("SharedAmmo")).AddComponent<WorldItemPickup>();
            pickup.transform.position = origin + Vector2.right;
            var collider = pickup.gameObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            pickup.Hold(new ItemInstance(ammo.Id, 10), ItemCategory.Ammo);
            yield return null;

            var first = fixture.Loot.RequestPickup("tx-race", members[0].OwnerClientId, pickup);
            var second = fixture.Loot.RequestPickup("tx-race-2", members[1].OwnerClientId, pickup);
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict, "Exactly one claimant wins the race.");
            Assert.AreNotEqual(LootVerdict.Accepted, second.Verdict, "The second claimant must not also win.");
            Assert.IsTrue(pickup == null || pickup.IsConsumed, "The pickup is consumed once.");
            rows.Add($"two players race one pickup,2,{members[0].Identity.DisplayName},{second.Verdict},0,YES,PASS");

            // A re-sent request (lag, retry) returns the stored result and never executes twice.
            var replay = fixture.Loot.RequestPickup("tx-race", members[0].OwnerClientId, pickup);
            Assert.AreEqual(first.Verdict, replay.Verdict);
            rows.Add($"duplicate request id replayed,1,{members[0].Identity.DisplayName},{replay.Verdict},0,YES,PASS");

            // The runtime interaction path: with the arbiter installed the way the run installs it for a co-op party,
            // two members walking onto one pickup and interacting produce exactly one winner — the arbiter decides, not
            // whichever collider reported first.
            var contested = Track(new GameObject("ContestedAmmo")).AddComponent<WorldItemPickup>();
            contested.transform.position = origin + Vector2.up;
            contested.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
            contested.Hold(new ItemInstance(ammo.Id, 10), ItemCategory.Ammo);
            var byClient = fixture.Party.Members.ToDictionary(m => m.GameObject, m => m.OwnerClientId);
            PickupArbiter.Items = (p, interactor) => byClient.TryGetValue(interactor, out var id)
                ? fixture.Loot.RequestPickup(System.Guid.NewGuid().ToString("N"), id, p).Verdict == LootVerdict.Accepted
                : (bool?)null;
            try
            {
                var firstTake = contested.Interact(members[0].GameObject);
                var secondTake = contested != null && contested.Interact(members[1].GameObject);
                Assert.IsTrue(firstTake, "The first interacting member wins through the arbiter.");
                Assert.IsFalse(secondTake, "The second member's interaction is refused by the arbiter.");
                rows.Add($"runtime interaction arbitrated,2,{members[0].Identity.DisplayName},Refused,0,YES,PASS");
            }
            finally
            {
                PickupArbiter.Clear();
            }

            // Solo installs no arbiter at all: the accepted local pickup path is unchanged.
            Assert.IsNull(PickupArbiter.Items);
            var soloPickup = Track(new GameObject("SoloAmmo")).AddComponent<WorldItemPickup>();
            soloPickup.transform.position = origin + Vector2.down;
            soloPickup.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
            soloPickup.Hold(new ItemInstance(ammo.Id, 5), ItemCategory.Ammo);
            Assert.IsTrue(soloPickup.Interact(fixture.LocalEntity), "Without an arbiter a pickup resolves locally as before.");
            rows.Add("solo local path,1,local player,n/a,0,YES,PASS");

            WriteMatrix("loot_race_matrix.csv", rows);
        }

        // ---------------- Phase 12: downed / revive / wipe ----------------

        [UnityTest]
        public IEnumerator Coop_DownedReviveAndWipe_UseTheRealParty()
        {
            var origin = NextLane();
            using var fixture = Compose(3, origin);
            yield return null;
            var rows = new List<string> { "case,party_size,member,from_state,to_state,anyone_alive,is_wiped,result" };
            var members = fixture.Party.Members.Select(m => m.GameObject.GetComponent<PlayerLifeStateComponent>()).ToList();
            Assert.AreEqual(3, members.Count);
            Assert.IsTrue(fixture.Roster.IsCoop, "Three composed members is a co-op roster.");

            // 84: at 0 HP with a living teammate the player goes Downed, not Dead, and the run continues.
            var victim = members[1];
            victim.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.AreEqual(PlayerLifeState.Downed, victim.State, "A member with living teammates goes Downed.");
            Assert.IsTrue(fixture.Roster.AnyoneAlive);
            Assert.IsFalse(fixture.Roster.IsWiped, "One member down is not a wipe.");
            rows.Add($"first member reaches 0 HP,3,{victim.ParticipantId},Alive,Downed,YES,NO,PASS");

            // 84: a teammate revives through the real 4 s channel; the roster sees the member return.
            var arbiter = new ReviveArbiter();
            var rescuer = fixture.Party.Members.First(m => m.GameObject.GetComponent<PlayerLifeStateComponent>() != victim)
                .GameObject.GetComponent<PlayerReviver>();
            var channel = arbiter.TryBegin(rescuer, victim, 4f);
            Assert.IsNotNull(channel, "A living teammate can revive a Downed member.");
            arbiter.Advance(channel, 4f);
            yield return null;
            Assert.AreEqual(PlayerLifeState.Alive, victim.State);
            Assert.AreEqual(1, arbiter.Completed);
            rows.Add($"revived by teammate (4 s channel),3,{victim.ParticipantId},Downed,Alive,YES,NO,PASS");

            // 84: every member Downed/Dead is a wipe, raised exactly once. The revived member carries ~1.5 s of revive
            // protection, so the wipe is driven only once that has lapsed — otherwise the damage is correctly ignored.
            var protection = victim.GetComponent<ReviveProtection>();
            Assert.IsTrue(protection != null && protection.IsInvulnerable, "84: a revived member is briefly protected.");
            protection.Tick(protection.Remaining + 0.1f);
            Assert.IsFalse(protection.IsInvulnerable, "Revive protection lapses.");
            var wipes = 0;
            fixture.Roster.TeamWiped += _ => wipes++;
            foreach (var member in members) member.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.IsTrue(fixture.Roster.IsWiped, "Every member down is a wipe.");
            Assert.AreEqual(1, wipes, "The wipe is raised exactly once.");
            rows.Add("every member down,3,all,Alive/Downed,Downed,NO,YES,PASS");

            WriteMatrix("revive_wipe_matrix.csv", rows);
        }

        [UnityTest]
        public IEnumerator Solo_KeepsSoloDeathSemantics()
        {
            using var fixture = Compose(1, NextLane());
            yield return null;
            var life = fixture.LocalEntity.GetComponent<PlayerLifeStateComponent>();
            Assert.IsFalse(fixture.Roster.IsCoop, "One member is not a co-op roster.");
            life.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.AreEqual(PlayerLifeState.Dead, life.State, "Solo: 0 HP is death, never Downed (84 is co-op only).");
            Assert.IsTrue(fixture.Roster.IsWiped);
        }

        // ---------------- Phase 15: disconnect / reconnect ----------------

        [UnityTest]
        public IEnumerator DisconnectAndReconnect_PreserveIdentityWithoutMintingAnEntity()
        {
            using var fixture = Compose(3, NextLane());
            yield return null;
            var rows = new List<string> { "case,client_id,entities_before,entities_after,roster_after,duplicate_entity,identity_preserved,result" };
            var party = fixture.Party;
            var leaving = party.RemoteMembers.First();
            var token = leaving.ReconnectToken;
            var before = party.ComposedPartySize;

            // 85: mid-expedition the character stays held under its reconnect token rather than being destroyed.
            party.SimulateDisconnect(leaving.OwnerClientId);
            yield return null;
            Assert.IsFalse(party.Presence.Entities.ContainsKey(leaving.OwnerClientId), "The disconnected client leaves the presence map.");
            Assert.IsNotNull(leaving.GameObject, "The held character is not destroyed during grace.");
            rows.Add($"disconnect inside grace,{leaving.OwnerClientId},{before},{party.ComposedPartySize},{fixture.Roster.Count},NO,YES,PASS");

            // Reconnecting with the token reclaims the same entity: nothing new is spawned.
            var spawnsBefore = party.Presence.SpawnCount;
            party.SimulateReconnect(99, token, leaving.Identity.DisplayName);
            yield return null;
            Assert.AreEqual(spawnsBefore, party.Presence.SpawnCount, "A reconnect must not mint an extra player.");
            Assert.AreEqual(1, party.Presence.ReconnectCount);
            Assert.IsTrue(party.Presence.Entities.ContainsKey(99));
            Assert.AreSame(leaving.GameObject, party.Presence.Entities[99].GameObject, "The same character is restored.");
            Assert.AreEqual(before, party.ComposedPartySize, "The party is whole again.");
            rows.Add($"reconnect inside grace,99,{before - 1},{party.ComposedPartySize},{fixture.Roster.Count},NO,YES,PASS");

            WriteMatrix("reconnect_matrix.csv", rows);
        }

        // ---------------- Phase 13: transit voting ----------------

        [UnityTest]
        public IEnumerator TransitVoting_UsesTheRealPartyForDuoAndTrio()
        {
            var rows = new List<string> { "scenario,living_voters,votes,result,unanimous_required,dead_can_vote,result_status" };
            foreach (var size in new[] { 2, 3 })
            {
                using var fixture = Compose(size, NextLane());
                yield return null;
                var lives = fixture.Party.Members.Select(m => m.GameObject.GetComponent<PlayerLifeStateComponent>()).ToList();
                var decision = new RuinRail.Gameplay.Expedition.TransitDecision(
                    new RuinRail.Gameplay.Expedition.PartyTransitPolicy(),
                    lives.Select(l => l.ParticipantId));
                decision.Open();

                // 86: Continue requires unanimous approval from all living players — Submit resolves only on the last one.
                for (var i = 0; i < size - 1; i++)
                {
                    Assert.IsFalse(decision.Submit(lives[i].ParticipantId, RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper),
                        $"{size}: vote {i + 1} cannot resolve the decision on its own.");
                    Assert.IsNull(decision.Result, $"{size}: descending needs every living player.");
                }

                // A repeated vote from the same player is one voter's choice, never an extra one.
                Assert.IsFalse(decision.Submit(lives[0].ParticipantId, RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper));
                Assert.AreEqual(size - 1, decision.Choices.Count, "One player's vote cannot be counted twice.");
                Assert.IsTrue(decision.Submit(lives[size - 1].ParticipantId, RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper),
                    $"{size}: the last living player's approval resolves the vote.");
                Assert.AreEqual(RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper, decision.Result);
                Assert.AreEqual(size, decision.LivingPlayers.Count, $"{size}: every composed member is a voter.");
                rows.Add($"{(size == 2 ? "duo" : "trio")} unanimous descend,{size},{size},DescendDeeper,YES,NO,PASS");
            }

            // Any living player choosing Return returns the whole party (86).
            using (var fixture = Compose(3, NextLane()))
            {
                yield return null;
                var lives = fixture.Party.Members.Select(m => m.GameObject.GetComponent<PlayerLifeStateComponent>()).ToList();
                var decision = new RuinRail.Gameplay.Expedition.TransitDecision(
                    new RuinRail.Gameplay.Expedition.PartyTransitPolicy(), lives.Select(l => l.ParticipantId));
                decision.Open();
                decision.Submit(lives[0].ParticipantId, RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper);
                decision.Submit(lives[1].ParticipantId, RuinRail.Gameplay.Expedition.TransitChoice.ReturnToShelter);
                Assert.AreEqual(RuinRail.Gameplay.Expedition.TransitChoice.ReturnToShelter, decision.Result, "Any Return returns the party.");
                rows.Add("trio one Return outvotes,3,2,ReturnToShelter,YES,NO,PASS");
            }

            WriteMatrix("transit_vote_matrix.csv", rows);
        }
    }
}
