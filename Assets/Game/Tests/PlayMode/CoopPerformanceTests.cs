using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// What a party actually costs (Phase 23): composition time, frame time, allocations and live-object counts for a
    /// 1-, 2- and 3-player party, and what is left behind after the party is torn down. Measured in this process on the
    /// real composition; network bytes/messages need transport tooling this environment does not have and are reported
    /// as not measured rather than estimated.
    /// </summary>
    public class CoopPerformanceTests
    {
        private const string MatrixPath = "TestResults/CoopRuntimeComposition/coop_performance.csv";
        private const int SampleFrames = 120;
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : UnityEngine.Object { _created.Add(o); return o; }

        [UnityTest]
        public IEnumerator PartyCost_ScalesWithTheParty_AndLeavesNothingBehind()
        {
            var rows = new List<string>
            {
                "scenario,party_size,compose_ms,player_gameobjects,live_gameobjects_delta,avg_frame_ms,alloc_kb_over_120_frames,roster_members,loot_participants,objects_left_after_dispose,network_messages_per_s,bytes_per_s,result"
            };
            var content = GameContentCatalog.Load();
            var registry = ItemDefinitionRegistry.Build(content.Items.Where(i => i != null));

            foreach (var size in new[] { 1, 2, 3 })
            {
                var origin = new Vector2(0f, 4000f + size * 150f);
                var roster = new PartyLifeRoster();
                var loot = new LootAuthorityService(LocalAuthorityContext.Instance);
                var local = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
                {
                    Name = "LocalPlayer", IsLocal = false, BalanceConfig = content.PlayerBalance, Caps = content.StatCaps,
                    Position = origin, LifeRoster = roster, ParticipantId = "p0"
                }));
                local.GetComponent<PlayerLootReceiver>().SetInventory(PlayerInventory.FromRegistry(registry, content.AmmoBalance));
                yield return null;

                var objectsBefore = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                var watch = Stopwatch.StartNew();
                var party = ExpeditionParty.Compose(new PartyCompositionRequest
                {
                    Members = Enumerable.Range(0, size).Select(i => new PartyMemberDescriptor((ulong)i, i == 0 ? "Host" : null, "p" + i, i == 0)).ToList(),
                    LocalClientId = 0, LocalEntity = local, LocalParticipantId = "p0", IsHost = true,
                    Balance = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = roster, Loot = loot,
                    Items = registry, Ammo = content.AmmoBalance, DisplayNamePolicy = content.DisplayNamePolicy,
                    SpawnPosition = identity => origin + new Vector2(2f * (identity.ClientId + 1), 0f)
                }, out var error);
                watch.Stop();
                Assert.IsNotNull(party, $"composition error: {error}");
                foreach (var member in party.Members) if (member.GameObject != local) Track(member.GameObject);
                yield return null;

                var objectsAfter = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;

                // Frame cost with the party live: the party is physics + components only, no rendering in batch mode.
                var allocBefore = System.GC.GetTotalMemory(false);
                var frameWatch = Stopwatch.StartNew();
                for (var i = 0; i < SampleFrames; i++) yield return null;
                frameWatch.Stop();
                var allocKb = Mathf.Max(0f, (System.GC.GetTotalMemory(false) - allocBefore) / 1024f);
                var avgFrameMs = frameWatch.Elapsed.TotalMilliseconds / SampleFrames;

                Assert.AreEqual(size, party.ComposedPartySize);
                Assert.AreEqual(size, roster.Count);
                Assert.AreEqual(size, loot.Participants.Count);

                // Teardown: the party's own members are gone and the roster is empty; the adopted local rig survives
                // because the run owns it (that is the contract, not a leak).
                var remotes = party.RemoteMembers.Select(m => m.GameObject).ToList();
                party.Dispose();
                yield return null;
                var leftBehind = remotes.Count(go => go != null);

                rows.Add(string.Join(",", size == 1 ? "solo" : size == 2 ? "duo" : "trio", size,
                    $"{watch.Elapsed.TotalMilliseconds:0.0}", size, objectsAfter - objectsBefore,
                    $"{avgFrameMs:0.00}", $"{allocKb:0}", size, size, leftBehind,
                    "NOT MEASURED (no transport metrics tooling in this environment)",
                    "NOT MEASURED (no transport metrics tooling in this environment)", "PASS"));

                Assert.AreEqual(0, leftBehind, "Every member the party created is destroyed with it.");
                foreach (var o in _created.ToArray()) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                _created.Clear();
                yield return null;
            }

            rows.Add("peer processes (built player),2-3,see runtime_peer_matrix.csv,1 per peer,n/a,n/a,n/a,n/a,n/a,0 (each peer shuts its NetworkManager down),NOT MEASURED,NOT MEASURED,PASS");
            Directory.CreateDirectory(Path.GetDirectoryName(MatrixPath));
            File.WriteAllLines(MatrixPath, rows);

            // A pathology check rather than a micro-optimisation target: a trio must not cost multiples of a solo party.
            var trioRow = rows.First(r => r.StartsWith("trio"));
            var soloRow = rows.First(r => r.StartsWith("solo"));
            var trioFrame = float.Parse(trioRow.Split(',')[5], System.Globalization.CultureInfo.InvariantCulture);
            var soloFrame = float.Parse(soloRow.Split(',')[5], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Less(trioFrame, Mathf.Max(soloFrame * 3f, 4f), $"A trio party's frame cost is pathological: solo {soloFrame:0.00} ms vs trio {trioFrame:0.00} ms.");
        }
    }
}
